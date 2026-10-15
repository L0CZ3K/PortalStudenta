using System;
using System.Collections.Generic;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class AnnouncementFilterAndSortTests
    {
        [Fact]
        public void FilterAndSort_RemovesExpiredAnnouncements()
        {
            var now = new DateTime(2026, 10, 10, 12, 0, 0);
            var items = new List<AnnouncementItem>
            {
                new AnnouncementItem { Title = "Wygasłe", ExpireDate = now.AddMinutes(-5), CreatedAt = now.AddDays(-1) },
                new AnnouncementItem { Title = "Aktywne", ExpireDate = now.AddHours(2), CreatedAt = now.AddHours(-1) },
                new AnnouncementItem { Title = "Stałe (brak daty wygaśnięcia)", ExpireDate = default, CreatedAt = now.AddDays(-2) }
            };

            var result = AnnouncementLogic.FilterAndSort(items, now);

            Assert.Equal(2, result.Count);
            Assert.Contains(result, a => a.Title == "Aktywne");
            Assert.Contains(result, a => a.Title == "Stałe (brak daty wygaśnięcia)");
            Assert.DoesNotContain(result, a => a.Title == "Wygasłe");
        }

        [Fact]
        public void FilterAndSort_SortsPriorityAnnouncementsFirst()
        {
            var now = new DateTime(2026, 10, 10, 12, 0, 0);
            var normalNewer = new AnnouncementItem { Title = "Normalne Nowsze", IsPriority = false, CreatedAt = now.AddMinutes(-5) };
            var normalOlder = new AnnouncementItem { Title = "Normalne Starsze", IsPriority = false, CreatedAt = now.AddMinutes(-30) };
            var priorityOlder = new AnnouncementItem { Title = "Pilne Starsze", IsPriority = true, CreatedAt = now.AddMinutes(-60) };

            var result = AnnouncementLogic.FilterAndSort(new[] { normalNewer, priorityOlder, normalOlder }, now);

            Assert.Equal("Pilne Starsze", result[0].Title);
            Assert.Equal("Normalne Nowsze", result[1].Title);
            Assert.Equal("Normalne Starsze", result[2].Title);
        }

        [Fact]
        public void FilterAndSort_WithinSamePriority_SortsNewestFirst()
        {
            var now = new DateTime(2026, 10, 10, 12, 0, 0);
            var a1 = new AnnouncementItem { Title = "A", CreatedAt = now.AddHours(-3) };
            var a2 = new AnnouncementItem { Title = "B", CreatedAt = now.AddHours(-1) };
            var a3 = new AnnouncementItem { Title = "C", CreatedAt = now.AddHours(-2) };

            var result = AnnouncementLogic.FilterAndSort(new[] { a1, a2, a3 }, now);

            Assert.Equal(new[] { "B", "C", "A" }, new[] { result[0].Title, result[1].Title, result[2].Title });
        }

        [Fact]
        public void FilterAndSort_HandlesNullOrEmptyCollections()
        {
            Assert.Empty(AnnouncementLogic.FilterAndSort(null, DateTime.Now));
            Assert.Empty(AnnouncementLogic.FilterAndSort(new List<AnnouncementItem>(), DateTime.Now));
        }

        [Fact]
        public void FormatDisplayTime_Today_ReturnsDzisiajFormat()
        {
            var today = new DateTime(2026, 10, 10);
            var time = new DateTime(2026, 10, 10, 15, 30, 0);

            var formatted = AnnouncementLogic.FormatDisplayTime(time, today);

            Assert.Equal("Dzisiaj 15:30", formatted);
        }

        [Fact]
        public void FormatDisplayTime_OtherDate_ReturnsDayMonthFormat()
        {
            var today = new DateTime(2026, 10, 10);
            var time = new DateTime(2026, 10, 5, 9, 15, 0);

            var formatted = AnnouncementLogic.FormatDisplayTime(time, today);

            Assert.Equal("05.10 09:15", formatted);
        }

        [Fact]
        public void FormatDisplayTime_Default_ReturnsEmptyString()
        {
            var today = new DateTime(2026, 10, 10);
            Assert.Equal(string.Empty, AnnouncementLogic.FormatDisplayTime(default, today));
        }
    }
}
