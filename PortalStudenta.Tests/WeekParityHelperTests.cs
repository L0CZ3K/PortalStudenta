using System;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class WeekParityHelperTests
    {
        [Fact]
        public void CalculateIsWeekEven_AlwaysEvenMode_ReturnsTrueForAnyDate()
        {
            var date1 = new DateTime(2026, 1, 1);
            var date2 = new DateTime(2026, 5, 15);

            Assert.True(WeekParityHelper.CalculateIsWeekEven(date1, WeekParityMode.AlwaysEven));
            Assert.True(WeekParityHelper.CalculateIsWeekEven(date2, WeekParityMode.AlwaysEven));
        }

        [Fact]
        public void CalculateIsWeekEven_AlwaysOddMode_ReturnsFalseForAnyDate()
        {
            var date1 = new DateTime(2026, 1, 1);
            var date2 = new DateTime(2026, 5, 15);

            Assert.False(WeekParityHelper.CalculateIsWeekEven(date1, WeekParityMode.AlwaysOdd));
            Assert.False(WeekParityHelper.CalculateIsWeekEven(date2, WeekParityMode.AlwaysOdd));
        }

        [Fact]
        public void CalculateIsWeekEven_AutoISO8601_CorrectParity()
        {
            // Rok 2026:
            // Tydzień 1 (nieparzysty): 29 grudnia 2025 – 4 stycznia 2026
            var oddWeekDate = new DateTime(2026, 1, 2); // Piątek w tygodniu 1
            Assert.False(WeekParityHelper.CalculateIsWeekEven(oddWeekDate, WeekParityMode.Auto));

            // Tydzień 2 (parzysty): 5 stycznia 2026 – 11 stycznia 2026
            var evenWeekDate = new DateTime(2026, 1, 7); // Środa w tygodniu 2
            Assert.True(WeekParityHelper.CalculateIsWeekEven(evenWeekDate, WeekParityMode.Auto));

            // Tydzień 41 (nieparzysty): 5 października 2026
            var week41 = new DateTime(2026, 10, 5);
            Assert.False(WeekParityHelper.CalculateIsWeekEven(week41, WeekParityMode.Auto));

            // Tydzień 42 (parzysty): 12 października 2026
            var week42 = new DateTime(2026, 10, 12);
            Assert.True(WeekParityHelper.CalculateIsWeekEven(week42, WeekParityMode.Auto));
        }

        [Fact]
        public void CalculateIsWeekEven_InvertedMode_InvertsParity()
        {
            var oddWeekDate = new DateTime(2026, 1, 2); // Normalnie nieparzysty (false)
            var evenWeekDate = new DateTime(2026, 1, 7); // Normalnie parzysty (true)

            // Tryb Inverted powinien odwrócić
            Assert.True(WeekParityHelper.CalculateIsWeekEven(oddWeekDate, WeekParityMode.Inverted));
            Assert.False(WeekParityHelper.CalculateIsWeekEven(evenWeekDate, WeekParityMode.Inverted));

            // Flaga invertWeeks = true powinna zadziałać identycznie
            Assert.True(WeekParityHelper.CalculateIsWeekEven(oddWeekDate, WeekParityMode.Auto, invertWeeks: true));
            Assert.False(WeekParityHelper.CalculateIsWeekEven(evenWeekDate, WeekParityMode.Auto, invertWeeks: true));
        }

        [Fact]
        public void CalculateIsWeekEven_AcademicTermStart_CalculatesRelativeWeeks()
        {
            // Semestr startuje w poniedziałek 1 października 2026
            string termStart = "2026-10-01";

            // Tydzień 1 semestru (1-7 października) -> Nieparzysty (false)
            var week1Date = new DateTime(2026, 10, 3);
            Assert.False(WeekParityHelper.CalculateIsWeekEven(week1Date, WeekParityMode.Auto, academicTermStart: termStart));

            // Tydzień 2 semestru (8-14 października) -> Parzysty (true)
            var week2Date = new DateTime(2026, 10, 10);
            Assert.True(WeekParityHelper.CalculateIsWeekEven(week2Date, WeekParityMode.Auto, academicTermStart: termStart));

            // Tydzień 3 semestru (15-21 października) -> Nieparzysty (false)
            var week3Date = new DateTime(2026, 10, 17);
            Assert.False(WeekParityHelper.CalculateIsWeekEven(week3Date, WeekParityMode.Auto, academicTermStart: termStart));
        }

        [Fact]
        public void GetNextParityMode_CyclesProperlyThroughAllModes()
        {
            var mode = WeekParityMode.Auto;

            mode = WeekParityHelper.GetNextParityMode(mode);
            Assert.Equal(WeekParityMode.Inverted, mode);

            mode = WeekParityHelper.GetNextParityMode(mode);
            Assert.Equal(WeekParityMode.AlwaysEven, mode);

            mode = WeekParityHelper.GetNextParityMode(mode);
            Assert.Equal(WeekParityMode.AlwaysOdd, mode);

            mode = WeekParityHelper.GetNextParityMode(mode);
            Assert.Equal(WeekParityMode.Auto, mode);
        }

        [Fact]
        public void GetParityLabel_ReturnsAccurateLabels()
        {
            Assert.Equal("Parzysty (B)", WeekParityHelper.GetParityLabel(true));
            Assert.Equal("Nieparzysty (A)", WeekParityHelper.GetParityLabel(false));
        }
    }
}
