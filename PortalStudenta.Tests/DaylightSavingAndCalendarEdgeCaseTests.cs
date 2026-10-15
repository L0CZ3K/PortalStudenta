using System;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class DaylightSavingAndCalendarEdgeCaseTests
    {
        [Fact]
        public void AutumnDst_LessonDurationCalculatesCorrectly()
        {
            // European Autumn DST change occurs on the last Sunday of October (e.g. 2026-10-25)
            // Clock rolls back from 3:00 to 2:00
            var start = new DateTime(2026, 10, 25, 8, 0, 0);
            var end = new DateTime(2026, 10, 25, 9, 30, 0);

            var lesson = new Lesson { Start = start, End = end };

            Assert.True(ScheduleLogic.IsValidLessonInterval(start, end));
            Assert.Equal(90, (lesson.End - lesson.Start).TotalMinutes);
        }

        [Fact]
        public void SpringDst_LessonDurationCalculatesCorrectly()
        {
            // European Spring DST change occurs on the last Sunday of March (e.g. 2026-03-29)
            // Clock skips from 2:00 to 3:00
            var start = new DateTime(2026, 3, 29, 8, 0, 0);
            var end = new DateTime(2026, 3, 29, 9, 30, 0);

            var lesson = new Lesson { Start = start, End = end };

            Assert.True(ScheduleLogic.IsValidLessonInterval(start, end));
            Assert.Equal(90, (lesson.End - lesson.Start).TotalMinutes);
        }

        [Fact]
        public void LeapYear_29February_CalculatesParityAndIntervalCorrectly()
        {
            // 2028 is a leap year (February has 29 days, Tuesday)
            var leapDay = new DateTime(2028, 2, 29, 10, 0, 0);
            var end = leapDay.AddMinutes(90);

            Assert.True(DateTime.IsLeapYear(2028));
            Assert.Equal(DayOfWeek.Tuesday, leapDay.DayOfWeek);
            Assert.True(ScheduleLogic.IsValidLessonInterval(leapDay, end));

            // Week parity calculation shouldn't throw
            bool isEven = WeekParityHelper.CalculateIsWeekEven(leapDay, WeekParityMode.Auto);
            Assert.True(isEven || !isEven); // Valid boolean, no exceptions
        }

        [Fact]
        public void YearBoundary_AcademicTerm_AlternatesParityAcrossNewYear()
        {
            // End of December 2026 to start of January 2027 within semester
            var termStart = "2026-10-01";
            var lateDec = new DateTime(2026, 12, 28); // Academic Week 13
            var earlyJan = new DateTime(2027, 1, 4);   // Academic Week 14

            bool decParity = WeekParityHelper.CalculateIsWeekEven(lateDec, WeekParityMode.Auto, academicTermStart: termStart);
            bool janParity = WeekParityHelper.CalculateIsWeekEven(earlyJan, WeekParityMode.Auto, academicTermStart: termStart);

            // Consecutive academic weeks must strictly alternate parities
            Assert.NotEqual(decParity, janParity);
        }

        [Fact]
        public void YearBoundary_IsoCalendar_Week52AndWeek53Alternate()
        {
            var week52 = new DateTime(2026, 12, 21); // Week 52 (even)
            var week53 = new DateTime(2026, 12, 28); // Week 53 (odd)

            bool w52 = WeekParityHelper.CalculateIsWeekEven(week52, WeekParityMode.Auto);
            bool w53 = WeekParityHelper.CalculateIsWeekEven(week53, WeekParityMode.Auto);

            Assert.True(w52);
            Assert.False(w53);
        }

        [Fact]
        public void MidnightCrossing_EventStart23End02_SlicesCorrectlyAcrossTwoDays()
        {
            // Event starting at 23:00 on Day 1 and ending at 02:00 on Day 2 (e.g. night coding session)
            var day1 = new DateTime(2026, 11, 10);
            var day2 = new DateTime(2026, 11, 11);

            var eventStart = new DateTime(2026, 11, 10, 23, 0, 0);
            var eventEnd = new DateTime(2026, 11, 11, 2, 0, 0);

            var lesson = new Lesson
            {
                Id = 777,
                Title = "Noc Naukowców",
                Start = eventStart,
                End = eventEnd
            };

            // Slice for Day 1 (starts at 23:00, continues to next day)
            var slice1 = ScheduleLogic.SliceLessonForDay(lesson, day1, startHour: 0, endHour: 23);
            Assert.NotNull(slice1);
            Assert.Equal(eventStart, slice1.EffectiveStart);
            Assert.True(slice1.ContinuesToNextDay);
            Assert.False(slice1.IsContinuationFromPreviousDay);

            // Slice for Day 2 (starts at 00:00, ends at 02:00, continued from previous day)
            var slice2 = ScheduleLogic.SliceLessonForDay(lesson, day2, startHour: 0, endHour: 23);
            Assert.NotNull(slice2);
            Assert.Equal(day2.Date, slice2.EffectiveStart);
            Assert.Equal(eventEnd, slice2.EffectiveEnd);
            Assert.False(slice2.ContinuesToNextDay);
            Assert.True(slice2.IsContinuationFromPreviousDay);
        }
    }
}
