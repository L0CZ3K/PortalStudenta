using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Portal_Studenta.Models;
using SQLite;

namespace Portal_Studenta.Services
{
    public interface IFirebaseNoteSync
    {
        Task<List<Note>> GetUserNotesFromCloudAsync(string albumNumber);
        Task SyncNoteToCloudAsync(string albumNumber, Note note);
        Task DeleteNoteFromCloudAsync(string albumNumber, Guid noteId);
        Task<HashSet<Guid>> GetDeletedNoteIdsFromCloudAsync(string albumNumber);
        Task<List<Note>> GetGroupSharedNotesAsync(string groupCode);
        Task ShareNoteToGroupLobbyAsync(string groupCode, Note note);
        Task UnshareNoteFromGroupLobbyAsync(string groupCode, Guid noteId);
        Task<HashSet<Guid>> GetGroupDeletedNoteIdsFromCloudAsync(string groupCode);
    }

    [Table("DeletedNoteTombstones")]
    public class DeletedNoteTombstone
    {
        [PrimaryKey]
        public Guid NoteId { get; set; }
        public DateTime DeletedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Service managing student academic notes, folder hierarchies, SQLite persistence,
    /// and tokenized inline attachments (whiteboard scans, photos, documents).
    /// Thread-safe via internal locking and asynchronous SQLite SemaphoreSlim locks.
    /// </summary>
    public class NoteService : IDisposable
    {
        private readonly SemaphoreSlim _dbLock = new(1, 1);
        private readonly SemaphoreSlim _syncLock = new(1, 1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> _locallyDeletedNoteIds = new();
        private readonly List<Note> _notes = new();
        private SQLiteAsyncConnection? _db;
        private bool _isInitialized;

        /// <summary>
        /// Event fired whenever notes collection is modified (created, edited, deleted, pinned).
        /// </summary>
        public event Action? OnNotesChanged;

        public NoteService()
        {
            // Próba wczytania notatek z lokalnej bazy SQLite w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    await EnsureInitializedAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NoteService] Błąd inicjalizacji bazy SQLite: {ex.Message}");
                }
            });
        }

