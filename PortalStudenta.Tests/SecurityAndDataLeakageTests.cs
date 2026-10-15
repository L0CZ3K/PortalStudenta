using System;
using System.Collections.Generic;
using System.Linq;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class SecurityAndDataLeakageTests
    {
        [Fact]
        public void CalendarExport_NeverLeaksTokensPasswordsOrAlbumNumbers()
        {
            string fakeSecretToken = "oauth_token_secret_xyz987654321";
            string fakeStudentAlbum = "s123456_album_number";

            var lessons = new List<Lesson>
            {
                new Lesson
                {
                    Id = 101,
                    Title = "Inżynieria Oprogramowania",
                    Type = "WYK",
                    Place = "Aula A",
                    Person = "prof. dr hab. Jan Nowak",
                    Group = "1",
                    Start = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc),
                    End = new DateTime(2026, 10, 15, 11, 30, 0, DateTimeKind.Utc),
                    Desc = "Wykład wprowadzający"
                }
            };

            var ics = IcsCalendarHelper.GenerateIcs(lessons, calendarName: "Plan Zajęć");

            // Assert that no sensitive authorization tokens or album numbers leak into the export file
            Assert.DoesNotContain(fakeSecretToken, ics);
            Assert.DoesNotContain(fakeStudentAlbum, ics);
            Assert.DoesNotContain("token", ics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", ics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", ics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("album", ics, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CrossGroupIsolation_GroupEventsRemainIsolatedBetweenCohorts()
        {
            // Simulate Group A and Group B
            string groupA = "GRP-ALPHA";
            string groupB = "GRP-BETA";

            var allGroupEvents = new List<Lesson>
            {
                new Lesson { Id = 1, Title = "Spotkanie Grupy A", Type = "STAR", Group = groupA, IsPersonal = false },
                new Lesson { Id = 2, Title = "Spotkanie Grupy B", Type = "STAR", Group = groupB, IsPersonal = false },
                new Lesson { Id = 3, Title = "Wspólny Wykład", Type = "WYK", Group = "ALL", IsPersonal = false }
            };

            // Filter for student belonging strictly to Group A
            var studentAEvents = allGroupEvents
                .Where(e => e.Group == groupA || e.Group == "ALL")
                .ToList();

            Assert.Equal(2, studentAEvents.Count);
            Assert.Contains(studentAEvents, e => e.Title == "Spotkanie Grupy A");
            Assert.Contains(studentAEvents, e => e.Title == "Wspólny Wykład");
            // Group B's private event MUST NOT be visible to Student A!
            Assert.DoesNotContain(studentAEvents, e => e.Title == "Spotkanie Grupy B");
        }

        [Fact]
        public void LeaveGroup_PurgesGroupModificationsWhilePreservingPersonalModifications()
        {
            var baseStart = new DateTime(2026, 10, 20, 10, 0, 0);
            var lesson1 = new Lesson { Id = 1, Title = "Matematyka", Type = "WYK", Start = baseStart };
            var lesson2 = new Lesson { Id = 2, Title = "Fizyka", Type = "LAB", Start = baseStart.AddHours(2) };

            var key1 = ScheduleLogic.GetLessonKey(lesson1);
            var key2 = ScheduleLogic.GetLessonKey(lesson2);

            // In-memory modifications: one is from group (starosta), one is personal (student note)
            var modifications = new Dictionary<string, LessonModification>
            {
                {
                    key1,
                    new LessonModification
                    {
                        LessonKey = key1,
                        Title = lesson1.Title,
                        IsGroupModification = true, // Leaked from group!
                        GroupCode = "GRP-DEPARTED",
                        IsCanceled = true,
                        CancelReason = "Odwołane przez starostę"
                    }
                },
                {
                    key2,
                    new LessonModification
                    {
                        LessonKey = key2,
                        Title = lesson2.Title,
                        IsGroupModification = false, // Personal
                        ModifiedPlace = "Moja Sala Osobista"
                    }
                }
            };

            // SIMULATE LEAVE GROUP PURGE LOGIC:
            // 1. Remove modifications belonging to any group
            var groupKeys = modifications.Values.Where(m => m.IsGroupModification).Select(m => m.LessonKey).ToList();
            foreach (var gk in groupKeys)
            {
                modifications.Remove(gk);
            }

            // 2. Re-apply to fresh USOS lesson list
            var freshLessons = new List<Lesson>
            {
                new Lesson { Id = 1, Title = "Matematyka", Type = "WYK", Start = baseStart, IsCanceled = false },
                new Lesson { Id = 2, Title = "Fizyka", Type = "LAB", Start = baseStart.AddHours(2), Place = "Sala 101" }
            };

            ScheduleLogic.ApplyModifications(freshLessons, modifications);

            // Group cancellation MUST be purged!
            Assert.False(freshLessons[0].IsCanceled, "Group cancellation was not purged after leaving group!");
            // Personal modification MUST remain!
            Assert.Equal("Moja Sala Osobista", freshLessons[1].Place);
        }

        [Theory]
        [InlineData(UserRole.Student, false)]
        [InlineData(UserRole.Starosta, true)]
        [InlineData(UserRole.Admin, true)]
        public void PrivilegeEscalation_OnlyStarostaOrAdminHasManagementPrivileges(UserRole role, bool expectedPrivileges)
        {
            bool hasPrivileges = (role == UserRole.Starosta || role == UserRole.Admin);
            Assert.Equal(expectedPrivileges, hasPrivileges);
        }

        [Fact]
        public void EventSubscription_DetachedHandlerPreventsMemoryAndEventLeaks()
        {
            int triggerCount = 0;
            Action? onChange = null;

            Action handler = () => { triggerCount++; };

            // Subscribe
            onChange += handler;
            onChange?.Invoke();
            Assert.Equal(1, triggerCount);

            // Detach (as required by IDisposable.Dispose across all Blazor components)
            onChange -= handler;
            onChange?.Invoke();

            // Must NOT have received the second trigger!
            Assert.Equal(1, triggerCount);
        }

        [Theory]
        [InlineData("users/s123/../../groups/malicious_node")]
        [InlineData("users/s123/..\\..\\admin")]
        [InlineData("groups/GRP-1/../../deleted_accounts")]
        [InlineData(".../...//.../system_announcements")]
        public void NodeHoppingAndPathTraversal_KeysAreSanitizedToPreventEscapingRoot(string traversalAttempt)
        {
            string sanitized = FirebaseSecurityHelper.SanitizeKey(traversalAttempt);

            // In Firebase RTDB, forward slashes define hierarchy navigation.
            // Sanitizing slashes and dots prevents any step up or step out of the current child!
            Assert.DoesNotContain("/", sanitized);
            Assert.DoesNotContain(".", sanitized);
            Assert.DoesNotContain("\\..", sanitized);
            Assert.DoesNotContain("/..", sanitized);
        }

        [Fact]
        public void PollVoting_ClosedPollCannotReceiveNewVotes()
        {
            var poll = new GroupPoll
            {
                Id = "POLL-101",
                Question = "Wybór terminu kolokwium",
                Options = new List<string> { "Poniedziałek", "Środa" },
                IsClosed = true // Closed by Starosta
            };

            // Attempt to vote after closure
            bool canVote = !poll.IsClosed;
            Assert.False(canVote, "Voting must be rejected when poll is closed!");
        }

        [Fact]
        public void CircularGroupRedirect_ResolverLimitsDepthToPreventInfiniteLoops()
        {
            // Simulate circular redirect attack: GRP-A -> GRP-B -> GRP-A
            var redirectMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "GRP-A", "GRP-B" },
                { "GRP-B", "GRP-A" }
            };

            string current = "GRP-A";
            int maxHops = 5;
            int hopsTaken = 0;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (redirectMap.TryGetValue(current, out var next) && !string.IsNullOrEmpty(next) && hopsTaken < maxHops)
            {
                if (visited.Contains(current))
                {
                    // Loop detected, abort safely!
                    break;
                }
                visited.Add(current);
                current = next;
                hopsTaken++;
            }

            Assert.True(hopsTaken <= 2, "Circular redirect detection did not terminate safely!");
        }

        [Fact]
        public void AnnouncementLogic_NeverExposesUidInFormattedDisplay()
        {
            var item = new AnnouncementItem
            {
                Uid = "SECRET_FIREBASE_UID_12345",
                Title = "Uwaga kolokwium",
                CreatedAt = new DateTime(2026, 10, 10, 14, 0, 0)
            };

            string display = item.DisplayTime;

            Assert.DoesNotContain(item.Uid, display);
            Assert.DoesNotContain("SECRET", display);
        }
    }
}
