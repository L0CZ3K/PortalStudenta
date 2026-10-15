using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Portal_Studenta.Services
{
    /// <summary>
    /// Lekki, lokalny serwer HTTP do bezpośredniego przesyłania zdjęć tablicy ze smartfona na komputer przez lokalną sieć Wi-Fi (P2P direct) bez chmury i bez kabla.
    /// </summary>
    public class LocalNetworkCameraServer : IDisposable
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private bool _isRunning;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public event Func<byte[], string, Task>? OnPhotoReceived;

        public int Port { get; private set; } = 5273;
        public string? LocalIpAddress { get; private set; }
        public string AuthToken { get; set; } = Guid.NewGuid().ToString("N");
        public string? EndpointUrl => !string.IsNullOrEmpty(LocalIpAddress) && _isRunning ? $"http://{LocalIpAddress}:{Port}" : null;
        public bool IsRunning => _isRunning;

        public static string? GetLocalIpAddress()
        {
            try
            {
                // Preferuj interfejsy Wi-Fi (en0 / en1 na Macu) lub aktywne interfejsy Ethernet
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                 !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                                 !ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase));

                foreach (var ni in interfaces)
                {
                    var props = ni.GetIPProperties();
                    foreach (var ip in props.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(ip.Address))
                        {
                            var str = ip.Address.ToString();
                            // Wybierz typowy adres z sieci domowej / uczelnianej
                            if (str.StartsWith("192.168.") || str.StartsWith("10.") || str.StartsWith("172."))
                            {
                                return str;
                            }
                        }
                    }
                }

                // Fallback na DNS host name
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalNetworkCameraServer] Błąd pobierania IP: {ex.Message}");
            }
            return null;
        }

        public async Task<bool> StartAsync(int preferredPort = 5273)
        {
            await _lock.WaitAsync();
            try
            {
                if (_isRunning) return true;

                LocalIpAddress = GetLocalIpAddress();
                if (string.IsNullOrEmpty(LocalIpAddress))
                {
                    Console.WriteLine("[LocalNetworkCameraServer] Brak aktywnego adresu IP w sieci lokalnej.");
                    return false;
                }

                // Spróbuj portu preferowanego lub kolejnych 5 portów
                for (int port = preferredPort; port < preferredPort + 5; port++)
                {
                    try
                    {
                        _listener = new TcpListener(IPAddress.Any, port);
                        _listener.Start();
                        Port = port;
                        _isRunning = true;
                        break;
                    }
                    catch (SocketException)
                    {
                        // Port zajęty, spróbuj następnego
                        _listener = null;
                    }
                }

                if (_listener == null || !_isRunning)
                {
                    Console.WriteLine("[LocalNetworkCameraServer] Nie udało się otworzyć portu nasłuchującego.");
                    return false;
                }

                _cts = new CancellationTokenSource();
                _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
                Console.WriteLine($"[LocalNetworkCameraServer] Uruchomiono serwer Wi-Fi: {EndpointUrl}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalNetworkCameraServer] Błąd uruchamiania serwera: {ex.Message}");
                _isRunning = false;
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Stop()
        {
            _lock.Wait();
            try
            {
                if (!_isRunning) return;
                _isRunning = false;
                _cts?.Cancel();
                _listener?.Stop();
                _listener = null;
                Console.WriteLine("[LocalNetworkCameraServer] Zatrzymano serwer Wi-Fi.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalNetworkCameraServer] Błąd zatrzymywania: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleClientAsync(client, ct);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[LocalNetworkCameraServer] Błąd obsługi klienta: {ex.Message}");
                        }
                    }, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Console.WriteLine($"[LocalNetworkCameraServer] Błąd accept: {ex.Message}");
                    }
                    break;
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                stream.ReadTimeout = 5000;
                stream.WriteTimeout = 5000;

                // Odczytaj nagłówki HTTP w pętli do bufora dynamicznego
                using var headerMs = new MemoryStream();
                var readChunk = new byte[4096];
                int headerEndIndex = -1;
                const int maxHeaderSize = 64 * 1024; // 64 KB limit na nagłówki HTTP

                while (!ct.IsCancellationRequested && headerMs.Length < maxHeaderSize)
                {
                    var read = await stream.ReadAsync(readChunk.AsMemory(0, readChunk.Length), ct);
                    if (read == 0) break;

                    headerMs.Write(readChunk, 0, read);

                    var currentBuffer = headerMs.GetBuffer();
                    var currentLength = (int)headerMs.Length;
                    headerEndIndex = FindHeaderEnd(currentBuffer, currentLength);
                    if (headerEndIndex >= 0)
                    {
                        break;
                    }
                }

                if (headerEndIndex < 0) return;

                var allHeaderBytes = headerMs.GetBuffer();
                var headersText = Encoding.UTF8.GetString(allHeaderBytes, 0, headerEndIndex);
                var lines = headersText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return;

                var requestLine = lines[0]; // np. "POST /api/upload_whiteboard HTTP/1.1"

                // Obsługa CORS OPTIONS (Preflight)
                if (requestLine.StartsWith("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    var corsResp = "HTTP/1.1 204 No Content\r\n" +
                                   "Access-Control-Allow-Origin: *\r\n" +
                                   "Access-Control-Allow-Methods: POST, GET, OPTIONS\r\n" +
                                   "Access-Control-Allow-Headers: Content-Type, X-Auth-Token\r\n" +
                                   "Content-Length: 0\r\n" +
                                   "Connection: close\r\n\r\n";
                    var corsBytes = Encoding.UTF8.GetBytes(corsResp);
                    await stream.WriteAsync(corsBytes.AsMemory(0, corsBytes.Length), ct);
                    return;
                }

                // Obsługa Ping / Health-check (sprawdzanie czy PC rzeczywiście żyje w sieci)
                if (requestLine.StartsWith("GET /api/ping", StringComparison.OrdinalIgnoreCase) ||
                    requestLine.StartsWith("HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    var pingResp = "HTTP/1.1 200 OK\r\n" +
                                   "Access-Control-Allow-Origin: *\r\n" +
                                   "Content-Type: application/json\r\n" +
                                   "Connection: close\r\n\r\n" +
                                   "{\"pong\":true}";
                    var pingBytes = Encoding.UTF8.GetBytes(pingResp);
                    await stream.WriteAsync(pingBytes.AsMemory(0, pingBytes.Length), ct);
                    return;
                }

                // Odczytaj Content-Length i nagłówek X-Auth-Token
                int contentLength = 0;
                string? receivedHeaderToken = null;
                foreach (var line in lines)
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(':');
                        if (parts.Length > 1) int.TryParse(parts[1].Trim(), out contentLength);
                    }
                    else if (line.StartsWith("X-Auth-Token:", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(':');
                        if (parts.Length > 1) receivedHeaderToken = parts[1].Trim();
                    }
                }

                if (contentLength <= 0 || contentLength > 25 * 1024 * 1024) // Maksymalnie 25 MB
                {
                    var badReq = "HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(badReq).AsMemory(), ct);
                    return;
                }

                // Odczytaj ciało żądania
                using var bodyMs = new MemoryStream(contentLength);
                var initialBodyOffset = headerEndIndex + 4;
                var totalHeaderBytes = (int)headerMs.Length;
                var initialBodyBytes = totalHeaderBytes - initialBodyOffset;
                if (initialBodyBytes > 0)
                {
                    bodyMs.Write(allHeaderBytes, initialBodyOffset, initialBodyBytes);
                }

                var remaining = contentLength - initialBodyBytes;
                var chunk = new byte[8192];
                while (remaining > 0)
                {
                    var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), ct);
                    if (read == 0) break;
                    bodyMs.Write(chunk, 0, read);
                    remaining -= read;
                }

                var bodyBytes = bodyMs.ToArray();
                byte[]? finalPhotoBytes = null;
                string contentType = "image/jpeg";
                string? clientToken = receivedHeaderToken;

                // Sprawdź czy body to JSON z base64
                try
                {
                    var jsonStr = Encoding.UTF8.GetString(bodyBytes);
                    using var doc = JsonDocument.Parse(jsonStr);
                    if (doc.RootElement.TryGetProperty("token", out var tokProp))
                    {
                        clientToken = tokProp.GetString();
                    }
                    if (doc.RootElement.TryGetProperty("base64", out var b64Prop))
                    {
                        var b64 = b64Prop.GetString();
                        if (!string.IsNullOrEmpty(b64))
                        {
                            finalPhotoBytes = Convert.FromBase64String(b64);
                        }
                    }
                    if (doc.RootElement.TryGetProperty("contentType", out var ctProp))
                    {
                        contentType = ctProp.GetString() ?? "image/jpeg";
                    }
                }
                catch
                {
                    // Body to surowe bajty obrazka JPEG/PNG
                    finalPhotoBytes = bodyBytes;
                }

                // Bezpieczeństwo w sieci lokalnej: weryfikacja tokena parowania urządzeń
                if (!string.IsNullOrEmpty(AuthToken) && !string.Equals(AuthToken, clientToken, StringComparison.Ordinal))
                {
                    Console.WriteLine("[LocalNetworkCameraServer] Odrzucono nieautoryzowane żądanie Wi-Fi (niepoprawny token).");
                    var unauthResp = "HTTP/1.1 401 Unauthorized\r\n" +
                                     "Content-Type: application/json\r\n" +
                                     "Connection: close\r\n\r\n" +
                                     "{\"error\":\"Invalid session token\"}";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(unauthResp).AsMemory(), ct);
                    return;
                }

                if (finalPhotoBytes != null && finalPhotoBytes.Length > 0 && OnPhotoReceived != null)
                {
                    await OnPhotoReceived.Invoke(finalPhotoBytes, contentType);

                    var okResp = "HTTP/1.1 200 OK\r\n" +
                                 "Access-Control-Allow-Origin: *\r\n" +
                                 "Content-Type: application/json\r\n" +
                                 "Connection: close\r\n\r\n" +
                                 "{\"success\":true,\"message\":\"Photo received over Wi-Fi\"}";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(okResp).AsMemory(), ct);
                }
                else
                {
                    var errResp = "HTTP/1.1 500 Internal Server Error\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(errResp).AsMemory(), ct);
                }
            }
        }

        /// <summary>
        /// Wysyła zdjęcie z telefonu bezpośrednio do serwera na komputerze przez sieć Wi-Fi.
        /// </summary>
        public static async Task<bool> SendPhotoOverWifiAsync(string endpointUrl, string base64Photo, string contentType = "image/jpeg", string? authToken = null)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl) || string.IsNullOrWhiteSpace(base64Photo))
            {
                return false;
            }

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(2200) };
                if (!string.IsNullOrEmpty(authToken))
                {
                    httpClient.DefaultRequestHeaders.Add("X-Auth-Token", authToken);
                }

                var payload = JsonSerializer.Serialize(new
                {
                    base64 = base64Photo,
                    contentType = contentType,
                    token = authToken
                });

                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var url = endpointUrl.TrimEnd('/') + "/api/upload_whiteboard";
                var response = await httpClient.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalNetworkCameraServer] Wi-Fi direct upload nie powiódł się (przejście na chmurę): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Szybko sprawdza czy serwer na Macu jest włączony i osiągalny pod danym adresem IP.
        /// </summary>
        public static async Task<bool> PingAsync(string endpointUrl)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl)) return false;
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(700) };
                var url = endpointUrl.TrimEnd('/') + "/api/ping";
                var response = await httpClient.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static int FindHeaderEnd(byte[] buffer, int length)
        {
            for (int i = 0; i <= length - 4; i++)
            {
                if (buffer[i] == 13 && buffer[i + 1] == 10 && buffer[i + 2] == 13 && buffer[i + 3] == 10)
                {
                    return i;
                }
            }
            return -1;
        }

        public void Dispose()
        {
            Stop();
            _lock.Dispose();
            _cts?.Dispose();
        }
    }
}
