using System;
using System.Collections.Generic;
using Portal_Studenta.Models;

namespace Portal_Studenta.Services
{
    /// <summary>
    /// Czysta logika biznesowa i algorytmy dla harmonogramu zajęć.
    /// Zapewnia brak wyjątków, odporność na błędne dane i pełną testowalność jednostkową.
    /// </summary>
    public static class ScheduleLogic
    {
        public const int DefaultCacheValidityMinutes = 60;

        /// <summary>
        /// Generuje stabilny, bezpieczny klucz lekcji (odporny na znaki specjalne Firebase i wartości null).
        /// </summary>
        public static string GetLessonKey(Lesson? l)
        {
            if (l == null) return "EMPTY_LESSON";

            var title = string.IsNullOrWhiteSpace(l.Title) ? "UNTITLED" : l.Title.Trim();
            var type = string.IsNullOrWhiteSpace(l.Type) ? "EVENT" : l.Type.Trim();
            var baseStart = l.OriginalStart != default ? l.OriginalStart : l.Start;
            var grp = string.IsNullOrWhiteSpace(l.Group) ? "ALL" : l.Group.Trim();

            var rawKey = $"{title}_{type}_{baseStart:yyyyMMddHHmm}_{grp}".ToUpperInvariant();
            return FirebaseSecurityHelper.SanitizeKey(rawKey);
        }

        /// <summary>
        /// Sprawdza, czy pamięć podręczna harmonogramu USOS jest nadal świeża (polityka 60 minut).
        /// </summary>
        public static bool IsCacheFresh(
            int lessonCount, 
            DateTime? lastFetchTimeUtc, 
            DateTime nowUtc, 
            int validityMinutes = DefaultCacheValidityMinutes)
        {
            if (lessonCount <= 0) return false;
            if (!lastFetchTimeUtc.HasValue) return false;

            var diff = nowUtc - lastFetchTimeUtc.Value;
            // Ochrona przed zegarem cofniętym w przeszłość lub ponad validityMinutes
            return diff.TotalMinutes >= 0 && diff.TotalMinutes < validityMinutes;
        }

        /// <summary>
        /// Sprawdza, czy znacznik usunięcia konta (tombstone) pochodzi ze starej sesji i powinien zostać zignorowany.
        /// </summary>
        public static bool IsTombstoneObsolete(DateTime? tombstoneDeletedAt, DateTime? sessionStartedAt)
        {
            if (!tombstoneDeletedAt.HasValue || !sessionStartedAt.HasValue) return false;
            return tombstoneDeletedAt.Value <= sessionStartedAt.Value;
        }

        /// <summary>
        /// Sprawdza poprawność przedziału czasowego zajęć: początek musi być ściśle przed końcem.
        /// </summary>
        public static bool IsValidLessonInterval(DateTime start, DateTime end)
        {
            if (start == default || end == default) return false;
            return start < end;
        }

        /// <summary>
        /// Sprawdza, czy zajęcia już się zakończyły w odniesieniu do podanego czasu bieżącego.
        /// </summary>
        public static bool IsPastLesson(DateTime end, DateTime now)
        {
            if (end == default) return false;
            return end <= now;
        }

        /// <summary>
        /// Aplikuje modyfikacje starosty/użytkownika do listy zajęć (odwołania, sale, godziny, zadania/egzaminy).
        /// </summary>
        public static void ApplyModifications(List<Lesson> lessons, IDictionary<string, LessonModification>? modifications)
        {
            if (lessons == null || modifications == null || modifications.Count == 0) return;

            foreach (var l in lessons)
            {
                if (l == null) continue;
                var key = GetLessonKey(l);
                if (modifications.TryGetValue(key, out var mod) && mod != null)
                {
                    l.IsCanceled = mod.IsCanceled;
                    if (!string.IsNullOrEmpty(mod.CancelReason))
                        l.Desc = mod.CancelReason;
                    if (mod.ModifiedStart.HasValue)
                        l.Start = mod.ModifiedStart.Value;
                    if (mod.ModifiedEnd.HasValue)
                        l.End = mod.ModifiedEnd.Value;
                    if (!string.IsNullOrEmpty(mod.ModifiedPlace))
                        l.Place = mod.ModifiedPlace;
                    l.HasExam = mod.HasExam;
                    l.HasTask = mod.HasTask;
                    if (!string.IsNullOrEmpty(mod.TaskDetails))
                        l.TaskDetails = mod.TaskDetails;
                }
            }
        }