        private string GetDatabasePath()
        {
            try
            {
                var localDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(localDir) || !Directory.Exists(localDir))
                {
                    localDir = AppDomain.CurrentDomain.BaseDirectory;
                }
                var appFolder = Path.Combine(localDir, "UniAvio");
                Directory.CreateDirectory(appFolder);
                return Path.Combine(appFolder, "student_notes.db3");
            }
            catch
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "student_notes.db3");
            }
        }

        /// <summary>
        /// Ensures SQLite database connection is established and the Note table exists.
        /// </summary>
        public async Task EnsureInitializedAsync()
        {
            if (_isInitialized) return;

            await _dbLock.WaitAsync();
            try
            {
                if (_isInitialized) return;

                var dbPath = GetDatabasePath();
                _db = new SQLiteAsyncConnection(dbPath, SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
                await _db.CreateTableAsync<Note>();
                await _db.CreateTableAsync<DeletedNoteTombstone>();

                var cutoff = DateTime.UtcNow.AddDays(-30);
                var tombstones = await _db.Table<DeletedNoteTombstone>().ToListAsync();
                foreach (var t in tombstones)
                {
                    if (t.DeletedAt >= cutoff)
                    {
                        _locallyDeletedNoteIds[t.NoteId] = t.DeletedAt;
                    }
                }

                var persisted = await _db.Table<Note>().ToListAsync();
                lock (_notes)
                {
                    // Scal z pamięcią (np. jeśli dodano notatki w testach przed inicjalizacją bazy)
                    var existingIds = _notes.Select(n => n.Id).ToHashSet();
                    foreach (var note in persisted)
                    {
                        // Nie przywracaj notatek, które zostały usunięte
                        if (_locallyDeletedNoteIds.ContainsKey(note.Id))
                        {
                            continue;
                        }

                        if (!existingIds.Contains(note.Id))
                        {
                            _notes.Add(note);
                        }
                    }
                }

                _isInitialized = true;
                OnNotesChanged?.Invoke();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task RecordLocalTombstoneAsync(Guid id)
        {
            _locallyDeletedNoteIds[id] = DateTime.UtcNow;
            if (_db != null)
            {
                await _dbLock.WaitAsync();
                try
                {
                    await _db.InsertOrReplaceAsync(new DeletedNoteTombstone { NoteId = id, DeletedAt = DateTime.UtcNow });
                }
                catch {}
                finally
                {
                    _dbLock.Release();
                }
            }
        }

        public async Task RemoveLocalTombstoneAsync(Guid id)
        {
            _locallyDeletedNoteIds.TryRemove(id, out _);
            if (_db != null)
            {
                await _dbLock.WaitAsync();
                try
                {
                    await _db.DeleteAsync<DeletedNoteTombstone>(id);
                }
                catch {}
                finally
                {
                    _dbLock.Release();
                }
            }
        }

        // =======================================================
        // METODY ZGODNE Z ISTNIEJĄCYMI TESTAMI I KODEM
        // =======================================================

        /// <summary>
        /// Returns all notes ordered with pinned notes first, then descending by last update timestamp.
        /// </summary>
        public List<Note> GetAllNotes()
        {
            lock (_notes)
            {
                return _notes.OrderByDescending(n => n.IsPinned)
                             .ThenByDescending(n => n.UpdatedAt)
                             .ToList();
            }
        }

        /// <summary>
        /// Returns notes associated with a specific university lesson.
        /// </summary>
        public List<Note> GetNotesForLesson(string subject, string type, DateTime date)
        {
            if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("Subject cannot be null or whitespace.", nameof(subject));
            if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("Type cannot be null or whitespace.", nameof(type));

            lock (_notes)
            {
                return _notes.Where(n =>
                    string.Equals(n.Subject, subject, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(n.LessonType, type, StringComparison.OrdinalIgnoreCase) &&
                    n.LessonDate.Date == date.Date)
                    .OrderByDescending(n => n.CreatedAt)
                    .ToList();
            }
        }

        /// <summary>
        /// Adds or updates a note in memory and triggers background persistence to SQLite.
        /// </summary>
        public void AddNote(Note note)
        {
            if (note == null) throw new ArgumentNullException(nameof(note), "Note cannot be null.");
            _locallyDeletedNoteIds.TryRemove(note.Id, out _);
            note.Content = StripHeavyDataUris(note.Content);

            lock (_notes)
            {
                var existingIndex = _notes.FindIndex(n => n.Id == note.Id);
                if (existingIndex >= 0)
                {
                    _notes[existingIndex] = note;
                }
                else
                {
                    _notes.Add(note);
                }
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await PersistNoteToDbAsync(note);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NoteService] AddNote persist error: {ex.Message}");
                }
            });

            OnNotesChanged?.Invoke();
        }

        public void DeleteNote(Guid id)
        {
            _locallyDeletedNoteIds[id] = DateTime.UtcNow;
            Note? note;
            lock (_notes)
            {
                note = _notes.FirstOrDefault(n => n.Id == id);
                if (note != null) _notes.Remove(note);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await RecordLocalTombstoneAsync(id);
                    DeleteAttachmentFiles(note);
                    await EnsureInitializedAsync();
                    if (_db != null)
                    {
                        await _dbLock.WaitAsync();
                        try
                        {
                            await _db.DeleteAsync<Note>(id);
                        }
                        finally
                        {
                            _dbLock.Release();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NoteService] DeleteNote persist error: {ex.Message}");
                }
            });

            OnNotesChanged?.Invoke();
        }

        /// <summary>
        /// Saves note changes to memory and writes them synchronously/asynchronously to the local SQLite database.
        /// Automatically strips raw Base64 data URIs from HTML content to prevent database bloating.
        /// </summary>
        public async Task SaveNoteAsync(Note note)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            _locallyDeletedNoteIds.TryRemove(note.Id, out _);
            _ = RemoveLocalTombstoneAsync(note.Id);
            note.UpdatedAt = DateTime.Now;
            note.Content = StripHeavyDataUris(note.Content);

            lock (_notes)
            {
                var idx = _notes.FindIndex(n => n.Id == note.Id);
                if (idx >= 0) _notes[idx] = note;
                else _notes.Add(note);
            }

            await PersistNoteToDbAsync(note);
            OnNotesChanged?.Invoke();
        }

        /// <summary>
        /// Deletes note from memory and removes its record from the SQLite database.
        /// </summary>
        public async Task DeleteNoteAsync(Guid id)
        {
            _locallyDeletedNoteIds[id] = DateTime.UtcNow;
            Note? n;
            lock (_notes)
            {
                n = _notes.FirstOrDefault(x => x.Id == id);
                if (n != null) _notes.Remove(n);
            }

            DeleteAttachmentFiles(n);
            await RecordLocalTombstoneAsync(id);

            await EnsureInitializedAsync();
            if (_db != null)
            {
                await _dbLock.WaitAsync();
                try
                {
                    await _db.DeleteAsync<Note>(id);
                }
                finally
                {
                    _dbLock.Release();
                }
            }

            OnNotesChanged?.Invoke();
        }

        private static void DeleteAttachmentFiles(Note? note)
        {
            if (note?.Attachments == null) return;
            foreach (var att in note.Attachments)
            {
                try
                {
                    if (!string.IsNullOrEmpty(att.LocalPath) && File.Exists(att.LocalPath))
                    {
                        File.Delete(att.LocalPath);
                    }
                    if (!string.IsNullOrEmpty(att.ThumbnailPath) && File.Exists(att.ThumbnailPath))
                    {
                        File.Delete(att.ThumbnailPath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NoteService] Error deleting attachment file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Toggles pinned state of a note, keeping it pinned to top of the list.
        /// </summary>
        public async Task TogglePinAsync(Guid id)
        {
            Note? note;
            lock (_notes)
            {
                note = _notes.FirstOrDefault(n => n.Id == id);
                if (note != null)
                {
                    note.IsPinned = !note.IsPinned;
                    note.UpdatedAt = DateTime.Now;
                }
            }

            if (note != null)
            {
                await PersistNoteToDbAsync(note);
                OnNotesChanged?.Invoke();
            }
        }

        private async Task PersistNoteToDbAsync(Note note)
        {
            try
            {
                note.Content = StripHeavyDataUris(note.Content);
                await EnsureInitializedAsync();
                if (_db != null)
                {
                    await _dbLock.WaitAsync();
                    try
                    {
                        await _db.InsertOrReplaceAsync(note);
                    }
                    finally
                    {
                        _dbLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NoteService] PersistNoteToDb error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sanitizes HTML note content by stripping inline Base64 data URIs from images associated
        /// with attachments (preserving data-attachment-id) and removing oversized pasted data URIs.
        /// </summary>
        public static string StripHeavyDataUris(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            if (!html.Contains("data:image", StringComparison.OrdinalIgnoreCase)) return html;

            // Zdejmij atrybuty src="data:image/..." z tagów z data-attachment-id
            var result = System.Text.RegularExpressions.Regex.Replace(
                html,
                @"(<img[^>]*\bdata-attachment-id=[^>]*?)\s*src=""data:image\/[^""]*""",
                "$1",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                @"(<img[^>]*?)\s*src=""data:image\/[^""]*""([^>]*\bdata-attachment-id=)",
                "$1$2",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            // Zabezpieczenie przed gigantycznymi ciągami wklejonymi bezpośrednio bez data-attachment-id (>1000 znaków)
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                @"\s*src=""data:image\/[^""]{1000,}""",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            return result;
        }

        // =======================================================
        // METODA CLOUD-READY: Zgodna z testami
        // =======================================================
        public async Task<string> UploadImageToCloudAsync(string base64Image)
        {
            if (string.IsNullOrWhiteSpace(base64Image)) throw new ArgumentException("Base64 image cannot be null or whitespace.", nameof(base64Image));
            await Task.Delay(100);
            return base64Image;
        }

        // =======================================================
        // INTELIGENTNA HIERARCHIA FOLDERÓW Z USOS
        // =======================================================

        /// <summary>
        /// Builds hierarchical folder tree combining default system folders (All, Pinned, Shared, Uncategorized)
        /// with academic subjects and groups retrieved from USOS timetable.
        /// </summary>
        public List<NoteFolderNode> BuildFolderHierarchy(IEnumerable<Lesson>? scheduleLessons)
        {
            var rootFolders = new List<NoteFolderNode>();

            List<Note> allNotesSnapshot;
            lock (_notes)
            {
                allNotesSnapshot = _notes.ToList();
            }

            // 1. Domyślne foldery systemowe
            rootFolders.Add(new NoteFolderNode
            {
                Id = "system_all",
                Name = "Wszystkie notatki",
                Icon = "📝",
                FolderType = NoteFolderType.All,
                NoteCount = allNotesSnapshot.Count
            });

            rootFolders.Add(new NoteFolderNode
            {
                Id = "system_pinned",
                Name = "Przypięte",
                Icon = "📌",
                FolderType = NoteFolderType.Pinned,
                NoteCount = allNotesSnapshot.Count(n => n.IsPinned)
            });

            rootFolders.Add(new NoteFolderNode
            {
                Id = "system_devices",
                Name = "Moje urządzenia",
                Icon = "📱💻",
                FolderType = NoteFolderType.BetweenDevices,
                NoteCount = allNotesSnapshot.Count(n => n.SharingMode == NoteSharingMode.BetweenDevices)
            });

            rootFolders.Add(new NoteFolderNode
            {
                Id = "system_local",
                Name = "Tylko lokalnie",
                Icon = "🔒",
                FolderType = NoteFolderType.LocalOnly,
                NoteCount = allNotesSnapshot.Count(n => n.SharingMode == NoteSharingMode.LocalOnly)
            });

            rootFolders.Add(new NoteFolderNode
            {
                Id = "system_shared",
                Name = "Udostępnione w grupie",
                Icon = "👥",
                FolderType = NoteFolderType.Shared,
                NoteCount = allNotesSnapshot.Count(n => n.SharingMode == NoteSharingMode.SharedWithAll || n.IsSharedWithGroup)
            });

            rootFolders.Add(new NoteFolderNode
            {
                Id = "system_uncategorized",
                Name = "Własne / Luźne notatki",
                Icon = "📁",
                FolderType = NoteFolderType.Uncategorized,
                NoteCount = allNotesSnapshot.Count(n => string.IsNullOrWhiteSpace(n.Subject))
            });

            // 2. Przedmioty z USOS oraz z istniejących notatek
            var lessons = scheduleLessons?.ToList() ?? new List<Lesson>();

            // Pobierz unikalne nazwy przedmiotów (z planu oraz z notatek)
            var subjectNames = lessons.Select(l => l.Title)
                .Concat(allNotesSnapshot.Select(n => n.Subject))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();

            foreach (var subject in subjectNames)
            {
                var subjectNode = new NoteFolderNode
                {
                    Id = $"subj_{subject}",
                    Name = subject,
                    Icon = "📚",
                    Subject = subject,
                    FolderType = NoteFolderType.Subject,
                    IsExpanded = true
                };

                // Wszystkie lekcje i notatki dla tego przedmiotu
                var subjectLessons = lessons.Where(l => string.Equals(l.Title, subject, StringComparison.OrdinalIgnoreCase)).ToList();
                var subjectNotes = allNotesSnapshot.Where(n => string.Equals(n.Subject, subject, StringComparison.OrdinalIgnoreCase)).ToList();

                subjectNode.NoteCount = subjectNotes.Count;

                // Pobierz typy zajęć dla tego przedmiotu (np. WYK, LAB, CW, PROJ)
                var types = subjectLessons.Select(l => NormalizeLessonType(l.Type))
                    .Concat(subjectNotes.Select(n => NormalizeLessonType(n.LessonType)))
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(t => t)
                    .ToList();

                foreach (var lessonType in types)
                {
                    var typeLessons = subjectLessons.Where(l => string.Equals(NormalizeLessonType(l.Type), lessonType, StringComparison.OrdinalIgnoreCase)).ToList();
                    var typeNotes = subjectNotes.Where(n => string.Equals(NormalizeLessonType(n.LessonType), lessonType, StringComparison.OrdinalIgnoreCase)).ToList();

                    var typeNode = new NoteFolderNode
                    {
                        Id = $"type_{subject}_{lessonType}",
                        Name = FormatLessonTypeName(lessonType),
                        Icon = GetLessonTypeIcon(lessonType),
                        Subject = subject,
                        LessonType = lessonType,
                        FolderType = NoteFolderType.LessonType,
                        NoteCount = typeNotes.Count,
                        IsExpanded = true
                    };

                    // Sprawdzamy podział na grupy:
                    // Szukamy unikalnych grup (np. "Gr. 1", "Gr. 2")
                    var distinctGroups = typeLessons.Select(l => CleanGroupName(l.Group))
                        .Concat(typeNotes.Select(n => CleanGroupName(n.GroupName)))
                        .Where(g => !string.IsNullOrWhiteSpace(g) && !string.Equals(g, "ALL", StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(g => g)
                        .ToList();

                    // Kluczowa zasada: jeśli jest WIĘCEJ NIŻ 1 grupa, tworzymy podfoldery grup.
                    // Jeśli jest tylko jedna grupa lub brak grup -> NIE tworzymy podfolderu grupy!
                    if (distinctGroups.Count > 1)
                    {
                        foreach (var group in distinctGroups)
                        {
                            var groupNotes = typeNotes.Where(n => string.Equals(CleanGroupName(n.GroupName), group, StringComparison.OrdinalIgnoreCase)).ToList();

                            var groupNode = new NoteFolderNode
                            {
                                Id = $"grp_{subject}_{lessonType}_{group}",
                                Name = group,
                                Icon = "👥",
                                Subject = subject,
                                LessonType = lessonType,
                                GroupName = group,
                                FolderType = NoteFolderType.Group,
                                NoteCount = groupNotes.Count
                            };

                            typeNode.Children.Add(groupNode);
                        }
                    }

                    subjectNode.Children.Add(typeNode);
                }

                rootFolders.Add(subjectNode);
            }

            return rootFolders;
        }

        private static string NormalizeLessonType(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var trimmed = raw.Trim();
            if (trimmed.Contains("Wykład", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("WYK", StringComparison.OrdinalIgnoreCase)) return "WYK";
            if (trimmed.Contains("Laboratorium", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("LAB", StringComparison.OrdinalIgnoreCase)) return "LAB";
            if (trimmed.Contains("Ćwiczenia", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("CW", StringComparison.OrdinalIgnoreCase)) return "CW";
            if (trimmed.Contains("Projekt", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("PROJ", StringComparison.OrdinalIgnoreCase)) return "PROJ";
            if (trimmed.Contains("Lektorat", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("LEKT", StringComparison.OrdinalIgnoreCase)) return "LEKT";
            if (trimmed.Contains("Seminarium", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("SEM", StringComparison.OrdinalIgnoreCase)) return "SEM";
            return trimmed;
        }

        private static string FormatLessonTypeName(string typeCode)
        {
            return typeCode switch
            {
                "WYK" => "Wykład",
                "LAB" => "Laboratorium",
                "CW" => "Ćwiczenia",
                "PROJ" => "Projekt",
                "LEKT" => "Lektorat",
                "SEM" => "Seminarium",
                _ => typeCode
            };
        }

        private static string GetLessonTypeIcon(string typeCode)
        {
            return typeCode switch
            {
                "WYK" => "🎓",
                "LAB" => "💻",
                "CW" => "📝",
                "PROJ" => "📐",
                "LEKT" => "🗣️",
                "SEM" => "🔬",
                _ => "📂"
            };
        }

        private static string CleanGroupName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var trimmed = raw.Trim();
            if (trimmed.Equals("ALL", StringComparison.OrdinalIgnoreCase)) return "";
            return trimmed;
        }

        /// <summary>
        /// Returns the directory where note attachments and whiteboard photo scans are stored on disk.
        /// </summary>
        public static string GetAttachmentsDirectory()
        {
            var localDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localDir) || !Directory.Exists(localDir))
            {
                localDir = AppDomain.CurrentDomain.BaseDirectory;
            }
            var dir = Path.Combine(localDir, "UniAvio", "notes_attachments");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Saves an attachment stream to disk, optimizes images via <see cref="WhiteboardImageOptimizer"/>,
        /// generates a thumbnail, and registers the attachment metadata on the note.
        /// </summary>
        public async Task<NoteAttachment?> AddAttachmentAsync(Guid noteId, Stream stream, string fileName, string contentType, bool alreadyOptimized = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (string.IsNullOrWhiteSpace(fileName)) fileName = $"zalacznik_{DateTime.Now:yyyyMMdd_HHmmss}";

            Note? targetNote;
            lock (_notes)
            {
                targetNote = _notes.FirstOrDefault(n => n.Id == noteId);
            }
            if (targetNote == null) return null;

            var attachmentId = Guid.NewGuid().ToString("N");
            var ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext))
            {
                ext = contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ? ".pdf" : ".jpg";
            }

            var safeBaseName = $"{noteId}_{attachmentId}";
            var attachmentsDir = GetAttachmentsDirectory();
            var targetFilePath = Path.Combine(attachmentsDir, $"{safeBaseName}{ext}");
            var thumbFilePath = Path.Combine(attachmentsDir, $"{safeBaseName}_thumb.jpg");

            long sizeBytes;
            string finalThumbPath = "";

            var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                          ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                          ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                          ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                          ext.Equals(".heic", StringComparison.OrdinalIgnoreCase) ||
                          ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);

            if (isImage && !alreadyOptimized)
            {
                var (mainSize, thumbSize) = await WhiteboardImageOptimizer.OptimizeAndSaveAsync(stream, targetFilePath, thumbFilePath);
                sizeBytes = mainSize;
                finalThumbPath = thumbFilePath;
            }
            else
            {
                // Zapisz bezpośrednio (już zoptymalizowany obraz lub PDF/dokument)
                using (var outFs = File.Create(targetFilePath))
                {
                    await stream.CopyToAsync(outFs);
                }
                sizeBytes = new FileInfo(targetFilePath).Length;
                if (isImage)
                {
                    File.Copy(targetFilePath, thumbFilePath, true);
                    finalThumbPath = thumbFilePath;
                }
            }

            var attachment = new NoteAttachment
            {
                Id = attachmentId,
                FileName = fileName,
                ContentType = contentType,
                SizeBytes = sizeBytes,
                LocalPath = targetFilePath,
                ThumbnailPath = finalThumbPath,
                UploadedAt = DateTime.UtcNow
            };

            lock (_notes)
            {
                var list = targetNote.Attachments;
                list.Add(attachment);
                targetNote.Attachments = list;
                targetNote.UpdatedAt = DateTime.Now;
            }

            await SaveNoteAsync(targetNote);
            OnNotesChanged?.Invoke();
            return attachment;
        }

        /// <summary>
        /// Removes attachment metadata from note and deletes its files and thumbnails from disk.
        /// </summary>
        public async Task<bool> DeleteAttachmentAsync(Guid noteId, string attachmentId)
        {
            Note? targetNote;
            lock (_notes)
            {
                targetNote = _notes.FirstOrDefault(n => n.Id == noteId);
            }
            if (targetNote == null) return false;

            NoteAttachment? toRemove;
            lock (_notes)
            {
                var list = targetNote.Attachments;
                toRemove = list.FirstOrDefault(a => a.Id == attachmentId);
                if (toRemove != null)
                {
                    list.Remove(toRemove);
                    targetNote.Attachments = list;
                    targetNote.UpdatedAt = DateTime.Now;
                }
            }

            if (toRemove != null)
            {
                try
                {
                    if (!string.IsNullOrEmpty(toRemove.LocalPath) && File.Exists(toRemove.LocalPath))
                    {
                        File.Delete(toRemove.LocalPath);
                    }
                    if (!string.IsNullOrEmpty(toRemove.ThumbnailPath) && File.Exists(toRemove.ThumbnailPath))
                    {
                        File.Delete(toRemove.ThumbnailPath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NoteService] Attachment delete error: {ex.Message}");
                }

                await SaveNoteAsync(targetNote);
                OnNotesChanged?.Invoke();
                return true;
            }

            return false;
        }

        public static async Task<string> GetAttachmentDataUriAsync(string filePath, string contentType)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return "";
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(filePath);
                var base64 = Convert.ToBase64String(bytes);
                return $"data:{contentType};base64,{base64}";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Synchronizes notes between current device, user's private cloud (BetweenDevices),
        /// and university group shared notes (SharedWithAll).
        /// </summary>
        public async Task SyncWithCloudAsync(IFirebaseNoteSync firebaseService, string? albumNumber, string? groupCode)
        {
            if (firebaseService == null) return;
            if (!await _syncLock.WaitAsync(0)) return;

            try
            {
                await EnsureInitializedAsync();
                var localNotesSnapshot = GetAllNotes();

                // 1. Synchronizacja prywatna (Moje urządzenia - BetweenDevices & SharedWithAll)
                if (!string.IsNullOrWhiteSpace(albumNumber))
                {
                    var cloudDeletedIds = await firebaseService.GetDeletedNoteIdsFromCloudAsync(albumNumber);

                    // A. Usuń notatki skasowane w chmurze
                    foreach (var localNote in localNotesSnapshot)
                    {
                        if (cloudDeletedIds.Contains(localNote.Id) || _locallyDeletedNoteIds.ContainsKey(localNote.Id))
                        {
                            await DeleteNoteAsync(localNote.Id);
                        }
                    }

                    var cloudUserNotes = await firebaseService.GetUserNotesFromCloudAsync(albumNumber);
                    var cloudMap = cloudUserNotes.ToDictionary(n => n.Id, n => n);

                    // Odśwież migawkę po usunięciach
                    localNotesSnapshot = GetAllNotes();

                    foreach (var cloudNote in cloudUserNotes)
                    {
                        // Jeśli notatka została skasowana lokalnie, ale jeszcze wisi w chmurze, usuń z chmury i pomiń
                        if (_locallyDeletedNoteIds.ContainsKey(cloudNote.Id) || cloudDeletedIds.Contains(cloudNote.Id))
                        {
                            await firebaseService.DeleteNoteFromCloudAsync(albumNumber, cloudNote.Id);
                            continue;
                        }

                        var localMatch = localNotesSnapshot.FirstOrDefault(l => l.Id == cloudNote.Id);
                        if (localMatch == null)
                        {
                            // Nowa notatka z drugiego urządzenia (np. utworzona na telefonie)
                            AddNote(cloudNote);
                        }
                        else if (cloudNote.UpdatedAt > localMatch.UpdatedAt)
                        {
                            // Notatka w chmurze ma nowsze zmiany
                            await SaveNoteAsync(cloudNote);
                        }
                        else if (localMatch.UpdatedAt > cloudNote.UpdatedAt && localMatch.SharingMode != NoteSharingMode.LocalOnly)
                        {
                            // Notatka lokalna jest nowsza – zaktualizuj chmurę
                            await firebaseService.SyncNoteToCloudAsync(albumNumber, localMatch);
                        }
                    }

                    // Wyślij lokalne notatki BetweenDevices/SharedWithAll, których jeszcze nie ma w chmurze
                    // (Tylko jeśli nie ma ich w tombstones!)
                    foreach (var localNote in localNotesSnapshot.Where(n => n.SharingMode is NoteSharingMode.BetweenDevices or NoteSharingMode.SharedWithAll))
                    {
                        if (!cloudMap.ContainsKey(localNote.Id) &&
                            !cloudDeletedIds.Contains(localNote.Id) &&
                            !_locallyDeletedNoteIds.ContainsKey(localNote.Id))
                        {
                            await firebaseService.SyncNoteToCloudAsync(albumNumber, localNote);
                        }
                    }
                }

                // 2. Synchronizacja grupowa (Udostępnione wszystkim - SharedWithAll)
                if (!string.IsNullOrWhiteSpace(groupCode))
                {
                    var groupDeletedIds = await firebaseService.GetGroupDeletedNoteIdsFromCloudAsync(groupCode);

                    // Usuń lokalnie notatki usunięte z lobby grupy
                    foreach (var localNote in localNotesSnapshot.Where(n => n.SharingMode == NoteSharingMode.SharedWithAll))
                    {
                        if (groupDeletedIds.Contains(localNote.Id))
                        {
                            await DeleteNoteAsync(localNote.Id);
                        }
                    }

                    var groupNotes = await firebaseService.GetGroupSharedNotesAsync(groupCode);
                    localNotesSnapshot = GetAllNotes();

                    foreach (var gNote in groupNotes)
                    {
                        if (groupDeletedIds.Contains(gNote.Id) || _locallyDeletedNoteIds.ContainsKey(gNote.Id))
                        {
                            continue;
                        }

                        var localMatch = localNotesSnapshot.FirstOrDefault(l => l.Id == gNote.Id);
                        if (localMatch == null)
                        {
                            gNote.SharingMode = NoteSharingMode.SharedWithAll;
                            AddNote(gNote);
                        }
                        else if (gNote.UpdatedAt > localMatch.UpdatedAt)
                        {
                            gNote.SharingMode = NoteSharingMode.SharedWithAll;
                            await SaveNoteAsync(gNote);
                        }
                    }
                }

                OnNotesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NoteService] SyncWithCloudAsync error: {ex.Message}");
            }
            finally
            {
                _syncLock.Release();
            }
        }

        public void Dispose()
        {
            _dbLock.Dispose();
            _syncLock.Dispose();
        }
    }
}
