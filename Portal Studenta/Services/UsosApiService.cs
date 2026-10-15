using System.Net.Http.Headers;
using System.Text.Json;

namespace Portal_Studenta.Services
{
    public class UsosApiService
    {
        private readonly HttpClient _httpClient;
        private static readonly JsonSerializerOptions CaseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };

        public UsosApiService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(25)
            };
        }

        public async Task<T?> GetAsync<T>(string endpoint, Dictionary<string, string>? queryParameters = null)
        {
            var token = await UsosAuthService.GetTokenAsync("usos_access_token");
            var tokenSecret = await UsosAuthService.GetTokenAsync("usos_access_token_secret");

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(tokenSecret))
            {
                throw new UnauthorizedAccessException("Brak tokenu dostępu USOS.");
            }

            string url = $"{UsosAuthService.BaseUrl}/{endpoint.TrimStart('/')}";
            
            if (queryParameters != null && queryParameters.Count > 0)
            {
                var query = string.Join("&", queryParameters.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
                url = $"{url}?{query}";
            }

            // Generowanie nagłówka OAuth dla konkretnego żądania
            string header = UsosAuthService.GenerateOAuthHeader("GET", url, UsosAuthService.ConsumerKey, UsosAuthService.ConsumerSecret, token, tokenSecret, null, null, null);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", header);

            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(json, CaseInsensitiveOptions);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                UsosAuthService.NotifySessionExpired();
                throw new UnauthorizedAccessException("Sesja USOS wygasła lub token został unieważniony. Zaloguj się ponownie.");
            }

            // W przypadku innego błędu rzuć wyjątkiem z treścią błędu
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"USOS API Error: {response.StatusCode} - {error}");
        }
    }
}
