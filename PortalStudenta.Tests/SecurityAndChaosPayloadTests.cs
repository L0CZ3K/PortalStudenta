using System;
using System.Text.Json;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class SecurityAndChaosPayloadTests
    {
        [Theory]
        [InlineData("../../../../etc/passwd", "etc_passwd")]
        [InlineData("..\\..\\windows\\system32", "windows\\system32")]
        [InlineData("....//secret/token", "secret_token")]
        [InlineData("/var/log/uniavio.log", "var_log_uniavio_log")]
        public void PathTraversal_Payloads_SanitizedWithoutPathDelimiters(string maliciousInput, string expectedSubstr)
        {
            var sanitized = FirebaseSecurityHelper.SanitizeKey(maliciousInput);

            Assert.DoesNotContain("/", sanitized);
            Assert.DoesNotContain(".", sanitized);
            Assert.Contains(expectedSubstr, sanitized, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("users/.json?shallow=true")]
        [InlineData("groups/$priority")]
        [InlineData("items/#index")]
        [InlineData("node/[0]/value")]
        public void FirebaseInternalTokens_CannotBeInjectedIntoPaths(string payload)
        {
            var sanitized = FirebaseSecurityHelper.SanitizeKey(payload);

            Assert.DoesNotContain(".", sanitized);
            Assert.DoesNotContain("$", sanitized);
            Assert.DoesNotContain("#", sanitized);
            Assert.DoesNotContain("[", sanitized);
            Assert.DoesNotContain("]", sanitized);
            Assert.DoesNotContain("/", sanitized);
        }

        [Theory]
        [InlineData("<script>alert('xss')</script>")]
        [InlineData("<img src=x onerror=alert(1)>")]
        [InlineData("<svg/onload=eval(atob('ZG9jdW1lbnQ='))")]
        [InlineData("<iframe src=\"javascript:alert(1)\">")]
        public void XssPayloads_InTitlesOrKeys_SanitizedWithoutExecutableSlashesOrBrackets(string payload)
        {
            var lesson = new Lesson
            {
                Title = payload,
                Type = "WYK",
                Start = new DateTime(2026, 10, 15, 10, 0, 0)
            };

            var key = ScheduleLogic.GetLessonKey(lesson);

            Assert.DoesNotContain("/", key);
            Assert.DoesNotContain("<script>", key);
            Assert.DoesNotContain("</script>", key);
        }

        [Fact]
        public void NullByteInjection_DoesNotTruncateOrCrashKeyGenerator()
        {
            string payload = "admin_user\0_escalation_attempt";
            string sanitized = FirebaseSecurityHelper.SanitizeKey(payload);

            Assert.NotNull(sanitized);
            Assert.NotEmpty(sanitized);
            Assert.Contains("admin_user", sanitized, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("' OR '1'='1")]
        [InlineData("admin' --")]
        [InlineData("1; DROP TABLE Users; --")]
        [InlineData("{\"title\": {\"$ne\": null}}")]
        public void SqlAndNoSqlInjectionTokens_InKeys_RenderedSafeForFirebase(string payload)
        {
            string sanitized = FirebaseSecurityHelper.SanitizeKey(payload);

            // Slashes, brackets, dots are removed
            Assert.DoesNotContain(".", sanitized);
            Assert.DoesNotContain("[", sanitized);
            Assert.DoesNotContain("]", sanitized);
            Assert.DoesNotContain("/", sanitized);
            Assert.DoesNotContain("$", sanitized);
        }

        [Fact]
        public void UnicodeRtlOverrideAndZeroWidthCharacters_HandledWithoutCrashing()
        {
            // \u202E is Right-to-Left Override, \u200B is zero-width space
            string trickyString = "Normal\u202E\u200BReversed";

            var lesson = new Lesson
            {
                Title = trickyString,
                Start = new DateTime(2026, 10, 15, 10, 0, 0)
            };

            var key = ScheduleLogic.GetLessonKey(lesson);

            Assert.NotNull(key);
            Assert.NotEmpty(key);
        }

        [Fact]
        public void MalformedJson_DeeplyNestedOrBroken_HandlesGracefully()
        {
            string deeplyNested = new string('[', 200) + new string(']', 100); // Mismatched brackets

            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<List<Lesson>>(deeplyNested));
        }
    }
}
