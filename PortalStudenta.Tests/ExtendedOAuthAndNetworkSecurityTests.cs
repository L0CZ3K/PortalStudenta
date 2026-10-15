using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    /// <summary>
    /// Rozszerzone testy bezpieczeństwa protokołu sieciowego i autoryzacji OAuth 1.0a (RFC 5849).
    /// Weryfikuje ochronę przed atakami: Parameter Pollution, CRLF Injection, Replay, Secret Leakage.
    /// </summary>
    public class ExtendedOAuthAndNetworkSecurityTests
    {
        private const string TestKey = "ck_test_uniavio_key";
        private const string TestSecret = "cs_top_secret_key_12345!@#$%^&*()";
        private const string TestToken = "tok_access_student_abc";
        private const string TestTokenSecret = "ts_secret_tok_val_789";

        [Fact]
        public void SecretConfidentiality_SecretsAreNeverExposedInPlaintextHeader()
        {
            var header = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/users/user",
                TestKey,
                TestSecret,
                TestToken,
                TestTokenSecret);

            // Zgodnie z RFC 5849 sekrety są używane WYŁĄCZNIE do generowania sygnatury HMAC-SHA1
            // Nigdy nie wolno ich umieszczać w nagłówku Authorization!
            Assert.DoesNotContain(TestSecret, header);
            Assert.DoesNotContain(TestTokenSecret, header);
            Assert.DoesNotContain("top_secret", header);
            Assert.DoesNotContain("secret_tok", header);
        }

        [Fact]
        public void HttpParameterPollution_QueryStringCannotOverrideOAuthConsumerKey()
        {
            // Próba ataku Parameter Pollution: wstrzyknięcie fałszywego oauth_consumer_key w parametrach query URL
            string spoofedUrl = "https://usosapi.polsl.pl/services/users/user?oauth_consumer_key=hacked_attacker_key";

            var header = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                spoofedUrl,
                TestKey, // Prawdziwy klucz aplikacji
                TestSecret,
                TestToken,
                TestTokenSecret);

            // W nagłówku OAuth musi znajdować się autentyczny klucz aplikacji, a nie sfałszowany z query
            Assert.Contains($"oauth_consumer_key=\"{TestKey}\"", header);
            Assert.DoesNotContain("hacked_attacker_key", header);
        }

        [Theory]
        [InlineData("https://usosapi.polsl.pl:443/services/users/user")]
        [InlineData("https://usosapi.polsl.pl/services/users/user#anchor")]
        [InlineData("https://usosapi.polsl.pl/services/users/user?")]
        [InlineData("https://usosapi.polsl.pl/services/users/user?flag")]
        public void UrlNormalization_HandlesIrregularUrlsSafely(string irregularUrl)
        {
            var header = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                irregularUrl,
                TestKey,
                TestSecret,
                TestToken,
                TestTokenSecret);

            Assert.StartsWith("OAuth ", header);
            Assert.Contains("oauth_signature=", header);
            // Fragment URL nie może wyciekać do parametrów nagłówka
            Assert.DoesNotContain("#anchor", header);
        }

        [Fact]
        public void CRLFInjection_InParameters_IsSafelyEscapedByRfc3986()
        {
            // Próba ataku HTTP Response/Header Splitting za pomocą \r\n
            string crlfKey = "key\r\nX-Injected-Header: evil\r\n";
            string crlfToken = "token\r\nCookie: session=stolen";

            var header = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/users/user",
                crlfKey,
                TestSecret,
                crlfToken,
                TestTokenSecret);

            // Żaden literalny znak nowej linii (\r lub \n) nie ma prawa pojawić się w nagłówku!
            Assert.DoesNotContain("\r", header);
            Assert.DoesNotContain("\n", header);
            // Muszą zostać zakodowane procentowo (%0D%0A)
            Assert.Contains("%0D%0A", header);
        }

        [Fact]
        public void NonceGeneration_ConcurrentGeneration_ProducesZeroCollisions()
        {
            // Test generowania unikalnych identyfikatorów nonce w warunkach wielowątkowych
            var nonces = new ConcurrentBag<string>();

            Parallel.For(0, 1000, _ =>
            {
                var header = OAuth1Helper.GenerateOAuthHeader(
                    "GET",
                    "https://usosapi.polsl.pl/services/users/user",
                    TestKey,
                    TestSecret,
                    TestToken,
                    TestTokenSecret);

                // Ekstrakcja oauth_nonce
                int nonceIdx = header.IndexOf("oauth_nonce=\"", StringComparison.Ordinal);
                if (nonceIdx >= 0)
                {
                    int start = nonceIdx + "oauth_nonce=\"".Length;
                    int end = header.IndexOf('"', start);
                    nonces.Add(header[start..end]);
                }
            });

            Assert.Equal(1000, nonces.Count);
            // Wszystkie 1000 nonce muszą być unikalne (brak podatności na Replay Attack)
            Assert.Equal(1000, nonces.Distinct().Count());
        }

        [Fact]
        public void SignatureIntegrity_HttpMethodChange_InvalidatesSignature()
        {
            long fixedTimestamp = 1710000000;
            string fixedNonce = "fixed_nonce_anti_tamper";

            var getHeader = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/users/user",
                TestKey,
                TestSecret,
                TestToken,
                TestTokenSecret,
                customNonce: fixedNonce,
                customTimestamp: fixedTimestamp);

            var postHeader = OAuth1Helper.GenerateOAuthHeader(
                "POST",
                "https://usosapi.polsl.pl/services/users/user",
                TestKey,
                TestSecret,
                TestToken,
                TestTokenSecret,
                customNonce: fixedNonce,
                customTimestamp: fixedTimestamp);

            // Zmiana metody HTTP musi spowodować wygenerowanie całkowicie innej sygnatury!
            Assert.NotEqual(getHeader, postHeader);
        }

        [Fact]
        public void SignatureIntegrity_QueryParamOrderInUrl_DoesNotAffectCanonicalSignature()
        {
            // Zgodnie z RFC 5849 parametry w sygnaturze bazowej są sortowane leksykograficznie,
            // więc kolejność parametrów w query stringu URL nie może zmienić sygnatury.
            long fixedTimestamp = 1710000000;
            string fixedNonce = "fixed_nonce_123";

            var urlA = "https://usosapi.polsl.pl/services/tt/user?a=1&b=2";
            var urlB = "https://usosapi.polsl.pl/services/tt/user?b=2&a=1";

            var headerA = OAuth1Helper.GenerateOAuthHeader("GET", urlA, TestKey, TestSecret, TestToken, TestTokenSecret, customNonce: fixedNonce, customTimestamp: fixedTimestamp);
            var headerB = OAuth1Helper.GenerateOAuthHeader("GET", urlB, TestKey, TestSecret, TestToken, TestTokenSecret, customNonce: fixedNonce, customTimestamp: fixedTimestamp);

            Assert.Equal(headerA, headerB);
        }

        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("data:text/html,<script>alert(1)</script>")]
        [InlineData("file:///etc/passwd")]
        [InlineData("https://attacker-site.com/steal-token")]
        public void MaliciousCallback_IsEscapedAndSignedWithoutCorruptingOAuthHeader(string maliciousCallback)
        {
            var header = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/oauth/request_token",
                TestKey,
                TestSecret,
                token: null,
                tokenSecret: null,
                callback: maliciousCallback);

            Assert.StartsWith("OAuth ", header);
            Assert.Contains("oauth_callback=", header);
            // Dwukropki i slashe muszą być bezpiecznie zakodowane
            Assert.DoesNotContain("oauth_callback=\"javascript:", header);
            Assert.DoesNotContain("oauth_callback=\"data:", header);
            Assert.DoesNotContain("oauth_callback=\"file:", header);
        }
    }
}
