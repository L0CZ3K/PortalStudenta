using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class FirebaseSecurityTests
    {
        [Theory]
        [InlineData(null, "EMPTY")]
        [InlineData("", "EMPTY")]
        [InlineData("   ", "EMPTY")]
        [InlineData("\t\n", "EMPTY")]
        public void SanitizeKey_NullOrEmptyOrWhitespace_ReturnsEMPTY(string? input, string expected)
        {
            var result = FirebaseSecurityHelper.SanitizeKey(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("12345", "12345")]
        [InlineData("album_studenta", "album_studenta")]
        [InlineData("GROUP123", "GROUP123")]
        public void SanitizeKey_ValidKeyWithoutSpecialChars_RemainsIntact(string input, string expected)
        {
            var result = FirebaseSecurityHelper.SanitizeKey(input);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void SanitizeKey_ReplacesAllFirebaseForbiddenCharacters()
        {
            // Firebase zabrania znaków: '.', '$', '#', '[', ']', '/', '|' oraz spacji
            string dangerousKey = "user.1$2#3[A]B/C|D E";
            string expectedKey = "user_1_2_3_A_B_C_D_E";

            var result = FirebaseSecurityHelper.SanitizeKey(dangerousKey);

            Assert.Equal(expectedKey, result);
            Assert.DoesNotContain(".", result);
            Assert.DoesNotContain("$", result);
            Assert.DoesNotContain("#", result);
            Assert.DoesNotContain("[", result);
            Assert.DoesNotContain("]", result);
            Assert.DoesNotContain("/", result);
            Assert.DoesNotContain("|", result);
            Assert.DoesNotContain(" ", result);
        }

        [Fact]
        public void SanitizeKey_UsosCourseCodesWithDotsAndSlashes_SanitizesSafely()
        {
            string usosCourse = "1000-213B-ANG.1/WZ";
            var result = FirebaseSecurityHelper.SanitizeKey(usosCourse);

            Assert.Equal("1000-213B-ANG_1_WZ", result);
        }

        [Fact]
        public void SanitizeKey_PreservesPolishCharacters()
        {
            string polishSubject = "Inżynieria_Oprogramowania_Ćwiczenia";
            var result = FirebaseSecurityHelper.SanitizeKey(polishSubject);

            Assert.Equal("Inżynieria_Oprogramowania_Ćwiczenia", result);
        }

        [Fact]
        public void SanitizeKey_TrimsSurroundingWhitespace()
        {
            string input = "   123456   ";
            var result = FirebaseSecurityHelper.SanitizeKey(input);

            Assert.Equal("123456", result);
        }

        // --- TESTY WALIDACJI KODU GRUPY (IsValidGroupCode) ---

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("1234")]          // Za krótki (< 5 znaków)
        [InlineData("ABCD")]          // Za krótki (< 5 znaków)
        [InlineData("ABCDEFGHIJKLM")] // Za długi (> 12 znaków)
        [InlineData("GROUP-1")]       // Myślnik jest zabroniony
        [InlineData("GROUP 1")]       // Spacja jest zabroniona
        [InlineData("GROUP_1")]       // Podkreślnik jest zabroniony w kodzie grupy
        [InlineData("GROUP.1")]       // Kropka jest zabroniona
        [InlineData("GRP#99")]        // Hasztag zabroniony
        [InlineData("GRP$99")]        // Dolar zabroniony
        public void IsValidGroupCode_InvalidInputs_ReturnsFalse(string? code)
        {
            var isValid = FirebaseSecurityHelper.IsValidGroupCode(code);
            Assert.False(isValid);
        }

        [Theory]
        [InlineData("ABCDE")]        // Minimalna długość (5)
        [InlineData("12345")]        // Cyfry (5)
        [InlineData("GRUPA1")]       // 6 znaków
        [InlineData("grupa1")]       // Małe litery są akceptowane (case-insensitive)
        [InlineData("TESTGROUP12")]  // 11 znaków
        [InlineData("1234567890AB")] // Maksymalna długość (12)
        public void IsValidGroupCode_ValidAlphanumericCodes_ReturnsTrue(string code)
        {
            var isValid = FirebaseSecurityHelper.IsValidGroupCode(code);
            Assert.True(isValid);
        }

        // --- TESTY GENEROWANIA LOSOWEGO KODU GRUPY (GenerateRandomGroupCode) ---

        [Theory]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(8)]
        [InlineData(12)]
        public void GenerateRandomGroupCode_SpecifiedValidLengths_GeneratesValidCodes(int length)
        {
            var code = FirebaseSecurityHelper.GenerateRandomGroupCode(length);

            Assert.Equal(length, code.Length);
            Assert.True(FirebaseSecurityHelper.IsValidGroupCode(code));
        }

        [Theory]
        [InlineData(0, 5)]    // 0 ograniczane do minimum 5
        [InlineData(-10, 5)]  // ujemne ograniczane do 5
        [InlineData(3, 5)]    // < 5 ograniczane do 5
        [InlineData(20, 12)]  // > 12 ograniczane do 12
        [InlineData(100, 12)] // > 12 ograniczane do 12
        public void GenerateRandomGroupCode_OutOfBoundsLength_ClampsSafely(int requestedLength, int expectedLength)
        {
            var code = FirebaseSecurityHelper.GenerateRandomGroupCode(requestedLength);

            Assert.Equal(expectedLength, code.Length);
            Assert.True(FirebaseSecurityHelper.IsValidGroupCode(code));
        }

        [Fact]
        public void GenerateRandomGroupCode_MultipleGenerations_ProducesDistinctCodes()
        {
            var codes = new HashSet<string>();
            for (int i = 0; i < 50; i++)
            {
                codes.Add(FirebaseSecurityHelper.GenerateRandomGroupCode(8));
            }

            // Prawdopodobieństwo kolizji przy 8-znakowych kodach z 36-znakowego alfabetu jest astronomicznie małe
            Assert.True(codes.Count >= 49, "Generowane kody grup powinny mieć wysoką entropię i brak kolizji.");
        }

        [Fact]
        public void DatabaseRulesJson_ExistsAndHasValidJsonSyntax()
        {
            // Find database.rules.json relative to tests directory or repository root
            string path = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "../../../../database.rules.json");
            if (!System.IO.File.Exists(path))
            {
                path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "../../../database.rules.json");
            }
            if (!System.IO.File.Exists(path))
            {
                path = "database.rules.json";
            }

            Assert.True(System.IO.File.Exists(path), $"Plik reguł bazy danych powinien istnieć pod ścieżką: {path}");

            string content = System.IO.File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            Assert.True(doc.RootElement.TryGetProperty("rules", out var rulesEl));
            Assert.True(rulesEl.TryGetProperty("users", out _));
            Assert.True(rulesEl.TryGetProperty("groups", out _));
            Assert.True(rulesEl.TryGetProperty("deleted_accounts", out _));
            Assert.True(rulesEl.TryGetProperty("system_announcements", out var sysEl));

            // System announcements must not be writable by regular clients
            Assert.False(sysEl.GetProperty(".write").GetBoolean());
        }

        [Fact]
        public void DatabaseRulesJson_PollsRule_EnforcesClosedPollWriteProtection()
        {
            string path = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "../../../../database.rules.json");
            if (!System.IO.File.Exists(path))
            {
                path = "database.rules.json";
            }
            if (!System.IO.File.Exists(path)) return;

            string content = System.IO.File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var pollVoteWrite = doc.RootElement
                .GetProperty("rules")
                .GetProperty("groups")
                .GetProperty("$groupCode")
                .GetProperty("polls")
                .GetProperty("$pollId")
                .GetProperty("Votes")
                .GetProperty("$albumNumber")
                .GetProperty(".write")
                .GetString();

            Assert.NotNull(pollVoteWrite);
            Assert.Contains("IsClosed", pollVoteWrite);
            Assert.Contains("root.child('groups')", pollVoteWrite);
        }
    }
}
