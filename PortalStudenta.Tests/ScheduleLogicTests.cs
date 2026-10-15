using System;
using System.Collections.Generic;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class ScheduleLogicTests
    {
        // --- TESTY GENEROWANIA KLUCZY ZAJĘĆ (GetLessonKey) ---

        [Fact]
        public void GetLessonKey_NullLesson_ReturnsEMPTY_LESSON()
        {
            var key = ScheduleLogic.GetLessonKey(null);
            Assert.Equal("EMPTY_LESSON", key);
        }

        [Fact]
        public void GetLessonKey_StandardLesson_ReturnsFormattedUpperInvariantKey()
        {
            var lesson = new Lesson
            {
                Title = "Matematyka Dyskretna",
                Type = "WYK",
                Start = new DateTime(2026, 10, 15, 8, 30, 0),
                Group = "1"
            };

            var key = ScheduleLogic.GetLessonKey(lesson);

            Assert.Equal("MATEMATYKA DYSKRETNA_WYK_202610150830_1".Replace(" ", "_"), key);
        }

        [Fact]
        public void GetLessonKey_UsesOriginalStartIfAvailable()
        {
            var lesson = new Lesson
            {
                Title = "Programowanie C#",
                Type = "LAB",
                OriginalStart = new DateTime(2026, 10, 15, 10, 15, 0),
                Start = new DateTime(2026, 10, 15, 12, 0, 0), // Przełożone na późniejszą godzinę
                Group = "ALL"
            };

            var key = ScheduleLogic.GetLessonKey(lesson);

            // Klucz powinien bazować na pierwotnej dacie (OriginalStart), aby utrzymać identyfikator USOS
            Assert.Contains("202610151015", key);
            Assert.DoesNotContain("202610151200", key);
        }

        [Fact]
        public void GetLessonKey_NullOrEmptyGroup_DefaultsToALL()
        {
            var lesson = new Lesson
            {
                Title = "Fizyka",
                Type = "WYK",
                Start = new DateTime(2026, 11, 2, 9, 0, 0),
                Group = ""
            };

            var key = ScheduleLogic.GetLessonKey(lesson);
            Assert.EndsWith("_ALL", key);
        }

        [Fact]
        public void GetLessonKey_SpecialCharactersInTitleAndType_SanitizesSafely()
        {
            var lesson = new Lesson
            {
                Title = "C# .NET / Zaawansowane [sem. 3]",
                Type = "ĆW.",
                Start = new DateTime(2026, 10, 20, 14, 0, 0),
                Group = "G/1"
            };

            var key = ScheduleLogic.GetLessonKey(lesson);

            // Żadne niedozwolone znaki Firebase nie mogą przejść do klucza
            Assert.DoesNotContain(".", key);
            Assert.DoesNotContain("/", key);
            Assert.DoesNotContain("[", key);
            Assert.DoesNotContain("]", key);
            Assert.DoesNotContain(" ", key);
            Assert.DoesNotContain("#", key);
        }

        // --- TESTY ŚWIEŻOŚCI PAMIĘCI PODRĘCZNEJ USOS (IsCacheFresh - Polityka 60 min) ---

        [Fact]
        public void IsCacheFresh_ZeroLessons_ReturnsFalse()
        {
            var now = DateTime.UtcNow;
            var fetchTime = now.AddMinutes(-5);

            var isFresh = ScheduleLogic.IsCacheFresh(0, fetchTime, now);
            Assert.False(isFresh);
        }

        [Fact]
        public void IsCacheFresh_NullFetchTime_ReturnsFalse()
        {
            var now = DateTime.UtcNow;

            var isFresh = ScheduleLogic.IsCacheFresh(10, null, now);
            Assert.False(isFresh);
        }

        [Fact]
        public void IsCacheFresh_ClockSkew_FutureFetchTime_ReturnsFalse()
        {
            var now = DateTime.UtcNow;
            var futureFetchTime = now.AddMinutes(5); // Zegar w przyszłości

            var isFresh = ScheduleLogic.IsCacheFresh(10, futureFetchTime, now);
            Assert.False(isFresh);
        }

        [Theory]
        [InlineData(0, true)]    // Pobrane przed chwilą -> świeże
        [InlineData(15, true)]   // Pobrane 15 min temu -> świeże
        [InlineData(45, true)]   // Pobrane 45 min temu -> świeże
        [InlineData(59, true)]   // Pobrane 59 min temu -> świeże (w oknie 60 min)
        [InlineData(60, false)]  // Dokładnie 60 min temu -> nieświeże (wymaga odświeżenia)
        [InlineData(61, false)]  // 61 min temu -> nieświeże
        [InlineData(1440, false)] // 24h temu -> nieświeże
        public void IsCacheFresh_Respects60MinutePolicyWindow(int minutesAgo, bool expectedFreshness)
        {
            var now = DateTime.UtcNow;
            var fetchTime = now.AddMinutes(-minutesAgo);

            var isFresh = ScheduleLogic.IsCacheFresh(25, fetchTime, now, 60);

            Assert.Equal(expectedFreshness, isFresh);
        }

        // --- TESTY POPRAWNOŚCI PRZEDZIAŁÓW CZASOWYCH I ZAJĘĆ PRZESZŁYCH ---

        [Fact]
        public void IsValidLessonInterval_ValidatesStartBeforeEnd()
        {
            var start = new DateTime(2026, 10, 10, 10, 0, 0);
            var end = new DateTime(2026, 10, 10, 11, 30, 0);

            Assert.True(ScheduleLogic.IsValidLessonInterval(start, end));
            Assert.False(ScheduleLogic.IsValidLessonInterval(end, start)); // Odwrócone
            Assert.False(ScheduleLogic.IsValidLessonInterval(start, start)); // Ta sama godzina
            Assert.False(ScheduleLogic.IsValidLessonInterval(default, end));
        }

        [Fact]
        public void IsPastLesson_ChecksIfLessonHasConcluded()
        {
            var now = new DateTime(2026, 10, 10, 12, 0, 0);

            var pastLessonEnd = new DateTime(2026, 10, 10, 11, 30, 0);
            var ongoingLessonEnd = new DateTime(2026, 10, 10, 13, 0, 0);

            Assert.True(ScheduleLogic.IsPastLesson(pastLessonEnd, now));
            Assert.False(ScheduleLogic.IsPastLesson(ongoingLessonEnd, now));
            Assert.False(ScheduleLogic.IsPastLesson(default, now));
        }

        // --- TESTY APLIKOWANIA MODYFIKACJI HARMONOGRAMU (ApplyModifications) ---

        [Fact]
        public void ApplyModifications_AppliesCancellationsAndRoomChangesCorrectly()
        {
            var l1 = new Lesson
            {
                Title = "Bazy Danych",
                Type = "WYK",
                Start = new DateTime(2026, 10, 12, 8, 0, 0),
                End = new DateTime(2026, 10, 12, 9, 30, 0),
                Place = "Sala 101"
            };

            var l2 = new Lesson
            {
                Title = "Sieci Komputerowe",
                Type = "LAB",
                Start = new DateTime(2026, 10, 12, 10, 0, 0),
                End = new DateTime(2026, 10, 12, 11, 30, 0),
                Place = "Lab 5"
            };

            var lessons = new List<Lesson> { l1, l2 };
            var key1 = ScheduleLogic.GetLessonKey(l1);

            var modifications = new Dictionary<string, LessonModification>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    key1,
                    new LessonModification
                    {
                        LessonKey = key1,
                        IsCanceled = true,
                        CancelReason = "Choroba prowadzącego",
                        ModifiedPlace = "Sala 205",
                        HasExam = true,
                        HasTask = true,
                        TaskDetails = "Projekt końcowy"
                    }
                }
            };

            ScheduleLogic.ApplyModifications(lessons, modifications);

            // l1 powinno przyjąć modyfikacje
            Assert.True(l1.IsCanceled);
            Assert.Equal("Choroba prowadzącego", l1.Desc);
            Assert.Equal("Sala 205", l1.Place);
            Assert.True(l1.HasExam);
            Assert.True(l1.HasTask);
            Assert.Equal("Projekt końcowy", l1.TaskDetails);

            // l2 powinno pozostać nienaruszone
            Assert.False(l2.IsCanceled);
            Assert.Equal("Lab 5", l2.Place);
            Assert.False(l2.HasExam);
        }

        // --- TESTY GENERATORA UNIKALNYCH IDENTYFIKATORÓW WYDARZEŃ (GenerateUniqueEventId) ---

        [Fact]
        public void GenerateUniqueEventId_ReturnsPositiveNumberInSafeRange()
        {
            var id = ScheduleLogic.GenerateUniqueEventId();

            Assert.True(id > 0);
            Assert.True(id <= int.MaxValue);
        }

        [Fact]
        public void GenerateUniqueEventId_AvoidsCollisionsWithExistingIds()
        {
            var existingIds = new List<int> { 12345, 99999, 100_000_001 };

            for (int i = 0; i < 50; i++)
            {
                var newId = ScheduleLogic.GenerateUniqueEventId(existingIds);
                Assert.DoesNotContain(newId, existingIds);
                existingIds.Add(newId);
            }
        }

        [Fact]
        public void GenerateUniqueEventId_GeneratesDistinctNumbersAcrossManyCalls()
        {
            var generatedIds = new HashSet<int>();
            for (int i = 0; i < 100; i++)
            {
                var id = ScheduleLogic.GenerateUniqueEventId(generatedIds);
                generatedIds.Add(id);
            }

            Assert.Equal(100, generatedIds.Count);
        }
    }
}
