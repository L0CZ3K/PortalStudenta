using System;
using System.Collections.Generic;
using System.Linq;

namespace Portal_Studenta.Services
{
    public class AnnouncementItem
    {
        public string Uid { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string ShortContent { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public bool IsSystem { get; set; }
        public bool IsPriority { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpireDate { get; set; }

        public string DisplayTime => AnnouncementLogic.FormatDisplayTime(CreatedAt, DateTime.Today);
    }

    /// <summary>
    /// Czysta logika biznesowa filtrowania, sortowania i formatowania ogłoszeń.
    /// Zapewnia brak zależności od .NET MAUI i 100% testowalność.
    /// </summary>
    public static class AnnouncementLogic
    {
        public static List<AnnouncementItem> FilterAndSort(IEnumerable<AnnouncementItem>? items, DateTime now)
        {
            if (items == null) return new List<AnnouncementItem>();

            return items
                .Where(n => n != null && (n.ExpireDate == default || n.ExpireDate > now))
                .OrderByDescending(n => n.IsPriority)
                .ThenByDescending(n => n.CreatedAt)
                .ToList();
        }

        public static string FormatDisplayTime(DateTime createdAt, DateTime today)
        {
            if (createdAt == default) return string.Empty;

            return createdAt.Date == today.Date
                ? $"Dzisiaj {createdAt:HH:mm}"
                : $"{createdAt:dd.MM} {createdAt:HH:mm}";
        }
    }
}
