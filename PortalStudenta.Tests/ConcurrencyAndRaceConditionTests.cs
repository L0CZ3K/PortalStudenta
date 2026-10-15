using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class ConcurrencyAndRaceConditionTests
    {
        [Fact]
        public void GenerateUniqueEventId_MassiveParallelGeneration_ProducesZeroCollisions()
        {
            const int totalIds = 50_000;
            var generatedIds = new ConcurrentDictionary<int, byte>();
            var exceptions = new ConcurrentBag<Exception>();

            Parallel.For(0, totalIds, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, i =>
            {
                try
                {
                    int id = ScheduleLogic.GenerateUniqueEventId();
                    Assert.True(id > 0, "Event ID must be positive.");
                    bool added = generatedIds.TryAdd(id, 0);
                    Assert.True(added, $"Collision detected on ID {id} at iteration {i}!");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
            Assert.Equal(totalIds, generatedIds.Count);
        }

        [Fact]
        public void ApplyModifications_ConcurrentReadsAndWrites_ThreadSafeAndConsistent()
        {
            var baseStart = new DateTime(2026, 10, 15, 10, 0, 0);
            var lessons = Enumerable.Range(1, 100).Select(i => new Lesson
            {
                Id = i,
                Title = $"Course {i}",
                Type = "LAB",
                Group = "1",
                Start = baseStart.AddHours(i),
                End = baseStart.AddHours(i + 1),
                Place = "OriginalRoom"
            }).ToList();

            var modifications = new ConcurrentDictionary<string, LessonModification>();
            for (int i = 1; i <= 100; i++)
            {
                var l = lessons[i - 1];
                var key = ScheduleLogic.GetLessonKey(l);
                modifications[key] = new LessonModification
                {
                    LessonKey = key,
                    Title = l.Title,
                    ModifiedPlace = $"NewRoom_{i}",
                    IsCanceled = (i % 2 == 0)
                };
            }

            var exceptions = new ConcurrentBag<Exception>();

            // Run 50 concurrent threads applying modifications to distinct copies of lesson lists
            Parallel.For(0, 50, new ParallelOptions { MaxDegreeOfParallelism = 16 }, threadIdx =>
            {
                try
                {
                    var threadLessons = lessons.Select(l => new Lesson
                    {
                        Id = l.Id,
                        Title = l.Title,
                        Type = l.Type,
                        Group = l.Group,
                        Start = l.Start,
                        End = l.End,
                        Place = l.Place
                    }).ToList();

                    ScheduleLogic.ApplyModifications(threadLessons, modifications);

                    // Verify consistency for every lesson
                    for (int i = 1; i <= 100; i++)
                    {
                        var tl = threadLessons[i - 1];
                        Assert.Equal($"NewRoom_{i}", tl.Place);
                        Assert.Equal(i % 2 == 0, tl.IsCanceled);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
        }

        [Fact]
        public void SanitizeKey_ConcurrentHighThroughput_NeverThrowsOrCorrupts()
        {
            const int iterations = 20_000;
            var exceptions = new ConcurrentBag<Exception>();

            Parallel.For(0, iterations, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
            {
                try
                {
                    string input = $"User.Key_{i}$[#]/_Token";
                    string sanitized = FirebaseSecurityHelper.SanitizeKey(input);

                    Assert.NotNull(sanitized);
                    Assert.DoesNotContain(".", sanitized);
                    Assert.DoesNotContain("$", sanitized);
                    Assert.DoesNotContain("#", sanitized);
                    Assert.DoesNotContain("[", sanitized);
                    Assert.DoesNotContain("]", sanitized);
                    Assert.DoesNotContain("/", sanitized);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
        }

        [Fact]
        public void CalculateIsWeekEven_ConcurrentHighThroughputAcrossDates_Deterministic()
        {
            const int totalDates = 10_000;
            var exceptions = new ConcurrentBag<Exception>();
            var baseDate = new DateTime(2026, 1, 1);

            Parallel.For(0, totalDates, new ParallelOptions { MaxDegreeOfParallelism = 16 }, dayOffset =>
            {
                try
                {
                    var dt = baseDate.AddDays(dayOffset);
                    bool auto1 = WeekParityHelper.CalculateIsWeekEven(dt, WeekParityMode.Auto);
                    bool auto2 = WeekParityHelper.CalculateIsWeekEven(dt, WeekParityMode.Auto);
                    bool inv = WeekParityHelper.CalculateIsWeekEven(dt, WeekParityMode.Inverted);

                    Assert.Equal(auto1, auto2);
                    Assert.NotEqual(auto1, inv);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.Empty(exceptions);
        }
    }
}
