using System.Collections.Generic;
using System.Linq;

namespace Portal_Studenta.Services
{
    public class AnnouncementService
    {
        private readonly FirebaseService _firebaseService;
        private readonly AppState _appState;
        private readonly NotificationService? _notificationService;

        public AnnouncementService(FirebaseService firebaseService, AppState appState, NotificationService? notificationService = null)
        {
            _firebaseService = firebaseService;
            _appState = appState;
            _notificationService = notificationService;
        }

        public async Task<List<AnnouncementItem>> GetAnnouncementsAsync()
        {
            var allNews = new List<AnnouncementItem>();

            // 1. Zawsze pobieraj ogłoszenia systemowe (Dev)
            var systemNews = await _firebaseService.GetSystemAnnouncementsAsync();
            allNews.AddRange(systemNews);

            // 2. Jeśli student jest w grupie, pobierz ogłoszenia Starosty
            if (!string.IsNullOrEmpty(_appState.GroupCode))
            {
                var groupNews = await _firebaseService.GetAnnouncementsAsync(_appState.GroupCode);
                
                foreach (var item in groupNews)
                {
                    if (item.ExpireDate != default && item.ExpireDate < DateTime.Now)
                    {
                        // Autousuwanie z bazy po wygaśnięciu
                        _ = _firebaseService.DeleteAnnouncementAsync(_appState.GroupCode, item.Uid);
                    }
                    else
                    {
                        allNews.Add(item);
                    }
                }
            }

            // Powiadomienia o nowych ogłoszeniach
            try
            {
                var knownRaw = Microsoft.Maui.Storage.Preferences.Default.Get("known_announcements", "");
                var knownIds = (knownRaw ?? "")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet();

                bool isFirstRun = knownIds.Count == 0;

                foreach (var item in allNews)
                {
                    if (!string.IsNullOrEmpty(item.Uid) && !knownIds.Contains(item.Uid))
                    {
                        knownIds.Add(item.Uid);
                        if (!isFirstRun && (item.IsPriority || item.IsSystem))
                        {
                            _notificationService?.NotifyUrgentAnnouncement(item);
                        }
                    }
                }
                Microsoft.Maui.Storage.Preferences.Default.Set("known_announcements", string.Join(";", knownIds.TakeLast(60)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnnouncementService] Notification check error: {ex.Message}");
            }

            return AnnouncementLogic.FilterAndSort(allNews, DateTime.Now);
        }

        public async Task AddAnnouncementAsync(AnnouncementItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrEmpty(_appState.GroupCode)) return;

            if (!_appState.HasManagementPrivileges)
            {
                throw new UnauthorizedAccessException("Brak uprawnień do dodawania ogłoszeń grupy.");
            }

            item.CreatedAt = DateTime.Now;
            await _firebaseService.PublishAnnouncementAsync(_appState.GroupCode, item);
        }

        public async Task DeleteAnnouncementAsync(AnnouncementItem item)
        {
            if (item != null && !string.IsNullOrEmpty(_appState.GroupCode))
            {
                if (!_appState.HasManagementPrivileges)
                {
                    throw new UnauthorizedAccessException("Brak uprawnień do usuwania ogłoszeń grupy.");
                }
                await _firebaseService.DeleteAnnouncementAsync(_appState.GroupCode, item.Uid);
            }
        }
    }
}
