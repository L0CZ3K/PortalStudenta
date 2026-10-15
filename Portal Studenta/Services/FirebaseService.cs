using Firebase.Database;
using Firebase.Database.Query;
using Portal_Studenta.Models;
using System.Text.Json;
using System.Reactive.Linq;

namespace Portal_Studenta.Services
{
    public class FirebaseEvent
    {
        public string Uid { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public string Type { get; set; } = "";
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string Place { get; set; } = "";
        public string Desc { get; set; } = "";
        public string TargetGroup { get; set; } = "ALL";
        public bool IsCanceled { get; set; }
        public bool IsPersonal { get; set; }
        public bool IsHighPriority { get; set; }
        public DateTime OriginalStart { get; set; }
        public DateTime OriginalEnd { get; set; }
        public string OriginalPlace { get; set; } = "";
    }

    public class FirebaseGroup
    {
        public string OwnerId { get; set; } = "";
        public List<string> Admins { get; set; } = new();
        public List<string> Starostas { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public string? RedirectTo { get; set; }
    }

    public class UserMembership
    {
        public string GroupCode { get; set; } = "";
        public string Role { get; set; } = "Student";
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    public class DeletedAccountTombstone
    {
        public string Album { get; set; } = "";
        public DateTime? DeletedAt { get; set; }
    }

    public class FirebaseService : IDisposable, IFirebaseNoteSync
    {
        private readonly FirebaseClient _firebase;
        private readonly Dictionary<string, IDisposable> _membershipSubscriptions = new();

        public FirebaseService()
        {
            _firebase = new FirebaseClient("https://uniavio-default-rtdb.europe-west1.firebasedatabase.app/");
        }

        // --- OBSŁUGA CZŁONKOSTWA I SYNCHRONIZACJA MIĘDZY URZĄDZENIAMI ---

        public async Task SaveUserMembershipAsync(string albumNumber, string groupCode, string role)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return;
            try
            {
                albumNumber = SanitizeKey(albumNumber);
                groupCode = !string.IsNullOrWhiteSpace(groupCode) ? SanitizeKey(groupCode).ToUpperInvariant() : "";
                var membership = new UserMembership
                {
                    GroupCode = groupCode,
                    Role = role,
                    LastUpdated = DateTime.UtcNow
                };
                await _firebase.Child("users").Child(albumNumber).Child("membership").PutAsync(membership);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd zapisu członkostwa użytkownika: {ex.Message}");
            }
        }

        public async Task<UserMembership?> GetUserMembershipAsync(string albumNumber)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return null;
            try
            {
                albumNumber = SanitizeKey(albumNumber);
                var mem = await _firebase.Child("users").Child(albumNumber).Child("membership").OnceSingleAsync<UserMembership>();
                return mem;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd pobierania członkostwa użytkownika: {ex.Message}");
                return null;
            }
        }

        public async Task DeleteUserMembershipAsync(string albumNumber)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return;
            try
            {
                albumNumber = SanitizeKey(albumNumber);
                await _firebase.Child("users").Child(albumNumber).Child("membership").DeleteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd usuwania członkostwa użytkownika: {ex.Message}");
            }
        }

        public async Task LeaveGroupAsync(string? albumNumber, string? secondaryId = null, string? currentGroupCode = null)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(albumNumber)) ids.Add(SanitizeKey(albumNumber));
            if (!string.IsNullOrWhiteSpace(secondaryId)) ids.Add(SanitizeKey(secondaryId));
            if (!ids.Any()) return;

            try
            {
                // 1. Jeśli nie podano groupCode, spróbuj pobrać z chmury dla któregoś z ID
                if (string.IsNullOrWhiteSpace(currentGroupCode))
                {
                    foreach (var id in ids)
                    {
                        var mem = await GetUserMembershipAsync(id);
                        if (mem != null && !string.IsNullOrWhiteSpace(mem.GroupCode))
                        {
                            currentGroupCode = mem.GroupCode;
                            break;
                        }
                    }
                }

                // 2. Jeśli mamy kod grupy, zaktualizuj grupę (usuń ze Starostas, Admins, przekaż OwnerId jeśli potrzebne)
                if (!string.IsNullOrWhiteSpace(currentGroupCode))
                {
                    currentGroupCode = currentGroupCode.Trim().ToUpperInvariant();
                    var group = await GetGroupAsync(currentGroupCode);
                    if (group != null)
                    {
                        bool modified = false;

                        if (group.Starostas != null)
                        {
                            int removed = group.Starostas.RemoveAll(s => ids.Contains(s.Trim()));
                            if (removed > 0) modified = true;
                        }

                        if (group.Admins != null)
                        {
                            int removed = group.Admins.RemoveAll(a => ids.Contains(a.Trim()));
                            if (removed > 0) modified = true;
                        }

                        bool wasOwner = !string.IsNullOrEmpty(group.OwnerId) && ids.Contains(group.OwnerId.Trim());
                        if (wasOwner)
                        {
                            if (group.Starostas != null && group.Starostas.Any())
                            {
                                group.OwnerId = group.Starostas.First();
                                modified = true;
                            }
                            else if (group.Admins != null && group.Admins.Any())
                            {
                                group.OwnerId = group.Admins.First();
                                modified = true;
                            }
                            else
                            {
                                // Brak innych zarządców w grupie – grupa zostaje usunięta
                                await DeleteGroupAsync(currentGroupCode);
                                group = null;
                            }
                        }

                        if (group != null && modified)
                        {
                            await _firebase.Child("groups").Child(currentGroupCode).PatchAsync(group);
                        }
                    }
                }

                // 3. Usuń członkostwo dla wszystkich identyfikatorów użytkownika
                foreach (var id in ids)
                {
                    await _firebase.Child("users").Child(id).Child("membership").DeleteAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd opuszczania grupy: {ex.Message}");
            }
        }

