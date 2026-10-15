using System.Diagnostics;

namespace Portal_Studenta.Services
{
    public class UniversityInfo
    {
        public string Code { get; set; } = "";
        public string ShortName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string ApiBaseUrl { get; set; } = "";
        public string Cities { get; set; } = "";
        public bool IsActive { get; set; } = false;
        public bool IsSupported { get; set; } = true;
    }


    public class UniversityService
    {
        private readonly HttpClient _httpClient;
        private ConnectionHealth _currentHealth = ConnectionHealth.Online;
        private string _statusMessage = "";
        private long _lastPingMs = 0;

        public event Action? OnHealthChanged;

        public ConnectionHealth CurrentHealth => _currentHealth;
        public long LastPingMs => _lastPingMs;
        public string StatusMessage => _statusMessage;

        public string StatusLabel
        {
            get
            {
                var uni = GetSelectedUniversity();
                return UniversityHealthLogic.GetStatusLabel(_currentHealth, uni.ShortName);
            }
        }

        public string DotColor => UniversityHealthLogic.GetDotColor(_currentHealth);

        public UniversityService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            try
            {
                Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
                _ = RefreshHealthAsync();
            }
            catch {}
        }

        private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            _ = RefreshHealthAsync();
        }

        public async Task RefreshHealthAsync()
        {
            try
            {
                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                {
                    var (offlineHealth, offlineMsg) = UniversityHealthLogic.ResolveHealthState(false, ServerStatus.Offline, 0, "");
                    _currentHealth = offlineHealth;
                    _statusMessage = offlineMsg;
                    OnHealthChanged?.Invoke();
                    return;
                }

                var uni = GetSelectedUniversity();
                var result = await CheckServerHealthAsync(uni);
                _lastPingMs = result.pingMs;

                var (resolvedHealth, resolvedMsg) = UniversityHealthLogic.ResolveHealthState(true, result.status, result.pingMs, uni.ShortName, result.message);
                _currentHealth = resolvedHealth;
                _statusMessage = resolvedMsg;
            }
            catch
            {
                _currentHealth = ConnectionHealth.Offline;
                _statusMessage = "Brak połączenia z siecią";
            }
            finally
            {
                OnHealthChanged?.Invoke();
            }
        }

        public static readonly List<UniversityInfo> AvailableUniversities = new()
        {
            new UniversityInfo
            {
                Code = "POLSL",
                ShortName = "POLSL",
                FullName = "Politechnika Śląska",
                ApiBaseUrl = "https://usosapi.polsl.pl",
                Cities = "Gliwice • Katowice • Zabrze",
                IsSupported = true
            },
        };

        public UniversityInfo GetSelectedUniversity()
        {
            string savedCode = Preferences.Default.Get("selected_university_code", "POLSL");
            var uni = AvailableUniversities.FirstOrDefault(u => u.Code.Equals(savedCode, StringComparison.OrdinalIgnoreCase));
            return uni ?? AvailableUniversities[0];
        }

        public void SetSelectedUniversity(string code)
        {
            Preferences.Default.Set("selected_university_code", code);
        }

        public async Task<(ServerStatus status, long pingMs, string message)> CheckServerHealthAsync(UniversityInfo uni)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uni.ApiBaseUrl);
                // Krótki timeout i proste nagłówki do probe
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                stopwatch.Stop();

                // Jeżeli serwer zwrócił jakikolwiek kod odpowiedzi HTTP (nawet 302, 200, 401, 404), to znaczy, że serwer USOS żyje i odpowiada!
                int statusCode = (int)response.StatusCode;
                var (serverStatus, msg) = UniversityHealthLogic.ClassifyHttpResponse(statusCode, uni.ShortName, stopwatch.ElapsedMilliseconds);
                return (serverStatus, stopwatch.ElapsedMilliseconds, msg);
            }
            catch (TaskCanceledException)
            {
                stopwatch.Stop();
                return (ServerStatus.Offline, stopwatch.ElapsedMilliseconds, "Przekroczono limit czasu połączenia (Timeout)");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return (ServerStatus.Offline, stopwatch.ElapsedMilliseconds, $"Brak połączenia z serwerem USOS ({ex.Message})");
            }
        }
    }
}
