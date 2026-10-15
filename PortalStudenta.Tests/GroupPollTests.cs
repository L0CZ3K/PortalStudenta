using Portal_Studenta.Services;
using Xunit;

namespace PortalStudenta.Tests
{
    public class GroupPollTests
    {
        [Fact]
        public void TotalVotes_NullOrEmptyVotesDictionary_ReturnsZero()
        {
            var poll = new GroupPoll { Votes = null! };
            Assert.Equal(0, poll.TotalVotes);

            poll.Votes = new Dictionary<string, int>();
            Assert.Equal(0, poll.TotalVotes);
        }

        [Fact]
        public void TotalVotes_WithVotes_ReturnsCorrectCount()
        {
            var poll = new GroupPoll
            {
                Votes = new Dictionary<string, int>
                {
                    { "student1", 0 },
                    { "student2", 1 },
                    { "student3", 0 }
                }
            };

            Assert.Equal(3, poll.TotalVotes);
        }

        [Fact]
        public void GetVoteCountForOption_CountsCorrectly()
        {
            var poll = new GroupPoll
            {
                Options = new List<string> { "Opcja A", "Opcja B", "Opcja C" },
                Votes = new Dictionary<string, int>
                {
                    { "s1", 0 },
                    { "s2", 1 },
                    { "s3", 0 },
                    { "s4", 0 }
                }
            };

            Assert.Equal(3, poll.GetVoteCountForOption(0));
            Assert.Equal(1, poll.GetVoteCountForOption(1));
            Assert.Equal(0, poll.GetVoteCountForOption(2));
            Assert.Equal(0, poll.GetVoteCountForOption(99)); // Indeks poza zakresem
        }

        [Fact]
        public void GetPercentageForOption_ZeroTotalVotes_ReturnsZeroWithoutDivideByZero()
        {
            var poll = new GroupPoll
            {
                Options = new List<string> { "Tak", "Nie" },
                Votes = new Dictionary<string, int>()
            };

            // Nie powinno rzucać wyjątku DivideByZeroException
            var percent = poll.GetPercentageForOption(0);
            Assert.Equal(0.0, percent);
        }

        [Fact]
        public void GetPercentageForOption_CalculatesAndRoundsProperly()
        {
            // Scenariusz 1: 1 z 3 głosów = 33.333% -> zaokrąglenie do 33.3%
            var poll = new GroupPoll
            {
                Options = new List<string> { "A", "B" },
                Votes = new Dictionary<string, int>
                {
                    { "s1", 0 },
                    { "s2", 1 },
                    { "s3", 1 }
                }
            };

            Assert.Equal(33.3, poll.GetPercentageForOption(0));
            Assert.Equal(66.7, poll.GetPercentageForOption(1));
        }

        [Fact]
        public void GetPercentageForOption_HalfAndFullVotes()
        {
            var poll = new GroupPoll
            {
                Options = new List<string> { "A", "B" },
                Votes = new Dictionary<string, int>
                {
                    { "s1", 0 },
                    { "s2", 1 }
                }
            };

            Assert.Equal(50.0, poll.GetPercentageForOption(0));
            Assert.Equal(50.0, poll.GetPercentageForOption(1));

            // Wszyscy na opcję A
            poll.Votes["s2"] = 0;
            Assert.Equal(100.0, poll.GetPercentageForOption(0));
            Assert.Equal(0.0, poll.GetPercentageForOption(1));
        }
    }
}
