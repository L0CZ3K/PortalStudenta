using System;
using System.Collections.Generic;
using System.Linq;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class SemesterRoomChangeTests
    {
        [Fact]
        public void FindMatchingSemesterLessons_ReturnsOnlyFutureLessonsOfSameCourseAndType()
        {
            var baseDate = new DateTime(2026, 10, 12, 10, 0, 0);
            var refLesson = new Lesson
            {
                Id = 1,
                Title = "Programowanie Zaawansowane",
                Type = "LAB",
                Group = "1",
                Start = baseDate,
                End = baseDate.AddHours(2),
                Place = "Sala 101"
            };

            var allLessons = new List<Lesson>
            {
                // Past lesson of same course & type -> SHOULD NOT MATCH
                new Lesson { Id = 10, Title = "Programowanie Zaawansowane", Type = "LAB", Group = "1", Start = baseDate.AddDays(-7), End = baseDate.AddDays(-7).AddHours(2) },
                // Current reference lesson -> MATCHES
                refLesson,
                // Future lesson +1 week -> MATCHES
                new Lesson { Id = 11, Title = "Programowanie Zaawansowane", Type = "LAB", Group = "1", Start = baseDate.AddDays(7), End = baseDate.AddDays(7).AddHours(2) },
                // Future lesson +2 weeks -> MATCHES
                new Lesson { Id = 12, Title = "Programowanie Zaawansowane", Type = "LAB", Group = "1", Start = baseDate.AddDays(14), End = baseDate.AddDays(14).AddHours(2) },
                // Future lesson of different course -> SHOULD NOT MATCH
                new Lesson { Id = 13, Title = "Fizyka I", Type = "LAB", Group = "1", Start = baseDate.AddDays(7), End = baseDate.AddDays(7).AddHours(2) },
                // Future lecture of same course -> SHOULD NOT MATCH
                new Lesson { Id = 14, Title = "Programowanie Zaawansowane", Type = "WYK", Group = "ALL", Start = baseDate.AddDays(7), End = baseDate.AddDays(7).AddHours(2) },
                // Future lesson of same course but different group -> SHOULD NOT MATCH
                new Lesson { Id = 15, Title = "Programowanie Zaawansowane", Type = "LAB", Group = "2", Start = baseDate.AddDays(7), End = baseDate.AddDays(7).AddHours(2) },
            };

            var result = ScheduleLogic.FindMatchingSemesterLessons(allLessons, refLesson);

            Assert.Equal(3, result.Count);
            Assert.Contains(result, l => l.Id == 1);
            Assert.Contains(result, l => l.Id == 11);
            Assert.Contains(result, l => l.Id == 12);
            Assert.DoesNotContain(result, l => l.Id == 10);
            Assert.DoesNotContain(result, l => l.Id == 13);
            Assert.DoesNotContain(result, l => l.Id == 14);
            Assert.DoesNotContain(result, l => l.Id == 15);
        }

        [Fact]
        public void FindMatchingSemesterLessons_DoesNotMatchPastLessons()
        {
            var today = new DateTime(2026, 10, 20, 12, 0, 0);
            var refLesson = new Lesson
            {
                Id = 5,
                Title = "Bazy Danych",
                Type = "CW",
                Group = "1",
                Start = today,
                End = today.AddHours(2)
            };

            var pastLessons = new List<Lesson>
            {
                new Lesson { Id = 1, Title = "Bazy Danych", Type = "CW", Group = "1", Start = today.AddDays(-14), End = today.AddDays(-14).AddHours(2) },
                new Lesson { Id = 2, Title = "Bazy Danych", Type = "CW", Group = "1", Start = today.AddDays(-7), End = today.AddDays(-7).AddHours(2) },
                new Lesson { Id = 3, Title = "Bazy Danych", Type = "CW", Group = "1", Start = today.AddHours(-1), End = today.AddHours(1) },
                refLesson
            };

            var result = ScheduleLogic.FindMatchingSemesterLessons(pastLessons, refLesson);

            Assert.Single(result);
            Assert.Equal(5, result[0].Id);
        }

        [Fact]
        public void FindMatchingSemesterLessons_IsCaseAndWhitespaceInsensitive()
        {
            var start = new DateTime(2026, 11, 1, 8, 0, 0);
            var refLesson = new Lesson
            {
                Title = "  Matematyka Dyskretna  ",
                Type = "wyk",
                Group = "all",
                Start = start
            };

            var allLessons = new List<Lesson>
            {
                refLesson,
                new Lesson { Title = "MATEMATYKA DYSKRETNA", Type = "WYK", Group = "ALL", Start = start.AddDays(7) },
                new Lesson { Title = "matematyka dyskretna", Type = "Wyk", Group = "", Start = start.AddDays(14) }
            };

            var result = ScheduleLogic.FindMatchingSemesterLessons(allLessons, refLesson);

            Assert.Equal(3, result.Count);
        }

        [Fact]
        public void FindMatchingSemesterLessons_IncludesReferenceLessonEvenIfNotFoundInCollection()
        {
            var start = new DateTime(2026, 11, 1, 8, 0, 0);
            var refLesson = new Lesson
            {
                Id = 999,
                Title = "Sieci Komputerowe",
                Type = "LAB",
                Group = "3",
                Start = start
            };

            var otherLessons = new List<Lesson>
            {
                new Lesson { Id = 1000, Title = "Sieci Komputerowe", Type = "LAB", Group = "3", Start = start.AddDays(7) }
            };

            var result = ScheduleLogic.FindMatchingSemesterLessons(otherLessons, refLesson);

            Assert.Equal(2, result.Count);
            Assert.Contains(result, l => l.Id == 999);
            Assert.Contains(result, l => l.Id == 1000);
        }

        [Fact]
        public void FindMatchingSemesterLessons_HandlesNullOrEmptyInputSafely()
        {
            var refLesson = new Lesson { Title = "Test", Start = DateTime.Now };

            Assert.Empty(ScheduleLogic.FindMatchingSemesterLessons(null, refLesson));
            Assert.Empty(ScheduleLogic.FindMatchingSemesterLessons(new List<Lesson>(), null));
            Assert.Empty(ScheduleLogic.FindMatchingSemesterLessons(null, null));
        }

        [Fact]
        public void FindMatchingSemesterLessons_PreservesChronologicalOrder()
        {
            var start = new DateTime(2026, 11, 1, 10, 0, 0);
            var refLesson = new Lesson { Title = "Algorytmy", Type = "WYK", Start = start };

            var list = new List<Lesson>
            {
                new Lesson { Title = "Algorytmy", Type = "WYK", Start = start.AddDays(21) },
                refLesson,
                new Lesson { Title = "Algorytmy", Type = "WYK", Start = start.AddDays(7) },
                new Lesson { Title = "Algorytmy", Type = "WYK", Start = start.AddDays(14) },
            };

            var result = ScheduleLogic.FindMatchingSemesterLessons(list, refLesson);

            Assert.Equal(4, result.Count);
            for (int i = 0; i < result.Count - 1; i++)
            {
                Assert.True(result[i].Start <= result[i + 1].Start);
            }
        }
    }
}
