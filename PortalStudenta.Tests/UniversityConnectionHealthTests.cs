using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class UniversityConnectionHealthTests
    {
        [Theory]
        [InlineData(ConnectionHealth.Online, "POLSL", "POLSL • Online")]
        [InlineData(ConnectionHealth.Degraded, "POLSL", "POLSL • Słabe połączenie")]
        [InlineData(ConnectionHealth.ServerError, "POLSL", "POLSL • Błąd serwera")]
        [InlineData(ConnectionHealth.Offline, "POLSL", "Offline • Brak sieci")]
        [InlineData(ConnectionHealth.Online, "UW", "UW • Online")]
        public void GetStatusLabel_FormatsProperly_ForDifferentStates(ConnectionHealth health, string uni, string expected)
        {
            var label = UniversityHealthLogic.GetStatusLabel(health, uni);
            Assert.Equal(expected, label);
        }

        [Theory]
        [InlineData(ConnectionHealth.Online, "#30D158")]
        [InlineData(ConnectionHealth.Degraded, "#FF9F0A")]
        [InlineData(ConnectionHealth.ServerError, "#FF453A")]
        [InlineData(ConnectionHealth.Offline, "#FF453A")]
        public void GetDotColor_ReturnsStandardColors(ConnectionHealth health, string expectedHex)
        {
            var color = UniversityHealthLogic.GetDotColor(health);
            Assert.Equal(expectedHex, color);
        }

        [Theory]
        [InlineData(200, ServerStatus.Online)]
        [InlineData(204, ServerStatus.Online)]
        [InlineData(301, ServerStatus.Online)]
        [InlineData(302, ServerStatus.Online)]
        [InlineData(400, ServerStatus.Online)]
        [InlineData(401, ServerStatus.Online)]
        [InlineData(403, ServerStatus.Online)]
        [InlineData(404, ServerStatus.Online)]
        [InlineData(500, ServerStatus.Offline)]
        [InlineData(502, ServerStatus.Offline)]
        [InlineData(503, ServerStatus.Offline)]
        [InlineData(504, ServerStatus.Offline)]
        public void ClassifyHttpResponse_ClassifiesCorrectly(int statusCode, ServerStatus expectedStatus)
        {
            var (status, msg) = UniversityHealthLogic.ClassifyHttpResponse(statusCode, "POLSL", 120);
            Assert.Equal(expectedStatus, status);
            Assert.NotEmpty(msg);
        }

        [Fact]
        public void ResolveHealthState_ReturnsOffline_WhenNoInternetConnection()
        {
            var (health, msg) = UniversityHealthLogic.ResolveHealthState(
                hasInternet: false,
                serverStatus: ServerStatus.Online,
                pingMs: 50,
                uniShortName: "POLSL");

            Assert.Equal(ConnectionHealth.Offline, health);
            Assert.Contains("Brak połączenia z Internetem", msg);
        }

        [Fact]
        public void ResolveHealthState_ReturnsOnline_WhenServerIsOnlineAndFast()
        {
            var (health, msg) = UniversityHealthLogic.ResolveHealthState(
                hasInternet: true,
                serverStatus: ServerStatus.Online,
                pingMs: 250,
                uniShortName: "POLSL");

            Assert.Equal(ConnectionHealth.Online, health);
            Assert.Contains("online", msg);
            Assert.Contains("250 ms", msg);
        }

        [Fact]
        public void ResolveHealthState_ReturnsOnline_AtBoundary800ms()
        {
            var (health, _) = UniversityHealthLogic.ResolveHealthState(
                hasInternet: true,
                serverStatus: ServerStatus.Online,
                pingMs: 800,
                uniShortName: "POLSL");

            Assert.Equal(ConnectionHealth.Online, health);
        }

        [Fact]
        public void ResolveHealthState_ReturnsDegraded_WhenPingExceeds800ms()
        {
            var (health, msg) = UniversityHealthLogic.ResolveHealthState(
                hasInternet: true,
                serverStatus: ServerStatus.Online,
                pingMs: 801,
                uniShortName: "POLSL");

            Assert.Equal(ConnectionHealth.Degraded, health);
            Assert.Contains("odpowiada wolno", msg);
            Assert.Contains("801 ms", msg);
        }

        [Fact]
        public void ResolveHealthState_ReturnsServerError_WhenServerStatusIsOffline()
        {
            var (health, msg) = UniversityHealthLogic.ResolveHealthState(
                hasInternet: true,
                serverStatus: ServerStatus.Offline,
                pingMs: 1500,
                uniShortName: "POLSL",
                serverErrorMessage: "Błąd 503 Service Unavailable");

            Assert.Equal(ConnectionHealth.ServerError, health);
            Assert.Equal("Błąd 503 Service Unavailable", msg);
        }
    }
}
