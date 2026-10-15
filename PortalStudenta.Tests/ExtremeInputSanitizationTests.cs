using System;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class ExtremeInputSanitizationTests
    {
        [Fact]
        public void GetLessonKey_WithEmojisInTitle_ProducesValidSanitizedKey()
        {
            var lesson = new Lesson
            {
                Title = "🚀 Hackathon AI 🤖 & 📚 Kolokwium",
                Type = "LAB",
                Group = "1",
                Start = new DateTime(2026, 10, 15, 10, 0, 0)
            };

            var key = ScheduleLogic.GetLessonKey(lesson);

            Assert.NotNull(key);
            Assert.NotEmpty(key);
            // Firebase illegal characters must NEVER appear in keys
            Assert.DoesNotContain(".", key);
            Assert.DoesNotContain("$", key);
            Assert.DoesNotContain("#", key);
            Assert.DoesNotContain("[", key);
            Assert.DoesNotContain("]", key);
            Assert.DoesNotContain("/", key);
        }

        [Fact]
        public void GetLessonKey_WithPolishDiacritics_PreservesCharactersWithoutForbiddenTokens()
        {
            var lesson = new Lesson
            {
                Title = "Zażółć gęślą jaźń / Środa / Łódź",
                Type = "WYK",
                Group = "GR1",
                Start = new DateTime(2026, 10, 15, 8, 0, 0)
            };

            var key = ScheduleLogic.GetLessonKey(lesson);

            Assert.DoesNotContain("/", key);
            Assert.Contains("ŚRODA", key);
            Assert.Contains("ŁÓDŹ", key);
        }

        [Fact]
        public void GetLessonKey_WithExtremelyLongTitle_GeneratesKeyWithoutCrashing()
        {
            string hugeTitle = new string('A', 10000);
            var lesson = new Lesson
            {
                Title = hugeTitle,
                Type = "CW",
                Start = new DateTime(2026, 10, 15, 12, 0, 0)
            };

            var key = ScheduleLogic.GetLessonKey(lesson);

            Assert.NotNull(key);
            Assert.StartsWith("A", key);
        }

        [Theory]
        [InlineData("Course.With.Dots", "COURSE_WITH_DOTS")]
        [InlineData("Group$With$Dollar", "GROUP_WITH_DOLLAR")]
        [InlineData("Tag#With#Hash", "TAG_WITH_HASH")]
        [InlineData("Array[Index]", "ARRAY_INDEX_")]
        [InlineData("Path/With/Slash", "PATH_WITH_SLASH")]
        [InlineData("All.$#[.]/Together", "ALL_______TOGETHER")]
        public void SanitizeKey_StripsAllIllegalFirebaseCharacters(string input, string expectedSanitizedSubstr)
        {
            var sanitized = FirebaseSecurityHelper.SanitizeKey(input);

            Assert.DoesNotContain(".", sanitized);
            Assert.DoesNotContain("$", sanitized);
            Assert.DoesNotContain("#", sanitized);
            Assert.DoesNotContain("[", sanitized);
            Assert.DoesNotContain("]", sanitized);
            Assert.DoesNotContain("/", sanitized);
            Assert.Contains(expectedSanitizedSubstr.ToUpperInvariant(), sanitized.ToUpperInvariant());
        }

        [Fact]
        public void GetLessonKey_WithNullAndWhitespaceProperties_ProducesSafeUntitledKey()
        {
            var lesson = new Lesson
            {
                Title = "   ",
                Type = "",
                Group = "   ",
                Start = new DateTime(2026, 10, 15, 10, 0, 0)
            };

            var key = ScheduleLogic.GetLessonKey(lesson);

            Assert.StartsWith("UNTITLED_EVENT_202610151000_ALL", key);
        }

        [Fact]
        public void SanitizeKey_HtmlScriptTags_SanitizesSafely()
        {
            string injectionPayload = "<script>alert('XSS');</script>";
            var sanitized = FirebaseSecurityHelper.SanitizeKey(injectionPayload);

            // Slash '/' is stripped
            Assert.DoesNotContain("/", sanitized);
            Assert.DoesNotContain("</script>", sanitized);
        }
    }
}
