using System;
using System.Collections.Generic;
using System.Linq;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class ScheduleLayoutAndClusteringTests
    {
        [Fact]
        public void SliceLessonForDay_StandardSingleDayLesson_ReturnsEffectiveBoundsWithoutFlags()
        {
            var day = new DateTime(2026, 10, 15);
            var lesson = new Lesson
            {
                Id = 1,
                Title = "Analiza Matematyczna",
                Start = new DateTime(2026, 10, 15, 10, 15, 0),
                End = new DateTime(2026, 10, 15, 11, 45, 0)
            };

            var slice = ScheduleLogic.SliceLessonForDay(lesson, day, startHour: 7, endHour: 21);

            Assert.NotNull(slice);
            Assert.Equal(lesson.Start, slice.EffectiveStart);
            Assert.Equal(lesson.End, slice.EffectiveEnd);
            Assert.False(slice.IsContinuationFromPreviousDay);
            Assert.False(slice.ContinuesToNextDay);
        }

        [Fact]
        public void SliceLessonForDay_MultiDayEvent_StartDay_ClampedToDayEndAndContinuesToNextDay()
        {
            // Event from Friday 16:00 to Sunday 14:00 (e.g. 48h Hackathon)
            var friday = new DateTime(2026, 10, 16);
            var lesson = new Lesson
            {
                Id = 100,
                Title = "Hackathon AI",
                Start = new DateTime(2026, 10, 16, 16, 0, 0),
                End = new DateTime(2026, 10, 18, 14, 0, 0)
            };

            var slice = ScheduleLogic.SliceLessonForDay(lesson, friday, startHour: 8, endHour: 21);

            Assert.NotNull(slice);
            Assert.Equal(new DateTime(2026, 10, 16, 16, 0, 0), slice.EffectiveStart);
            // End should be clamped to 22:00 (endHour 21 + 1)
            Assert.Equal(new DateTime(2026, 10, 16, 22, 0, 0), slice.EffectiveEnd);
            Assert.False(slice.IsContinuationFromPreviousDay);
            Assert.True(slice.ContinuesToNextDay);
        }

        [Fact]
        public void SliceLessonForDay_MultiDayEvent_EndDay_ClampedToDayStartAndContinuesFromPreviousDay()
        {
            var sunday = new DateTime(2026, 10, 18);
            var lesson = new Lesson
            {
                Id = 100,
                Title = "Hackathon AI",
                Start = new DateTime(2026, 10, 16, 16, 0, 0),
                End = new DateTime(2026, 10, 18, 14, 0, 0)
            };

            var slice = ScheduleLogic.SliceLessonForDay(lesson, sunday, startHour: 8, endHour: 21);

            Assert.NotNull(slice);
            // Start should be clamped to 08:00
            Assert.Equal(new DateTime(2026, 10, 18, 8, 0, 0), slice.EffectiveStart);
            Assert.Equal(new DateTime(2026, 10, 18, 14, 0, 0), slice.EffectiveEnd);
            Assert.True(slice.IsContinuationFromPreviousDay);
            Assert.False(slice.ContinuesToNextDay);
        }

        [Fact]
        public void SliceLessonForDay_MultiDayEvent_MiddleDay_SpansWholeDayWithBothFlagsSet()
        {
            var saturday = new DateTime(2026, 10, 17);
            var lesson = new Lesson
            {
                Id = 100,
                Title = "Hackathon AI",
                Start = new DateTime(2026, 10, 16, 16, 0, 0),
                End = new DateTime(2026, 10, 18, 14, 0, 0)
            };

            var slice = ScheduleLogic.SliceLessonForDay(lesson, saturday, startHour: 8, endHour: 21);

            Assert.NotNull(slice);
            Assert.Equal(new DateTime(2026, 10, 17, 8, 0, 0), slice.EffectiveStart);
            Assert.Equal(new DateTime(2026, 10, 17, 22, 0, 0), slice.EffectiveEnd);
            Assert.True(slice.IsContinuationFromPreviousDay);
            Assert.True(slice.ContinuesToNextDay);
        }

        [Fact]
        public void SliceLessonForDay_EventOutsideDay_ReturnsNull()
        {
            var monday = new DateTime(2026, 10, 12);
            var lesson = new Lesson
            {
                Title = "Tuesday Class",
                Start = new DateTime(2026, 10, 13, 10, 0, 0),
                End = new DateTime(2026, 10, 13, 11, 30, 0)
            };

            var slice = ScheduleLogic.SliceLessonForDay(lesson, monday);
            Assert.Null(slice);
        }

        [Fact]
        public void SliceLessonForDay_ZeroOrNegativeDuration_ReturnsNull()
        {
            var day = new DateTime(2026, 10, 12);
            var invalidLesson = new Lesson
            {
                Title = "Invalid",
                Start = new DateTime(2026, 10, 12, 12, 0, 0),
                End = new DateTime(2026, 10, 12, 10, 0, 0)
            };

            Assert.Null(ScheduleLogic.SliceLessonForDay(invalidLesson, day));
            Assert.Null(ScheduleLogic.SliceLessonForDay(null, day));
        }

        [Fact]
        public void CalculateEventLayout_NonOverlappingLessons_SingleColumn()
        {
            var day = new DateTime(2026, 10, 12);
            var l1 = new ScheduleLogic.CalendarEventSlice
            {
                EffectiveStart = day.AddHours(8),
                EffectiveEnd = day.AddHours(9.5)
            };
            var l2 = new ScheduleLogic.CalendarEventSlice
            {
                EffectiveStart = day.AddHours(10),
                EffectiveEnd = day.AddHours(11.5)
            };
            var l3 = new ScheduleLogic.CalendarEventSlice
            {
                EffectiveStart = day.AddHours(12),
                EffectiveEnd = day.AddHours(13.5)
            };

            var result = ScheduleLogic.CalculateEventLayout(new[] { l1, l2, l3 });

            Assert.Equal(3, result.Count);
            Assert.All(result, r => Assert.Equal(0, r.Column));
            Assert.All(result, r => Assert.Equal(1, r.TotalColumns));
        }

        [Fact]
        public void CalculateEventLayout_TwoSimultaneousLessons_TwoColumns()
        {
            var day = new DateTime(2026, 10, 12);
            var l1 = new ScheduleLogic.CalendarEventSlice
            {
                EffectiveStart = day.AddHours(10),
                EffectiveEnd = day.AddHours(11.5)
            };
            var l2 = new ScheduleLogic.CalendarEventSlice
            {
                EffectiveStart = day.AddHours(10),
                EffectiveEnd = day.AddHours(11.5)
            };

            var result = ScheduleLogic.CalculateEventLayout(new[] { l1, l2 });

            Assert.Equal(2, result.Count);
            Assert.Contains(result, r => r.Column == 0 && r.TotalColumns == 2);
            Assert.Contains(result, r => r.Column == 1 && r.TotalColumns == 2);
        }

        [Fact]
        public void CalculateEventLayout_ThreeSimultaneousLessons_ThreeColumns()
        {
            var day = new DateTime(2026, 10, 12);
            var l1 = new ScheduleLogic.CalendarEventSlice { EffectiveStart = day.AddHours(10), EffectiveEnd = day.AddHours(12) };
            var l2 = new ScheduleLogic.CalendarEventSlice { EffectiveStart = day.AddHours(10), EffectiveEnd = day.AddHours(12) };
            var l3 = new ScheduleLogic.CalendarEventSlice { EffectiveStart = day.AddHours(10), EffectiveEnd = day.AddHours(12) };

            var result = ScheduleLogic.CalculateEventLayout(new[] { l1, l2, l3 });

            Assert.Equal(3, result.Count);
            Assert.All(result, r => Assert.Equal(3, r.TotalColumns));
            var columns = result.Select(r => r.Column).OrderBy(c => c).ToList();
            Assert.Equal(new[] { 0, 1, 2 }, columns);
        }

        [Fact]
        public void CalculateEventLayout_StaggeredOverlaps_SeparatesColumnsAccurately()
        {
            // Event 1: 10:00 - 11:30
            // Event 2: 11:00 - 12:30 (overlaps with Event 1)
            // Event 3: 11:45 - 13:00 (overlaps with Event 2, but NOT with Event 1)
            var day = new DateTime(2026, 10, 12);
            var l1 = new ScheduleLogic.CalendarEventSlice { EffectiveStart = day.AddHours(10), EffectiveEnd = day.AddHours(11.5) };
            var l2 = new ScheduleLogic.CalendarEventSlice { EffectiveStart = day.AddHours(11), EffectiveEnd = day.AddHours(12.5) };
            var l3 = new ScheduleLogic.CalendarEventSlice { EffectiveStart = day.AddHours(11.75), EffectiveEnd = day.AddHours(13) };

            var result = ScheduleLogic.CalculateEventLayout(new[] { l1, l2, l3 });

            Assert.Equal(3, result.Count);
            // In the cluster, no two overlapping items share the same column
            var ev1 = result.First(r => r.EffectiveStart == day.AddHours(10));
            var ev2 = result.First(r => r.EffectiveStart == day.AddHours(11));
            var ev3 = result.First(r => r.EffectiveStart == day.AddHours(11.75));

            Assert.NotEqual(ev1.Column, ev2.Column);
            Assert.NotEqual(ev2.Column, ev3.Column);
            // ev1 and ev3 do not overlap, so ev3 can reuse ev1's column!
            Assert.Equal(ev1.Column, ev3.Column);
        }

        [Fact]
        public void CalculateEventLayout_NullOrEmptyInput_ReturnsEmpty()
        {
            Assert.Empty(ScheduleLogic.CalculateEventLayout(null));
            Assert.Empty(ScheduleLogic.CalculateEventLayout(new List<ScheduleLogic.CalendarEventSlice>()));
        }
    }
}