        private static int _globalEventSequence = (int)((DateTime.UtcNow.Ticks / 10_000) % 900_000_000) + 100_000_000;

        /// <summary>
        /// Generuje unikalny, dodatni identyfikator wydarzenia z puli ~1.9 miliarda liczb,
        /// z gwarancją braku kolizji wielowątkowych (Interlocked) i pamięciowych (existingIds).
        /// </summary>
        public static int GenerateUniqueEventId(IEnumerable<int>? existingIds = null)
        {
            var existing = existingIds != null ? new HashSet<int>(existingIds) : null;
            for (int i = 0; i < 500; i++)
            {
                int nextVal = Interlocked.Increment(ref _globalEventSequence);
                // Utrzymujemy liczbę w bezpiecznym zakresie dodatnim [100_000_000, 2_000_000_000]
                int candidate = 100_000_000 + (Math.Abs(nextVal) % 1_900_000_000);
                if (existing == null || !existing.Contains(candidate))
                {
                    return candidate;
                }
            }
            // Fallback oparty na tickach zegara
            int tickId = (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
            return tickId > 0 ? tickId : 100_000_001;
        }

        /// <summary>
        /// Wyszukuje wszystkie przyszłe zajęcia tego samego przedmiotu i typu w semestrze (np. do globalnej zmiany sali).
        /// </summary>
        public static List<Lesson> FindMatchingSemesterLessons(IEnumerable<Lesson>? allLessons, Lesson? referenceLesson)
        {
            if (allLessons == null || referenceLesson == null) return new List<Lesson>();

            var refTitle = referenceLesson.Title?.Trim() ?? string.Empty;
            var refType = referenceLesson.Type?.Trim() ?? string.Empty;
            var refGroup = referenceLesson.Group?.Trim() ?? string.Empty;

            var matches = allLessons.Where(l =>
                l != null &&
                l.Title.Trim().Equals(refTitle, StringComparison.OrdinalIgnoreCase) &&
                l.Type.Trim().Equals(refType, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(refGroup) || string.IsNullOrWhiteSpace(l.Group) || l.Group.Trim().Equals(refGroup, StringComparison.OrdinalIgnoreCase)) &&
                l.Start >= referenceLesson.Start).ToList();

            if (!matches.Any(l => l == referenceLesson))
            {
                matches.Add(referenceLesson);
            }

            return matches.OrderBy(l => l.Start).ToList();
        }

        /// <summary>
        /// Tłumaczy pełną nazwę typu zajęć z USOS na standaryzowany skrót (WYK, CW, LAB, PROJ, LEKT).
        /// </summary>
        public static string ParseClassType(string? classtypeName)
        {
            if (string.IsNullOrWhiteSpace(classtypeName)) return "INNE";

            if (classtypeName.Contains("Wykład", StringComparison.OrdinalIgnoreCase) || classtypeName.Contains("Lecture", StringComparison.OrdinalIgnoreCase)) return "WYK";
            if (classtypeName.Contains("Ćwiczenia", StringComparison.OrdinalIgnoreCase) || classtypeName.Contains("Exercise", StringComparison.OrdinalIgnoreCase)) return "CW";
            if (classtypeName.Contains("Laboratorium", StringComparison.OrdinalIgnoreCase) || classtypeName.Contains("Lab", StringComparison.OrdinalIgnoreCase)) return "LAB";
            if (classtypeName.Contains("Projekt", StringComparison.OrdinalIgnoreCase) || classtypeName.Contains("Project", StringComparison.OrdinalIgnoreCase)) return "PROJ";
            if (classtypeName.Contains("Lektorat", StringComparison.OrdinalIgnoreCase) || classtypeName.Contains("Foreign language", StringComparison.OrdinalIgnoreCase)) return "LEKT";

            return classtypeName.Trim();
        }

        /// <summary>
        /// Sprawdza, czy dana jednostka zajęć (course_unit_id) należy do kierunku studenta (ochrona przed 200 grupami WF z innych wydziałów w God Mode).
        /// </summary>
        public static bool IsUnitAllowedForCohort(string? courseId, string? courseUnitId, IDictionary<string, HashSet<string>>? courseToStudentUnits)
        {
            if (courseToStudentUnits == null || courseToStudentUnits.Count == 0) return true;
            if (string.IsNullOrEmpty(courseId) || string.IsNullOrEmpty(courseUnitId)) return true;

            if (courseToStudentUnits.TryGetValue(courseId, out var validUnits) && validUnits.Count > 0)
            {
                return validUnits.Contains(courseUnitId);
            }

            return true;
        }

        /// <summary>
        /// Wycinek wydarzenia dla pojedynczego dnia widoku kalendarza z informacją o kolumnach i kontynuacji.
        /// </summary>
        public class CalendarEventSlice
        {
            public Lesson Lesson { get; set; } = new();
            public int Column { get; set; }
            public int TotalColumns { get; set; } = 1;
            public DateTime EffectiveStart { get; set; }
            public DateTime EffectiveEnd { get; set; }
            public bool IsContinuationFromPreviousDay { get; set; }
            public bool ContinuesToNextDay { get; set; }
        }

        /// <summary>
        /// Odcina zajęcia do granic danego dnia kalendarzowego (zapobiega błędom typu "dzień trwa 30 godzin" przy wydarzeniach wielodniowych).
        /// </summary>
        public static CalendarEventSlice? SliceLessonForDay(Lesson? lesson, DateTime date, int startHour = 7, int endHour = 21, bool showModified = true)
        {
            if (lesson == null) return null;

            var actStart = showModified ? lesson.Start : (lesson.OriginalStart != default ? lesson.OriginalStart : lesson.Start);
            var actEnd = showModified ? lesson.End : (lesson.OriginalEnd != default ? lesson.OriginalEnd : lesson.End);

            if (actEnd <= actStart) return null;
            if (actStart.Date > date.Date || actEnd.Date < date.Date) return null;

            var dayStart = date.Date.AddHours(startHour);
            var dayEnd = date.Date.AddHours(endHour + 1);

            var effStart = actStart < dayStart ? dayStart : actStart;
            var effEnd = actEnd > dayEnd ? dayEnd : actEnd;

            if (effEnd <= effStart) return null;

            return new CalendarEventSlice
            {
                Lesson = lesson,
                EffectiveStart = effStart,
                EffectiveEnd = effEnd,
                IsContinuationFromPreviousDay = actStart.Date < date.Date,
                ContinuesToNextDay = actEnd.Date > date.Date
            };
        }

        /// <summary>
        /// Wylicza bezkolizyjne kolumny dla nakładających się wydarzeń w obrębie danego dnia.
        /// </summary>
        public static List<CalendarEventSlice> CalculateEventLayout(IEnumerable<CalendarEventSlice>? slices)
        {
            var result = new List<CalendarEventSlice>();
            if (slices == null) return result;

            var ordered = slices
                .Where(s => s != null && s.EffectiveEnd > s.EffectiveStart)
                .OrderBy(s => s.EffectiveStart)
                .ThenByDescending(s => s.EffectiveEnd)
                .ToList();

            if (ordered.Count == 0) return result;

            var clusters = new List<List<CalendarEventSlice>>();
            var currentCluster = new List<CalendarEventSlice>();
            DateTime currentClusterEnd = DateTime.MinValue;

            foreach (var d in ordered)
            {
                if (currentCluster.Count == 0 || d.EffectiveStart < currentClusterEnd)
                {
                    currentCluster.Add(d);
                    if (d.EffectiveEnd > currentClusterEnd) currentClusterEnd = d.EffectiveEnd;
                }
                else
                {
                    clusters.Add(currentCluster);
                    currentCluster = new List<CalendarEventSlice> { d };
                    currentClusterEnd = d.EffectiveEnd;
                }
            }
            if (currentCluster.Count > 0) clusters.Add(currentCluster);

            foreach (var cluster in clusters)
            {
                var columnLastEnds = new List<DateTime>();
                foreach (var ev in cluster)
                {
                    bool placed = false;
                    for (int i = 0; i < columnLastEnds.Count; i++)
                    {
                        // Chronologicznie posortowany klaster: wystarczy sprawdzić koniec ostatniego zdarzenia w kolumnie (O(1) zamiast O(N))
                        if (ev.EffectiveStart >= columnLastEnds[i])
                        {
                            ev.Column = i;
                            columnLastEnds[i] = ev.EffectiveEnd;
                            placed = true;
                            break;
                        }
                    }
                    if (!placed)
                    {
                        ev.Column = columnLastEnds.Count;
                        columnLastEnds.Add(ev.EffectiveEnd);
                    }
                }
                int totalCols = columnLastEnds.Count;
                foreach (var ev in cluster)
                {
                    ev.TotalColumns = totalCols;
                    result.Add(ev);
                }
            }

            return result;
        }

        /// <summary>
        /// Formatuje błędy API USOS i sieci na czytelne dla użytkownika komunikaty z zachowaniem dwujęzyczności.
        /// </summary>
        public static string FormatUsosErrorMessage(string? rawError, string lang)
        {
            if (string.IsNullOrWhiteSpace(rawError))
                return lang == "en-US" ? "Unknown USOS connection error." : "Nieznany błąd połączenia z USOS.";

            if (rawError.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) || 
                rawError.Contains("401") || 
                rawError.Contains("Sesja USOS wygasła", StringComparison.OrdinalIgnoreCase) ||
                rawError.Contains("user_missing", StringComparison.OrdinalIgnoreCase))
            {
                return lang == "en-US" ? "USOS session expired. Please sign in again." : "Sesja USOS wygasła. Zaloguj się ponownie.";
            }

            if (rawError.Contains("InternalServerError", StringComparison.OrdinalIgnoreCase) || 
                rawError.Contains("Internal USOS API error", StringComparison.OrdinalIgnoreCase) || 
                rawError.Contains(" 500 ", StringComparison.OrdinalIgnoreCase) ||
                rawError.Contains(": 500", StringComparison.OrdinalIgnoreCase))
            {
                return lang == "en-US" 
                    ? "Temporary USOS server error (500). University server reported an internal issue. Please try again shortly."
                    : "Tymczasowy błąd serwera uczelnianego USOS (500). Spróbuj ponownie za chwilę.";
            }

            if (rawError.Contains("timed out", StringComparison.OrdinalIgnoreCase) || 
                rawError.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
                rawError.Contains("TaskCanceledException", StringComparison.OrdinalIgnoreCase))
            {
                return lang == "en-US" 
                    ? "USOS server connection timed out. Check your internet connection."
                    : "Przekroczono czas oczekiwania na odpowiedź serwera USOS. Sprawdź połączenie z siecią.";
            }

            var match = System.Text.RegularExpressions.Regex.Match(rawError, @"""message""\s*:\s*""([^""]+)""");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return rawError;
        }

