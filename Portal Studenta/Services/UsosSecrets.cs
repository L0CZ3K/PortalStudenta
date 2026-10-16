using System;
using System.Security.Cryptography;
using System.Text;

namespace Portal_Studenta.Services
{
    public static partial class UsosSecrets
    {
        private static byte[]? _obfuscatedKey;
        private static byte[]? _obfuscatedSecret;
        private static byte[]? _xorMask;

        private static string? _fallbackKey = "DUMMY_CONSUMER_KEY";
        private static string? _fallbackSecret = "DUMMY_CONSUMER_SECRET";

        /// <summary>
        /// Opcjonalny adres serwera pośredniczącego (Serverless Proxy / Cloudflare Worker) dla OAuth 1.0a.
        /// Jeśli pusty, aplikacja podpisuje żądania lokalnie z użyciem zaciemnionych kluczy.
        /// </summary>
        public static string OAuthProxyBaseUrl { get; set; } = string.Empty;

        public static string ConsumerKey
        {
            get => _obfuscatedKey != null && _xorMask != null
                ? Deobfuscate(_obfuscatedKey, _xorMask)
                : (_fallbackKey ?? string.Empty);
            set
            {
                _fallbackKey = value;
                _obfuscatedKey = null;
            }
        }

        public static string ConsumerSecret
        {
            get => _obfuscatedSecret != null && _xorMask != null
                ? Deobfuscate(_obfuscatedSecret, _xorMask)
                : (_fallbackSecret ?? string.Empty);
            set
            {
                _fallbackSecret = value;
                _obfuscatedSecret = null;
            }
        }

        static UsosSecrets()
        {
            LoadLocalSecrets();
        }

        static partial void LoadLocalSecrets();

        public static void SetObfuscatedSecrets(byte[] obfuscatedKey, byte[] obfuscatedSecret, byte[] xorMask)
        {
            _obfuscatedKey = obfuscatedKey;
            _obfuscatedSecret = obfuscatedSecret;
            _xorMask = xorMask;
            _fallbackKey = null;
            _fallbackSecret = null;
        }

        private static string Deobfuscate(ReadOnlySpan<byte> data, ReadOnlySpan<byte> mask)
        {
            if (data.IsEmpty || mask.IsEmpty) return string.Empty;

            Span<byte> buffer = data.Length <= 256 ? stackalloc byte[data.Length] : new byte[data.Length];
            try
            {
                for (int i = 0; i < data.Length; i++)
                {
                    buffer[i] = (byte)(data[i] ^ mask[i % mask.Length]);
                }
                return Encoding.UTF8.GetString(buffer);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }
    }
}

