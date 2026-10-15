using System;
using System.Collections.Generic;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class ComprehensiveModelInvariantsTests
    {
        [Fact]
        public void GroupPoll_ZeroVotes_DoesNotThrowDivideByZero()
        {
            var poll = new GroupPoll
            {
                Question = "Kiedy robimy koło?",
                Options = new List<string> { "Poniedziałek", "Środa", "Piątek" },
                Votes = new Dictionary<string, int>()
            };

            Assert.Equal(0, poll.TotalVotes);
            Assert.Equal(0, poll.GetVoteCountForOption(0));
            Assert.Equal(0.0, poll.GetPercentageForOption(0));
            Assert.Equal(0.0, poll.GetPercentageForOption(1));
            Assert.Equal(0.0, poll.GetPercentageForOption(99));
        }

        [Fact]
        public void GroupPoll_NullVotesDictionary_HandledGracefully()
        {
            var poll = new GroupPoll
            {
                Votes = null!
            };

            Assert.Equal(0, poll.TotalVotes);
            Assert.Equal(0, poll.GetVoteCountForOption(0));
            Assert.Equal(0.0, poll.GetPercentageForOption(0));
        }

        [Fact]
        public void GroupPoll_LargeVoteVolume_CalculatesPrecisePercentages()
        {
            var poll = new GroupPoll
            {
                Question = "Wybór terminu",
                Options = new List<string> { "Opcja A", "Opcja B", "Opcja C", "Opcja D" },
                Votes = new Dictionary<string, int>()
            };

            // 10 000 unikalnych studentów głosuje
            for (int i = 0; i < 4000; i++) poll.Votes[$"student_{i}"] = 0;
            for (int i = 4000; i < 7000; i++) poll.Votes[$"student_{i}"] = 1;
            for (int i = 7000; i < 9000; i++) poll.Votes[$"student_{i}"] = 2;
            for (int i = 9000; i < 10000; i++) poll.Votes[$"student_{i}"] = 3;

            Assert.Equal(10000, poll.TotalVotes);
            Assert.Equal(4000, poll.GetVoteCountForOption(0));
            Assert.Equal(3000, poll.GetVoteCountForOption(1));
            Assert.Equal(2000, poll.GetVoteCountForOption(2));
            Assert.Equal(1000, poll.GetVoteCountForOption(3));

            Assert.Equal(40.0, poll.GetPercentageForOption(0));
            Assert.Equal(30.0, poll.GetPercentageForOption(1));
            Assert.Equal(20.0, poll.GetPercentageForOption(2));
            Assert.Equal(10.0, poll.GetPercentageForOption(3));
        }

        [Fact]
        public void GroupPoll_StudentRevoting_OverwritesPreviousVote()
        {
            var poll = new GroupPoll
            {
                Options = new List<string> { "Tak", "Nie" },
                Votes = new Dictionary<string, int>()
            };

            poll.Votes["student_12345"] = 0; // Głos na 'Tak'
            Assert.Equal(1, poll.GetVoteCountForOption(0));
            Assert.Equal(0, poll.GetVoteCountForOption(1));

            // Zmiana zdania:
            poll.Votes["student_12345"] = 1; // Głos na 'Nie'
            Assert.Equal(0, poll.GetVoteCountForOption(0));
            Assert.Equal(1, poll.GetVoteCountForOption(1));
            Assert.Equal(1, poll.TotalVotes);
        }

        [Fact]
        public void Lesson_DefaultInvariants_ArePreserved()
        {
            var lesson = new Lesson();

            Assert.Equal(0, lesson.Id);
            Assert.Equal(string.Empty, lesson.Title);
            Assert.Equal(string.Empty, lesson.Type);
            Assert.Equal(string.Empty, lesson.Place);
            Assert.Equal(string.Empty, lesson.Person);
            Assert.Equal(string.Empty, lesson.Desc);
            Assert.Equal("ALL", lesson.Group);
            Assert.False(lesson.IsCanceled);
            Assert.False(lesson.IsPersonal);
            Assert.False(lesson.IsHighPriority);
            Assert.False(lesson.HasExam);
            Assert.False(lesson.HasTask);
            Assert.Equal(string.Empty, lesson.CourseId);
            Assert.Equal(string.Empty, lesson.CourseUnitId);
        }

        [Fact]
        public void Lesson_ModificationPreservesOriginalFields()
        {
            var origStart = new DateTime(2025, 10, 10, 8, 30, 0);
            var origEnd = new DateTime(2025, 10, 10, 10, 0, 0);

            var lesson = new Lesson
            {
                Id = 101,
                Title = "Analiza Matematyczna",
                Place = "Aula A",
                Start = origStart,
                End = origEnd,
                OriginalStart = origStart,
                OriginalEnd = origEnd,
                OriginalPlace = "Aula A"
            };

            // Symulacja przeniesienia zajęć przez starostę
            lesson.Start = origStart.AddHours(2);
            lesson.End = origEnd.AddHours(2);
            lesson.Place = "Sala 214";

            // Sprawdzamy czy pamiętamy stan początkowy z USOS
            Assert.Equal(origStart, lesson.OriginalStart);
            Assert.Equal(origEnd, lesson.OriginalEnd);
            Assert.Equal("Aula A", lesson.OriginalPlace);
            Assert.Equal(origStart.AddHours(2), lesson.Start);
            Assert.Equal("Sala 214", lesson.Place);
        }

        [Fact]
        public void LessonModification_StoresAllModificationProperties()
        {
            var mod = new LessonModification
            {
                LessonKey = "2025-10-10_8_30_Analiza",
                Title = "Analiza Matematyczna",
                IsCanceled = true,
                CancelReason = "Choroba prowadzącego",
                HasExam = false,
                HasTask = true,
                TaskDetails = "Zadania 1-5 ze strony 42",
                IsGroupModification = true,
                GroupCode = "INF_ROK2"
            };

            Assert.Equal("2025-10-10_8_30_Analiza", mod.LessonKey);
            Assert.True(mod.IsCanceled);
            Assert.Equal("Choroba prowadzącego", mod.CancelReason);
            Assert.True(mod.HasTask);
            Assert.Equal("Zadania 1-5 ze strony 42", mod.TaskDetails);
            Assert.True(mod.IsGroupModification);
            Assert.Equal("INF_ROK2", mod.GroupCode);
        }
    }
}
