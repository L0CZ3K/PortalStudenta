using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.Maui.Authentication;

namespace Portal_Studenta.Services
{
    public class UsosAuthService
    {
        public static string ConsumerKey => UsosSecrets.ConsumerKey;
        public static string ConsumerSecret => UsosSecrets.ConsumerSecret;
        public static readonly string BaseUrl = "https://usosapi.polsl.pl";
        public static readonly string CallbackUrl = "uniavio://callback";

        private readonly HttpClient _httpClient;

        public UsosAuthService()
        {
            _httpClient = new HttpClient();
        }

        public static async Task SetTokenAsync(string key, string value)
        {
            try
            {
                await SecureStorage.SetAsync(key, value);
            }
            catch
            {
                Preferences.Default.Set(key, value);
            }
        }

        public static async Task<string?> GetTokenAsync(string key)
        {
            try
            {
                var val = await SecureStorage.GetAsync(key);
                if (!string.IsNullOrEmpty(val)) return val;
            }
            catch { }
            return Preferences.Default.Get<string?>(key, null);
        }

        public static event Action? OnSessionExpired;

        public static void RemoveToken(string key)
        {
            try { SecureStorage.Remove(key); } catch { }
            Preferences.Default.Remove(key);
        }

        public static void NotifySessionExpired()
        {
            RemoveToken("usos_access_token");
            RemoveToken("usos_access_token_secret");
            try
            {
                OnSessionExpired?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UsosAuthService] OnSessionExpired error: {ex.Message}");
            }
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            var token = await GetTokenAsync("usos_access_token");
            return !string.IsNullOrEmpty(token);
        }

        /// <summary>
        /// Weryfikuje czy token USOS jest rzeczywiście aktywny na serwerze USOS API.
        /// Jeśli serwer zwróci 401 Unauthorized, token zostaje automatycznie usunięty i wywołane jest powiadomienie o wygaśnięciu sesji.
        /// W trybie offline lub w przypadku awarii serwera (5xx) zachowuje stan lokalny dla działania offline.
        /// </summary>
        public async Task<bool> ValidateTokenAsync()
        {
            var token = await GetTokenAsync("usos_access_token");
            var secret = await GetTokenAsync("usos_access_token_secret");
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(secret))
            {
                return false;
            }

            // Jeśli urządzenie jest w trybie offline, zachowujemy sesję offline dla lokalnej pamięci podręcznej
            try
            {
                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                {
                    return true;
                }
            }
            catch { }

            try
            {
                string url = $"{BaseUrl}/services/users/user?fields=id";
                string header = GenerateOAuthHeader("GET", url, ConsumerKey, ConsumerSecret, token, secret, null, null, null);

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("Authorization", header);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                using var resp = await _httpClient.SendAsync(req, cts.Token);

                if (resp.IsSuccessStatusCode)
                {
                    return true;
                }

                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine("[UsosAuthService] Wykryto unieważniony lub wygasły token USOS (401 Unauthorized). Reset sesji.");
                    await LogoutAsync();
                    NotifySessionExpired();
                    return false;
                }

