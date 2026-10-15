using System.Text.Json;
using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class UsosProfileParsingTests
    {
        [Fact]
        public void ParseUserProfile_FullValidPayload_ParsesAllFields()
        {
            string jsonString = """
            {
                "id": "123456",
                "first_name": "Jan",
                "last_name": "Kowalski-Nowak",
                "student_number": "301928",
                "student_programmes": [
                    {
                        "programme": {
                            "id": "INF-ST-1",
                            "description": {
                                "pl": "Informatyka Stosowana I stopnia",
                                "en": "Applied Computer Science B.Sc."
                            }
                        },
                        "faculty": {
                            "id": "RAU"
                        }
                    }
                ]
            }
            """;

            using var doc = JsonDocument.Parse(jsonString);
            var result = ProfileLogic.ParseUserProfile(doc.RootElement);

            Assert.Equal("123456", result.Id);
            Assert.Equal("Jan", result.FirstName);
            Assert.Equal("Kowalski-Nowak", result.LastName);
            Assert.Equal("301928", result.StudentNumber);
            Assert.Equal("INF-ST-1", result.ProgrammeId);
            Assert.Equal("Informatyka Stosowana I stopnia", result.ProgrammeName);
            Assert.Equal("RAU", result.FacultyId);
        }

        [Fact]
        public void ParseUserProfile_FallbackToEnglishDescription_WhenPolishMissing()
        {
            string jsonString = """
            {
                "id": "999",
                "first_name": "John",
                "last_name": "Smith",
                "student_number": "100200",
                "student_programmes": [
                    {
                        "programme": {
                            "id": "CS-ENG",
                            "description": {
                                "en": "Computer Science Engineering"
                            }
                        },
                        "faculty": {
                            "id": "WTI"
                        }
                    }
                ]
            }
            """;

            using var doc = JsonDocument.Parse(jsonString);
            var result = ProfileLogic.ParseUserProfile(doc.RootElement);

            Assert.Equal("Computer Science Engineering", result.ProgrammeName);
            Assert.Equal("WTI", result.FacultyId);
        }

        [Fact]
        public void ParseUserProfile_EmptyProgrammesArray_DoesNotThrow()
        {
            string jsonString = """
            {
                "id": "555",
                "first_name": "Anna",
                "last_name": "Nowak",
                "student_number": "777888",
                "student_programmes": []
            }
            """;

            using var doc = JsonDocument.Parse(jsonString);
            var result = ProfileLogic.ParseUserProfile(doc.RootElement);

            Assert.Equal("555", result.Id);
            Assert.Equal("Anna", result.FirstName);
            Assert.Equal("Nowak", result.LastName);
            Assert.Equal("777888", result.StudentNumber);
            Assert.Empty(result.ProgrammeId);
            Assert.Empty(result.ProgrammeName);
            Assert.Empty(result.FacultyId);
        }

        [Fact]
        public void ParseUserProfile_MissingOptionalFields_PopulatesDefaults()
        {
            string jsonString = """
            {
                "id": "111"
            }
            """;

            using var doc = JsonDocument.Parse(jsonString);
            var result = ProfileLogic.ParseUserProfile(doc.RootElement);

            Assert.Equal("111", result.Id);
            Assert.Empty(result.FirstName);
            Assert.Empty(result.LastName);
            Assert.Empty(result.StudentNumber);
            Assert.Empty(result.ProgrammeId);
        }

        [Fact]
        public void ParseUserProfile_NonObjectRoot_HandledGracefully()
        {
            string jsonString = "[]";

            using var doc = JsonDocument.Parse(jsonString);
            var result = ProfileLogic.ParseUserProfile(doc.RootElement);

            Assert.Empty(result.Id);
            Assert.Empty(result.FirstName);
            Assert.Empty(result.LastName);
        }

        [Fact]
        public void ParseUserProfile_UnicodeAndDiacritics_PreservedAccurately()
        {
            string jsonString = """
            {
                "id": "333",
                "first_name": "Michał-Stanisław",
                "last_name": "Brzęczyszczykiewicz",
                "student_number": "999999",
                "student_programmes": [
                    {
                        "programme": {
                            "id": "LOT-1",
                            "description": {
                                "pl": "Lotnictwo i Kosmonautyka (Specjalność: Awionika ✈️)"
                            }
                        }
                    }
                ]
            }
            """;

            using var doc = JsonDocument.Parse(jsonString);
            var result = ProfileLogic.ParseUserProfile(doc.RootElement);

            Assert.Equal("Michał-Stanisław", result.FirstName);
            Assert.Equal("Brzęczyszczykiewicz", result.LastName);
            Assert.Equal("Lotnictwo i Kosmonautyka (Specjalność: Awionika ✈️)", result.ProgrammeName);
        }
    }
}
