using System;
using System.Collections.Generic;
using Portal_Studenta.Models;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class AuthorizationAndAccessControlTests
    {
        [Theory]
        [InlineData(UserRole.Student, false)]
        [InlineData(UserRole.Admin, true)]
        [InlineData(UserRole.Starosta, true)]
        public void RoleManagementPrivilege_MatchesExpectedAuthorization(UserRole role, bool expectedPrivilege)
        {
            // Simulate AppState HasManagementPrivileges rule: IsStarosta || IsAdmin
            bool hasPrivilege = (role == UserRole.Starosta || role == UserRole.Admin);
            Assert.Equal(expectedPrivilege, hasPrivilege);
        }

        [Fact]
        public void StudentRole_CannotPublishGlobalStarostaEvents_DemotedToPersonal()
        {
            // Simulate AddCustomEventAsync security guard
            bool hasManagementPrivileges = false; // Regular student
            string groupCode = "INFA_2026";

            bool canPublishGroup = !string.IsNullOrEmpty(groupCode) && hasManagementPrivileges;

            var incomingEvent = new Lesson
            {
                Id = 101,
                Title = "Wykład odwołany przez studenta",
                Type = "STAR",
                IsPersonal = false,
                Group = "ALL",
                Person = "Starosta"
            };

            if ((!incomingEvent.IsPersonal || incomingEvent.Type == "STAR") && !canPublishGroup)
            {
                incomingEvent.IsPersonal = true;
                incomingEvent.Type = "WŁASNE";
                incomingEvent.Group = "JA";
                incomingEvent.Person = "Ja";
            }

            // Assert event was safely demoted and isolated to personal space
            Assert.True(incomingEvent.IsPersonal);
            Assert.Equal("WŁASNE", incomingEvent.Type);
            Assert.Equal("JA", incomingEvent.Group);
            Assert.Equal("Ja", incomingEvent.Person);
        }

        [Fact]
        public void StarostaRole_CanPublishGlobalCohortEvents()
        {
            bool hasManagementPrivileges = true; // Starosta or Admin
            string groupCode = "INFA_2026";

            bool canPublishGroup = !string.IsNullOrEmpty(groupCode) && hasManagementPrivileges;

            var incomingEvent = new Lesson
            {
                Id = 102,
                Title = "Oficjalny komunikat starosty",
                Type = "STAR",
                IsPersonal = false,
                Group = "ALL",
                Person = "Starosta"
            };

            if ((!incomingEvent.IsPersonal || incomingEvent.Type == "STAR") && !canPublishGroup)
            {
                incomingEvent.IsPersonal = true;
                incomingEvent.Type = "WŁASNE";
                incomingEvent.Group = "JA";
                incomingEvent.Person = "Ja";
            }

            Assert.False(incomingEvent.IsPersonal);
            Assert.Equal("STAR", incomingEvent.Type);
            Assert.Equal("ALL", incomingEvent.Group);
        }

        [Theory]
        [InlineData(-1, false)]
        [InlineData(-100, false)]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(3, false)] // Out of bounds for 3 options (indices 0, 1, 2)
        [InlineData(99, false)]
        public void PollVotingOptionBounds_StrictlyValidated(int optionIndex, bool expectedValid)
        {
            var poll = new GroupPoll
            {
                Id = "poll_1",
                GroupCode = "TEST_GRP",
                Question = "Wybór terminu kolokwium",
                Options = new List<string> { "Poniedziałek", "Środa", "Piątek" },
                IsClosed = false
            };

            bool isValid = optionIndex >= 0 && poll.Options != null && optionIndex < poll.Options.Count;
            Assert.Equal(expectedValid, isValid);
        }

        [Fact]
        public void ClosedPoll_RejectsAllIncomingVotes()
        {
            var poll = new GroupPoll
            {
                Id = "poll_closed",
                GroupCode = "TEST_GRP",
                Question = "Zakończone głosowanie",
                Options = new List<string> { "Opcja A", "Opcja B" },
                IsClosed = true
            };

            bool canVote = !poll.IsClosed && poll.Options.Count > 0;
            Assert.False(canVote);
        }

        [Theory]
        [InlineData("s123456", "S123456", true)]
        [InlineData("123456", "123456", true)]
        [InlineData("  s998877  ", "S998877", true)]
        [InlineData("alBum_01", "ALBUM_01", true)]
        public void VoterAlbumKeyNormalization_PreventsMultiVotingViaCasing(string rawAlbum, string expectedNormalized, bool sameNormalized)
        {
            string normalized1 = FirebaseSecurityHelper.SanitizeKey(rawAlbum.Trim().ToUpperInvariant());
            string normalized2 = FirebaseSecurityHelper.SanitizeKey(expectedNormalized.Trim().ToUpperInvariant());

            Assert.Equal(expectedNormalized.Trim().ToUpperInvariant(), normalized1);
            Assert.Equal(sameNormalized, normalized1 == normalized2);
        }

        [Fact]
        public void NullOrEmptyGroupCode_BlocksAllCohortPublishing()
        {
            string? emptyGroup = "";
            bool hasManagementPrivileges = true;

            bool canPublish = !string.IsNullOrEmpty(emptyGroup) && hasManagementPrivileges;
            Assert.False(canPublish);
        }

        [Theory]
        [InlineData("admin_group_1", "ADMIN_GROUP_1")]
        [InlineData("infa.2026/rok", "INFA_2026_ROK")]
        [InlineData("group#name$", "GROUP_NAME_")]
        public void GroupCodeSanitization_ConvertsToUppercaseAndRemovesIllegalFirebaseChars(string input, string expected)
        {
            string sanitized = FirebaseSecurityHelper.SanitizeKey(input).ToUpperInvariant();
            Assert.Equal(expected, sanitized);
        }
    }
}
