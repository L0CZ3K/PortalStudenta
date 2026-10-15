using System;
using System.Globalization;

namespace Portal_Studenta.Services
{
    /// <summary>
    /// Logika obliczania parzystości tygodnia (ISO-8601, semestr akademicki, tryby ręczne).
    /// </summary>
    public static class WeekParityHelper
    {
        /// <summary>
        /// Wylicza, czy podana data przypada na tydzień parzysty (Tydzień B).
        /// </summary>
        public static bool CalculateIsWeekEven(
            DateTime weekDate, 
            WeekParityMode mode, 
            string? academicTermStart = null, 
            bool invertWeeks = false)
        {
            if (mode == WeekParityMode.AlwaysEven) return true;
            if (mode == WeekParityMode.AlwaysOdd) return false;

            bool baseEven;
            if (!string.IsNullOrWhiteSpace(academicTermStart) && 
                DateTime.TryParse(academicTermStart, out var termStart) && 
                weekDate.Date >= termStart.Date)
            {
                // Numer tygodnia akademickiego od daty startu semestru (tydzień 1 to nieparzysty)
                int daysDiff = (weekDate.Date - termStart.Date).Days;
                int academicWeekNum = (daysDiff / 7) + 1;
                baseEven = (academicWeekNum % 2 == 0);
            }
            else
            {
                // Domyślny kalendarz wg standardu ISO-8601
                baseEven = (ISOWeek.GetWeekOfYear(weekDate) % 2 == 0);
            }

            if (mode == WeekParityMode.Inverted || invertWeeks)
            {
                return !baseEven;
            }

            return baseEven;
        }

        /// <summary>
        /// Cykliczne przełączanie trybu parzystości tygodnia:
        /// Auto -> Inverted -> AlwaysEven -> AlwaysOdd -> Auto
        /// </summary>
        public static WeekParityMode GetNextParityMode(WeekParityMode current)
        {
            return current switch
            {
                WeekParityMode.Auto => WeekParityMode.Inverted,
                WeekParityMode.Inverted => WeekParityMode.AlwaysEven,
                WeekParityMode.AlwaysEven => WeekParityMode.AlwaysOdd,
                WeekParityMode.AlwaysOdd => WeekParityMode.Auto,
                _ => WeekParityMode.Auto
            };
        }

        /// <summary>
        /// Zwraca czytelną etykietę dla danego tygodnia: "Parzysty (B)" lub "Nieparzysty (A)".
        /// </summary>
        public static string GetParityLabel(bool isEven)
        {
            return isEven ? "Parzysty (B)" : "Nieparzysty (A)";
        }
    }
}
