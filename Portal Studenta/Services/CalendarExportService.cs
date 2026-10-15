using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Portal_Studenta.Models;

namespace Portal_Studenta.Services
{
    /// <summary>
    /// Service responsible for generating and exporting iCalendar (.ics) files for university schedules.
    /// Compatible with system calendars on iOS, macOS, Android, and Windows.
    /// </summary>
    public class CalendarExportService
    {
        /// <summary>
        /// Generates an iCalendar (RFC 5545) formatted string from a collection of lessons.
        /// </summary>
        /// <param name="lessons">The collection of lessons to include in the calendar.</param>
        /// <param name="calendarName">The display name of the calendar.</param>
        /// <param name="includeAlarms">Whether to include 15-minute reminders.</param>
        /// <param name="includeCanceled">Whether to include canceled classes in the export.</param>
        /// <returns>A string containing valid iCalendar format data.</returns>
        public string GenerateIcs(IEnumerable<Lesson> lessons, string calendarName = "UniAvio - Plan Zajęć", bool includeAlarms = true, bool includeCanceled = false)
        {
            return IcsCalendarHelper.GenerateIcs(lessons, calendarName, includeAlarms, includeCanceled);
        }

        /// <summary>
        /// Saves generated ICS content to a temporary file in the local cache directory.
        /// </summary>
        /// <param name="lessons">Lessons to export.</param>
        /// <param name="fileName">Desired file name.</param>
        /// <param name="includeAlarms">Whether to include reminder alarms.</param>
        /// <param name="includeCanceled">Whether to include canceled classes.</param>
        /// <returns>The absolute path to the generated file on disk.</returns>
        public async Task<string> SaveIcsToFileAsync(IEnumerable<Lesson> lessons, string fileName = "Plan_Zajec_UniAvio.ics", bool includeAlarms = true, bool includeCanceled = false)
        {
            var icsContent = GenerateIcs(lessons, "UniAvio Plan Zajęć", includeAlarms, includeCanceled);
            var cachePath = Path.Combine(FileSystem.CacheDirectory, fileName);
            await File.WriteAllTextAsync(cachePath, icsContent, Encoding.UTF8);
            return cachePath;
        }

        /// <summary>
        /// Exports lessons directly to the system calendar, falling back to system Share Sheet if needed.
        /// </summary>
        /// <param name="lessons">Lessons to export.</param>
        /// <param name="includeAlarms">Whether to include reminder alarms.</param>
        /// <param name="includeCanceled">Whether to include canceled classes.</param>
        public async Task ExportToSystemCalendarAsync(IEnumerable<Lesson> lessons, bool includeAlarms = true, bool includeCanceled = false)
        {
            var filePath = await SaveIcsToFileAsync(lessons, "Plan_Zajec_UniAvio.ics", includeAlarms, includeCanceled);

            try
            {
                // Próba bezpośredniego otwarcia w systemowym Kalendarzu
                var opened = await Launcher.Default.OpenAsync(new OpenFileRequest
                {
                    Title = "Dodaj do Kalendarza",
                    File = new ReadOnlyFile(filePath, "text/calendar")
                });

                if (opened) return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CalendarExport] Launcher error: {ex.Message}");
            }

            // Fallback: systemowy arkusz udostępniania (Share Sheet)
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Eksportuj plan do Kalendarza",
                File = new ShareFile(filePath, "text/calendar")
            });
        }
        
        /// <summary>
        /// Opens system share dialog to share the generated .ics calendar file.
        /// </summary>
        /// <param name="lessons">Lessons to export.</param>
        /// <param name="includeAlarms">Whether to include reminder alarms.</param>
        /// <param name="includeCanceled">Whether to include canceled classes.</param>
        public async Task ShareIcsFileAsync(IEnumerable<Lesson> lessons, bool includeAlarms = true, bool includeCanceled = false)
        {
            var filePath = await SaveIcsToFileAsync(lessons, "Plan_Zajec_UniAvio.ics", includeAlarms, includeCanceled);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Udostępnij lub dodaj do Kalendarza",
                File = new ShareFile(filePath, "text/calendar")
            });
        }

        private static string EscapeIcs(string text) => IcsCalendarHelper.EscapeIcs(text);
    }
}
