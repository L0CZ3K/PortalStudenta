using System;
using System.Collections.Generic;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class GodModeFilteringTests
    {
        [Fact]
        public void IsUnitAllowedForCohort_WhenUnitInStudentCourseUnits_ReturnsTrue()
        {
            var courseToUnits = new Dictionary<string, HashSet<string>>
            {
                { "WF-101", new HashSet<string> { "UNIT_10", "UNIT_11" } }
            };

            bool allowed1 = ScheduleLogic.IsUnitAllowedForCohort("WF-101", "UNIT_10", courseToUnits);
            bool allowed2 = ScheduleLogic.IsUnitAllowedForCohort("WF-101", "UNIT_11", courseToUnits);

            Assert.True(allowed1);
            Assert.True(allowed2);
        }

        [Fact]
        public void IsUnitAllowedForCohort_WhenUnitNotInStudentCourseUnits_ReturnsFalse()
        {
            var courseToUnits = new Dictionary<string, HashSet<string>>
            {
                { "WF-101", new HashSet<string> { "UNIT_10", "UNIT_11" } }
            };

            // Group from another faculty (e.g. Mechanical Engineering)
            bool allowed = ScheduleLogic.IsUnitAllowedForCohort("WF-101", "UNIT_99", courseToUnits);

            Assert.False(allowed);
        }

        [Fact]
        public void IsUnitAllowedForCohort_WhenNoRestrictionsForCourse_ReturnsTrue()
        {
            var courseToUnits = new Dictionary<string, HashSet<string>>
            {
                { "WF-101", new HashSet<string> { "UNIT_10" } }
            };

            // Math course with no faculty unit restriction in the dictionary
            bool allowed = ScheduleLogic.IsUnitAllowedForCohort("MAT-200", "UNIT_55", courseToUnits);

            Assert.True(allowed);
        }

        [Fact]
        public void IsUnitAllowedForCohort_WhenRestrictionsDictionaryEmptyOrNull_ReturnsTrue()
        {
            Assert.True(ScheduleLogic.IsUnitAllowedForCohort("COURSE-1", "UNIT-1", null));
            Assert.True(ScheduleLogic.IsUnitAllowedForCohort("COURSE-1", "UNIT-1", new Dictionary<string, HashSet<string>>()));
        }

        [Fact]
        public void IsUnitAllowedForCohort_WhenCourseOrUnitNullOrEmpty_ReturnsTrueSafely()
        {
            var dict = new Dictionary<string, HashSet<string>> { { "C1", new HashSet<string> { "U1" } } };

            Assert.True(ScheduleLogic.IsUnitAllowedForCohort(null, "U1", dict));
            Assert.True(ScheduleLogic.IsUnitAllowedForCohort("", "U1", dict));
            Assert.True(ScheduleLogic.IsUnitAllowedForCohort("C1", null, dict));
            Assert.True(ScheduleLogic.IsUnitAllowedForCohort("C1", "", dict));
        }

        [Fact]
        public void IsUnitAllowedForCohort_Handles200PeGroupsFiltering_OnlyAcceptsPermittedUnits()
        {
            // Simulate 200 physical education groups university-wide, but only 3 belong to IT faculty
            var itAllowedUnits = new HashSet<string> { "PE_IT_G1", "PE_IT_G2", "PE_IT_G3" };
            var mapping = new Dictionary<string, HashSet<string>>
            {
                { "PE-UNI", itAllowedUnits }
            };

            int acceptedCount = 0;
            int rejectedCount = 0;

            for (int i = 1; i <= 200; i++)
            {
                string candidateUnit = i switch
                {
                    15 => "PE_IT_G1",
                    32 => "PE_IT_G2",
                    77 => "PE_IT_G3",
                    _ => $"PE_OTHER_{i}"
                };

                if (ScheduleLogic.IsUnitAllowedForCohort("PE-UNI", candidateUnit, mapping))
                {
                    acceptedCount++;
                }
                else
                {
                    rejectedCount++;
                }
            }

            Assert.Equal(3, acceptedCount);
            Assert.Equal(197, rejectedCount);
        }
    }
}
