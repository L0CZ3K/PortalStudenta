using System;
using System.Collections.Generic;
using System.Linq;

namespace Portal_Studenta.Services
{
    /// <summary>
    /// Model ankiety grupowej (Group Poll) wraz z logiką przeliczania głosów i procentów.
    /// Zabezpieczony przed dzieleniem przez zero i wartościami null.
    /// </summary>
    public class GroupPoll
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string GroupCode { get; set; } = "";
        public string Question { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Options { get; set; } = new();
        public Dictionary<string, int> Votes { get; set; } = new(); // safeAlbumNumber -> optionIndex
        public string CreatedBy { get; set; } = "Starosta";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
        public bool IsClosed { get; set; } = false;

        public int GetVoteCountForOption(int index)
        {
            if (index < 0) return 0;
            if (Options != null && Options.Any() && index >= Options.Count) return 0;
            if (Votes == null || !Votes.Any()) return 0;
            return Votes.Values.Count(v => v == index);
        }

        public int TotalVotes => (Votes == null)
            ? 0
            : (Options != null && Options.Any())
                ? Votes.Values.Count(v => v >= 0 && v < Options.Count)
                : Votes.Values.Count(v => v >= 0);

        public double GetPercentageForOption(int index)
        {
            if (index < 0) return 0.0;
            if (Options != null && Options.Any() && index >= Options.Count) return 0.0;
            int total = TotalVotes;
            if (total == 0) return 0.0;
            return Math.Round((double)GetVoteCountForOption(index) / total * 100.0, 1);
        }
    }
}
