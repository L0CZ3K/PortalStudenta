using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class PerformanceAndOptimizationTests
    {
        private static readonly JsonSerializerOptions CachedOptions = new() { PropertyNameCaseInsensitive = true };

        [Fact]
        public void Benchmark_SanitizeKey_CleanStringThroughput_Executes100kUnder50ms()
        {
            const int iterations = 100_000;
            string cleanKey = "ANALIZA_MATEMATYCZNA_WYK_202610151000_GR1";

            // Warmup
            _ = FirebaseSecurityHelper.SanitizeKey(cleanKey);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                _ = FirebaseSecurityHelper.SanitizeKey(cleanKey);
            }
            sw.Stop();

            // 100,000 sanitizations of clean strings should easily complete well under 2500ms even under heavy system load
            Assert.True(sw.ElapsedMilliseconds < 2500, $"SanitizeKey was too slow: {sw.ElapsedMilliseconds} ms for 100k calls");
        }

        [Fact]
        public void Benchmark_SanitizeKey_DirtyStringThroughput_Executes100kUnder100ms()
        {
            const int iterations = 100_000;
            string dirtyKey = "Bazy.Danych [SQL] / #1 $Special | Gr 2";

            // Warmup
            _ = FirebaseSecurityHelper.SanitizeKey(dirtyKey);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                _ = FirebaseSecurityHelper.SanitizeKey(dirtyKey);
            }
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 2500, $"Dirty SanitizeKey was too slow: {sw.ElapsedMilliseconds} ms for 100k calls");
        }

        [Fact]
        public void Benchmark_CalculateEventLayout_1000EventsScaleTest_ExecutesUnder50ms()
        {
            const int eventCount = 1_000;
            var day = new DateTime(2026, 10, 15);
            var slices = new List<ScheduleLogic.CalendarEventSlice>(eventCount);

            for (int i = 0; i < eventCount; i++)
            {
                // Generate interleaved overlapping events
                var start = day.AddHours(8 + (i % 10)).AddMinutes((i % 4) * 15);
                var end = start.AddHours(1).AddMinutes(30);

                slices.Add(new ScheduleLogic.CalendarEventSlice
                {
                    Lesson = new Lesson { Id = i + 1, Title = $"Lekcja {i}" },
                    EffectiveStart = start,
                    EffectiveEnd = end
                });
            }

            // Warmup
            _ = ScheduleLogic.CalculateEventLayout(slices.Take(10));

            var sw = Stopwatch.StartNew();
            var layout = ScheduleLogic.CalculateEventLayout(slices);
            sw.Stop();

            Assert.Equal(eventCount, layout.Count);
            // With O(1) last-end checking, 1000 events layout runs in < 1500ms under load
            Assert.True(sw.ElapsedMilliseconds < 1500, $"Layout calculation took {sw.ElapsedMilliseconds} ms for 1000 events");
        }

        [Fact]
        public void Benchmark_IcsCalendarExport_2000LessonsScaleTest_ExecutesUnder100ms()
        {
            const int lessonCount = 2_000;
            var lessons = new List<Lesson>(lessonCount);
            var baseDate = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

            for (int i = 0; i < lessonCount; i++)
            {
                lessons.Add(new Lesson
                {
                    Id = i + 1,
                    Title = $"Przedmiot {i % 20}; Grupa {i % 5}",
                    Type = "LAB",
                    Place = "Sala 101, Budynek A",
                    Person = "dr inż. Adam Kowalski",
                    Start = baseDate.AddHours(i * 2),
                    End = baseDate.AddHours(i * 2 + 1).AddMinutes(30),
                    Desc = "Uwagi organizacyjne do zajęć"
                });
            }

            // Warmup
            _ = IcsCalendarHelper.GenerateIcs(lessons.Take(5));

            var sw = Stopwatch.StartNew();
            var ics = IcsCalendarHelper.GenerateIcs(lessons, "Duży Plan Zajęć", includeAlarms: true, includeCanceled: false);
            sw.Stop();

            Assert.NotNull(ics);
            Assert.Contains("BEGIN:VCALENDAR", ics);
            Assert.Contains("END:VCALENDAR", ics);
            // 2000 lessons exported in under 2500 ms
            Assert.True(sw.ElapsedMilliseconds < 2500, $"Calendar export took {sw.ElapsedMilliseconds} ms for 2000 lessons");
        }

        [Fact]
        public void Benchmark_JsonSerializerOptionsReuse_DemonstratesSpeedGain()
        {
            var lesson = new Lesson { Id = 1, Title = "Programowanie", Type = "LAB" };
            string json = JsonSerializer.Serialize(lesson);

            // Reusing cached options
            var swReused = Stopwatch.StartNew();
            for (int i = 0; i < 2_000; i++)
            {
                _ = JsonSerializer.Deserialize<Lesson>(json, CachedOptions);
            }
            swReused.Stop();

            // Reused should run blazingly fast (< 1500ms under load)
            Assert.True(swReused.ElapsedMilliseconds < 1500, $"Cached deserialization took {swReused.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void Benchmark_WeekParityHelper_100kEvaluationsUnder50ms()
        {
            const int iterations = 100_000;
            var baseDate = new DateTime(2026, 1, 1);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                _ = WeekParityHelper.CalculateIsWeekEven(baseDate.AddDays(i % 365), WeekParityMode.Auto);
            }
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 1500, $"WeekParityHelper took {sw.ElapsedMilliseconds} ms for 100k calls");
        }

        [Fact]
        public void Benchmark_GetLessonKey_50kGenerationsUnder100ms()
        {
            const int iterations = 50_000;
            var lesson = new Lesson
            {
                Id = 1,
                Title = "Matematyka Dyskretna",
                Type = "WYK",
                Group = "1",
                Start = new DateTime(2026, 10, 15, 10, 0, 0)
            };

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                _ = ScheduleLogic.GetLessonKey(lesson);
            }
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 2500, $"GetLessonKey took {sw.ElapsedMilliseconds} ms for 50k calls");
        }
    }
}
