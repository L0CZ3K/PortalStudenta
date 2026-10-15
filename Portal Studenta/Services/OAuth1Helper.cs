using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace Portal_Studenta.Services
{
    /// <summary>
    /// Logika generowania nagłówków i sygnatur OAuth 1.0a zgodna ze standardem RFC 5849 i API USOS.
    /// W pełni testowalna jednostkowo (brak zależności od platformy .NET MAUI).
    /// </summary>
    public static class OAuth1Helper
    {
        public static string GenerateOAuthHeader(
            string method, 
            string url, 
            string consumerKey, 
            string consumerSecret, 
            string? token, 
            string? tokenSecret, 
            string? callback = null, 
            string? verifier = null, 
            string? scopes = null,
            string? customNonce = null,
            long? customTimestamp = null)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("URL cannot be empty.", nameof(url));
            if (string.IsNullOrWhiteSpace(consumerKey)) throw new ArgumentException("ConsumerKey cannot be empty.", nameof(consumerKey));

            var uri = new Uri(url);
            var baseUrl = uri.GetLeftPart(UriPartial.Path);

            var nonce = !string.IsNullOrEmpty(customNonce) ? customNonce : Guid.NewGuid().ToString("N");
            var timestamp = customTimestamp.HasValue ? customTimestamp.Value.ToString() : DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            var parameters = new Dictionary<string, string>
            {
                { "oauth_consumer_key", consumerKey },
                { "oauth_nonce", nonce },
                { "oauth_signature_method", "HMAC-SHA1" },
                { "oauth_timestamp", timestamp },
                { "oauth_version", "1.0" }
            };

            if (!string.IsNullOrEmpty(token)) parameters.Add("oauth_token", token);
            if (!string.IsNullOrEmpty(callback)) parameters.Add("oauth_callback", callback);
            if (!string.IsNullOrEmpty(verifier)) parameters.Add("oauth_verifier", verifier);
            if (!string.IsNullOrEmpty(scopes)) parameters.Add("scopes", scopes);

            // Dodaj parametry query string do bazy sygnatury
            var query = HttpUtility.ParseQueryString(uri.Query);
            var addedQueryKeys = new List<string>();
            foreach (string? key in query.AllKeys)
            {
                if (!string.IsNullOrEmpty(key) && !parameters.ContainsKey(key))
                {
                    parameters.Add(key, query[key]!);
                    addedQueryKeys.Add(key);
                }
            }

            var sortedParams = parameters.OrderBy(p => p.Key, StringComparer.Ordinal)
                                         .ThenBy(p => p.Value, StringComparer.Ordinal)
                                         .Select(p => $"{Rfc3986UriEscape(p.Key)}={Rfc3986UriEscape(p.Value)}");
            var paramString = string.Join("&", sortedParams);

            var baseString = $"{method.ToUpperInvariant()}&{Rfc3986UriEscape(baseUrl)}&{Rfc3986UriEscape(paramString)}";
            var keyString = $"{Rfc3986UriEscape(consumerSecret)}&{(string.IsNullOrEmpty(tokenSecret) ? "" : Rfc3986UriEscape(tokenSecret))}";

            using var hmac = new HMACSHA1(Encoding.ASCII.GetBytes(keyString));
            var hash = hmac.ComputeHash(Encoding.ASCII.GetBytes(baseString));
            var signature = Convert.ToBase64String(hash);

            // Scopes jest częścią sygnatury, ale NIE jest częścią nagłówka Authorization OAuth
            parameters.Remove("scopes");

            // Tylko parametry faktycznie dodane z query string należą do URL, a nie do nagłówka
            foreach (string key in addedQueryKeys)
            {
                parameters.Remove(key);
            }

            parameters.Add("oauth_signature", signature);

            var headerValue = "OAuth " + string.Join(", ", parameters.Select(p => $"{Rfc3986UriEscape(p.Key)}=\"{Rfc3986UriEscape(p.Value)}\""));
            return headerValue;
        }

        public static string Rfc3986UriEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return Uri.EscapeDataString(value);
        }
    }
}
