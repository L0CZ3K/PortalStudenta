using System;
using Portal_Studenta.Models;

namespace Portal_Studenta.Services
{
    /// <summary>
    /// Logika formatowania powiadomień systemowych (Push, Local, In-App).
    /// Zapewnia jednolite szablony komunikatów i pełną testowalność jednostkową.
    /// </summary>
    public static class NotificationMessageLogic
    {
        public static (string title, string body) FormatLessonCanceled(Lesson lesson, string? reason, DateTime now)
        {
            var title = $"🚨 Odwołano: {lesson.Title}";
            var dateStr = lesson.Start.Date == now.Date
                ? $"dzisiaj o {lesson.Start:HH:mm}"
                : $"{lesson.Start:dd.MM} o {lesson.Start:HH:mm}";

            var body = string.IsNullOrWhiteSpace(reason)
                ? $"Zajęcia {lesson.Type} ({dateStr}) zostały odwołane."
                : $"Zajęcia {lesson.Type} ({dateStr}) zostały odwołane. Powód: {reason}";

            return (title, body);
        }

        public static (string title, string body) FormatLessonRoomChanged(Lesson lesson, string newRoom, DateTime now)
        {
            var title = $"📍 Zmiana sali: {lesson.Title}";
            var dateStr = lesson.Start.Date == now.Date ? "dzisiaj" : $"{lesson.Start:dd.MM}";
            var body = $"Zajęcia {lesson.Type} ({dateStr}, {lesson.Start:HH:mm}) przeniesiono do sali: {newRoom}.";
            return (title, body);
        }

        public static (string title, string body) FormatLessonRescheduled(Lesson lesson, DateTime newStart, DateTime newEnd)
        {
            var title = $"🕒 Zmiana terminu: {lesson.Title}";
            var body = $"Nowy termin: {newStart:dd.MM (ddd)} w godz. {newStart:HH:mm} – {newEnd:HH:mm}.";
            return (title, body);
        }

        public static (string title, string body) FormatAnnouncement(AnnouncementItem announcement)
        {
            var isDev = announcement.IsSystem || string.Equals(announcement.Author, "dev", StringComparison.OrdinalIgnoreCase) || string.Equals(announcement.Author, "system", StringComparison.OrdinalIgnoreCase);
            var author = string.IsNullOrWhiteSpace(announcement.Author) ? "System" : announcement.Author;
            var title = isDev ? "🛠️ Nowy komunikat od Devów" : $"📢 Ogłoszenie od: {author}";
            var textContent = !string.IsNullOrWhiteSpace(announcement.ShortContent) 
                ? announcement.ShortContent 
                : announcement.Content;
            var body = $"{announcement.Title}\n{textContent}".TrimEnd();
            return (title, body);
        }

        public static (string title, string body) FormatNewPoll(GroupPoll poll)
        {
            var title = "🗳️ Nowa ankieta w grupie";
            var body = $"{poll.Question}\nZagłosuj w panelu Starosty lub na pulpicie.";
            return (title, body);
        }
    }
}