        public async Task DeleteUserAccountAsync(string albumNumber, string? secondaryId = null)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(albumNumber)) ids.Add(SanitizeKey(albumNumber));
            if (!string.IsNullOrWhiteSpace(secondaryId)) ids.Add(SanitizeKey(secondaryId));
            if (!ids.Any()) return;

            try
            {
                foreach (var sAlbum in ids)
                {
                    // 1. Sprawdź, czy użytkownik ma grupę i zaktualizuj grupę
                    var mem = await GetUserMembershipAsync(sAlbum);
                    if (mem != null && !string.IsNullOrWhiteSpace(mem.GroupCode))
                    {
                        var group = await GetGroupAsync(mem.GroupCode);
                        if (group != null)
                        {
                            if (!string.IsNullOrEmpty(group.OwnerId) && group.OwnerId.Trim().Equals(sAlbum, StringComparison.OrdinalIgnoreCase))
                            {
                                await DeleteGroupAsync(mem.GroupCode);
                            }
                            else
                            {
                                bool modified = false;
                                if (group.Starostas != null && group.Starostas.Remove(sAlbum)) modified = true;
                                if (group.Admins != null && group.Admins.Remove(sAlbum)) modified = true;
                                if (modified)
                                {
                                    await _firebase.Child("groups").Child(mem.GroupCode).PutAsync(group);
                                }
                            }
                        }
                    }

                    // 2. Usuń dane użytkownika (membership, personal events, personal mods)
                    await _firebase.Child("users").Child(sAlbum).DeleteAsync();
                    await _firebase.Child("personal_events").Child(sAlbum).DeleteAsync();
                    await _firebase.Child("personal_modifications").Child(sAlbum).DeleteAsync();

                    // 3. Zapisz tombstone w deleted_accounts, aby natychmiast poinformować inne urządzenia
                    await _firebase.Child("deleted_accounts").Child(sAlbum).PutAsync(new
                    {
                        DeletedAt = DateTime.UtcNow,
                        Album = sAlbum
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd pełnego usuwania konta: {ex.Message}");
            }
        }

        public async Task<bool> IsUserAccountDeletedAsync(string? albumNumber, string? secondaryId = null, DateTime? sessionStartedAt = null)
        {
            var ids = new List<string>();
            if (!string.IsNullOrWhiteSpace(albumNumber)) ids.Add(SanitizeKey(albumNumber));
            if (!string.IsNullOrWhiteSpace(secondaryId)) ids.Add(SanitizeKey(secondaryId));
            if (!ids.Any()) return false;

            foreach (var id in ids)
            {
                try
                {
                    var tombstone = await _firebase.Child("deleted_accounts").Child(id).OnceSingleAsync<DeletedAccountTombstone>();
                    if (tombstone != null)
                    {
                        if (sessionStartedAt.HasValue && tombstone.DeletedAt.HasValue)
                        {
                            // Jeśli konto usunięto przed zalogowaniem w bieżącej sesji, ignorujemy stary tombstone i go usuwamy
                            if (ScheduleLogic.IsTombstoneObsolete(tombstone.DeletedAt, sessionStartedAt))
                            {
                                _ = ClearUserAccountDeletedAsync(albumNumber, secondaryId);
                                continue;
                            }
                        }
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FirebaseService] Błąd sprawdzania statusu usunięcia konta ({id}): {ex.Message}");
                }
            }
            return false;
        }

        public async Task ClearUserAccountDeletedAsync(string? albumNumber, string? secondaryId = null)
        {
            var ids = new List<string>();
            if (!string.IsNullOrWhiteSpace(albumNumber)) ids.Add(SanitizeKey(albumNumber));
            if (!string.IsNullOrWhiteSpace(secondaryId)) ids.Add(SanitizeKey(secondaryId));

            foreach (var id in ids)
            {
                try
                {
                    await _firebase.Child("deleted_accounts").Child(id).DeleteAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FirebaseService] Błąd czyszczenia flagi usunięcia konta ({id}): {ex.Message}");
                }
            }
        }

        private readonly Dictionary<string, System.Timers.Timer> _deletionSubscriptions = new();

        public void SubscribeToUserAccountDeletion(string albumNumber, Action onDeleted, string? secondaryId = null, DateTime? sessionStartedAt = null)
        {
            var key = $"{albumNumber}_{secondaryId}";
            if (string.IsNullOrWhiteSpace(albumNumber) && string.IsNullOrWhiteSpace(secondaryId)) return;

            lock (_deletionSubscriptions)
            {
                if (_deletionSubscriptions.ContainsKey(key)) return;
            }

            try
            {
                var timer = new System.Timers.Timer(12000); // sprawdzaj co 12s dla szybkiej reakcji
                timer.Elapsed += async (s, e) =>
                {
                    try
                    {
                        bool isDeleted = await IsUserAccountDeletedAsync(albumNumber, secondaryId, sessionStartedAt);
                        if (isDeleted)
                        {
                            timer.Stop();
                            lock (_deletionSubscriptions)
                            {
                                _deletionSubscriptions.Remove(key);
                            }
                            timer.Dispose();
                            onDeleted?.Invoke();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FirebaseService] Błąd sprawdzania usunięcia konta w tle: {ex.Message}");
                    }
                };
                timer.AutoReset = true;
                timer.Start();

                lock (_deletionSubscriptions)
                {
                    _deletionSubscriptions[key] = timer;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Nie udało się zasubskrybować sprawdzania usunięcia konta: {ex.Message}");
            }
        }

        public async Task<UserMembership?> FindUserGroupAsync(string albumNumber)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return null;
            try
            {
                albumNumber = albumNumber.Trim();
                var groups = await _firebase.Child("groups").OnceAsync<FirebaseGroup>();
                foreach (var g in groups)
                {
                    var grp = g.Object;
                    if (grp == null) continue;

                    bool isOwner = !string.IsNullOrEmpty(grp.OwnerId) && grp.OwnerId.Trim().Equals(albumNumber, StringComparison.OrdinalIgnoreCase);
                    bool isStarosta = isOwner || (grp.Starostas != null && grp.Starostas.Any(s => s.Trim().Equals(albumNumber, StringComparison.OrdinalIgnoreCase)));
                    bool isAdmin = !isStarosta && grp.Admins != null && grp.Admins.Any(a => a.Trim().Equals(albumNumber, StringComparison.OrdinalIgnoreCase));

                    if (isStarosta || isAdmin)
                    {
                        var roleStr = isStarosta ? "Starosta" : "Admin";
                        var mem = new UserMembership
                        {
                            GroupCode = g.Key,
                            Role = roleStr,
                            LastUpdated = DateTime.UtcNow
                        };
                        await SaveUserMembershipAsync(albumNumber, g.Key, roleStr);
                        return mem;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd wyszukiwania grupy: {ex.Message}");
            }
            return null;
        }

        public void SubscribeToUserMembership(string albumNumber, Action<UserMembership?> onUpdated)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return;
            albumNumber = albumNumber.Trim();

            lock (_membershipSubscriptions)
            {
                if (_membershipSubscriptions.ContainsKey(albumNumber)) return;
            }

            try
            {
                var timer = new System.Timers.Timer(15000); // sprawdzanie co 15s dla płynnej synchronizacji
                timer.Elapsed += async (s, e) =>
                {
                    try
                    {
                        var mem = await GetUserMembershipAsync(albumNumber);
                        onUpdated?.Invoke(mem);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FirebaseService] Błąd synchronizacji członkostwa: {ex.Message}");
                    }
                };
                timer.AutoReset = true;
                timer.Start();

                lock (_membershipSubscriptions)
                {
                    _membershipSubscriptions[albumNumber] = timer;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Nie udało się zasubskrybować członkostwa: {ex.Message}");
            }
        }

        public void UnsubscribeUserMembership(string albumNumber)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return;
            albumNumber = albumNumber.Trim();
            lock (_membershipSubscriptions)
            {
                if (_membershipSubscriptions.TryGetValue(albumNumber, out var sub))
                {
                    try { sub.Dispose(); } catch { }
                    _membershipSubscriptions.Remove(albumNumber);
                }
            }
        }

        public void UnsubscribeUserAccountDeletion(string albumNumber, string? secondaryId = null)
        {
            var key = $"{albumNumber}_{secondaryId}";
            lock (_deletionSubscriptions)
            {
                if (_deletionSubscriptions.TryGetValue(key, out var timer))
                {
                    try { timer.Stop(); timer.Dispose(); } catch { }
                    _deletionSubscriptions.Remove(key);
                }
            }
        }

        public void UnsubscribeAllSubscriptions()
        {
            lock (_membershipSubscriptions)
            {
                foreach (var sub in _membershipSubscriptions.Values)
                {
                    try { sub.Dispose(); } catch { }
                }
                _membershipSubscriptions.Clear();
            }

            lock (_deletionSubscriptions)
            {
                foreach (var timer in _deletionSubscriptions.Values)
                {
                    try { timer.Stop(); timer.Dispose(); } catch { }
                }
                _deletionSubscriptions.Clear();
            }
        }

        public void Dispose()
        {
            UnsubscribeAllSubscriptions();
        }

        public async Task SyncUserSessionAsync(string albumNumber, AppState appState, ScheduleService? scheduleService = null)
        {
            if (string.IsNullOrWhiteSpace(albumNumber))
            {
                albumNumber = Preferences.Default.Get("user_student_number", "");
                if (string.IsNullOrWhiteSpace(albumNumber))
                    albumNumber = Preferences.Default.Get("user_usos_id", "");
            }

            var secondaryId = Preferences.Default.Get("user_usos_id", "");
            if (string.Equals(albumNumber, secondaryId, StringComparison.OrdinalIgnoreCase))
            {
                var studentNum = Preferences.Default.Get("user_student_number", "");
                if (!string.IsNullOrWhiteSpace(studentNum) && !string.Equals(albumNumber, studentNum, StringComparison.OrdinalIgnoreCase))
                    secondaryId = studentNum;
                else
                    secondaryId = null;
            }

            if (string.IsNullOrWhiteSpace(albumNumber)) return;
            albumNumber = albumNumber.Trim();

            try
            {
                // 0. Sprawdź, czy konto nie zostało usunięte na innym urządzeniu
                DateTime? sessionStart = null;
                var sessionLoginStr = Preferences.Default.Get("session_login_at", "");
                if (!string.IsNullOrEmpty(sessionLoginStr) && DateTime.TryParse(sessionLoginStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                {
                    sessionStart = dt;
                }

                bool isDeleted = await IsUserAccountDeletedAsync(albumNumber, secondaryId, sessionStart);
                if (isDeleted)
                {
                    Console.WriteLine("[FirebaseService] Wykryto usunięcie konta – czyszczenie lokalnej sesji.");
                    appState.Reset();
                    scheduleService?.Clear();
                    return;
                }

                // 1. Sprawdź, czy w chmurze istnieje już membership
                var membership = await GetUserMembershipAsync(albumNumber);
                if (membership == null && !string.IsNullOrWhiteSpace(secondaryId))
                {
                    membership = await GetUserMembershipAsync(secondaryId);
                }

                // 2. Jeśli brak membership w chmurze:
                // Chmura jest źródłem prawdy! Jeśli użytkownik opuścił grupę lub nie ma grupy,
                // czyścimy lokalny kod grupy i rolę. Nigdy nie wskrzeszamy starej grupy do chmury!
                if (membership == null || string.IsNullOrWhiteSpace(membership.GroupCode))
                {
                    if (!string.IsNullOrEmpty(appState.GroupCode))
                    {
                        appState.SetGroupCode("");
                        appState.SetUserRole(UserRole.Student);
                    }
                }
                else
                {
                    var activeGroupCode = await ResolveGroupCodeAsync(membership.GroupCode);
                    if (activeGroupCode != membership.GroupCode)
                    {
                        membership.GroupCode = activeGroupCode;
                        await SaveUserMembershipAsync(albumNumber, activeGroupCode, membership.Role);
                    }

                    var group = await GetGroupAsync(activeGroupCode);
                    UserRole targetRole = UserRole.Student;
                    if (group != null)
                    {
                        bool isOwner = (!string.IsNullOrEmpty(group.OwnerId) && 
                            (group.OwnerId.Trim().Equals(albumNumber, StringComparison.OrdinalIgnoreCase) || 
                             (!string.IsNullOrEmpty(secondaryId) && group.OwnerId.Trim().Equals(secondaryId, StringComparison.OrdinalIgnoreCase))));

                        bool isStarosta = isOwner || (group.Starostas != null && 
                            (group.Starostas.Any(s => s.Trim().Equals(albumNumber, StringComparison.OrdinalIgnoreCase)) || 
                             (!string.IsNullOrEmpty(secondaryId) && group.Starostas.Any(s => s.Trim().Equals(secondaryId, StringComparison.OrdinalIgnoreCase)))));

                        bool isAdmin = !isStarosta && group.Admins != null && 
                            (group.Admins.Any(a => a.Trim().Equals(albumNumber, StringComparison.OrdinalIgnoreCase)) || 
                             (!string.IsNullOrEmpty(secondaryId) && group.Admins.Any(a => a.Trim().Equals(secondaryId, StringComparison.OrdinalIgnoreCase))));

                        if (isStarosta) targetRole = UserRole.Starosta;
                        else if (isAdmin) targetRole = UserRole.Admin;
                        else targetRole = UserRole.Student;

                        if (membership.Role != targetRole.ToString())
                        {
                            membership.Role = targetRole.ToString();
                            await SaveUserMembershipAsync(albumNumber, activeGroupCode, membership.Role);
                        }
                    }
                    else if (Enum.TryParse<UserRole>(membership.Role, out var parsedRole))
                    {
                        targetRole = parsedRole;
                    }

                    appState.SetGroupCode(activeGroupCode);
                    appState.SetUserRole(targetRole);
                }

                // 4. ZAWSZE wykonaj synchronizację zdarzeń planu w chmurze (modyfikacje grupowe i osobiste)
                if (scheduleService != null)
                {
                    await scheduleService.SyncCloudEventsAsync();
                }

                // 5. Uruchom reaktywne nasłuchiwanie w czasie rzeczywistym
                SubscribeToUserMembership(albumNumber, (updatedMem) =>
                {
                    try
                    {
                        if (updatedMem != null && !string.IsNullOrWhiteSpace(updatedMem.GroupCode))
                        {
                            bool groupChanged = appState.GroupCode != updatedMem.GroupCode;
                            if (groupChanged)
                            {
                                appState.SetGroupCode(updatedMem.GroupCode);
                            }

                            if (Enum.TryParse<UserRole>(updatedMem.Role, out var newRole) && appState.Role != newRole)
                            {
                                appState.SetUserRole(newRole);
                            }

                            if (groupChanged && scheduleService != null)
                            {
                                _ = Task.Run(async () =>
                                {
                                    try { await scheduleService.SyncCloudEventsAsync(); }
                                    catch (Exception ex) { Console.WriteLine($"[FirebaseService] SyncCloudEvents error: {ex.Message}"); }
                                });
                            }
                        }
                        else
                        {
                            // Użytkownik nie ma już grupy w chmurze
                            if (!string.IsNullOrEmpty(appState.GroupCode))
                            {
                                appState.SetGroupCode("");
                                appState.SetUserRole(UserRole.Student);
                                if (scheduleService != null)
                                {
                                    _ = Task.Run(async () =>
                                    {
                                        try { await scheduleService.SyncCloudEventsAsync(); }
                                        catch (Exception ex) { Console.WriteLine($"[FirebaseService] SyncCloudEvents error: {ex.Message}"); }
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FirebaseService] Błąd w obsłudze aktualizacji członkostwa: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] SyncUserSessionAsync błąd: {ex.Message}");
            }
        }

        // --- OBSŁUGA GRUP STAROSTY & ADMINÓW ---

        private const string DatabaseUrl = "https://uniavio-default-rtdb.europe-west1.firebasedatabase.app/";
        private static readonly HttpClient _restClient = new() { Timeout = TimeSpan.FromSeconds(20) };
        private const string GroupCodeChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        /// <summary>
        /// Kod grupy: 5–12 znaków, wyłącznie A–Z i 0–9 (bez znaków niedozwolonych w kluczach Firebase).
        /// </summary>
        public static bool IsValidGroupCode(string? code) => FirebaseSecurityHelper.IsValidGroupCode(code);

        public static string GenerateRandomGroupCode(int length = 6) => FirebaseSecurityHelper.GenerateRandomGroupCode(length);

        /// <summary>
        /// Atomowo tworzy węzeł tylko wtedy, gdy jeszcze nie istnieje (REST: X-Firebase-ETag + if-match).
        /// Zwraca false, jeśli węzeł istnieje lub ktoś zajął go w międzyczasie (HTTP 412).
        /// </summary>
        private static async Task<bool> TryCreateNodeIfAbsentAsync(string path, object value)
        {
            var url = $"{DatabaseUrl}{path}.json";

            using var getReq = new HttpRequestMessage(HttpMethod.Get, url);
            getReq.Headers.TryAddWithoutValidation("X-Firebase-ETag", "true");
            using var getResp = await _restClient.SendAsync(getReq);
            getResp.EnsureSuccessStatusCode();

            var body = (await getResp.Content.ReadAsStringAsync()).Trim();
            if (body != "null") return false;

            string? etag = getResp.Headers.NonValidated.TryGetValues("ETag", out var etagValues)
                ? etagValues.FirstOrDefault()
                : null;
            if (string.IsNullOrEmpty(etag))
                throw new InvalidOperationException("Firebase nie zwrócił nagłówka ETag – zapis warunkowy niemożliwy.");

            using var putReq = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json")
            };
            putReq.Headers.TryAddWithoutValidation("if-match", etag);
            using var putResp = await _restClient.SendAsync(putReq);

            if (putResp.StatusCode == System.Net.HttpStatusCode.PreconditionFailed) return false;
            putResp.EnsureSuccessStatusCode();
            return true;
        }

        public async Task<string> CreateGroupAsync(string ownerAlbumNumber)
        {
            try
            {
                var group = new FirebaseGroup 
                { 
                    OwnerId = ownerAlbumNumber,
                    Admins = new List<string> { ownerAlbumNumber },
                    Starostas = new List<string> { ownerAlbumNumber },
                    CreatedAt = DateTime.UtcNow 
                };

                // Atomowe zajęcie wolnego kodu – brak kolizji nawet przy równoczesnym tworzeniu grup
                string? groupCode = null;
                for (int attempt = 0; attempt < 10 && groupCode == null; attempt++)
                {
                    string candidate = GenerateRandomGroupCode(attempt < 5 ? 6 : 8);
                    if (await TryCreateNodeIfAbsentAsync($"groups/{candidate}", group))
                    {
                        groupCode = candidate;
                    }
                }

                if (groupCode == null)
                    throw new InvalidOperationException("Nie udało się wygenerować unikalnego kodu grupy.");

                await SaveUserMembershipAsync(ownerAlbumNumber, groupCode, "Starosta");
                return groupCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd tworzenia grupy: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteGroupAsync(string groupCode)
        {
            try
            {
                await _firebase.Child("groups").Child(groupCode).DeleteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd usuwania grupy {groupCode}: {ex.Message}");
            }
        }

        public async Task<string> ResolveGroupCodeAsync(string groupCode)
        {
            if (string.IsNullOrWhiteSpace(groupCode)) return groupCode;
            // Ochrona przed zapętlonymi aliasami (A -> B -> A) oraz zbyt długimi łańcuchami
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string current = SanitizeKey(groupCode).ToUpperInvariant();
            try
            {
                for (int depth = 0; depth < 10 && visited.Add(current); depth++)
                {
                    var group = await _firebase.Child("groups").Child(current).OnceSingleAsync<FirebaseGroup>();
                    if (group == null || string.IsNullOrEmpty(group.RedirectTo) || group.RedirectTo.Equals(current, StringComparison.OrdinalIgnoreCase))
                        return current;
                    current = group.RedirectTo;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd rozwiązywania aliasu grupy: {ex.Message}");
            }
            return current;
        }

        public enum ChangeGroupResult
        {
            Success,
            AlreadyExists,
            NotFound,
            InvalidInput,
            Error
        }

        public async Task<ChangeGroupResult> ChangeGroupCodeAsync(string oldCode, string newCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(oldCode) || !IsValidGroupCode(newCode)) return ChangeGroupResult.InvalidInput;
                oldCode = oldCode.Trim().ToUpperInvariant();
                newCode = newCode.Trim().ToUpperInvariant();
                if (oldCode == newCode) return ChangeGroupResult.Success;

                // 1. Pobierz dane starej grupy
                var oldGroup = await _firebase.Child("groups").Child(oldCode).OnceSingleAsync<FirebaseGroup>();
                if (oldGroup == null) return ChangeGroupResult.NotFound;

                // 2. ATOMOWE zajęcie nowego kodu – jeśli węzeł istnieje lub ktoś zajął go w tej samej chwili, przerywamy
                var newGroup = new FirebaseGroup
                {
                    OwnerId = oldGroup.OwnerId,
                    Admins = oldGroup.Admins ?? new List<string>(),
                    Starostas = oldGroup.Starostas ?? new List<string>(),
                    CreatedAt = oldGroup.CreatedAt == default ? DateTime.UtcNow : oldGroup.CreatedAt
                };
                if (!await TryCreateNodeIfAbsentAsync($"groups/{newCode}", newGroup))
                {
                    Console.WriteLine($"[FirebaseService] Zmiana kodu odrzucona: kod '{newCode}' jest już zajęty.");
                    return ChangeGroupResult.AlreadyExists;
                }

                // 3. Skopiuj dane podrzędne (wydarzenia, ogłoszenia, modyfikacje planu, ankiety)
                foreach (var child in new[] { "events", "announcements", "modifications", "polls" })
                {
                    try
                    {
                        var data = await _firebase.Child("groups").Child(oldCode).Child(child).OnceAsJsonAsync();
                        if (!string.IsNullOrWhiteSpace(data) && data.Trim() != "null")
                        {
                            await _firebase.Child("groups").Child(newCode).Child(child).PutAsync(data);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FirebaseService] Błąd kopiowania '{child}' przy zmianie kodu: {ex.Message}");
                    }
                }

                // 4. Zapisz alias w starej grupie (RedirectTo) – lobby nie rozłącza studentów!
                await _firebase.Child("groups").Child(oldCode).Child("RedirectTo").PutAsync($"\"{newCode}\"");

                return ChangeGroupResult.Success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd zmiany kodu grupy: {ex.Message}");
                return ChangeGroupResult.Error;
            }
        }

        public async Task<FirebaseGroup?> GetGroupAsync(string groupCode)
        {
            try
            {
                var group = await _firebase.Child("groups").Child(groupCode).OnceSingleAsync<FirebaseGroup>();
                if (group != null)
                {
                    if (!string.IsNullOrEmpty(group.RedirectTo) && !group.RedirectTo.Equals(groupCode, StringComparison.OrdinalIgnoreCase))
                    {
                        return await GetGroupAsync(group.RedirectTo);
                    }
                    group.Admins ??= new List<string>();
                    group.Starostas ??= new List<string>();
                }
                return group;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd pobierania grupy {groupCode}: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> AddGroupAdminAsync(string groupCode, string albumNumber)
        {
            try
            {
                groupCode = await ResolveGroupCodeAsync(groupCode);
                var group = await GetGroupAsync(groupCode);
                if (group == null) return false;
                
                group.Admins ??= new List<string>();
                if (!group.Admins.Contains(albumNumber))
                {
                    group.Admins.Add(albumNumber);
                    await _firebase.Child("groups").Child(groupCode).PatchAsync(group);
                }

                // Sprawdź czy to nie Starosta, by nie nadpisać wyższej roli
                bool isStarosta = (group.OwnerId != null && group.OwnerId.Trim().Equals(albumNumber, StringComparison.OrdinalIgnoreCase)) ||
                                  (group.Starostas != null && group.Starostas.Contains(albumNumber));
                await SaveUserMembershipAsync(albumNumber, groupCode, isStarosta ? "Starosta" : "Admin");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd dodawania admina: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RemoveGroupAdminAsync(string groupCode, string albumNumber)
        {
            try
            {
                groupCode = await ResolveGroupCodeAsync(groupCode);
                var group = await GetGroupAsync(groupCode);
                if (group == null) return false;
                
                group.Admins ??= new List<string>();
                if (group.Admins.Remove(albumNumber))
                {
                    await _firebase.Child("groups").Child(groupCode).PatchAsync(group);
                }

                bool isStarosta = (group.OwnerId != null && group.OwnerId.Trim().Equals(albumNumber, StringComparison.OrdinalIgnoreCase)) ||
                                  (group.Starostas != null && group.Starostas.Contains(albumNumber));
                await SaveUserMembershipAsync(albumNumber, groupCode, isStarosta ? "Starosta" : "Student");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd usuwania admina: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> AddGroupStarostaAsync(string groupCode, string albumNumber)
        {
            try
            {
                var group = await GetGroupAsync(groupCode);
                if (group == null) return false;
                
                group.Starostas ??= new List<string>();
                if (!group.Starostas.Contains(albumNumber))
                {
                    group.Starostas.Add(albumNumber);
                    await _firebase.Child("groups").Child(groupCode).PatchAsync(group);
                }
                await SaveUserMembershipAsync(albumNumber, groupCode, "Starosta");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd dodawania starosty: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RemoveGroupStarostaAsync(string groupCode, string albumNumber)
        {
            try
            {
                var group = await GetGroupAsync(groupCode);
                if (group == null) return false;
                
                group.Starostas ??= new List<string>();
                if (group.Starostas.Remove(albumNumber))
                {
                    await _firebase.Child("groups").Child(groupCode).PatchAsync(group);
                }

                string newRole = (group.Admins != null && group.Admins.Contains(albumNumber)) ? "Admin" : "Student";
                await SaveUserMembershipAsync(albumNumber, groupCode, newRole);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd usuwania starosty: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> TransferGroupOwnershipAsync(string groupCode, string newOwnerAlbum)
        {
            try
            {
                var group = await GetGroupAsync(groupCode) ?? new FirebaseGroup { CreatedAt = DateTime.UtcNow };
                string oldOwner = group.OwnerId;
                group.OwnerId = newOwnerAlbum;
                group.Admins ??= new List<string>();
                if (!group.Admins.Contains(newOwnerAlbum)) group.Admins.Add(newOwnerAlbum);
                group.Starostas ??= new List<string>();
                if (!group.Starostas.Contains(newOwnerAlbum)) group.Starostas.Add(newOwnerAlbum);

                await _firebase.Child("groups").Child(groupCode).PatchAsync(group);

                await SaveUserMembershipAsync(newOwnerAlbum, groupCode, "Starosta");
                if (!string.IsNullOrEmpty(oldOwner) && !oldOwner.Equals(newOwnerAlbum, StringComparison.OrdinalIgnoreCase))
                {
                    await SaveUserMembershipAsync(oldOwner, groupCode, "Student");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd przekazywania grupy {groupCode}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> GroupExistsAsync(string groupCode)
        {
            try
            {
                var group = await GetGroupAsync(groupCode);
                return group != null && !string.IsNullOrEmpty(group.OwnerId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd sprawdzania grupy {groupCode}: {ex.Message}");
                return false;
            }
        }

        public async Task PublishGroupEventAsync(string groupCode, FirebaseEvent ev)
        {
            try
            {
                groupCode = await ResolveGroupCodeAsync(groupCode);
                await _firebase.Child("groups").Child(groupCode).Child("events").Child(ev.Uid).PutAsync(ev);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd publikacji wydarzenia: {ex.Message}");
            }
        }

        public async Task DeleteGroupEventAsync(string groupCode, string eventUid)
        {
            try
            {
                groupCode = await ResolveGroupCodeAsync(groupCode);
                await _firebase.Child("groups").Child(groupCode).Child("events").Child(eventUid).DeleteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd usuwania wydarzenia: {ex.Message}");
            }
        }

        public IObservable<FirebaseEvent> SubscribeToGroupEvents(string groupCode)
        {
            if (string.IsNullOrWhiteSpace(groupCode)) return Observable.Empty<FirebaseEvent>();
            var sGroup = SanitizeKey(groupCode).ToUpperInvariant();

            return Observable.Create<FirebaseEvent>(observer =>
            {
                var knownEvents = new Dictionary<string, string>();
                var timer = new System.Timers.Timer(15000);

                async Task CheckForUpdates()
                {
                    try
                    {
                        var events = await GetGroupEventsAsync(sGroup);
                        foreach (var ev in events)
                        {
                            var sig = $"{ev.Start:O}_{ev.End:O}_{ev.Place}_{ev.Desc}_{ev.IsCanceled}_{ev.IsHighPriority}";
                            if (!knownEvents.TryGetValue(ev.Uid, out var oldSig) || oldSig != sig)
                            {
                                knownEvents[ev.Uid] = sig;
                                observer.OnNext(ev);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FirebaseService] Błąd odpytywania zdarzeń grupy ({sGroup}): {ex.Message}");
                    }
                }

                _ = Task.Run(CheckForUpdates);

                timer.Elapsed += async (s, e) => await CheckForUpdates();
                timer.AutoReset = true;
                timer.Start();

                return () =>
                {
                    timer.Stop();
                    timer.Dispose();
                };
            });
        }
        
        public async Task<List<FirebaseEvent>> GetGroupEventsAsync(string groupCode)
        {
            try
            {
                groupCode = await ResolveGroupCodeAsync(groupCode);
                var events = await _firebase.Child("groups").Child(groupCode).Child("events").OnceAsync<FirebaseEvent>();
                return events.Select(x => x.Object).ToList();
            }
            catch
            {
                return new List<FirebaseEvent>();
            }
        }

        // --- OGŁOSZENIA (ANNOUNCEMENTS) ---

        public async Task PublishAnnouncementAsync(string groupCode, AnnouncementItem item)
        {
            try
            {
                groupCode = await ResolveGroupCodeAsync(groupCode);
                var key = string.IsNullOrEmpty(item.Uid) ? Guid.NewGuid().ToString() : item.Uid;
                item.Uid = key;
                await _firebase.Child("groups").Child(groupCode).Child("announcements").Child(key).PutAsync(item);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd publikacji ogłoszenia: {ex.Message}");
            }
        }

        public async Task DeleteAnnouncementAsync(string groupCode, string key)
        {
            try
            {
                groupCode = await ResolveGroupCodeAsync(groupCode);
                await _firebase.Child("groups").Child(groupCode).Child("announcements").Child(key).DeleteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd usuwania ogłoszenia: {ex.Message}");
            }
        }

        public async Task<List<AnnouncementItem>> GetAnnouncementsAsync(string groupCode)
        {
            try
            {
                groupCode = await ResolveGroupCodeAsync(groupCode);
                var events = await _firebase.Child("groups").Child(groupCode).Child("announcements").OnceAsync<AnnouncementItem>();
                return events.Select(x => {
                    var obj = x.Object;
                    if (string.IsNullOrEmpty(obj.Uid)) obj.Uid = x.Key;
                    return obj;
                }).ToList();
            }
            catch
            {
                return new List<AnnouncementItem>();
            }
        }

        public async Task<List<AnnouncementItem>> GetSystemAnnouncementsAsync()
        {
            try
            {
                var events = await _firebase.Child("system_announcements").OnceAsync<AnnouncementItem>();
                var items = events.Select(x => {
                    var obj = x.Object;
                    if (obj != null)
                    {
                        if (string.IsNullOrEmpty(obj.Uid)) obj.Uid = x.Key;
                        obj.IsSystem = true;
                        if (string.IsNullOrEmpty(obj.Author)) obj.Author = "System";
                    }
                    return obj;
                }).Where(x => x != null).Cast<AnnouncementItem>().ToList();

                if (items.Any())
                {
                    try
                    {
                        var json = JsonSerializer.Serialize(items);
                        Preferences.Default.Set("cached_system_announcements", json);
                    }
                    catch { }
                    return items;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd pobierania ogłoszeń systemowych: {ex.Message}");
            }

            // Fallback: pobierz z pamięci podręcznej (offline-first)
            try
            {
                var cachedJson = Preferences.Default.Get("cached_system_announcements", string.Empty);
                if (!string.IsNullOrEmpty(cachedJson))
                {
                    var cached = JsonSerializer.Deserialize<List<AnnouncementItem>>(cachedJson);
                    if (cached != null && cached.Any()) return cached.Where(x => x != null).Cast<AnnouncementItem>().ToList();
                }
            }
            catch { }

            return new List<AnnouncementItem>();
        }

        // --- OBSŁUGA DANYCH PRYWATNYCH (SYNC MIĘDZY URZĄDZENIAMI) ---

        public async Task PublishPersonalEventAsync(string albumNumber, FirebaseEvent ev)
        {
            if (string.IsNullOrWhiteSpace(albumNumber) || ev == null) return;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var sUid = SanitizeKey(ev.Uid);
                ev.Uid = sUid;
                await _firebase.Child("users").Child(sAlbum).Child("personal_events").Child(sUid).PutAsync(ev);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd publikacji wydarzenia osobistego: {ex.Message}");
            }
        }

        public async Task DeletePersonalEventAsync(string albumNumber, string eventUid)
        {
            if (string.IsNullOrWhiteSpace(albumNumber) || string.IsNullOrWhiteSpace(eventUid)) return;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var sUid = SanitizeKey(eventUid);
                await _firebase.Child("users").Child(sAlbum).Child("personal_events").Child(sUid).DeleteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd usuwania wydarzenia osobistego: {ex.Message}");
            }
        }

        public async Task<List<FirebaseEvent>> GetPersonalEventsAsync(string albumNumber)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return new List<FirebaseEvent>();
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var events = await _firebase.Child("users").Child(sAlbum).Child("personal_events").OnceAsync<FirebaseEvent>();
                return events.Select(x => {
                    var obj = x.Object;
                    if (string.IsNullOrEmpty(obj.Uid)) obj.Uid = x.Key;
                    return obj;
                }).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd pobierania wydarzeń osobistych: {ex.Message}");
                return new List<FirebaseEvent>();
            }
        }

        public async Task PublishPersonalModificationAsync(string albumNumber, string key, object mod)
        {
            if (string.IsNullOrWhiteSpace(albumNumber) || string.IsNullOrWhiteSpace(key) || mod == null) return;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var safeKey = SanitizeKey(key);
                await _firebase.Child("users").Child(sAlbum).Child("modifications").Child(safeKey).PutAsync(mod);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd publikacji modyfikacji osobistej: {ex.Message}");
            }
        }

        public async Task<List<T>> GetPersonalModificationsAsync<T>(string albumNumber)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return new List<T>();
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var mods = await _firebase.Child("users").Child(sAlbum).Child("modifications").OnceAsync<T>();
                return mods.Select(x => x.Object).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd pobierania modyfikacji osobistych: {ex.Message}");
                return new List<T>();
            }
        }
        
        public async Task SaveAttendanceAsync(string albumNumber, string courseKey, bool isAttended)
        {
            if (string.IsNullOrWhiteSpace(albumNumber) || string.IsNullOrWhiteSpace(courseKey)) return;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var sCourse = SanitizeKey(courseKey);
                await _firebase.Child("users").Child(sAlbum).Child("attendances").Child(sCourse).PutAsync(isAttended);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd zapisu obecności: {ex.Message}");
            }
        }

        public static string SanitizeKey(string key) => FirebaseSecurityHelper.SanitizeKey(key);

        // --- OBSŁUGA ANKIET GRUPOWYCH (GROUP POLLS) ---

        public async Task<bool> CreateGroupPollAsync(string groupCode, GroupPoll poll)
        {
            if (string.IsNullOrWhiteSpace(groupCode) || poll == null) return false;
            try
            {
                var sGroup = SanitizeKey(groupCode).ToUpperInvariant();
                if (string.IsNullOrEmpty(poll.Id)) poll.Id = Guid.NewGuid().ToString("N");
                var sPollId = SanitizeKey(poll.Id);
                poll.GroupCode = sGroup;

                await _firebase.Child("groups").Child(sGroup).Child("polls").Child(sPollId).PutAsync(poll);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd tworzenia ankiety: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> VoteGroupPollAsync(string groupCode, string pollId, string albumNumber, int optionIndex)
        {
            if (string.IsNullOrWhiteSpace(groupCode) || string.IsNullOrWhiteSpace(pollId) || string.IsNullOrWhiteSpace(albumNumber) || optionIndex < 0) return false;
            try
            {
                var sGroup = SanitizeKey(groupCode).ToUpperInvariant();
                var sPollId = SanitizeKey(pollId);
                var sAlbum = SanitizeKey(albumNumber.Trim().ToUpperInvariant());

                var poll = await _firebase.Child("groups").Child(sGroup).Child("polls").Child(sPollId).OnceSingleAsync<GroupPoll>();
                if (poll == null || poll.IsClosed) return false;
                if (poll.Options != null && optionIndex >= poll.Options.Count) return false;

                await _firebase.Child("groups").Child(sGroup).Child("polls").Child(sPollId).Child("Votes").Child(sAlbum).PutAsync(optionIndex);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd oddawania głosu w ankiecie: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CloseGroupPollAsync(string groupCode, string pollId)
        {
            if (string.IsNullOrWhiteSpace(groupCode) || string.IsNullOrWhiteSpace(pollId)) return false;
            try
            {
                var sGroup = SanitizeKey(groupCode).ToUpperInvariant();
                var sPollId = SanitizeKey(pollId);

                await _firebase.Child("groups").Child(sGroup).Child("polls").Child(sPollId).Child("IsClosed").PutAsync(true);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd zamykania ankiety: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteGroupPollAsync(string groupCode, string pollId)
        {
            if (string.IsNullOrWhiteSpace(groupCode) || string.IsNullOrWhiteSpace(pollId)) return false;
            try
            {
                var sGroup = SanitizeKey(groupCode).ToUpperInvariant();
                var sPollId = SanitizeKey(pollId);

                await _firebase.Child("groups").Child(sGroup).Child("polls").Child(sPollId).DeleteAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd usuwania ankiety: {ex.Message}");
                return false;
            }
        }

        public async Task<List<GroupPoll>> GetGroupPollsAsync(string groupCode)
        {
            if (string.IsNullOrWhiteSpace(groupCode)) return new List<GroupPoll>();
            try
            {
                var sGroup = SanitizeKey(groupCode).ToUpperInvariant();
                var list = await _firebase.Child("groups").Child(sGroup).Child("polls").OnceAsync<GroupPoll>();
                return list
                    .Where(x => x?.Object != null)
                    .Select(x =>
                    {
                        var p = x.Object;
                        if (string.IsNullOrEmpty(p.Id)) p.Id = x.Key;
                        if (p.Options == null) p.Options = new List<string>();
                        if (p.Votes == null) p.Votes = new Dictionary<string, int>();
                        return p;
                    })
                    .OrderByDescending(p => p.CreatedAt)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd pobierania ankiet: {ex.Message}");
                return new List<GroupPoll>();
            }
        }

        public IObservable<GroupPoll> SubscribeToGroupPolls(string groupCode)
        {
            if (string.IsNullOrWhiteSpace(groupCode)) return Observable.Empty<GroupPoll>();
            var sGroup = SanitizeKey(groupCode).ToUpperInvariant();

            return Observable.Create<GroupPoll>(observer =>
            {
                var knownPolls = new Dictionary<string, int>();
                var timer = new System.Timers.Timer(15000);

                async Task CheckPolls()
                {
                    try
                    {
                        var polls = await GetGroupPollsAsync(sGroup);
                        foreach (var poll in polls)
                        {
                            int voteCount = poll.Votes?.Count ?? 0;
                            int hash = voteCount * 31 + (poll.IsClosed ? 1 : 0);
                            if (!knownPolls.TryGetValue(poll.Id, out var oldHash) || oldHash != hash)
                            {
                                knownPolls[poll.Id] = hash;
                                observer.OnNext(poll);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FirebaseService] Błąd odpytywania ankiet grupy ({sGroup}): {ex.Message}");
                    }
                }

                _ = Task.Run(CheckPolls);

                timer.Elapsed += async (s, e) => await CheckPolls();
                timer.AutoReset = true;
                timer.Start();

                return () =>
                {
                    timer.Stop();
                    timer.Dispose();
                };
            });
        }

        // ==============================================================================
        // APARAT ZDALNY (CROSS-DEVICE CAMERA & WI-FI DIRECT) & NOTATKI W CHMURZE
        // ==============================================================================

        public const string PermanentCameraSessionId = "default";

        public async Task<RemoteCameraSession?> SetPermanentRemoteCameraStandbyAsync(string albumNumber, string noteId, string noteTitle, string? localEndpoint = null, string? authToken = null)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return null;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var session = new RemoteCameraSession
                {
                    SessionId = PermanentCameraSessionId,
                    NoteId = noteId,
                    NoteTitle = noteTitle,
                    LocalEndpoint = localEndpoint,
                    AuthToken = authToken,
                    Status = "idle",
                    IsListening = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _firebase.Child("users").Child(sAlbum).Child("camera_sessions").Child(PermanentCameraSessionId).PutAsync(session);
                return session;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd ustawiania stanu oczekiwania aparatu: {ex.Message}");
                return null;
            }
        }

        public async Task<RemoteCameraSession?> SetPermanentRemoteCameraReadyAsync(string albumNumber, string noteId, string noteTitle, string? localEndpoint = null, string? authToken = null)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return null;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var session = new RemoteCameraSession
                {
                    SessionId = PermanentCameraSessionId,
                    NoteId = noteId,
                    NoteTitle = noteTitle,
                    LocalEndpoint = localEndpoint,
                    AuthToken = authToken,
                    Status = "ready",
                    IsListening = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _firebase.Child("users").Child(sAlbum).Child("camera_sessions").Child(PermanentCameraSessionId).PutAsync(session);
                return session;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd ustawiania stałej sesji aparatu: {ex.Message}");
                return null;
            }
        }

        public async Task<RemoteCameraSession?> GetPermanentRemoteCameraSessionAsync(string albumNumber)
        {
            return await GetRemoteCameraSessionAsync(albumNumber, PermanentCameraSessionId);
        }

        public async Task SetPermanentRemoteCameraOfflineAsync(string albumNumber)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var session = await GetPermanentRemoteCameraSessionAsync(albumNumber);
                if (session != null)
                {
                    session.Status = "offline";
                    session.IsListening = false;
                    session.LocalEndpoint = null;
                    session.UpdatedAt = DateTime.UtcNow;
                    await _firebase.Child("users").Child(sAlbum).Child("camera_sessions").Child(PermanentCameraSessionId).PutAsync(session);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd oznaczania sesji aparatu jako offline: {ex.Message}");
            }
        }

        public async Task UpdatePermanentRemoteCameraHeartbeatAsync(string albumNumber)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var session = await GetPermanentRemoteCameraSessionAsync(albumNumber);
                if (session != null && session.Status != "offline")
                {
                    session.UpdatedAt = DateTime.UtcNow;
                    await _firebase.Child("users").Child(sAlbum).Child("camera_sessions").Child(PermanentCameraSessionId).PutAsync(session);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd heartbeat aparatu: {ex.Message}");
            }
        }

        public async Task<RemoteCameraSession?> CreateRemoteCameraSessionAsync(string albumNumber, string noteId, string noteTitle)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return null;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var sessionId = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
                var session = new RemoteCameraSession
                {
                    SessionId = sessionId,
                    NoteId = noteId,
                    NoteTitle = noteTitle,
                    Status = "waiting",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _firebase.Child("users").Child(sAlbum).Child("camera_sessions").Child(sessionId).PutAsync(session);
                return session;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd tworzenia sesji aparatu zdalnego: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> UploadRemoteCameraPhotoAsync(string albumNumber, string sessionId, string base64Photo, string contentType = "image/jpeg")
        {
            if (string.IsNullOrWhiteSpace(albumNumber) || string.IsNullOrWhiteSpace(sessionId)) return false;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var sSession = SanitizeKey(sessionId);

                var session = await _firebase.Child("users").Child(sAlbum).Child("camera_sessions").Child(sSession).OnceSingleAsync<RemoteCameraSession>();
                if (session == null)
                {
                    // Utwórz jeśli to sesja stała
                    session = new RemoteCameraSession
                    {
                        SessionId = sSession,
                        Status = "uploaded",
                        CreatedAt = DateTime.UtcNow
                    };
                }

                session.PhotoDataBase64 = base64Photo;
                session.ContentType = contentType;
                session.Status = "uploaded";
                session.UpdatedAt = DateTime.UtcNow;

                await _firebase.Child("users").Child(sAlbum).Child("camera_sessions").Child(sSession).PutAsync(session);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd przesyłania zdjęcia ze zdalnego aparatu: {ex.Message}");
                return false;
            }
        }

        public async Task<RemoteCameraSession?> GetRemoteCameraSessionAsync(string albumNumber, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(albumNumber) || string.IsNullOrWhiteSpace(sessionId)) return null;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var sSession = SanitizeKey(sessionId);
                return await _firebase.Child("users").Child(sAlbum).Child("camera_sessions").Child(sSession).OnceSingleAsync<RemoteCameraSession>();
            }
            catch
            {
                return null;
            }
        }

        public async Task DeleteRemoteCameraSessionAsync(string albumNumber, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(albumNumber) || string.IsNullOrWhiteSpace(sessionId)) return;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var sSession = SanitizeKey(sessionId);
                await _firebase.Child("users").Child(sAlbum).Child("camera_sessions").Child(sSession).DeleteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd usuwania sesji aparatu: {ex.Message}");
            }
        }

        public async Task ShareNoteToGroupLobbyAsync(string groupCode, Note note)
        {
            if (string.IsNullOrWhiteSpace(groupCode) || note == null) return;
            try
            {
                var sGroup = SanitizeKey(groupCode).ToUpperInvariant();
                var sNoteId = SanitizeKey(note.Id.ToString());
                await _firebase.Child("groups").Child(sGroup).Child("shared_notes").Child(sNoteId).PutAsync(note);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd udostępniania notatki w grupie: {ex.Message}");
            }
        }

        public async Task UnshareNoteFromGroupLobbyAsync(string groupCode, Guid noteId)
        {
            if (string.IsNullOrWhiteSpace(groupCode)) return;
            try
            {
                var sGroup = SanitizeKey(groupCode).ToUpperInvariant();
                var sNoteId = SanitizeKey(noteId.ToString());
                await _firebase.Child("groups").Child(sGroup).Child("shared_notes").Child(sNoteId).DeleteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd usuwania notatki z grupy: {ex.Message}");
            }
        }

        public async Task<List<Note>> GetGroupSharedNotesAsync(string groupCode)
        {
            if (string.IsNullOrWhiteSpace(groupCode)) return new List<Note>();
            try
            {
                var sGroup = SanitizeKey(groupCode).ToUpperInvariant();
                var notes = await _firebase.Child("groups").Child(sGroup).Child("shared_notes").OnceAsync<Note>();
                return notes?.Select(n => n.Object).Where(n => n != null).ToList() ?? new List<Note>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd pobierania udostępnionych notatek: {ex.Message}");
                return new List<Note>();
            }
        }

        public async Task SyncNoteToCloudAsync(string albumNumber, Note note)
        {
            if (string.IsNullOrWhiteSpace(albumNumber) || note == null) return;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var sNoteId = SanitizeKey(note.Id.ToString());
                await _firebase.Child("users").Child(sAlbum).Child("notes").Child(sNoteId).PutAsync(note);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd synchronizacji notatki w chmurze: {ex.Message}");
            }
        }

        public async Task DeleteNoteFromCloudAsync(string albumNumber, Guid noteId)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return;
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var sNoteId = SanitizeKey(noteId.ToString());
                await _firebase.Child("users").Child(sAlbum).Child("notes").Child(sNoteId).DeleteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd usuwania notatki z chmury: {ex.Message}");
            }
        }

        public async Task<List<Note>> GetUserNotesFromCloudAsync(string albumNumber)
        {
            if (string.IsNullOrWhiteSpace(albumNumber)) return new List<Note>();
            try
            {
                var sAlbum = SanitizeKey(albumNumber);
                var notes = await _firebase.Child("users").Child(sAlbum).Child("notes").OnceAsync<Note>();
                return notes?.Select(n => n.Object).Where(n => n != null).ToList() ?? new List<Note>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FirebaseService] Błąd pobierania notatek użytkownika z chmury: {ex.Message}");
                return new List<Note>();
            }
        }
    }

    public class RemoteCameraSession
    {
        public string SessionId { get; set; } = "default";
        public string NoteId { get; set; } = "";
        public string NoteTitle { get; set; } = "";
        public string Status { get; set; } = "idle"; // idle, ready, uploaded, completed, cancelled, offline
        public bool IsListening { get; set; } = false;
        public string? LocalEndpoint { get; set; } // http://192.168.1.124:5273
        public string? AuthToken { get; set; }
        public string? PhotoDataBase64 { get; set; }
        public string? ContentType { get; set; } = "image/jpeg";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
