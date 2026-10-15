using System;
using System.Collections.Generic;
using System.Text.Json;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class UsosDataParsingAndCacheTests
    {
        [Theory]
        [InlineData("Wykład", "WYK")]
        [InlineData("Wykład ogólnouczelniany", "WYK")]
        [InlineData("Lecture", "WYK")]
        [InlineData("Ćwiczenia", "CW")]
        [InlineData("Ćwiczenia audytoryjne", "CW")]
        [InlineData("Exercise", "CW")]
        [InlineData("Laboratorium", "LAB")]
        [InlineData("Laboratorium komputerowe", "LAB")]
        [InlineData("Lab session", "LAB")]
        [InlineData("Projekt", "PROJ")]
        [InlineData("Projekt zespołowy", "PROJ")]
        [InlineData("Project", "PROJ")]
        [InlineData("Lektorat", "LEKT")]
        [InlineData("Lektorat j. angielskiego", "LEKT")]
        [InlineData("Foreign language", "LEKT")]
        [InlineData("Seminarium magisterskie", "Seminarium magisterskie")]
        [InlineData("", "INNE")]
        [InlineData("   ", "INNE")]
        [InlineData(null, "INNE")]
        public void ParseClassType_MapsToStandardAbreviations(string? input, string expected)
        {
            Assert.Equal(expected, ScheduleLogic.ParseClassType(input));
        }

        [Fact]
        public void LessonSerialization_RoundtripPreservesAllFields()
        {
            var original = new Lesson
            {
                Id = 12345,
                Title = "Programowanie Współbieżne",
                Type = "LAB",
                Place = "Sala 301 (CEK)",
                Person = "dr hab. inż. Jan Kowalski",
                Group = "4B",
                Start = new DateTime(2026, 10, 15, 12, 15, 0),
                End = new DateTime(2026, 10, 15, 13, 45, 0),
                OriginalStart = new DateTime(2026, 10, 15, 10, 15, 0),
                OriginalEnd = new DateTime(2026, 10, 15, 11, 45, 0),
                OriginalPlace = "Sala 100",
                IsCanceled = true,
                HasExam = true,
                HasTask = true,
                TaskDetails = "Projekt końcowy w C#",
                Desc = "Zajęcia odwołane przez dziekana",
                CourseId = "INF-301",
                CourseUnitId = "U-88"
            };

            var json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<Lesson>(json);

            Assert.NotNull(deserialized);
            Assert.Equal(original.Id, deserialized.Id);
            Assert.Equal(original.Title, deserialized.Title);
            Assert.Equal(original.Type, deserialized.Type);
            Assert.Equal(original.Place, deserialized.Place);
            Assert.Equal(original.Person, deserialized.Person);
            Assert.Equal(original.Group, deserialized.Group);
            Assert.Equal(original.Start, deserialized.Start);
            Assert.Equal(original.End, deserialized.End);
            Assert.Equal(original.OriginalStart, deserialized.OriginalStart);
            Assert.Equal(original.OriginalEnd, deserialized.OriginalEnd);
            Assert.Equal(original.OriginalPlace, deserialized.OriginalPlace);
            Assert.Equal(original.IsCanceled, deserialized.IsCanceled);
            Assert.Equal(original.HasExam, deserialized.HasExam);
            Assert.Equal(original.HasTask, deserialized.HasTask);
            Assert.Equal(original.TaskDetails, deserialized.TaskDetails);
            Assert.Equal(original.Desc, deserialized.Desc);
            Assert.Equal(original.CourseId, deserialized.CourseId);
            Assert.Equal(original.CourseUnitId, deserialized.CourseUnitId);
        }

        [Fact]
        public void LessonModificationSerialization_RoundtripPreservesAllFields()
        {
            var original = new LessonModification
            {
                LessonKey = "TEST_LAB_202610151215_4B",
                Title = "Programowanie",
                OriginalStart = new DateTime(2026, 10, 15, 12, 15, 0),
                ModifiedStart = new DateTime(2026, 10, 15, 14, 0, 0),
                ModifiedEnd = new DateTime(2026, 10, 15, 15, 30, 0),
                ModifiedPlace = "Sala 500",
                IsCanceled = false,
                HasExam = true,
                HasTask = false,
                TaskDetails = "",
                CancelReason = "",
                IsGroupModification = true,
                GroupCode = "GRP-999"
            };

            var json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<LessonModification>(json);

            Assert.NotNull(deserialized);
            Assert.Equal(original.LessonKey, deserialized.LessonKey);
            Assert.Equal(original.Title, deserialized.Title);
            Assert.Equal(original.OriginalStart, deserialized.OriginalStart);
            Assert.Equal(original.ModifiedStart, deserialized.ModifiedStart);
            Assert.Equal(original.ModifiedEnd, deserialized.ModifiedEnd);
            Assert.Equal(original.ModifiedPlace, deserialized.ModifiedPlace);
            Assert.Equal(original.IsGroupModification, deserialized.IsGroupModification);
            Assert.Equal(original.GroupCode, deserialized.GroupCode);
            Assert.True(deserialized.HasExam);
        }

        [Fact]
        public void CacheDeserialization_CorruptOrTruncatedJson_ThrowsJsonException()
        {
            string truncatedJson = "[{\"Id\": 1, \"Title\": \"Programowanie\", \"Start\": \"2026-10-15T12:00:00\"";

            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<List<Lesson>>(truncatedJson));
        }

        [Fact]
        public void CacheDeserialization_ValidJsonWithExtraUnknownFields_DeserializesSafely()
        {
            string futureJson = "[{\"Id\": 1, \"Title\": \"Math\", \"UnknownFutureProp\": 12345, \"Start\": \"2026-10-15T10:00:00\"}]";

            var lessons = JsonSerializer.Deserialize<List<Lesson>>(futureJson);

            Assert.NotNull(lessons);
            Assert.Single(lessons);
            Assert.Equal("Math", lessons[0].Title);
        }

        [Fact]
        public void CacheDeserialization_EmptyArray_ReturnsEmptyList()
        {
            string json = "[]";
            var lessons = JsonSerializer.Deserialize<List<Lesson>>(json);

            Assert.NotNull(lessons);
            Assert.Empty(lessons);
        }

        [Fact]
        public void IsCacheFresh_WhenLessonCountZero_ReturnsFalse()
        {
            var now = DateTime.UtcNow;
            bool fresh = ScheduleLogic.IsCacheFresh(lessonCount: 0, lastFetchTimeUtc: now, nowUtc: now, validityMinutes: 60);
            Assert.False(fresh, "Cache with 0 lessons must not be considered fresh so that app fetches data from USOS");
        }

        [Fact]
        public void IsCacheFresh_WhenValidLessonCountAndRecentFetch_ReturnsTrue()
        {
            var now = DateTime.UtcNow;
            var recentFetch = now.AddMinutes(-15);
            bool fresh = ScheduleLogic.IsCacheFresh(lessonCount: 5, lastFetchTimeUtc: recentFetch, nowUtc: now, validityMinutes: 60);
            Assert.True(fresh);
        }

        [Fact]
        public void IsCacheFresh_WhenFetchOlderThanValidityWindow_ReturnsFalse()
        {
            var now = DateTime.UtcNow;
            var oldFetch = now.AddMinutes(-65);
            bool fresh = ScheduleLogic.IsCacheFresh(lessonCount: 5, lastFetchTimeUtc: oldFetch, nowUtc: now, validityMinutes: 60);
            Assert.False(fresh);
        }

        [Fact]
        public void IsTombstoneObsolete_WhenDeletedBeforeSessionStart_ReturnsTrue()
        {
            var deletedAt = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
            var sessionStarted = new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc);
            Assert.True(ScheduleLogic.IsTombstoneObsolete(deletedAt, sessionStarted));
        }

        [Fact]
        public void IsTombstoneObsolete_WhenDeletedAfterSessionStart_ReturnsFalse()
        {
            var sessionStarted = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
            var deletedAt = new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc);
            Assert.False(ScheduleLogic.IsTombstoneObsolete(deletedAt, sessionStarted));
        }

        [Fact]
        public void IsTombstoneObsolete_WhenNullArguments_ReturnsFalse()
        {
            Assert.False(ScheduleLogic.IsTombstoneObsolete(null, DateTime.UtcNow));
            Assert.False(ScheduleLogic.IsTombstoneObsolete(DateTime.UtcNow, null));
            Assert.False(ScheduleLogic.IsTombstoneObsolete(null, null));
        }

        [Theory]
        [InlineData("USOS API Error: InternalServerError - {\"message\": \"Internal USOS API error has occured. We're working on fixing this!\"}", "pl", "Tymczasowy błąd serwera uczelnianego USOS (500). Spróbuj ponownie za chwilę.")]
        [InlineData("USOS API Error: InternalServerError - {\"message\": \"Internal USOS API error has occured. We're working on fixing this!\"}", "en-US", "Temporary USOS server error (500). University server reported an internal issue. Please try again shortly.")]
        [InlineData("401 Unauthorized", "pl", "Sesja USOS wygasła. Zaloguj się ponownie.")]
        [InlineData("401 Unauthorized", "en-US", "USOS session expired. Please sign in again.")]
        [InlineData("The operation has timed out.", "pl", "Przekroczono czas oczekiwania na odpowiedź serwera USOS. Sprawdź połączenie z siecią.")]
        [InlineData("The operation has timed out.", "en-US", "USOS server connection timed out. Check your internet connection.")]
        [InlineData("{\"message\": \"Invalid start parameter\"}", "pl", "Invalid start parameter")]
        [InlineData("", "pl", "Nieznany błąd połączenia z USOS.")]
        public void FormatUsosErrorMessage_ReturnsHumanReadableMessage(string rawError, string lang, string expected)
        {
            var result = ScheduleLogic.FormatUsosErrorMessage(rawError, lang);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Sala 825", "825")]
        [InlineData("Sala aula e (bud. A)", "aula e")]
        [InlineData("Sala 687", "687")]
        [InlineData("Sala 621a (Wydział Informatyki)", "621a")]
        [InlineData("Brak sali", "")]
        [InlineData(null, "")]
        [InlineData("", "")]
        public void GetCompactRoom_ExtractsConciseRoom(string? place, string expected)
        {
            var result = ScheduleLogic.GetCompactRoom(place);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("WYK", false, false, "#1c3d73", "#3b82f6")]
        [InlineData("LAB", false, false, "#1b4d24", "#22c55e")]
        [InlineData("CW", false, false, "#24522a", "#4ade80")]
        [InlineData("LEKT", false, false, "#854d0e", "#f59e0b")]
        [InlineData("PROJ", false, false, "#7c2d12", "#fb923c")]
        [InlineData("STAR", false, false, "#581c87", "#c084fc")]
        [InlineData("WŁASNE", false, false, "#1e3a8a", "#60a5fa")]
        [InlineData("WYK", true, false, "#7f1d1d", "#ef4444")]
        public void GetCalendarEventColor_ReturnsCorrectStyle(string type, bool isHighPriority, bool isPersonal, string expBg, string expBorder)
        {
            var (bg, border, badgeBg) = ScheduleLogic.GetCalendarEventColor(type, isHighPriority, isPersonal);
            Assert.Equal(expBg, bg);
            Assert.Equal(expBorder, border);
            Assert.False(string.IsNullOrWhiteSpace(badgeBg));
        }

        [Fact]
        public void GetUsosDayName_FormatsCompactAndFullDays()
        {
            Assert.Equal("pon.", ScheduleLogic.GetUsosDayName(DayOfWeek.Monday, false, "pl"));
            Assert.Equal("wtorek", ScheduleLogic.GetUsosDayName(DayOfWeek.Tuesday, false, "pl"));
            Assert.Equal("środa", ScheduleLogic.GetUsosDayName(DayOfWeek.Wednesday, false, "pl"));
            Assert.Equal("czw.", ScheduleLogic.GetUsosDayName(DayOfWeek.Thursday, false, "pl"));
            Assert.Equal("piątek", ScheduleLogic.GetUsosDayName(DayOfWeek.Friday, false, "pl"));
            Assert.Equal("S", ScheduleLogic.GetUsosDayName(DayOfWeek.Saturday, true, "pl"));
            Assert.Equal("N", ScheduleLogic.GetUsosDayName(DayOfWeek.Sunday, true, "pl"));
            Assert.Equal("sob.", ScheduleLogic.GetUsosDayName(DayOfWeek.Saturday, false, "pl"));
            Assert.Equal("niedz.", ScheduleLogic.GetUsosDayName(DayOfWeek.Sunday, false, "pl"));

            Assert.Equal("Mon", ScheduleLogic.GetUsosDayName(DayOfWeek.Monday, false, "en-US"));
            Assert.Equal("S", ScheduleLogic.GetUsosDayName(DayOfWeek.Saturday, true, "en-US"));
        }

        [Fact]
        public void GetWeekDisplayName_ReturnsCurrentNextAndPreviousFriendlyNames()
        {
            var monday = new DateTime(2026, 10, 12);
            var today = new DateTime(2026, 10, 13); // wtorek w tym samym tygodniu
            
            Assert.Equal("bieżący tydzień", ScheduleLogic.GetWeekDisplayName(monday, today, "pl"));
            Assert.Equal("Current week", ScheduleLogic.GetWeekDisplayName(monday, today, "en-US"));

            var nextMonday = monday.AddDays(7);
            Assert.Equal("następny tydzień", ScheduleLogic.GetWeekDisplayName(nextMonday, today, "pl"));
            Assert.Equal("Next week", ScheduleLogic.GetWeekDisplayName(nextMonday, today, "en-US"));

            var prevMonday = monday.AddDays(-7);
            Assert.Equal("poprzedni tydzień", ScheduleLogic.GetWeekDisplayName(prevMonday, today, "pl"));
            Assert.Equal("Previous week", ScheduleLogic.GetWeekDisplayName(prevMonday, today, "en-US"));
        }
    }
}