        /// <summary>
        /// Zwraca zwięzłą nazwę sali (np. "aula e", "825", "621a") do mikro-etykiety w kafelku siatki USOS.
        /// </summary>
        public static string GetCompactRoom(string? place)
        {
            if (string.IsNullOrWhiteSpace(place) || place.Equals("Brak sali", StringComparison.OrdinalIgnoreCase))
                return "";

            var trimmed = place.Trim();
            if (trimmed.StartsWith("Sala ", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(5).Trim();

            var bracketIdx = trimmed.IndexOf('(');
            if (bracketIdx > 0)
            {
                var roomOnly = trimmed.Substring(0, bracketIdx).Trim();
                if (!string.IsNullOrEmpty(roomOnly))
                    return roomOnly;
            }

            return trimmed;
        }

        /// <summary>
        /// Zwraca zestaw kolorów (tło, obramowanie, tło plakietki sali) zgodny ze stylem siatki USOS.
        /// </summary>
        public static (string Background, string Border, string RoomBadgeBg) GetCalendarEventColor(string? type, bool isHighPriority, bool isPersonal)
        {
            if (isHighPriority)
            {
                return ("#7f1d1d", "#ef4444", "rgba(239, 68, 68, 0.45)");
            }

            var t = (type ?? "").Trim().ToUpperInvariant();
            return t switch
            {
                "WYK" => ("#1c3d73", "#3b82f6", "rgba(59, 130, 246, 0.35)"),
                "LAB" => ("#1b4d24", "#22c55e", "rgba(34, 197, 94, 0.35)"),
                "CW" => ("#24522a", "#4ade80", "rgba(74, 222, 128, 0.35)"),
                "LEKT" => ("#854d0e", "#f59e0b", "rgba(245, 158, 11, 0.35)"),
                "PROJ" => ("#7c2d12", "#fb923c", "rgba(251, 146, 60, 0.35)"),
                "STAR" => ("#581c87", "#c084fc", "rgba(192, 132, 252, 0.35)"),
                "WŁASNE" => ("#1e3a8a", "#60a5fa", "rgba(96, 165, 250, 0.35)"),
                _ => ("#1f2937", "#4b5563", "rgba(75, 85, 99, 0.35)")
            };
        }

        /// <summary>
        /// Zwraca nazwę dnia tygodnia w formacie mobilnej siatki USOS (np. pon., wtorek, środa, czw., piątek, S, N).
        /// </summary>
        public static string GetUsosDayName(DayOfWeek day, bool isCompactWeekend, string lang)
        {
            if (lang == "en-US")
            {
                return day switch
                {
                    DayOfWeek.Monday => "Mon",
                    DayOfWeek.Tuesday => "Tue",
                    DayOfWeek.Wednesday => "Wed",
                    DayOfWeek.Thursday => "Thu",
                    DayOfWeek.Friday => "Fri",
                    DayOfWeek.Saturday => isCompactWeekend ? "S" : "Sat",
                    DayOfWeek.Sunday => isCompactWeekend ? "S" : "Sun",
                    _ => ""
                };
            }

            return day switch
            {
                DayOfWeek.Monday => "pon.",
                DayOfWeek.Tuesday => "wtorek",
                DayOfWeek.Wednesday => "środa",
                DayOfWeek.Thursday => "czw.",
                DayOfWeek.Friday => "piątek",
                DayOfWeek.Saturday => isCompactWeekend ? "S" : "sob.",
                DayOfWeek.Sunday => isCompactWeekend ? "N" : "niedz.",
                _ => ""
            };
        }

        /// <summary>
        /// Zwraca przyjazny tytuł tygodnia (np. "bieżący tydzień", "następny tydzień", "poprzedni tydzień" lub zakres dat).
        /// </summary>
        public static string GetWeekDisplayName(DateTime currentWeekStart, DateTime today, string lang)
        {
            int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var thisMonday = today.Date.AddDays(-diff);

            if (currentWeekStart.Date == thisMonday)
            {
                return lang == "en-US" ? "Current week" : "bieżący tydzień";
            }
            if (currentWeekStart.Date == thisMonday.AddDays(7))
            {
                return lang == "en-US" ? "Next week" : "następny tydzień";
            }
            if (currentWeekStart.Date == thisMonday.AddDays(-7))
            {
                return lang == "en-US" ? "Previous week" : "poprzedni tydzień";
            }

            return $"{currentWeekStart:dd.MM} – {currentWeekStart.AddDays(6):dd.MM}";
        }
    }
}
