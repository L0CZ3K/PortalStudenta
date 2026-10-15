using System;
using System.Collections.Generic;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class CalendarExportIcsTests
    {
        [Fact]
        public void GenerateIcs_ContainsStandardRfc5545Headers()
        {
            var ics = IcsCalendarHelper.GenerateIcs(new List<Lesson>());

            Assert.Contains("BEGIN:VCALENDAR", ics);
            Assert.Contains("VERSION:2.0", ics);
            Assert.Contains("PRODID:-//UniAvio//Portal Studenta//PL", ics);
            Assert.Contains("CALSCALE:GREGORIAN", ics);
            Assert.Contains("METHOD:PUBLISH", ics);
            Assert.Contains("END:VCALENDAR", ics);
        }

        [Fact]
        public void GenerateIcs_EscapesSpecialCharacters_BackslashSemicolonCommaNewlines()
        {
            var lesson = new Lesson
            {
                Id = 1,
                Title = "Bazy Danych; SQL, NoSQL & C#",
                Place = "Sala 2.15, Budynek A\\B",
                Desc = "Linia 1\r\nLinia 2\nLinia 3",
                Start = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc)
            };

            var ics = IcsCalendarHelper.GenerateIcs(new[] { lesson });

            Assert.Contains("SUMMARY:Bazy Danych\\; SQL\\, NoSQL & C#", ics);
            Assert.Contains("LOCATION:Sala 2.15\\, Budynek A\\\\B", ics);
            Assert.Contains("Linia 1\\nLinia 2\\nLinia 3", ics);
            Assert.DoesNotContain("\r\nLinia 2", ics);
        }

        [Fact]
        public void GenerateIcs_OmmitsCanceledLessonsWhenIncludeCanceledIsFalse()
        {
            var activeLesson = new Lesson { Id = 1, Title = "Lekcja Aktywna", Start = DateTime.UtcNow, End = DateTime.UtcNow.AddHours(1) };
            var canceledLesson = new Lesson { Id = 2, Title = "Lekcja Odwołana", IsCanceled = true, Start = DateTime.UtcNow, End = DateTime.UtcNow.AddHours(1) };

            var ics = IcsCalendarHelper.GenerateIcs(new[] { activeLesson, canceledLesson }, includeCanceled: false);

            Assert.Contains("Lekcja Aktywna", ics);
            Assert.DoesNotContain("Lekcja Odwołana", ics);
        }

        [Fact]
        public void GenerateIcs_IncludesCanceledLessonsWithCancelledStatusWhenTrue()
        {
            var canceledLesson = new Lesson
            {
                Id = 2,
                Title = "Fizyka",
                Type = "WYK",
                IsCanceled = true,
                Start = DateTime.UtcNow,
                End = DateTime.UtcNow.AddHours(1)
            };

            var ics = IcsCalendarHelper.GenerateIcs(new[] { canceledLesson }, includeCanceled: true);

            Assert.Contains("[ODWOŁANE] Fizyka (WYK)", ics);
            Assert.Contains("STATUS:CANCELLED", ics);
        }

        [Fact]
        public void GenerateIcs_Includes15MinAlarmWhenRequested()
        {
            var lesson = new Lesson
            {
                Id = 10,
                Title = "Matematyka",
                Start = DateTime.UtcNow,
                End = DateTime.UtcNow.AddHours(1)
            };

            var icsWithAlarm = IcsCalendarHelper.GenerateIcs(new[] { lesson }, includeAlarms: true);
            var icsWithoutAlarm = IcsCalendarHelper.GenerateIcs(new[] { lesson }, includeAlarms: false);

            Assert.Contains("BEGIN:VALARM", icsWithAlarm);
            Assert.Contains("TRIGGER:-PT15M", icsWithAlarm);
            Assert.DoesNotContain("BEGIN:VALARM", icsWithoutAlarm);
        }

        [Fact]
        public void GenerateIcs_OmmitsAlarmForCanceledClasses()
        {
            var canceled = new Lesson
            {
                Id = 11,
                Title = "Chemia",
                IsCanceled = true,
                Start = DateTime.UtcNow,
                End = DateTime.UtcNow.AddHours(1)
            };

            var ics = IcsCalendarHelper.GenerateIcs(new[] { canceled }, includeAlarms: true, includeCanceled: true);

            // Canceled classes should not wake students up with a 15-minute alarm
            Assert.DoesNotContain("BEGIN:VALARM", ics);
        }

        [Fact]
        public void GenerateIcs_GeneratesDeterministicUidAndUtcTimestamps()
        {
            var start = new DateTime(2026, 11, 20, 14, 30, 0, DateTimeKind.Utc);
            var lesson = new Lesson
            {
                Id = 42,
                Title = "Projekt",
                Start = start,
                End = start.AddHours(2)
            };

            var ics = IcsCalendarHelper.GenerateIcs(new[] { lesson });

            Assert.Contains("UID:uniavio_42_202611201430@uniavio.app", ics);
            Assert.Contains("DTSTART:20261120T143000Z", ics);
            Assert.Contains("DTEND:20261120T163000Z", ics);
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("Clean String", "Clean String")]
        [InlineData("Hello, World; Welcome", "Hello\\, World\\; Welcome")]
        public void EscapeIcs_HandlesInputsCorrectly(string? input, string expected)
        {
            Assert.Equal(expected, IcsCalendarHelper.EscapeIcs(input));
        }
    }
}
