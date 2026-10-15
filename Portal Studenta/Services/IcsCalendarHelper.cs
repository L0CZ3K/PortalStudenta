using System;
using System.Collections.Generic;
using System.Text;
using Portal_Studenta.Models;

namespace Portal_Studenta.Services
{
    /// <summary>
    /// Czysta logika biznesowa generowania plików kalendarza iCalendar (RFC 5545).
    /// Nie zależy od API platformowego (.NET MAUI), co umożliwia pełne testowanie jednostkowe.
    /// </summary>
    public static class IcsCalendarHelper
    {
        public static string GenerateIcs(
            IEnumerable<Lesson>? lessons, 
            string calendarName = "UniAvio - Plan Zajęć", 
            bool includeAlarms = true, 
            bool includeCanceled = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BEGIN:VCALENDAR");
            sb.AppendLine("VERSION:2.0");
            sb.AppendLine("PRODID:-//UniAvio//Portal Studenta//PL");
            sb.AppendLine("CALSCALE:GREGORIAN");
            sb.AppendLine("METHOD:PUBLISH");
            sb.AppendLine($"X-WR-CALNAME:{EscapeIcs(calendarName)}");
            sb.AppendLine("X-WR-TIMEZONE:Europe/Warsaw");

            if (lessons == null)
            {
                sb.AppendLine("END:VCALENDAR");
                return sb.ToString();
            }

            var nowUtc = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");

            foreach (var lesson in lessons)
            {
                if (lesson == null) continue;
                if (lesson.IsCanceled && !includeCanceled)
                    continue;

                var startUtc = lesson.Start.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
                var endUtc = lesson.End.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
                var uid = $"uniavio_{lesson.Id}_{lesson.Start:yyyyMMddHHmm}@uniavio.app";

                var summary = $"{lesson.Title} ({lesson.Type})";
                if (lesson.IsCanceled) summary = $"[ODWOŁANE] {summary}";

                var descBuilder = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(lesson.Person)) descBuilder.AppendLine($"Prowadzący: {lesson.Person}");
                if (!string.IsNullOrWhiteSpace(lesson.Group)) descBuilder.AppendLine($"Grupa: Gr. {lesson.Group}");
                if (!string.IsNullOrWhiteSpace(lesson.Place)) descBuilder.AppendLine($"Sala: {lesson.Place}");
                if (!string.IsNullOrWhiteSpace(lesson.Desc)) descBuilder.AppendLine($"Uwagi: {lesson.Desc}");
                if (lesson.HasExam) descBuilder.AppendLine("⚠️ EGZAMIN / KOLOKWIUM");
                if (lesson.HasTask) descBuilder.AppendLine($"Zadanie: {lesson.TaskDetails}");

                sb.AppendLine("BEGIN:VEVENT");
                sb.AppendLine($"UID:{uid}");
                sb.AppendLine($"DTSTAMP:{nowUtc}");
                sb.AppendLine($"DTSTART:{startUtc}");
                sb.AppendLine($"DTEND:{endUtc}");
                sb.AppendLine($"SUMMARY:{EscapeIcs(summary)}");
                if (!string.IsNullOrWhiteSpace(lesson.Place))
                {
                    sb.AppendLine($"LOCATION:{EscapeIcs(lesson.Place)}");
                }
                sb.AppendLine($"DESCRIPTION:{EscapeIcs(descBuilder.ToString().TrimEnd())}");
                sb.AppendLine(lesson.IsCanceled ? "STATUS:CANCELLED" : "STATUS:CONFIRMED");

                if (includeAlarms && !lesson.IsCanceled)
                {
                    sb.AppendLine("BEGIN:VALARM");
                    sb.AppendLine("TRIGGER:-PT15M");
                    sb.AppendLine("ACTION:DISPLAY");
                    sb.AppendLine($"DESCRIPTION:Zajęcia {EscapeIcs(lesson.Title)} za 15 minut");
                    sb.AppendLine("END:VALARM");
                }

                sb.AppendLine("END:VEVENT");
            }

            sb.AppendLine("END:VCALENDAR");
            return sb.ToString();
        }

        public static string EscapeIcs(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            // Szybka ścieżka (Zero Alloc): jeśli brak znaków wymagających ucieczki, zwracamy bez kopiowania
            bool needsEscape = false;
            foreach (char c in text)
            {
                if (c is '\\' or ';' or ',' or '\r' or '\n')
                {
                    needsEscape = true;
                    break;
                }
            }

            if (!needsEscape) return text;

            var sb = new StringBuilder(text.Length + 16);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case ';': sb.Append("\\;"); break;
                    case ',': sb.Append("\\,"); break;
                    case '\r':
                        if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                        sb.Append("\\n");
                        break;
                    case '\n': sb.Append("\\n"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