                // Inne kody (500, 502, 503) oznaczają chwilową niedostępność serwera USOS — nie wylogowujemy użytkownika
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UsosAuthService] Błąd sieciowy podczas weryfikacji tokenu: {ex.Message}");
                // Błąd połączenia / timeout / brak DNS — pozwalamy na działanie offline
                return true;
            }
        }

        public async Task LogoutAsync()
        {
            RemoveToken("usos_access_token");
            RemoveToken("usos_access_token_secret");
        }

        public async Task<(bool success, string error)> AuthenticateAsync()
        {
            try
            {
                // 1. Zdobądź Request Token
                var (requestToken, requestTokenSecret) = await GetRequestTokenAsync();

                // 2. Otwórz przeglądarkę dla użytkownika
                string authUrl = $"{BaseUrl}/services/oauth/authorize?oauth_token={requestToken}";
                
                string verifier = "";

#if WINDOWS
                verifier = await AuthenticateViaWebViewAsync(authUrl, CallbackUrl);
#else
                try
                {
                    var authResult = await WebAuthenticator.AuthenticateAsync(
                        new WebAuthenticatorOptions
                        {
                            Url = new Uri(authUrl),
                            CallbackUrl = new Uri(CallbackUrl),
                            PrefersEphemeralWebBrowserSession = true
                        });

                    if (authResult.Properties.TryGetValue("oauth_verifier", out var v) && !string.IsNullOrWhiteSpace(v))
                    {
                        verifier = v;
                    }
                    else if (authResult.CallbackUri != null)
                    {
                        var query = HttpUtility.ParseQueryString(authResult.CallbackUri.Query);
                        verifier = query["oauth_verifier"] ?? "";
                    }
                }
                catch (Exception authEx) when (authEx is not TaskCanceledException)
                {
                    Console.WriteLine($"[UsosAuthService] WebAuthenticator błąd ({authEx.Message}), próba przez wbudowany WebView...");
                    verifier = await AuthenticateViaWebViewAsync(authUrl, CallbackUrl);
                }

                if (string.IsNullOrWhiteSpace(verifier))
                {
                    throw new Exception("Nie znaleziono parametru 'oauth_verifier' w odpowiedzi serwera USOS.");
                }
#endif

                // 3. Wymień Request Token na Access Token
                var (accessToken, accessTokenSecret) = await GetAccessTokenAsync(requestToken, requestTokenSecret, verifier);

                // 4. Zapisz Access Token
                await SetTokenAsync("usos_access_token", accessToken);
                await SetTokenAsync("usos_access_token_secret", accessTokenSecret);

                return (true, "");
            }
            catch (TaskCanceledException)
            {
                return (false, "Logowanie zostało anulowane.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UsosAuthService] Błąd uwierzytelniania: {ex.Message}");
                return (false, ex.Message);
            }
        }

        private Task<string> AuthenticateViaWebViewAsync(string authUrl, string callbackUrl)
        {
            var tcs = new TaskCompletionSource<string>();

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    var webView = new Microsoft.Maui.Controls.WebView
                    {
                        Source = authUrl,
                        VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Fill,
                        HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Fill
                    };

                    var grid = new Microsoft.Maui.Controls.Grid
                    {
                        RowDefinitions = 
                        { 
                            new Microsoft.Maui.Controls.RowDefinition { Height = Microsoft.Maui.GridLength.Auto }, 
                            new Microsoft.Maui.Controls.RowDefinition { Height = Microsoft.Maui.GridLength.Star } 
                        },
                        BackgroundColor = Microsoft.Maui.Graphics.Colors.White
                    };

                    var cancelButton = new Microsoft.Maui.Controls.Button 
                    { 
                        Text = "Anuluj logowanie", 
                        Margin = new Microsoft.Maui.Thickness(10), 
                        HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Start,
                        BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#e74c3c"),
                        TextColor = Microsoft.Maui.Graphics.Colors.White
                    };
                    
                    int isDismissed = 0;
                    async Task SafePopModalAsync()
                    {
                        if (Interlocked.Exchange(ref isDismissed, 1) == 0)
                        {
                            try
                            {
                                var nav = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
                                if (nav != null && nav.ModalStack.Count > 0)
                                {
                                    await nav.PopModalAsync();
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[UsosAuthService] Błąd zamykania modala: {ex.Message}");
                            }
                        }
                    }

                    cancelButton.Clicked += async (s, e) => 
                    {
                        try
                        {
                            await SafePopModalAsync();
                            tcs.TrySetException(new Exception("Logowanie anulowane przez użytkownika."));
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    };

                    grid.Children.Add(cancelButton);
                    Microsoft.Maui.Controls.Grid.SetRow(cancelButton, 0);

                    grid.Children.Add(webView);
                    Microsoft.Maui.Controls.Grid.SetRow(webView, 1);

                    var page = new Microsoft.Maui.Controls.ContentPage
                    {
                        Content = grid
                    };

                    webView.Navigating += async (s, e) =>
                    {
                        try
                        {
                            if (e.Url.StartsWith(callbackUrl))
                            {
                                e.Cancel = true;
                                await SafePopModalAsync();

                                try
                                {
                                    var uri = new Uri(e.Url);
                                    var query = HttpUtility.ParseQueryString(uri.Query);
                                    var oauthVerifier = query["oauth_verifier"];
                                    tcs.TrySetResult(oauthVerifier!);
                                }
                                catch (Exception ex)
                                {
                                    tcs.TrySetException(ex);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    };

                    await Application.Current!.Windows[0].Page!.Navigation.PushModalAsync(page);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            return tcs.Task;
        }

        private async Task<(string token, string secret)> GetRequestTokenAsync()
        {
            string url = $"{BaseUrl}/services/oauth/request_token";
            string scopes = "offline_access|studies|grades|cards";
            string header = GenerateOAuthHeader("POST", url, ConsumerKey, ConsumerSecret, null, null, CallbackUrl, null, scopes);
            
            var request = new HttpRequestMessage(HttpMethod.Post, $"{url}?scopes={Uri.EscapeDataString(scopes)}");
            request.Headers.Add("Authorization", header);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) {
                var err = await response.Content.ReadAsStringAsync();
                throw new Exception(err);
            }
            
            var content = await response.Content.ReadAsStringAsync();
            var parsed = HttpUtility.ParseQueryString(content);
            
            return (parsed["oauth_token"]!, parsed["oauth_token_secret"]!);
        }

        private async Task<(string token, string secret)> GetAccessTokenAsync(string requestToken, string requestTokenSecret, string verifier)
        {
            string url = $"{BaseUrl}/services/oauth/access_token";
            string header = GenerateOAuthHeader("POST", url, ConsumerKey, ConsumerSecret, requestToken, requestTokenSecret, null, verifier, null);
            
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", header);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) {
                var err = await response.Content.ReadAsStringAsync();
                throw new Exception(err);
            }
            
            var content = await response.Content.ReadAsStringAsync();
            var parsed = HttpUtility.ParseQueryString(content);
            
            return (parsed["oauth_token"]!, parsed["oauth_token_secret"]!);
        }

        public static string GenerateOAuthHeader(string method, string url, string consumerKey, string consumerSecret, string? token, string? tokenSecret, string? callback, string? verifier, string? scopes)
        {
            return OAuth1Helper.GenerateOAuthHeader(method, url, consumerKey, consumerSecret, token, tokenSecret, callback, verifier, scopes);
        }
    }
}
