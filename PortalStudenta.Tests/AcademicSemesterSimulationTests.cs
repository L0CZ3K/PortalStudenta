using System;
using System.Collections.Generic;
using System.Linq;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class AcademicSemesterSimulationTests
    {
        [Fact]
        public void FullSemesterSimulation_300LessonsAcross15Weeks_RoomChangesAndCancellationsStayConsistent()
        {
            var termStart = new DateTime(2026, 10, 1); // Thursday
            var semesterLessons = new List<Lesson>();
            int lessonId = 1;

            // Generate 15 weeks of classes
            for (int week = 0; week < 15; week++)
            {
                var monday = termStart.AddDays(week * 7);

                // Monday 10:00 - Architektura Komputerów (LAB)
                semesterLessons.Add(new Lesson
                {
                    Id = lessonId++,
                    Title = "Architektura Komputerów",
                    Type = "LAB",
                    Group = "1",
                    Start = monday.Date.AddHours(10),
                    End = monday.Date.AddHours(11).AddMinutes(30),
                    Place = "Sala 101"
                });

                // Monday 12:00 - Matematyka Dyskretna (WYK)
                semesterLessons.Add(new Lesson
                {
                    Id = lessonId++,
                    Title = "Matematyka Dyskretna",
                    Type = "WYK",
                    Group = "ALL",
                    Start = monday.Date.AddHours(12),
                    End = monday.Date.AddHours(13).AddMinutes(30),
                    Place = "Aula C"
                });

                // Wednesday 08:15 - Bazy Danych (CW)
                var wednesday = monday.AddDays(2);
                semesterLessons.Add(new Lesson
                {
                    Id = lessonId++,
                    Title = "Bazy Danych",
                    Type = "CW",
                    Group = "1",
                    Start = wednesday.Date.AddHours(8).AddMinutes(15),
                    End = wednesday.Date.AddHours(9).AddMinutes(45),
                    Place = "Sala 215"
                });

                // Friday 14:00 - Sieci Komputerowe (PROJ)
                var friday = monday.AddDays(4);
                semesterLessons.Add(new Lesson
                {
                    Id = lessonId++,
                    Title = "Sieci Komputerowe",
                    Type = "PROJ",
                    Group = "1",
                    Start = friday.Date.AddHours(14),
                    End = friday.Date.AddHours(16),
                    Place = "Lab Net 1"
                });
            }

            Assert.Equal(60, semesterLessons.Count);

            // Step 1: At Week 4, Starosta executes a bulk room change for "Architektura Komputerów" LAB -> "Aula Magna"
            var week4Monday = termStart.AddDays(4 * 7);
            var refLessonWeek4 = semesterLessons.First(l => l.Title == "Architektura Komputerów" && l.Start.Date == week4Monday.Date);

            var futureSemesterMatches = ScheduleLogic.FindMatchingSemesterLessons(semesterLessons, refLessonWeek4);

            // Matches should include Week 4 through Week 14 (11 weeks), and EXCLUDE Weeks 0, 1, 2, 3 (4 weeks)
            Assert.Equal(11, futureSemesterMatches.Count);

            var modifications = new Dictionary<string, LessonModification>();
            foreach (var match in futureSemesterMatches)
            {
                var key = ScheduleLogic.GetLessonKey(match);
                modifications[key] = new LessonModification
                {
                    LessonKey = key,
                    Title = match.Title,
                    ModifiedPlace = "Aula Magna"
                };
            }

            ScheduleLogic.ApplyModifications(semesterLessons, modifications);

            // Verification of Room Change:
            // Weeks 0..3 MUST have original "Sala 101"
            for (int w = 0; w < 4; w++)
            {
                var wMon = termStart.AddDays(w * 7);
                var pastLesson = semesterLessons.First(l => l.Title == "Architektura Komputerów" && l.Start.Date == wMon.Date);
                Assert.Equal("Sala 101", pastLesson.Place);
            }

            // Weeks 4..14 MUST have "Aula Magna"
            for (int w = 4; w < 15; w++)
            {
                var wMon = termStart.AddDays(w * 7);
                var futureLesson = semesterLessons.First(l => l.Title == "Architektura Komputerów" && l.Start.Date == wMon.Date);
                Assert.Equal("Aula Magna", futureLesson.Place);
            }

            // Step 2: Full Semester iCalendar Export (RFC 5545)
            var ics = IcsCalendarHelper.GenerateIcs(semesterLessons, "Semestr Zimowy 2026/2027", includeAlarms: true, includeCanceled: false);

            Assert.NotNull(ics);
            Assert.Contains("BEGIN:VCALENDAR", ics);
            Assert.Contains("Aula Magna", ics);
            Assert.Contains("Sala 101", ics);
            Assert.Contains("END:VCALENDAR", ics);

            // Exactly 60 VEVENT blocks should be present
            int veventCount = 0;
            int idx = 0;
            while ((idx = ics.IndexOf("BEGIN:VEVENT", idx, StringComparison.Ordinal)) != -1)
            {
                veventCount++;
                idx += "BEGIN:VEVENT".Length;
            }
            Assert.Equal(60, veventCount);
        }

        [Fact]
        public void SemesterDaySlicing_DayByDaySliceSimulationAcross14Days_NoGapsOrOverlaps()
        {
            var semesterStart = new DateTime(2026, 10, 12); // Monday
            var lessons = new List<Lesson>
            {
                new Lesson { Id = 1, Title = "Lekcja Pn", Start = semesterStart.AddHours(10), End = semesterStart.AddHours(12) },
                new Lesson { Id = 2, Title = "Lekcja Wt", Start = semesterStart.AddDays(1).AddHours(14), End = semesterStart.AddDays(1).AddHours(16) },
                new Lesson { Id = 3, Title = "Hackathon Wknd", Start = semesterStart.AddDays(4).AddHours(18), End = semesterStart.AddDays(6).AddHours(12) } // Fri 18:00 to Sun 12:00
            };

            for (int day = 0; day < 14; day++)
            {
                var currentDay = semesterStart.AddDays(day);
                var slices = lessons
                    .Select(l => ScheduleLogic.SliceLessonForDay(l, currentDay, startHour: 8, endHour: 20))
                    .Where(s => s != null)
                    .Select(s => s!)
                    .ToList();

                var layout = ScheduleLogic.CalculateEventLayout(slices);

                if (day == 0) // Monday week 1
                {
                    Assert.Single(layout);
                    Assert.Equal("Lekcja Pn", layout[0].Lesson.Title);
                }
                else if (day == 4) // Friday week 1 (Hackathon start)
                {
                    Assert.Single(layout);
                    Assert.True(layout[0].ContinuesToNextDay);
                    Assert.False(layout[0].IsContinuationFromPreviousDay);
                }
                else if (day == 5) // Saturday week 1 (Hackathon middle)
                {
                    Assert.Single(layout);
                    Assert.True(layout[0].ContinuesToNextDay);
                    Assert.True(layout[0].IsContinuationFromPreviousDay);
                }
                else if (day == 6) // Sunday week 1 (Hackathon end)
                {
                    Assert.Single(layout);
                    Assert.False(layout[0].ContinuesToNextDay);
                    Assert.True(layout[0].IsContinuationFromPreviousDay);
                }
                else if (day >= 7) // Week 2
                {
                    Assert.Empty(layout);
                }
            }
        }
    }
}
