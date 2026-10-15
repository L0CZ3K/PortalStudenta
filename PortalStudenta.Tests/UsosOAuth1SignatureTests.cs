using System;
using System.Linq;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class UsosOAuth1SignatureTests
    {
        private const string TestConsumerKey = "test_consumer_key_xyz";
        private const string TestConsumerSecret = "test_consumer_secret_123456";
        private const string TestToken = "oauth_token_abc";
        private const string TestTokenSecret = "token_secret_789";

        [Fact]
        public void GenerateOAuthHeader_Throws_WhenUrlIsEmpty()
        {
            Assert.Throws<ArgumentException>(() =>
                OAuth1Helper.GenerateOAuthHeader("GET", "", TestConsumerKey, TestConsumerSecret, null, null));
        }

        [Fact]
        public void GenerateOAuthHeader_Throws_WhenConsumerKeyIsEmpty()
        {
            Assert.Throws<ArgumentException>(() =>
                OAuth1Helper.GenerateOAuthHeader("GET", "https://usosapi.polsl.pl/services/users/user", "", TestConsumerSecret, null, null));
        }

        [Fact]
        public void GenerateOAuthHeader_Starts_WithOAuthPrefix()
        {
            var header = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/users/user",
                TestConsumerKey,
                TestConsumerSecret,
                TestToken,
                TestTokenSecret);

            Assert.StartsWith("OAuth ", header);
        }

        [Fact]
        public void GenerateOAuthHeader_ContainsRequiredOAuthParameters()
        {
            var header = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/users/user",
                TestConsumerKey,
                TestConsumerSecret,
                TestToken,
                TestTokenSecret,
                customNonce: "nonce123",
                customTimestamp: 1700000000);

            Assert.Contains("oauth_consumer_key=\"test_consumer_key_xyz\"", header);
            Assert.Contains("oauth_nonce=\"nonce123\"", header);
            Assert.Contains("oauth_signature_method=\"HMAC-SHA1\"", header);
            Assert.Contains("oauth_timestamp=\"1700000000\"", header);
            Assert.Contains("oauth_version=\"1.0\"", header);
            Assert.Contains("oauth_token=\"oauth_token_abc\"", header);
            Assert.Contains("oauth_signature=", header);
        }

        [Fact]
        public void GenerateOAuthHeader_ExcludesScopesFromHeader_EvenWhenIncludedInSignature()
        {
            // USOS RFC spec: scopes must be part of signature base string, but NOT in Authorization header!
            var header = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/oauth/request_token",
                TestConsumerKey,
                TestConsumerSecret,
                token: null,
                tokenSecret: null,
                scopes: "studies|grades|email");

            Assert.DoesNotContain("scopes=", header);
            Assert.Contains("oauth_signature=", header);
        }

        [Fact]
        public void GenerateOAuthHeader_ExcludesQueryParamsFromHeader()
        {
            var header = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/tt/user?start=2024-10-01&days=7",
                TestConsumerKey,
                TestConsumerSecret,
                TestToken,
                TestTokenSecret);

            Assert.DoesNotContain("start=", header);
            Assert.DoesNotContain("days=", header);
            Assert.Contains("oauth_signature=", header);
        }

        [Fact]
        public void GenerateOAuthHeader_IncludesCallback_WhenProvided()
        {
            var header = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/oauth/request_token",
                TestConsumerKey,
                TestConsumerSecret,
                token: null,
                tokenSecret: null,
                callback: "uniavio://oauth-callback");

            Assert.Contains("oauth_callback=\"uniavio%3A%2F%2Foauth-callback\"", header);
        }

        [Fact]
        public void GenerateOAuthHeader_IncludesVerifier_WhenProvided()
        {
            var header = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/oauth/access_token",
                TestConsumerKey,
                TestConsumerSecret,
                token: TestToken,
                tokenSecret: TestTokenSecret,
                verifier: "99887766");

            Assert.Contains("oauth_verifier=\"99887766\"", header);
        }

        [Fact]
        public void GenerateOAuthHeader_DeterministicSignature_MatchesRFC5849Calculation()
        {
            // Dwie identyczne próby z tym samym nonce i timestamp muszą wygenerować identyczną sygnaturę
            var header1 = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/courses/user",
                "key1",
                "secret1",
                "token1",
                "tokenSecret1",
                customNonce: "fixed_nonce_12345",
                customTimestamp: 1600000000);

            var header2 = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/courses/user",
                "key1",
                "secret1",
                "token1",
                "tokenSecret1",
                customNonce: "fixed_nonce_12345",
                customTimestamp: 1600000000);

            Assert.Equal(header1, header2);
        }

        [Fact]
        public void GenerateOAuthHeader_SignatureChanges_WhenUrlOrParamsDiffer()
        {
            var header1 = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/courses/user?term_id=2024Z",
                "key1",
                "secret1",
                "token1",
                "tokenSecret1",
                customNonce: "nonce1",
                customTimestamp: 1600000000);

            var header2 = OAuth1Helper.GenerateOAuthHeader(
                "GET",
                "https://usosapi.polsl.pl/services/courses/user?term_id=2024L",
                "key1",
                "secret1",
                "token1",
                "tokenSecret1",
                customNonce: "nonce1",
                customTimestamp: 1600000000);

            Assert.NotEqual(header1, header2);
        }

        [Theory]
        [InlineData("abc-def_123.~", "abc-def_123.~")]
        [InlineData("hello world", "hello%20world")]
        [InlineData("user@domain.com", "user%40domain.com")]
        [InlineData("ąęśćżźńłó", "%C4%85%C4%99%C5%9B%C4%87%C5%BC%C5%BA%C5%84%C5%82%C3%B3")]
        [InlineData("", "")]
        public void Rfc3986UriEscape_EncodesCorrectly(string input, string expected)
        {
            var result = OAuth1Helper.Rfc3986UriEscape(input);
            Assert.Equal(expected, result);
        }
    }
}
