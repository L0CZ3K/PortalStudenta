using Portal_Studenta.Services;
using Portal_Studenta.Models;
using Xunit;

namespace PortalStudenta.Tests
{
    public class HapticAndUxTests
    {
        [Theory]
        [InlineData(-100, 10, 250, true, "next")]   // Fast horizontal swipe left -> Next
        [InlineData(120, -15, 300, true, "prev")]   // Fast horizontal swipe right -> Prev
        [InlineData(-30, 10, 250, false, "none")]   // Below distance threshold (55px) -> Ignore
        [InlineData(-120, 110, 250, false, "none")] // Diagonal / vertical scroll dominance -> Ignore
        [InlineData(150, 5, 800, false, "none")]    // Too slow (>600ms) -> Ignore
        public void SwipeGestureLogic_CorrectlyClassifiesHorizontalSwipes(
            double deltaX, double deltaY, double elapsedMs, bool isSwipe, string expectedDirection)
        {
            bool detectedSwipe = Math.Abs(deltaX) > 55 &&
                                 Math.Abs(deltaX) > Math.Abs(deltaY) * 1.4 &&
                                 elapsedMs < 600;

            Assert.Equal(isSwipe, detectedSwipe);

            if (detectedSwipe)
            {
                string direction = deltaX < 0 ? "next" : "prev";
                Assert.Equal(expectedDirection, direction);
            }
        }

        [Fact]
        public void SwipeNavigation_DayBoundaries_TransitionProperly()
        {
            // When user is on Friday (index 4 of 5 work days) and swipes left:
            int currentDay = 4;
            int daysToDisplay = 5;
            bool weekShifted = false;

            // Swiping left at end of week
            if (currentDay < daysToDisplay - 1)
            {
                currentDay++;
            }
            else
            {
                currentDay = 0;
                weekShifted = true;
            }

            Assert.Equal(0, currentDay);
            Assert.True(weekShifted);

            // Swiping right at beginning of week
            weekShifted = false;
            if (currentDay > 0)
            {
                currentDay--;
            }
            else
            {
                currentDay = daysToDisplay - 1;
                weekShifted = true;
            }

            Assert.Equal(4, currentDay);
            Assert.True(weekShifted);
        }

        [Fact]
        public void OfflineCacheIndicator_ReflectsFreshnessProperly()
        {
            var nowUtc = DateTime.UtcNow;
            var freshFetch = nowUtc.AddMinutes(-30);
            var staleFetch = nowUtc.AddMinutes(-90);

            bool isFresh = ScheduleLogic.IsCacheFresh(15, freshFetch, nowUtc, 60);
            bool isStale = ScheduleLogic.IsCacheFresh(15, staleFetch, nowUtc, 60);
            bool isEmpty = ScheduleLogic.IsCacheFresh(0, freshFetch, nowUtc, 60);

            Assert.True(isFresh);
            Assert.False(isStale);
            Assert.False(isEmpty);
        }

        [Fact]
        public void OfflineCacheIndicator_NullFetchTime_IsStale()
        {
            var nowUtc = DateTime.UtcNow;
            bool isFresh = ScheduleLogic.IsCacheFresh(15, null, nowUtc, 60);
            Assert.False(isFresh);
        }
    }
}
