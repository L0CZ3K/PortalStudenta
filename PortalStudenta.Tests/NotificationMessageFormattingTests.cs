using System;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class NotificationMessageFormattingTests
    {
        [Fact]
        public void FormatLessonCanceled_Today_WithReason()
        {
            var now = new DateTime(2025, 10, 10, 8, 0, 0);
            var lesson = new Lesson
            {
                Title = "Programowanie w C#",
                Type = "Laboratorium",
                Start = new DateTime(2025, 10, 10, 12, 15, 0)
            };

            var (title, body) = NotificationMessageLogic.FormatLessonCanceled(lesson, "Awaria prądu", now);

            Assert.Equal("🚨 Odwołano: Programowanie w C#", title);
            Assert.Contains("dzisiaj o 12:15", body);
            Assert.Contains("Powód: Awaria prądu", body);
            Assert.Contains("Laboratorium", body);
        }

        [Fact]
        public void FormatLessonCanceled_FutureDate_WithoutReason()
        {
            var now = new DateTime(2025, 10, 10, 8, 0, 0);
            var lesson = new Lesson
            {
                Title = "Fizyka",
                Type = "Wykład",
                Start = new DateTime(2025, 10, 15, 14, 0, 0)
            };

            var (title, body) = NotificationMessageLogic.FormatLessonCanceled(lesson, null, now);

            Assert.Equal("🚨 Odwołano: Fizyka", title);
            Assert.Contains("15.10 o 14:00", body);
            Assert.DoesNotContain("Powód:", body);
        }

        [Fact]
        public void FormatLessonRoomChanged_FormatsAccurately()
        {
            var now = new DateTime(2025, 10, 10, 8, 0, 0);
            var lesson = new Lesson
            {
                Title = "Matematyka Dyskretna",
                Type = "Ćwiczenia",
                Start = new DateTime(2025, 10, 10, 10, 0, 0)
            };

            var (title, body) = NotificationMessageLogic.FormatLessonRoomChanged(lesson, "Aula 402", now);

            Assert.Equal("📍 Zmiana sali: Matematyka Dyskretna", title);
            Assert.Contains("dzisiaj, 10:00", body);
            Assert.Contains("Aula 402", body);
        }

        [Fact]
        public void FormatLessonRescheduled_FormatsTimespanAndDay()
        {
            var lesson = new Lesson
            {
                Title = "Algorytmy i Struktury Danych"
            };

            var newStart = new DateTime(2025, 10, 20, 9, 0, 0);
            var newEnd = new DateTime(2025, 10, 20, 10, 30, 0);

            var (title, body) = NotificationMessageLogic.FormatLessonRescheduled(lesson, newStart, newEnd);

            Assert.Equal("🕒 Zmiana terminu: Algorytmy i Struktury Danych", title);
            Assert.Contains("20.10", body);
            Assert.Contains("09:00 – 10:30", body);
        }

        [Fact]
        public void FormatAnnouncement_DevAndSystem_UseDevHeader()
        {
            var devAnn = new AnnouncementItem
            {
                Title = "Nowa wersja aplikacji",
                Content = "Dodano tryb ciemny.",
                Author = "Dev",
                IsSystem = false
            };

            var (title, body) = NotificationMessageLogic.FormatAnnouncement(devAnn);
            Assert.Equal("🛠️ Nowy komunikat od Devów", title);
            Assert.Contains("Nowa wersja aplikacji", body);
            Assert.Contains("Dodano tryb ciemny.", body);

            var sysAnn = new AnnouncementItem
            {
                Title = "Prace konserwacyjne",
                Content = "Przerwa techniczna w nocy.",
                Author = "",
                IsSystem = true
            };

            var (sysTitle, sysBody) = NotificationMessageLogic.FormatAnnouncement(sysAnn);
            Assert.Equal("🛠️ Nowy komunikat od Devów", sysTitle);
            Assert.Contains("Prace konserwacyjne", sysBody);
        }

        [Fact]
        public void FormatAnnouncement_Starosta_UsesStarostaHeader()
        {
            var starostaAnn = new AnnouncementItem
            {
                Title = "Zbiórka na prezent",
                Content = "Szczegóły w opisie.",
                Author = "Starosta Jan",
                IsSystem = false
            };

            var (title, body) = NotificationMessageLogic.FormatAnnouncement(starostaAnn);
            Assert.Equal("📢 Ogłoszenie od: Starosta Jan", title);
            Assert.Contains("Zbiórka na prezent", body);
        }

        [Fact]
        public void FormatNewPoll_IncludesQuestionAndCallToAction()
        {
            var poll = new GroupPoll
            {
                Question = "Wybór terminu kolokwium z Fizyki"
            };

            var (title, body) = NotificationMessageLogic.FormatNewPoll(poll);
            Assert.Equal("🗳️ Nowa ankieta w grupie", title);
            Assert.Contains("Wybór terminu kolokwium z Fizyki", body);
            Assert.Contains("Zagłosuj w panelu Starosty lub na pulpicie.", body);
        }
    }
}
