using Portal_Studenta.Models;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Portal_Studenta.Services
{

    public class ScheduleService : IDisposable
    {
        private readonly FirebaseService? _firebaseService;
        private readonly AppState? _appState;
        private readonly ProfileService? _profileService;
        private readonly NotificationService? _notificationService;

        private List<Lesson> _lessons = new();
        private List<Lesson> _godModeLessons = new();
        public List<Lesson> CustomEvents { get; set; } = new();
        private Dictionary<string, LessonModification> _modifications = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> _lecturersCache = new();
        private DateTime? _lastGodModeFetchTime;

        public bool IsLoaded { get; private set; } = false;
        public bool IsGodModeLoaded => _godModeLessons.Any();
        public event Action? OnScheduleLoaded;

        public const int CacheValidityMinutes = 60;

        public bool IsGodModeFresh(DateTime start, int days) =>
            _godModeLessons.Any(l => l.Start >= start && l.Start < start.AddDays(days)) &&
            _lastGodModeFetchTime.HasValue &&
            (DateTime.UtcNow - _lastGodModeFetchTime.Value).TotalMinutes < 30;

        public DateTime? LastUsosFetchTime
        {
            get
            {
                var raw = Preferences.Default.Get("usos_schedule_last_fetch_utc", string.Empty);
                if (DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                {
                    return dt;
                }
                return null;
            }
            set
            {
                if (value.HasValue)
                {
                    Preferences.Default.Set("usos_schedule_last_fetch_utc", value.Value.ToString("O"));
                }
                else
                {
                    Preferences.Default.Remove("usos_schedule_last_fetch_utc");
                }
            }
        }

        public bool IsCacheFresh =>
            ScheduleLogic.IsCacheFresh(_lessons.Count, LastUsosFetchTime, DateTime.UtcNow, CacheValidityMinutes);

        public string? LastUsosErrorMessage { get; private set; }
        public bool HasUsosAuthError { get; private set; }

        private readonly SemaphoreSlim _loadLock = new(1, 1);

        private static string ModificationsFilePath => Path.Combine(FileSystem.AppDataDirectory, "schedule_modifications.json");
        private static string CacheFilePath => Path.Combine(FileSystem.AppDataDirectory, "usos_schedule_cache.json");
        private static string CustomEventsFilePath => Path.Combine(FileSystem.AppDataDirectory, "custom_events_cache.json");
        private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

        private string _lastSyncedGroupCode = "";
        private string _subscribedGroupCode = "";
        private IDisposable? _groupEventsSubscription;
        private readonly System.Timers.Timer _cloudSyncTimer;

        public ScheduleService(
            FirebaseService? firebaseService = null, 
            AppState? appState = null, 
            ProfileService? profileService = null,
            NotificationService? notificationService = null)
        {
            _firebaseService = firebaseService;
            _appState = appState;
            _profileService = profileService;
            _notificationService = notificationService;

            if (_appState != null)
            {
                _lastSyncedGroupCode = _appState.GroupCode;
                _appState.OnChange += OnAppStateChanged;
            }

            LoadModificationsFromDisk();
            LoadCacheFromDisk();
            LoadCustomEventsFromDisk();

            _ = Task.Run(async () =>
            {
                try
                {
                    await SyncCloudEventsAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScheduleService] Background sync error: {ex.Message}");
                }
            });

            _cloudSyncTimer = new System.Timers.Timer(15000); // synchronizacja w tle co 15s dla płynnego cross-device sync
            _cloudSyncTimer.AutoReset = true;
            _cloudSyncTimer.Elapsed += async (s, e) =>
            {
                try
                {
                    await SyncCloudEventsAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScheduleService] Błąd cyklicznej synchronizacji chmury: {ex.Message}");
                }
            };
            _cloudSyncTimer.Start();
        }

        public void Dispose()
        {
            _cloudSyncTimer?.Stop();
            _cloudSyncTimer?.Dispose();
            _groupEventsSubscription?.Dispose();
            if (_appState != null)
            {
                _appState.OnChange -= OnAppStateChanged;
            }
        }

        private void OnAppStateChanged()
        {
            var curGroup = _appState?.GroupCode ?? "";
            if (string.IsNullOrEmpty(curGroup))
            {
                if (!string.IsNullOrEmpty(_lastSyncedGroupCode))
                {
                    _lastSyncedGroupCode = "";
                    PurgeGroupScheduleData();
                }
            }
            else if (curGroup != _lastSyncedGroupCode)
            {
                if (!string.IsNullOrEmpty(_lastSyncedGroupCode))
                {
                    PurgeGroupScheduleData();
                }
                _lastSyncedGroupCode = curGroup;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SyncCloudEventsAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ScheduleService] Błąd synchronizacji po zmianie grupy: {ex.Message}");
                    }
                });
            }
        }

        public void PurgeGroupScheduleData()
        {
            try
            {
                _groupEventsSubscription?.Dispose();
                _groupEventsSubscription = null;
                _subscribedGroupCode = "";

                // 1. Usuń wszystkie wydarzenia starosty z grupy (STAR)
                CustomEvents.RemoveAll(c => !c.IsPersonal || c.Type == "STAR");
                SaveCustomEventsToDisk();

                // 2. Usuń modyfikacje należące do grupy z pamięci
                var groupKeys = _modifications.Values.Where(m => m.IsGroupModification).Select(m => m.LessonKey).ToList();
                foreach (var gk in groupKeys)
                {
                    _modifications.Remove(gk);
                }
                SaveModificationsToDisk();

                // 3. Przywróć czysty plan z USOS (z pliku cache) i nałóż wyłącznie modyfikacje osobiste
                if (File.Exists(CacheFilePath))
                {
                    var json = File.ReadAllText(CacheFilePath);
                    var cached = JsonSerializer.Deserialize<List<Lesson>>(json);
                    if (cached != null && cached.Any())
                    {
                        _lessons = cached;
                    }
                }
                else
                {
                    foreach (var l in _lessons)
                    {
                        l.IsCanceled = false;
                        if (l.OriginalStart != default) l.Start = l.OriginalStart;
                        if (l.OriginalEnd != default) l.End = l.OriginalEnd;
                        if (!string.IsNullOrEmpty(l.OriginalPlace)) l.Place = l.OriginalPlace;
                        l.HasExam = false;
                        l.HasTask = false;
                        l.TaskDetails = string.Empty;
                    }
                }

                ApplyModifications(_lessons);
                SaveCacheToDisk();
                OnScheduleLoaded?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScheduleService] Błąd czyszczenia danych grupy: {ex.Message}");
            }
        }

        public string GetCurrentUserKey()
        {
            if (_profileService != null && !string.IsNullOrEmpty(_profileService.StudentNumber))
                return _profileService.StudentNumber.Trim();
            if (_profileService != null && !string.IsNullOrEmpty(_profileService.Id))
                return _profileService.Id.Trim();
            var prefNum = Preferences.Default.Get("user_student_number", "");
            if (!string.IsNullOrEmpty(prefNum)) return prefNum.Trim();
            var prefId = Preferences.Default.Get("user_usos_id", "");
            if (!string.IsNullOrEmpty(prefId)) return prefId.Trim();
            return string.Empty;
        }

        public void Clear()
        {
            _lessons.Clear();
            _godModeLessons.Clear();
            CustomEvents.Clear();
            _modifications.Clear();
            IsLoaded = false;
            LastUsosFetchTime = null;
            _lastGodModeFetchTime = null;
            HasUsosAuthError = false;
            LastUsosErrorMessage = null;
            try
            {
                if (File.Exists(CacheFilePath)) File.Delete(CacheFilePath);
                if (File.Exists(ModificationsFilePath)) File.Delete(ModificationsFilePath);
                if (File.Exists(CustomEventsFilePath)) File.Delete(CustomEventsFilePath);
            }
            catch {}
        }

        public static string GetLessonKey(Lesson l)
        {
            return ScheduleLogic.GetLessonKey(l);
        }

        private void LoadModificationsFromDisk()
        {
            try
            {
                if (File.Exists(ModificationsFilePath))
                {
                    var json = File.ReadAllText(ModificationsFilePath);
                    var list = JsonSerializer.Deserialize<List<LessonModification>>(json);
                    if (list != null)
                    {
                        _modifications = list.ToDictionary(m => m.LessonKey, m => m, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScheduleService] Błąd odczytu modyfikacji: {ex.Message}");
            }
        }

        private void SaveModificationsToDisk()
        {
            try
            {
                var json = JsonSerializer.Serialize(_modifications.Values.ToList(), IndentedJsonOptions);
                File.WriteAllText(ModificationsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScheduleService] Błąd zapisu modyfikacji: {ex.Message}");
            }
        }

        private void LoadCustomEventsFromDisk()
        {
            try
            {
                if (File.Exists(CustomEventsFilePath))
                {
                    var json = File.ReadAllText(CustomEventsFilePath);
                    var cached = JsonSerializer.Deserialize<List<Lesson>>(json);
                    if (cached != null)
                    {
                        CustomEvents = cached;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScheduleService] Błąd odczytu wydarzeń własnych/grupy: {ex.Message}");
            }
        }

        public void SaveCustomEventsToDisk()
        {
            try
            {
                var json = JsonSerializer.Serialize(CustomEvents, IndentedJsonOptions);
                File.WriteAllText(CustomEventsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScheduleService] Błąd zapisu wydarzeń własnych/grupy: {ex.Message}");
            }
        }

        private void LoadCacheFromDisk()
        {
            try
            {
                int cacheVer = Preferences.Default.Get("schedule_cache_version", 1);
                if (cacheVer < 2)
                {
                    if (File.Exists(CacheFilePath))
                    {
                        File.Delete(CacheFilePath);
                    }
                    Preferences.Default.Set("schedule_cache_version", 2);
                    LastUsosFetchTime = null;
                    _lessons.Clear();
                    return;
                }

                if (File.Exists(CacheFilePath))
                {
                    var json = File.ReadAllText(CacheFilePath);
                    var cached = JsonSerializer.Deserialize<List<Lesson>>(json);
                    if (cached != null && cached.Any())
                    {
                        _lessons = cached;
                        ApplyModifications(_lessons);
                        IsLoaded = true;
                        _ = _notificationService?.ScheduleUpcomingClassRemindersAsync(_lessons);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScheduleService] Błąd odczytu cache harmonogramu: {ex.Message}");
            }
        }

        private void SaveCacheToDisk()
        {
            try
            {
                var json = JsonSerializer.Serialize(_lessons, IndentedJsonOptions);
                File.WriteAllText(CacheFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScheduleService] Błąd zapisu cache harmonogramu: {ex.Message}");
            }
        }

        public void ApplyModifications(List<Lesson> lessons)
        {
            ScheduleLogic.ApplyModifications(lessons, _modifications);
        }

        public async Task CancelLessonAsync(Lesson lesson, string? reason = null)
        {
            var key = GetLessonKey(lesson);
            bool isGroup = _appState != null && !string.IsNullOrEmpty(_appState.GroupCode) && _appState.HasManagementPrivileges;
            if (!_modifications.TryGetValue(key, out var mod))
            {
                mod = new LessonModification
                {
                    LessonKey = key,
                    Title = lesson.Title,
                    OriginalStart = lesson.OriginalStart != default ? lesson.OriginalStart : lesson.Start,
                    IsGroupModification = isGroup,
                    GroupCode = _appState?.GroupCode
                };
                _modifications[key] = mod;
            }
            mod.IsCanceled = true;
            mod.CancelReason = reason;
            mod.IsGroupModification = isGroup;
            mod.GroupCode = _appState?.GroupCode;

            lesson.IsCanceled = true;
            if (!string.IsNullOrWhiteSpace(reason)) lesson.Desc = reason;

            SaveModificationsToDisk();
            SaveCacheToDisk();
            await SyncModificationToFirebaseAsync(mod, lesson);
            OnScheduleLoaded?.Invoke();
        }

        public async Task RestoreLessonAsync(Lesson lesson)
        {
            var key = GetLessonKey(lesson);
            bool isGroup = _appState != null && !string.IsNullOrEmpty(_appState.GroupCode) && _appState.HasManagementPrivileges;
            if (_modifications.TryGetValue(key, out var mod))
            {
                mod.IsCanceled = false;
                mod.CancelReason = null;
                mod.IsGroupModification = isGroup;
                mod.GroupCode = _appState?.GroupCode;
            }
            lesson.IsCanceled = false;

            SaveModificationsToDisk();
            SaveCacheToDisk();
            if (mod != null) await SyncModificationToFirebaseAsync(mod, lesson);
            OnScheduleLoaded?.Invoke();
        }

        public async Task RescheduleLessonAsync(Lesson lesson, DateTime newStart, DateTime newEnd)
        {
            var key = GetLessonKey(lesson);
            bool isGroup = _appState != null && !string.IsNullOrEmpty(_appState.GroupCode) && _appState.HasManagementPrivileges;
            if (!_modifications.TryGetValue(key, out var mod))
            {
                mod = new LessonModification
                {
                    LessonKey = key,
                    Title = lesson.Title,
                    OriginalStart = lesson.OriginalStart != default ? lesson.OriginalStart : lesson.Start,
                    IsGroupModification = isGroup,
                    GroupCode = _appState?.GroupCode
                };
                _modifications[key] = mod;
            }
            if (lesson.OriginalStart == default) lesson.OriginalStart = lesson.Start;
            if (lesson.OriginalEnd == default) lesson.OriginalEnd = lesson.End;

            lesson.Start = newStart;
            lesson.End = newEnd;
            mod.ModifiedStart = newStart;
            mod.ModifiedEnd = newEnd;
            mod.IsGroupModification = isGroup;
            mod.GroupCode = _appState?.GroupCode;

            SaveModificationsToDisk();
            SaveCacheToDisk();
            await SyncModificationToFirebaseAsync(mod, lesson);
            OnScheduleLoaded?.Invoke();
        }

        public async Task<int> ChangeRoomAsync(Lesson lesson, string newPlace, bool allFutureSemesterClasses = false)
        {
            int count = 1;
            bool isGroup = _appState != null && !string.IsNullOrEmpty(_appState.GroupCode) && _appState.HasManagementPrivileges;
            string? grpCode = _appState?.GroupCode;

            if (allFutureSemesterClasses)
            {
                var futureLessons = ScheduleLogic.FindMatchingSemesterLessons(_lessons, lesson);
                count = futureLessons.Count;

                foreach (var fl in futureLessons)
                {
                    var key = GetLessonKey(fl);
                    if (!_modifications.TryGetValue(key, out var mod))
                    {
                        mod = new LessonModification
                        {
                            LessonKey = key,
                            Title = fl.Title,
                            OriginalStart = fl.OriginalStart != default ? fl.OriginalStart : fl.Start,
                            IsGroupModification = isGroup,
                            GroupCode = grpCode
                        };
                        _modifications[key] = mod;
                    }
                    if (string.IsNullOrEmpty(fl.OriginalPlace)) fl.OriginalPlace = fl.Place;
                    fl.Place = newPlace;
                    mod.ModifiedPlace = newPlace;
                    mod.IsGroupModification = isGroup;
                    mod.GroupCode = grpCode;

                    await SyncModificationToFirebaseAsync(mod, fl);
                }
            }
            else
            {
                var key = GetLessonKey(lesson);
                if (!_modifications.TryGetValue(key, out var mod))
                {
                    mod = new LessonModification
                    {
                        LessonKey = key,
                        Title = lesson.Title,
                        OriginalStart = lesson.OriginalStart != default ? lesson.OriginalStart : lesson.Start,
                        IsGroupModification = isGroup,
                        GroupCode = grpCode
                    };
                    _modifications[key] = mod;
                }
                if (string.IsNullOrEmpty(lesson.OriginalPlace)) lesson.OriginalPlace = lesson.Place;

                lesson.Place = newPlace;
                mod.ModifiedPlace = newPlace;
                mod.IsGroupModification = isGroup;
                mod.GroupCode = grpCode;

                await SyncModificationToFirebaseAsync(mod, lesson);
            }

            SaveModificationsToDisk();
            SaveCacheToDisk();
            OnScheduleLoaded?.Invoke();
            return count;
        }

        public async Task AddTaskOrExamAsync(Lesson lesson, bool isExam, string details)
        {
            var key = GetLessonKey(lesson);
            if (!_modifications.TryGetValue(key, out var mod))
            {
                mod = new LessonModification
                {
                    LessonKey = key,
                    Title = lesson.Title,
                    OriginalStart = lesson.OriginalStart != default ? lesson.OriginalStart : lesson.Start
                };
                _modifications[key] = mod;
            }
            if (isExam) { lesson.HasExam = true; lesson.IsHighPriority = true; mod.HasExam = true; }
            else { lesson.HasTask = true; mod.HasTask = true; }
            lesson.TaskDetails = details;
            mod.TaskDetails = details;

            SaveModificationsToDisk();
            SaveCacheToDisk();
            await SyncModificationToFirebaseAsync(mod, lesson);
            OnScheduleLoaded?.Invoke();
        }

        public async Task AddCustomEventAsync(Lesson ev)
        {
            if (ev.Id <= 0) ev.Id = ScheduleLogic.GenerateUniqueEventId(CustomEvents.Select(c => c.Id));
            
            // Security check: Only users with management privileges can publish group/STAR events
            bool canPublishGroup = _appState != null && !string.IsNullOrEmpty(_appState.GroupCode) && _appState.HasManagementPrivileges;
            if ((!ev.IsPersonal || ev.Type == "STAR") && !canPublishGroup)
            {
                ev.IsPersonal = true;
                ev.Type = "WŁASNE";
                ev.Group = "JA";
                ev.Person = "Ja";
            }

            CustomEvents.RemoveAll(c => c.Id == ev.Id || (c.Title == ev.Title && c.Start == ev.Start && c.IsPersonal == ev.IsPersonal));
            CustomEvents.Add(ev);
            SaveCustomEventsToDisk();

            if (_firebaseService != null)
            {
                if (!ev.IsPersonal || ev.Type == "STAR")
                {
                    if (canPublishGroup)
                    {
                        var fbEvent = new FirebaseEvent
                        {
                            Uid = "STAR_" + ev.Id,
                            Title = ev.Title,
                            Type = "STAR",
                            Start = ev.Start,
                            End = ev.End,
                            Place = ev.Place ?? "",
                            Desc = ev.Desc ?? "",
                            TargetGroup = "ALL",
                            IsCanceled = false,
                            IsPersonal = false,
                            IsHighPriority = ev.IsHighPriority,
                            OriginalStart = ev.OriginalStart != default ? ev.OriginalStart : ev.Start,
                            OriginalEnd = ev.OriginalEnd != default ? ev.OriginalEnd : ev.End,
                            OriginalPlace = ev.Place ?? ""
                        };
                        await _firebaseService.PublishGroupEventAsync(_appState!.GroupCode, fbEvent);
                    }
                }
                else
                {
                    string userKey = GetCurrentUserKey();
                    if (!string.IsNullOrEmpty(userKey))
                    {
                        var fbEvent = new FirebaseEvent
                        {
                            Uid = "USER_" + ev.Id,
                            Title = ev.Title,
                            Type = "WŁASNE",
                            Start = ev.Start,
                            End = ev.End,
                            Place = ev.Place ?? "",
                            Desc = ev.Desc ?? "",
                            TargetGroup = "JA",
                            IsCanceled = false,
                            IsPersonal = true,
                            IsHighPriority = ev.IsHighPriority,
                            OriginalStart = ev.OriginalStart != default ? ev.OriginalStart : ev.Start,
                            OriginalEnd = ev.OriginalEnd != default ? ev.OriginalEnd : ev.End,
                            OriginalPlace = ev.Place ?? ""
                        };
                        await _firebaseService.PublishPersonalEventAsync(userKey, fbEvent);
                    }
                }
            }

            OnScheduleLoaded?.Invoke();
        }

        public async Task DeleteCustomEventAsync(Lesson ev)
        {
            bool canManageGroup = _appState != null && !string.IsNullOrEmpty(_appState.GroupCode) && _appState.HasManagementPrivileges;
            CustomEvents.RemoveAll(c => c.Id == ev.Id || (c.Title == ev.Title && c.Start == ev.Start));
            SaveCustomEventsToDisk();

            if (_firebaseService != null)
            {
                if (!ev.IsPersonal || ev.Type == "STAR")
                {
                    if (canManageGroup && _appState != null && !string.IsNullOrEmpty(_appState.GroupCode))
                    {
                        string uid = "STAR_" + ev.Id;
                        await _firebaseService.DeleteGroupEventAsync(_appState.GroupCode, uid);
                    }
                }
                else
                {
                    string userKey = GetCurrentUserKey();
                    if (!string.IsNullOrEmpty(userKey))
                    {
                        string uid = "USER_" + ev.Id;
                        await _firebaseService.DeletePersonalEventAsync(userKey, uid);
                    }
                }
            }

            OnScheduleLoaded?.Invoke();
        }

        private bool ProcessGroupEvent(FirebaseEvent ge)
        {
            if (ge == null) return false;
            bool changed = false;

            bool isStarCustomEvent = ge.Type == "STAR" || (!string.IsNullOrEmpty(ge.Uid) && ge.Uid.StartsWith("STAR_"));
            if (isStarCustomEvent)
            {
                int parsedId = 0;
                if (!string.IsNullOrEmpty(ge.Uid) && ge.Uid.StartsWith("STAR_") && int.TryParse(ge.Uid.Substring(5), out int pid))
                    parsedId = pid;
                if (parsedId == 0) parsedId = Math.Abs(ge.Uid.GetHashCode());

                var existing = CustomEvents.FirstOrDefault(c => (!c.IsPersonal || c.Type == "STAR") && (c.Id == parsedId || (c.Title == ge.Title && c.Start == ge.Start)));
                if (existing == null)
                {
                    CustomEvents.Add(new Lesson
                    {
                        Id = parsedId,
                        Title = ge.Title,
                        Type = "STAR",
                        Start = ge.Start,
                        End = ge.End,
                        Place = string.IsNullOrWhiteSpace(ge.Place) ? "Brak miejsca" : ge.Place,
                        Desc = ge.Desc,
                        Group = "ALL",
                        Person = "Starosta",
                        IsHighPriority = ge.IsHighPriority,
                        IsPersonal = false,
                        OriginalStart = ge.OriginalStart != default ? ge.OriginalStart : ge.Start,
                        OriginalEnd = ge.OriginalEnd != default ? ge.OriginalEnd : ge.End,
                        OriginalPlace = ge.Place
                    });
                    changed = true;
                }
                else
                {
                    if (existing.Start != ge.Start || existing.End != ge.End || existing.Place != ge.Place || existing.Desc != ge.Desc || existing.IsHighPriority != ge.IsHighPriority)
                    {
                        existing.Start = ge.Start;
                        existing.End = ge.End;
                        existing.Place = string.IsNullOrWhiteSpace(ge.Place) ? "Brak miejsca" : ge.Place;
                        existing.Desc = ge.Desc;
                        existing.IsHighPriority = ge.IsHighPriority;
                        changed = true;
                    }
                }
                return changed;
            }

            var baseDate = ge.OriginalStart != default ? ge.OriginalStart : ge.Start;
            var targetGrp = string.IsNullOrWhiteSpace(ge.TargetGroup) ? "ALL" : ge.TargetGroup.Trim();
            var key = $"{ge.Title.Trim()}_{ge.Type.Trim()}_{baseDate:yyyyMMddHHmm}_{targetGrp}".ToUpperInvariant();
            
            if (!_modifications.ContainsKey(key))
            {
                var newMod = new LessonModification
                {
                    LessonKey = key,
                    Title = ge.Title,
                    OriginalStart = baseDate,
                    IsCanceled = ge.IsCanceled,
                    CancelReason = ge.Desc,
                    ModifiedStart = ge.Start,
                    ModifiedEnd = ge.End,
                    ModifiedPlace = ge.Place,
                    HasExam = ge.IsHighPriority,
                    IsGroupModification = true,
                    GroupCode = _appState?.GroupCode
                };
                _modifications[key] = newMod;
                changed = true;

                if (ge.IsCanceled)
                {
                    _notificationService?.NotifyLessonCanceled(new Lesson { Title = ge.Title, Type = ge.Type, Start = ge.Start }, ge.Desc);
                }
                else if (!string.IsNullOrEmpty(ge.Place) && !string.IsNullOrEmpty(ge.OriginalPlace) && ge.OriginalPlace != ge.Place)
                {
                    _notificationService?.NotifyLessonRoomChanged(new Lesson { Title = ge.Title, Type = ge.Type, Start = ge.Start }, ge.Place);
                }
            }
            else
            {
                var m = _modifications[key];
                m.IsGroupModification = true;
                m.GroupCode = _appState?.GroupCode;
                bool wasCanceled = m.IsCanceled;
                string? oldPlace = m.ModifiedPlace;
                DateTime? oldStart = m.ModifiedStart;

                if (m.IsCanceled != ge.IsCanceled || m.CancelReason != ge.Desc || m.ModifiedStart != ge.Start || m.ModifiedEnd != ge.End || m.ModifiedPlace != ge.Place || m.HasExam != ge.IsHighPriority)
                {
                    m.IsCanceled = ge.IsCanceled;
                    if (!string.IsNullOrEmpty(ge.Desc)) m.CancelReason = ge.Desc;
                    m.ModifiedStart = ge.Start;
                    m.ModifiedEnd = ge.End;
                    m.ModifiedPlace = ge.Place;
                    m.HasExam = ge.IsHighPriority;
                    changed = true;

                    if (!wasCanceled && ge.IsCanceled)
                    {
                        _notificationService?.NotifyLessonCanceled(new Lesson { Title = ge.Title, Type = ge.Type, Start = ge.Start }, ge.Desc);
                    }
                    else if (!string.IsNullOrEmpty(ge.Place) && oldPlace != null && oldPlace != ge.Place)
                    {
                        _notificationService?.NotifyLessonRoomChanged(new Lesson { Title = ge.Title, Type = ge.Type, Start = ge.Start }, ge.Place);
                    }
                    else if (ge.Start != default && oldStart.HasValue && oldStart.Value != ge.Start)
                    {
                        _notificationService?.NotifyLessonRescheduled(new Lesson { Title = ge.Title, Type = ge.Type }, ge.Start, ge.End);
                    }
                }
            }

            return changed;
        }

        private void EnsureGroupEventsSubscription(string groupCode)
        {
            if (_firebaseService == null || string.IsNullOrWhiteSpace(groupCode)) return;
            groupCode = groupCode.Trim().ToUpperInvariant();
            if (_subscribedGroupCode == groupCode) return;

            try
            {
                _groupEventsSubscription?.Dispose();
                _subscribedGroupCode = groupCode;
                _groupEventsSubscription = _firebaseService.SubscribeToGroupEvents(groupCode).Subscribe(
                    ge =>
                    {
                        if (ge != null)
                        {
                            bool changed = ProcessGroupEvent(ge);
                            if (changed)
                            {
                                ApplyModifications(_lessons);
                                SaveModificationsToDisk();
                                SaveCustomEventsToDisk();
                                SaveCacheToDisk();
                                _ = _notificationService?.ScheduleUpcomingClassRemindersAsync(_lessons);
                                OnScheduleLoaded?.Invoke();
                            }
                        }
                    },
                    ex => Console.WriteLine($"[ScheduleService] Stream błąd: {ex.Message}"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScheduleService] Nie udało się zasubskrybować wydarzeń grupy: {ex.Message}");
            }
        }

        public async Task SyncCloudEventsAsync()
        {
            if (_firebaseService == null) return;

            bool hasChanges = false;

            // 1. Synchronizacja wydarzeń i modyfikacji grupy (Starosta/Admin -> widzą wszyscy w grupie)
            if (_appState != null && !string.IsNullOrEmpty(_appState.GroupCode))
            {
                EnsureGroupEventsSubscription(_appState.GroupCode);

                try
                {
                    var groupEvents = await _firebaseService.GetGroupEventsAsync(_appState.GroupCode);

                    // A. Usuwanie zdarzeń grupy, które zostały usunięte w chmurze
                    var activeGroupUids = new HashSet<string>(groupEvents.Select(g => g.Uid), StringComparer.OrdinalIgnoreCase);
                    int removedGroupCount = CustomEvents.RemoveAll(c =>
                        (!c.IsPersonal || c.Type == "STAR") &&
                        !activeGroupUids.Contains("STAR_" + c.Id) &&
                        !activeGroupUids.Contains(c.Id.ToString()) &&
                        !groupEvents.Any(ge => (ge.Type == "STAR" || (ge.Uid != null && ge.Uid.StartsWith("STAR_"))) && ge.Title == c.Title && ge.Start == c.Start));
                    if (removedGroupCount > 0) hasChanges = true;

                    // B. Dodawanie / aktualizacja zdarzeń grupy
                    foreach (var ge in groupEvents)
                    {
                        if (ProcessGroupEvent(ge))
                        {
                            hasChanges = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScheduleService] Błąd pobierania zdarzeń grupy: {ex.Message}");
                }
            }

            // 2. Synchronizacja osobistych wydarzeń i modyfikacji (Cross-Device Sync użytkownika)
            string userKey = GetCurrentUserKey();
            if (!string.IsNullOrEmpty(userKey))
            {
                try
                {
                    var personalEvents = await _firebaseService.GetPersonalEventsAsync(userKey);

                    // A. Usuwanie zdarzeń osobistych usuniętych na innym urządzeniu
                    var activePersonalUids = new HashSet<string>(personalEvents.Select(p => p.Uid), StringComparer.OrdinalIgnoreCase);
                    int removedPersonalCount = CustomEvents.RemoveAll(c =>
                        c.IsPersonal &&
                        !activePersonalUids.Contains("USER_" + c.Id) &&
                        !activePersonalUids.Contains(c.Id.ToString()) &&
                        !personalEvents.Any(pe => pe.Title == c.Title && pe.Start == c.Start));
                    if (removedPersonalCount > 0) hasChanges = true;

                    // B. Dodawanie / aktualizacja zdarzeń osobistych
                    foreach (var pe in personalEvents)
                    {
                        int parsedId = 0;
                        if (!string.IsNullOrEmpty(pe.Uid) && pe.Uid.StartsWith("USER_") && int.TryParse(pe.Uid.Substring(5), out int pid))
                            parsedId = pid;
                        if (parsedId == 0) parsedId = Math.Abs(pe.Uid.GetHashCode());

                        var existing = CustomEvents.FirstOrDefault(c => c.IsPersonal && (c.Id == parsedId || (c.Title == pe.Title && c.Start == pe.Start)));
                        if (existing == null)
                        {
                            CustomEvents.Add(new Lesson
                            {
                                Id = parsedId,
                                Title = pe.Title,
                                Type = "WŁASNE",
                                Start = pe.Start,
                                End = pe.End,
                                Place = string.IsNullOrWhiteSpace(pe.Place) ? "Brak miejsca" : pe.Place,
                                Desc = pe.Desc,
                                Group = "JA",
                                Person = "Ja",
                                IsHighPriority = pe.IsHighPriority,
                                IsPersonal = true,
                                OriginalStart = pe.OriginalStart != default ? pe.OriginalStart : pe.Start,
                                OriginalEnd = pe.OriginalEnd != default ? pe.OriginalEnd : pe.End,
                                OriginalPlace = pe.Place
                            });
                            hasChanges = true;
                        }
                        else
                        {
                            if (existing.Start != pe.Start || existing.End != pe.End || existing.Place != pe.Place || existing.Desc != pe.Desc || existing.IsHighPriority != pe.IsHighPriority)
                            {
                                existing.Start = pe.Start;
                                existing.End = pe.End;
                                existing.Place = string.IsNullOrWhiteSpace(pe.Place) ? "Brak miejsca" : pe.Place;
                                existing.Desc = pe.Desc;
                                existing.IsHighPriority = pe.IsHighPriority;
                                hasChanges = true;
                            }
                        }
                    }

                    // C. Modyfikacje osobiste planu
                    var personalMods = await _firebaseService.GetPersonalModificationsAsync<LessonModification>(userKey);
                    foreach (var pmod in personalMods)
                    {
                        if (!_modifications.TryGetValue(pmod.LessonKey, out var existingMod))
                        {
                            _modifications[pmod.LessonKey] = pmod;
                            hasChanges = true;
                        }
                        else
                        {
                            if (existingMod.IsCanceled != pmod.IsCanceled || existingMod.CancelReason != pmod.CancelReason ||
                                existingMod.ModifiedStart != pmod.ModifiedStart || existingMod.ModifiedEnd != pmod.ModifiedEnd ||
                                existingMod.ModifiedPlace != pmod.ModifiedPlace || existingMod.HasExam != pmod.HasExam ||
                                existingMod.HasTask != pmod.HasTask || existingMod.TaskDetails != pmod.TaskDetails)
                            {
                                existingMod.IsCanceled = pmod.IsCanceled;
                                existingMod.CancelReason = pmod.CancelReason;
                                existingMod.ModifiedStart = pmod.ModifiedStart;
                                existingMod.ModifiedEnd = pmod.ModifiedEnd;
                                existingMod.ModifiedPlace = pmod.ModifiedPlace;
                                existingMod.HasExam = pmod.HasExam;
                                existingMod.HasTask = pmod.HasTask;
                                existingMod.TaskDetails = pmod.TaskDetails;
                                hasChanges = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScheduleService] Błąd pobierania danych osobistych użytkownika: {ex.Message}");
                }
            }

            if (hasChanges)
            {
                ApplyModifications(_lessons);
                SaveModificationsToDisk();
                SaveCustomEventsToDisk();
                SaveCacheToDisk();
                _ = _notificationService?.ScheduleUpcomingClassRemindersAsync(_lessons);
                OnScheduleLoaded?.Invoke();
            }
        }

        private async Task SyncModificationToFirebaseAsync(LessonModification mod, Lesson lesson)
        {
            if (_firebaseService == null) return;

            // 1. Jeśli to modyfikacja starosty/admina dla grupy
            if (_appState != null && !string.IsNullOrEmpty(_appState.GroupCode) && _appState.HasManagementPrivileges)
            {
                mod.IsGroupModification = true;
                mod.GroupCode = _appState.GroupCode;
                try
                {
                    var fbEvent = new FirebaseEvent
                    {
                        Uid = FirebaseService.SanitizeKey(mod.LessonKey),
                        Title = lesson.Title,
                        Type = lesson.Type,
                        Start = lesson.Start,
                        End = lesson.End,
                        Place = lesson.Place,
                        Desc = lesson.Desc,
                        TargetGroup = lesson.Group,
                        IsCanceled = mod.IsCanceled,
                        IsPersonal = false,
                        IsHighPriority = mod.HasExam,
                        OriginalStart = lesson.OriginalStart,
                        OriginalEnd = lesson.OriginalEnd,
                        OriginalPlace = lesson.OriginalPlace
                    };
                    await _firebaseService.PublishGroupEventAsync(_appState.GroupCode, fbEvent);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScheduleService] Błąd synchronizacji modyfikacji grupy z Firebase: {ex.Message}");
                }
            }
            else
            {
                // 2. Synchronizacja osobista dla użytkownika (między urządzeniami) — TYLKO dla modyfikacji czysto osobistych
                mod.IsGroupModification = false;
                string userKey = GetCurrentUserKey();
                if (!string.IsNullOrEmpty(userKey))
                {
                    try
                    {
                        await _firebaseService.PublishPersonalModificationAsync(userKey, mod.LessonKey, mod);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ScheduleService] Błąd synchronizacji modyfikacji osobistej z Firebase: {ex.Message}");
                    }
                }
            }
        }

        private string GetShortBuildingName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Wydział Automatyki, Elektroniki i Informatyki", "AEiI" },
                { "Wydział Architektury", "WA" },
                { "Wydział Budownictwa", "WB" },
                { "Wydział Chemiczny", "WCh" },
                { "Wydział Elektryczny", "WE" },
                { "Wydział Górnictwa, Inżynierii Bezpieczeństwa i Automatyki Przemysłowej", "WGiBiAP" },
                { "Wydział Inżynierii Biomedycznej", "WIB" },
                { "Wydział Inżynierii Środowiska i Energetyki", "IŚiE" },
                { "Wydział Inżynierii Materiałowej", "WIM" },
                { "Wydział Matematyki Stosowanej", "MS" },
                { "Wydział Mechaniczny Technologiczny", "MT" },
                { "Wydział Organizacji i Zarządzania", "WOiZ" },
                { "Wydział Transportu i Inżynierii Lotniczej", "WTiIL" },
                { "Wydział Transportu", "WT" },
                { "Instytut Fizyki", "IF" },
                { "Studium Języków Obcych", "SJO" },
                { "Ośrodek Sportu", "OS" },
                { "Centrum Edukacyjno-Kongresowe", "CEK" },
                { "Centrum Nowych Technologii", "CNT" }
            };

            foreach (var kw in replacements)
            {
                if (name.Contains(kw.Key, StringComparison.OrdinalIgnoreCase))
                    return kw.Value;
            }

            var words = name.Split(new[] { ' ', ',', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            string acronym = "";
            foreach (var w in words)
            {
                if (w.Length > 0 && char.IsUpper(w[0]))
                {
                    if (w.Equals("Wydział", StringComparison.OrdinalIgnoreCase)) continue;
                    if (w.Equals("Instytut", StringComparison.OrdinalIgnoreCase)) continue;
                    acronym += w[0];
                }
                else if (w.ToLower() == "i")
                {
                    acronym += "i";
                }
            }

            return acronym.Length > 1 ? acronym : name;
        }

        public async Task<string?> FetchAndSyncTermFromUsosAsync(UsosApiService apiService)
        {
            try
            {
                var termsArr = await apiService.GetAsync<JsonElement>("services/terms/terms_index");
                if (termsArr.ValueKind == JsonValueKind.Array)
                {
                    var now = DateTime.Today;
                    JsonElement? matchingTerm = null;
                    DateTime latestStart = DateTime.MinValue;

                    foreach (var term in termsArr.EnumerateArray())
                    {
                        DateTime sDate = default, eDate = default;
                        bool hasStart = term.TryGetProperty("start_date", out var sDateProp) && DateTime.TryParse(sDateProp.GetString(), out sDate);
                        bool hasEnd = term.TryGetProperty("end_date", out var eDateProp) && DateTime.TryParse(eDateProp.GetString(), out eDate);

                        if (hasStart && hasEnd)
                        {
                            if (now >= sDate && now <= eDate)
                            {
                                matchingTerm = term;
                                break;
                            }
                            else if (sDate <= now && sDate > latestStart)
                            {
                                latestStart = sDate;
                                matchingTerm = term;
                            }
                        }
                    }

                    if (matchingTerm.HasValue)
                    {
                        var termId = matchingTerm.Value.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                        var startDateStr = matchingTerm.Value.TryGetProperty("start_date", out var sProp) ? sProp.GetString() ?? "" : "";
                        string termName = termId;
                        if (matchingTerm.Value.TryGetProperty("name", out var nameProp) && nameProp.TryGetProperty("pl", out var plName))
                        {
                            termName = plName.GetString() ?? termId;
                        }

                        if (_appState != null)
                        {
                            _appState.AcademicTermName = termName;
                            _appState.AcademicTermStart = startDateStr;
                        }

                        return $"{termName} ({startDateStr})";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScheduleService] FetchAndSyncTermFromUsosAsync error: {ex.Message}");
            }
            return null;
        }

        public async Task LoadDataFromUsosAsync(UsosApiService apiService, DateTime start, int days, bool godMode = false)
        {
            try
            {
                LastUsosErrorMessage = null;
                HasUsosAuthError = false;

                // W tle synchronizujemy dane semestru i parzystości tygodni
                _ = Task.Run(async () => { try { await FetchAndSyncTermFromUsosAsync(apiService); } catch {} });

                var newLessons = new List<Lesson>();
                var lecturerIdsToFetch = new HashSet<string>();
                int idCounter = 1;

                var allEventsJson = new List<JsonElement>();
                var courseToStudentUnits = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                // Zbieramy znane jednostki kursów z już załadowanego planu osobistego studenta
                foreach (var l in _lessons)
                {
                    if (!string.IsNullOrEmpty(l.CourseId) && !string.IsNullOrEmpty(l.CourseUnitId))
                    {
                        if (!courseToStudentUnits.ContainsKey(l.CourseId))
                            courseToStudentUnits[l.CourseId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        courseToStudentUnits[l.CourseId].Add(l.CourseUnitId);
                    }
                }

                if (!godMode)
                {
                    int remainingDays = days;
                    var currentStart = start;
                    while (remainingDays > 0)
                    {
                        int chunkDays = Math.Min(7, remainingDays);
                        // unit_id to prawidłowa nazwa pola w specyfikacji Activity USOS API (course_unit_id wywołuje 500 na serwerze USOS)
                        var url = $"services/tt/student?start={currentStart:yyyy-MM-dd}&days={chunkDays}&fields=type|start_time|end_time|course_id|course_name|classtype_name|room_number|lecturer_ids|building_name|group_number|unit_id";
                        JsonElement jsonArr;
                        try
                        {
                            jsonArr = await apiService.GetAsync<JsonElement>(url);
                        }
                        catch (Exception ex) when (!ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
                        {
                            // Awaryjny fallback: w razie gdyby konkretna instalacja USOS danej uczelni nie obsługiwała unit_id, odpytujemy o pola kanoniczne
                            var fallbackUrl = $"services/tt/student?start={currentStart:yyyy-MM-dd}&days={chunkDays}&fields=type|start_time|end_time|course_id|course_name|classtype_name|room_number|lecturer_ids|building_name|group_number";
                            jsonArr = await apiService.GetAsync<JsonElement>(fallbackUrl);
                        }

                        if (jsonArr.ValueKind == JsonValueKind.Array)
                        {
                            allEventsJson.AddRange(jsonArr.EnumerateArray());
                        }
                        remainingDays -= chunkDays;
                        currentStart = currentStart.AddDays(chunkDays);
                    }
                }
                else
                {
                    // W God Mode pobieramy edycje kursów studenta (services/courses/user nie posiada pola course_units)
                    var coursesUrl = "services/courses/user?fields=course_editions[course_id|term_id]";
                    var coursesObj = await apiService.GetAsync<JsonElement>(coursesUrl);

                    if (coursesObj.ValueKind == JsonValueKind.Object)
                    {
                        // Opcjonalnie próbujemy uzupełnić jednostki grup studenta z services/groups/user
                        try
                        {
                            var groupsObj = await apiService.GetAsync<JsonElement>("services/groups/user");
                            if (groupsObj.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var termProp in groupsObj.EnumerateObject())
                                {
                                    if (termProp.Value.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var grp in termProp.Value.EnumerateArray())
                                        {
                                            var uId = grp.TryGetProperty("course_unit_id", out var uidProp) ? uidProp.ToString() : null;
                                            var cId = grp.TryGetProperty("course_id", out var cidProp) ? cidProp.GetString() : null;
                                            if (!string.IsNullOrEmpty(uId) && !string.IsNullOrEmpty(cId))
                                            {
                                                if (!courseToStudentUnits.ContainsKey(cId))
                                                    courseToStudentUnits[cId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                                courseToStudentUnits[cId].Add(uId);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // services/groups/user jest opcjonalne – ignorujemy błąd w razie braku wsparcia na danej instalacji USOS
                        }

                        if (coursesObj.TryGetProperty("course_editions", out var editionsDict))
                        {
                            var tasks = new List<Task<JsonElement>>();
                            var allTermNames = editionsDict.EnumerateObject().Select(p => p.Name).ToList();
                            // Filtrujemy do maksymalnie 2 najnowszych semestrów (np. 2025Z, 2024L), aby nie odpytywać archiwalnych lat
                            var recentTerms = allTermNames.OrderByDescending(x => x).Take(2).ToHashSet();
                            var relevantEditions = editionsDict.EnumerateObject().Where(p => recentTerms.Contains(p.Name)).ToList();

                            foreach (var termProp in relevantEditions)
                            {
                                var termId = termProp.Name;
                                var coursesArr = termProp.Value;

                                foreach (var course in coursesArr.EnumerateArray())
                                {
                                    var courseId = course.TryGetProperty("course_id", out var cid) ? cid.GetString() : null;
                                    if (!string.IsNullOrEmpty(courseId))
                                    {
                                        int remainingDays = days;
                                        var currentStart = start;
                                        while (remainingDays > 0)
                                        {
                                            int chunkDays = Math.Min(7, remainingDays);
                                            var courseEventsUrl = $"services/tt/course_edition?course_id={Uri.EscapeDataString(courseId)}&term_id={Uri.EscapeDataString(termId)}&start={currentStart:yyyy-MM-dd}&days={chunkDays}&fields=type|start_time|end_time|course_id|course_name|classtype_name|room_number|lecturer_ids|building_name|group_number|unit_id";
                                            tasks.Add(apiService.GetAsync<JsonElement>(courseEventsUrl));
                                            remainingDays -= chunkDays;
                                            currentStart = currentStart.AddDays(chunkDays);
                                        }
                                    }
                                }
                            }

                            var results = await Task.WhenAll(tasks.Select(async t =>
                            {
                                try { return await t; } catch { return default; }
                            }));

                            foreach (var evArr in results)
                            {
                                if (evArr.ValueKind == JsonValueKind.Array)
                                {
                                    allEventsJson.AddRange(evArr.EnumerateArray());
                                }
                            }
                        }
                    }
                }

                // 1. Zbieramy identyfikatory wykładowców
                foreach (var ev in allEventsJson)
                {
                    if (ev.TryGetProperty("lecturer_ids", out var lecArray) && lecArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var lecId in lecArray.EnumerateArray())
                        {
                            if (lecId.ValueKind == JsonValueKind.Number)
                            {
                                lecturerIdsToFetch.Add(lecId.GetInt32().ToString());
                            }
                            else if (lecId.ValueKind == JsonValueKind.String)
                            {
                                lecturerIdsToFetch.Add(lecId.GetString() ?? "");
                            }
                        }
                    }
                }

                // 2. Pobieramy dane wykładowców (z wykorzystaniem trwałego cache)
                var lecturersDict = new Dictionary<string, string>();
                var unknownLecIds = lecturerIdsToFetch.Where(id => !string.IsNullOrEmpty(id) && !_lecturersCache.ContainsKey(id)).ToList();
                if (unknownLecIds.Any())
                {
                    var joinedIds = string.Join("|", unknownLecIds);
                    if (!string.IsNullOrEmpty(joinedIds))
                    {
                        try
                        {
                            var lecUrl = $"services/users/users?user_ids={joinedIds}&fields=id|first_name|last_name";
                            var lecObj = await apiService.GetAsync<JsonElement>(lecUrl);

                            if (lecObj.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var prop in lecObj.EnumerateObject())
                                {
                                    var lec = prop.Value;
                                    var id = lec.GetProperty("id").ToString();
                                    var fname = lec.TryGetProperty("first_name", out var fn) ? fn.GetString() : "";
                                    var lname = lec.TryGetProperty("last_name", out var ln) ? ln.GetString() : "";
                                    if (!string.IsNullOrEmpty(id))
                                    {
                                        _lecturersCache[id] = $"{fname} {lname}".Trim();
                                    }
                                }
                            }
                            else if (lecObj.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var lec in lecObj.EnumerateArray())
                                {
                                    var id = lec.GetProperty("id").ToString();
                                    var fname = lec.TryGetProperty("first_name", out var fn) ? fn.GetString() : "";
                                    var lname = lec.TryGetProperty("last_name", out var ln) ? ln.GetString() : "";
                                    if (!string.IsNullOrEmpty(id))
                                    {
                                        _lecturersCache[id] = $"{fname} {lname}".Trim();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ScheduleService] Błąd pobierania wykładowców: {ex.Message}");
                        }
                    }
                }

                foreach (var lecId in lecturerIdsToFetch)
                {
                    if (_lecturersCache.TryGetValue(lecId, out var name))
                    {
                        lecturersDict[lecId] = name;
                    }
                }

                // 3. Przetwarzamy zebrane eventy w obiekty Lesson
                foreach (var ev in allEventsJson)
                {
                    var startTimeStr = ev.TryGetProperty("start_time", out var st) ? st.GetString() : null;
                    var endTimeStr = ev.TryGetProperty("end_time", out var et) ? et.GetString() : null;

                    DateTime dtStart = string.IsNullOrEmpty(startTimeStr) ? DateTime.Now : DateTime.Parse(startTimeStr);
                    DateTime dtEnd = string.IsNullOrEmpty(endTimeStr) ? dtStart.AddMinutes(90) : DateTime.Parse(endTimeStr);

                    if (godMode)
                    {
                        if (dtStart < start || dtStart > start.AddDays(days))
                            continue;

                        // Weryfikacja czy zajęcia należą do kierunku studenta (np. WF ma 200 grup z czego dla danego kierunku jest tylko kilka grup powiązanych z jednostką studenta)
                        string? evCourseId = ev.TryGetProperty("course_id", out var cProp) ? cProp.GetString() : null;
                        string? evUnitId = ev.TryGetProperty("unit_id", out var uProp) ? uProp.ToString() : (ev.TryGetProperty("course_unit_id", out var legacyUProp) ? legacyUProp.ToString() : null);

                        if (!ScheduleLogic.IsUnitAllowedForCohort(evCourseId, evUnitId, courseToStudentUnits))
                        {
                            // Pomijamy grupy z innych wydziałów / jednostek
                            continue;
                        }
                    }

                    string title = "Zajęcia";
                    if (ev.TryGetProperty("course_name", out var cn))
                    {
                        title = cn.TryGetProperty("pl", out var plCn) ? plCn.GetString() ?? title :
                               (cn.TryGetProperty("en", out var enCn) ? enCn.GetString() ?? title : title);
                    }

                    string type = "INNE";
                    if (ev.TryGetProperty("classtype_name", out var ct))
                    {
                        type = ct.TryGetProperty("pl", out var plCt) ? plCt.GetString() ?? type : type;
                    }
                    type = ScheduleLogic.ParseClassType(type);

                    string room = ev.TryGetProperty("room_number", out var rm) && rm.ValueKind == JsonValueKind.String ? (rm.GetString() ?? "Brak sali") : "Brak sali";
                    if (ev.TryGetProperty("building_name", out var bn) && bn.ValueKind == JsonValueKind.Object)
                    {
                        string bname = GetShortBuildingName(bn.TryGetProperty("pl", out var plBn) ? plBn.GetString() ?? "" : "");
                        if (!string.IsNullOrEmpty(bname) && !room.Contains(bname, StringComparison.OrdinalIgnoreCase))
                        {
                            room += $" ({bname})";
                        }
                    }

                    List<string> names = new();
                    if (ev.TryGetProperty("lecturer_ids", out var lecArray) && lecArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var lecId in lecArray.EnumerateArray())
                        {
                            var id = lecId.ToString();
                            if (lecturersDict.TryGetValue(id, out var name)) names.Add(name);
                        }
                    }

                    string person = "Brak danych";
                    if (names.Any()) person = string.Join(", ", names);

                    string group = "ALL";
                    if (ev.TryGetProperty("group_number", out var grp))
                    {
                        if (grp.ValueKind == JsonValueKind.Number) group = grp.GetInt32().ToString();
                        else if (grp.ValueKind == JsonValueKind.String)
                        {
                            var gStr = grp.GetString();
                            if (!string.IsNullOrEmpty(gStr)) group = gStr;
                        }
                    }

                    string finalCourseId = ev.TryGetProperty("course_id", out var finalCid) ? finalCid.GetString() ?? "" : "";
                    string finalUnitId = ev.TryGetProperty("unit_id", out var finalUid) ? finalUid.ToString() : (ev.TryGetProperty("course_unit_id", out var legacyUid) ? legacyUid.ToString() : "");

                    newLessons.Add(new Lesson
                    {
                        Id = idCounter++,
                        Title = title,
                        Type = type,
                        Start = dtStart,
                        End = dtEnd,
                        Place = room,
                        Person = person,
                        Group = group,
                        OriginalStart = dtStart,
                        OriginalEnd = dtEnd,
                        OriginalPlace = room,
                        Desc = "",
                        CourseId = finalCourseId,
                        CourseUnitId = finalUnitId
                    });
                }

                // 4. Synchronizacja modyfikacji i wydarzeń z chmury (grupy oraz osobistych użytkownika)
                await SyncCloudEventsAsync();

                if (godMode)
                {
                    // God Mode zapisujemy w ODDZIELNEJ liście - NIGDY nie skażamy planu osobistego ani cache dyskowego!
                    ApplyModifications(newLessons);
                    _godModeLessons = newLessons.OrderBy(l => l.Start).ToList();
                    _lastGodModeFetchTime = DateTime.UtcNow;
                    IsLoaded = true;
                    OnScheduleLoaded?.Invoke();
                }
                else
                {
                    // 5. Scalanie z istniejącym cache dla planu osobistego:
                    // Zastępujemy lekcje w pobranym przedziale dat, zachowujemy resztę tygodni
                    var windowEnd = start.AddDays(days);
                    var existingOutsideWindow = _lessons.Where(l => l.Start < start || l.Start >= windowEnd).ToList();
                    var merged = existingOutsideWindow.Concat(newLessons).OrderBy(l => l.Start).ToList();

                    int assignedId = 1;
                    foreach (var l in merged) l.Id = assignedId++;

                    ApplyModifications(merged);

                    _lessons = merged;
                    SaveCacheToDisk();
                    IsLoaded = true;
                    LastUsosFetchTime = DateTime.UtcNow;
                    _ = _notificationService?.ScheduleUpcomingClassRemindersAsync(_lessons);
                    OnScheduleLoaded?.Invoke();
                }
            }
            catch (UnauthorizedAccessException authEx)
            {
                HasUsosAuthError = true;
                LastUsosErrorMessage = authEx.Message;
                Console.WriteLine("[ScheduleService] Błąd autoryzacji USOS: " + authEx.Message);
                if (!_lessons.Any())
                {
                    LoadCacheFromDisk();
                }
                IsLoaded = true;
                OnScheduleLoaded?.Invoke();
                throw;
            }
            catch (Exception ex)
            {
                LastUsosErrorMessage = ScheduleLogic.FormatUsosErrorMessage(ex.Message, "pl");
                Console.WriteLine("[ScheduleService] Błąd pobierania harmonogramu: " + ex);
                // Nie czyścimy cache! Jeśli cokolwiek mamy, trzymamy to offline
                if (!_lessons.Any())
                {
                    LoadCacheFromDisk();
                }
                IsLoaded = true;
                OnScheduleLoaded?.Invoke();
                throw;
            }
        }

        public async Task EnsureDataLoadedAsync(UsosApiService apiService, DateTime start, int days, bool force = false, bool godMode = false)
        {
            if (!_lessons.Any())
            {
                LoadCacheFromDisk();
            }

            if (godMode)
            {
                if (!force && IsGodModeFresh(start, days))
                {
                    return;
                }
            }
            else
            {
                if (!force && IsCacheFresh)
                {
                    IsLoaded = true;
                    _ = Task.Run(async () =>
                    {
                        try { await SyncCloudEventsAsync(); }
                        catch (Exception ex) { Console.WriteLine($"[ScheduleService] EnsureDataLoaded sync error: {ex.Message}"); }
                    });
                    return;
                }
            }

            // Bezpieczne oczekiwanie w kolejce blokady – wątki czekają na zwolnienie i weryfikują świeżość
            await _loadLock.WaitAsync();
            try
            {
                if (!force)
                {
                    if (godMode && IsGodModeFresh(start, days))
                    {
                        return;
                    }
                    if (!godMode && IsCacheFresh)
                    {
                        IsLoaded = true;
                        _ = Task.Run(async () =>
                        {
                            try { await SyncCloudEventsAsync(); }
                            catch (Exception ex) { Console.WriteLine($"[ScheduleService] EnsureDataLoaded sync error: {ex.Message}"); }
                        });
                        return;
                    }
                }

                await LoadDataFromUsosAsync(apiService, start, days, godMode);
            }
            finally
            {
                _loadLock.Release();
            }
        }

        public List<Lesson> GetPreparedData(bool godMode = false)
        {
            if (godMode && _godModeLessons.Any())
            {
                return _godModeLessons.ToList();
            }
            return _lessons.ToList();
        }

        public List<Lesson> GetAllEvents(bool godMode = false)
        {
            var baseList = (godMode && _godModeLessons.Any()) ? _godModeLessons : _lessons;
            return baseList.Concat(CustomEvents).OrderBy(l => l.Start).ToList();
        }
    }
}
