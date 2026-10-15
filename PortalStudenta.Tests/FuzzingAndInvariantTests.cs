using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class FuzzingAndInvariantTests
    {
        private static readonly string[] ChaosTokens = new[]
        {
            ".", "$", "#", "[", "]", "/", "\\", "\0", "\n", "\r", "\t",
            "'", "\"", ";", "-", "+", "%", "&", "<", ">", "=", "?", "@",
            " ", "a", "Z", "0", "9", "ą", "ę", "ś", "ć", "ź", "ż", "ó",
            "🚀", "🔥", "🎉", "🧠", "🤖", "📚"
        };

        [Fact]
        public void Fuzz_SanitizeKey_StrictFirebaseInvariantsAcross5000Inputs()
        {
            var rand = new Random(42); // Deterministic seed for reproducible testing

            for (int i = 0; i < 5000; i++)
            {
                int len = rand.Next(1, 100);
                var sb = new StringBuilder(len);
                for (int c = 0; c < len; c++)
                {
                    sb.Append(ChaosTokens[rand.Next(ChaosTokens.Length)]);
                }
                string candidate = sb.ToString();

                string sanitized = FirebaseSecurityHelper.SanitizeKey(candidate);

                // Invariant 1: Result cannot be null or empty
                Assert.False(string.IsNullOrEmpty(sanitized), $"Sanitized output was empty for candidate: '{candidate}'");

                // Invariant 2: None of the 6 forbidden Firebase characters can ever be present
                Assert.DoesNotContain(".", sanitized);
                Assert.DoesNotContain("$", sanitized);
                Assert.DoesNotContain("#", sanitized);
                Assert.DoesNotContain("[", sanitized);
                Assert.DoesNotContain("]", sanitized);
                Assert.DoesNotContain("/", sanitized);

                // Invariant 3: Result must be stable across repeated calls
                string secondCall = FirebaseSecurityHelper.SanitizeKey(candidate);
                Assert.Equal(sanitized, secondCall);
            }
        }

        [Fact]
        public void Fuzz_SliceLessonForDay_MathematicalBoundaryInvariantsAcross2500Runs()
        {
            var rand = new Random(1337);
            var testDay = new DateTime(2026, 10, 15);
            int startHour = 8;
            int endHour = 20;
            var dayStart = testDay.Date.AddHours(startHour);
            var dayEnd = testDay.Date.AddHours(endHour + 1);

            for (int i = 0; i < 2500; i++)
            {
                // Random offset from -3 days to +3 days
                int startDayOffset = rand.Next(-3, 4);
                int startH = rand.Next(0, 24);
                int startM = rand.Next(0, 60);

                // Random duration from -2 hours (invalid) up to 72 hours (multi-day)
                int durationMinutes = rand.Next(-120, 72 * 60);

                var actStart = testDay.AddDays(startDayOffset).AddHours(startH).AddMinutes(startM);
                var actEnd = actStart.AddMinutes(durationMinutes);

                var lesson = new Lesson
                {
                    Id = i + 1,
                    Title = $"Fuzz Lesson {i}",
                    Start = actStart,
                    End = actEnd
                };

                var slice = ScheduleLogic.SliceLessonForDay(lesson, testDay, startHour, endHour);

                if (slice == null)
                {
                    // If null, it MUST be either invalid (end <= start) or not intersecting testDay
                    bool wasInvalid = actEnd <= actStart;
                    bool outsideDay = actStart.Date > testDay.Date || actEnd.Date < testDay.Date;
                    bool outsideViewingHours = actStart >= dayEnd || actEnd <= dayStart;
                    Assert.True(wasInvalid || outsideDay || outsideViewingHours);
                }
                else
                {
                    // INVARIANT 1: EffectiveEnd MUST be strictly greater than EffectiveStart
                    Assert.True(slice.EffectiveEnd > slice.EffectiveStart, $"Invalid interval: {slice.EffectiveStart} >= {slice.EffectiveEnd}");

                    // INVARIANT 2: Slices are strictly clamped to viewing boundaries
                    Assert.True(slice.EffectiveStart >= dayStart, $"EffectiveStart {slice.EffectiveStart} was before dayStart {dayStart}");
                    Assert.True(slice.EffectiveEnd <= dayEnd, $"EffectiveEnd {slice.EffectiveEnd} was after dayEnd {dayEnd}");

                    // INVARIANT 3: Continuation flags match actual calendar day crossings
                    Assert.Equal(actStart.Date < testDay.Date, slice.IsContinuationFromPreviousDay);
                    Assert.Equal(actEnd.Date > testDay.Date, slice.ContinuesToNextDay);
                }
            }
        }

        [Fact]
        public void Fuzz_CalculateEventLayout_CollisionInvariantsNeverViolated()
        {
            var rand = new Random(2026);
            var day = new DateTime(2026, 10, 15);

            for (int run = 0; run < 150; run++)
            {
                int eventCount = rand.Next(2, 35);
                var slices = new List<ScheduleLogic.CalendarEventSlice>();

                for (int ev = 0; ev < eventCount; ev++)
                {
                    int startH = rand.Next(8, 18);
                    int startM = rand.Next(0, 4) * 15;
                    int durM = rand.Next(1, 6) * 30; // 30 min to 3 hours

                    var start = day.AddHours(startH).AddMinutes(startM);
                    var end = start.AddMinutes(durM);

                    slices.Add(new ScheduleLogic.CalendarEventSlice
                    {
                        Lesson = new Lesson { Id = ev + 1, Title = $"Ev {ev}" },
                        EffectiveStart = start,
                        EffectiveEnd = end
                    });
                }

                var layout = ScheduleLogic.CalculateEventLayout(slices);

                Assert.Equal(slices.Count, layout.Count);

                // INVARIANT 1: For any two events that overlap in time, their columns MUST be distinct!
                for (int a = 0; a < layout.Count; a++)
                {
                    for (int b = a + 1; b < layout.Count; b++)
                    {
                        var evA = layout[a];
                        var evB = layout[b];

                        bool timeOverlap = evA.EffectiveStart < evB.EffectiveEnd && evA.EffectiveEnd > evB.EffectiveStart;
                        if (timeOverlap)
                        {
                            Assert.NotEqual(evA.Column, evB.Column);
                        }
                    }
                }

                // INVARIANT 2: Column must be in range [0, TotalColumns - 1]
                foreach (var evItem in layout)
                {
                    Assert.True(evItem.Column >= 0);
                    Assert.True(evItem.Column < evItem.TotalColumns);
                    Assert.True(evItem.TotalColumns >= 1);
                }
            }
        }
    }
}
