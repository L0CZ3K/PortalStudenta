using System;
using System.Collections.Generic;
using System.Linq;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class LessonEdgeCaseTests
    {
        [Fact]
        public void MultiDayEvent_CalculatesSpanAndDaysCorrectly()
        {
            // Wydarzenie wielodniowe (np. hackathon, konferencja 2-dniowa)
            var start = new DateTime(2026, 10, 16, 10, 0, 0);
            var end = new DateTime(2026, 10, 17, 18, 0, 0);

            var multiDayEvent = new Lesson
            {
                Id = 1,
                Title = "Hackathon Uczelniany",
                Start = start,
                End = end,
                Type = "STAR"
            };

            // Sprawdzenie poprawności przedziału
            Assert.True(ScheduleLogic.IsValidLessonInterval(multiDayEvent.Start, multiDayEvent.End));
            Assert.True((multiDayEvent.End - multiDayEvent.Start).TotalHours > 24);

            // Sprawdzenie czy wydarzenie obejmuje 2 różne dni kalendarzowe
            Assert.NotEqual(multiDayEvent.Start.Date, multiDayEvent.End.Date);
            Assert.Equal(2, (multiDayEvent.End.Date - multiDayEvent.Start.Date).Days + 1);
        }

        [Fact]
        public void PastLessons_Filtering_OnlyReturnsFutureLessons()
        {
            var now = new DateTime(2026, 10, 10, 12, 0, 0);

            var pastLesson = new Lesson
            {
                Id = 1,
                Title = "Wykład rano",
                Start = new DateTime(2026, 10, 10, 8, 0, 0),
                End = new DateTime(2026, 10, 10, 9, 30, 0)
            };

            var ongoingLesson = new Lesson
            {
                Id = 2,
                Title = "Zajęcia w toku",
                Start = new DateTime(2026, 10, 10, 11, 30, 0),
                End = new DateTime(2026, 10, 10, 13, 0, 0)
            };

            var futureLesson = new Lesson
            {
                Id = 3,
                Title = "Laboratorium popołudniu",
                Start = new DateTime(2026, 10, 10, 14, 0, 0),
                End = new DateTime(2026, 10, 10, 15, 30, 0)
            };

            var allLessons = new List<Lesson> { pastLesson, ongoingLesson, futureLesson };

            // Filtr do wyszukiwarki starosty i modalów: wyklucza zajęcia, które już się zakończyły (End <= now)
            var activeAndFutureLessons = allLessons
                .Where(l => !ScheduleLogic.IsPastLesson(l.End, now))
                .ToList();

            Assert.Equal(2, activeAndFutureLessons.Count);
            Assert.DoesNotContain(pastLesson, activeAndFutureLessons);
            Assert.Contains(ongoingLesson, activeAndFutureLessons);
            Assert.Contains(futureLesson, activeAndFutureLessons);
        }

        [Fact]
        public void NoteModel_InitializesWithUniqueIdAndDefaults()
        {
            var note1 = new Note { Title = "Notatka 1", Content = "Treść 1" };
            var note2 = new Note { Title = "Notatka 2", Content = "Treść 2" };

            Assert.NotEqual(note1.Id, note2.Id);
            Assert.NotEqual(Guid.Empty, note1.Id);
            Assert.False(string.IsNullOrWhiteSpace(note1.Author));
            Assert.True(note1.CreatedAt <= DateTime.Now);
        }

        [Fact]
        public void GroupFiltering_MatchesUniversalAndSpecificGroups()
        {
            var userGroup = "1";

            var universalLesson = new Lesson { Title = "Wykład Ogólny", Group = "ALL" };
            var matchingGroupLesson = new Lesson { Title = "Laboratoria Gr. 1", Group = "1" };
            var otherGroupLesson = new Lesson { Title = "Laboratoria Gr. 2", Group = "2" };

            var lessons = new List<Lesson> { universalLesson, matchingGroupLesson, otherGroupLesson };

            // Logika dopasowywania grupy w harmonogramie
            var userLessons = lessons.Where(l => 
                string.Equals(l.Group, "ALL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(l.Group, userGroup, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            Assert.Equal(2, userLessons.Count);
            Assert.Contains(universalLesson, userLessons);
            Assert.Contains(matchingGroupLesson, userLessons);
            Assert.DoesNotContain(otherGroupLesson, userLessons);
        }
    }
}
