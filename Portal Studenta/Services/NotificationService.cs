using System;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using Portal_Studenta.Models;

#if IOS || MACCATALYST
using UserNotifications;
#endif

namespace Portal_Studenta.Services
{
    public class NotificationService
    {
        public const string PrefNotificationsEnabled = "notifications_enabled";
        public const string PrefNotifyCancellations = "notify_cancellations";
        public const string PrefNotifyRoomChanges = "notify_room_changes";
        public const string PrefNotifyReschedules = "notify_reschedules";
        public const string PrefNotifyAnnouncements = "notify_announcements";
        public const string PrefNotifyDevAnnouncements = "notify_dev_announcements";
        public const string PrefNotifyStarostaAnnouncements = "notify_starosta_announcements";
        public const string PrefNotifyUpcomingClasses = "notify_upcoming_classes";
        public const string PrefNotifyPolls = "notify_polls";
        public const string PrefReminderMinutes = "notify_reminder_minutes";

        public event Action<string, string>? OnInAppNotification;

        public bool IsEnabled
        {
            get => Preferences.Default.Get(PrefNotificationsEnabled, true);
            set => Preferences.Default.Set(PrefNotificationsEnabled, value);
        }

        public bool NotifyUpcomingClasses
        {
            get => Preferences.Default.Get(PrefNotifyUpcomingClasses, true);
            set => Preferences.Default.Set(PrefNotifyUpcomingClasses, value);
        }

        public bool NotifyPolls
        {
            get => Preferences.Default.Get(PrefNotifyPolls, true);
            set => Preferences.Default.Set(PrefNotifyPolls, value);
        }

        public int ReminderMinutes
        {
            get => Preferences.Default.Get(PrefReminderMinutes, 15);
            set => Preferences.Default.Set(PrefReminderMinutes, value);
        }

        public bool NotifyCancellations
        {
            get => Preferences.Default.Get(PrefNotifyCancellations, true);
            set => Preferences.Default.Set(PrefNotifyCancellations, value);
        }

        public bool NotifyRoomChanges
        {
            get => Preferences.Default.Get(PrefNotifyRoomChanges, true);
            set => Preferences.Default.Set(PrefNotifyRoomChanges, value);
        }

        public bool NotifyReschedules
        {
            get => Preferences.Default.Get(PrefNotifyReschedules, true);
            set => Preferences.Default.Set(PrefNotifyReschedules, value);
        }

        public bool NotifyAnnouncements
        {
            get => Preferences.Default.Get(PrefNotifyAnnouncements, true);
            set => Preferences.Default.Set(PrefNotifyAnnouncements, value);
        }

        public bool NotifyDevAnnouncements
        {
            get => Preferences.Default.Get(PrefNotifyDevAnnouncements, true);
            set => Preferences.Default.Set(PrefNotifyDevAnnouncements, value);
        }

        public bool NotifyStarostaAnnouncements
        {
            get => Preferences.Default.Get(PrefNotifyStarostaAnnouncements, true);
            set => Preferences.Default.Set(PrefNotifyStarostaAnnouncements, value);
        }

#if IOS || MACCATALYST
        public static readonly ForegroundNotificationDelegate NotificationDelegate = new ForegroundNotificationDelegate();
#endif

        public NotificationService()
        {
#if IOS || MACCATALYST
            try
            {
                UNUserNotificationCenter.Current.Delegate = NotificationDelegate;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationService] Delegate setup error: {ex.Message}");
            }
#endif
        }

        public async Task<bool> CheckPermissionAsync()
        {
#if IOS || MACCATALYST
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                UNUserNotificationCenter.Current.GetNotificationSettings(settings =>
                {
                    bool authorized = settings.AuthorizationStatus == UNAuthorizationStatus.Authorized ||
                                      settings.AuthorizationStatus == UNAuthorizationStatus.Provisional;
                    tcs.TrySetResult(authorized);
                });
                return await tcs.Task;
            }
            catch
            {
                return false;
            }
#else
            await Task.CompletedTask;
            return true;
#endif
        }

        public async Task<bool> EnsurePermissionRequestedAsync()
        {
#if IOS || MACCATALYST
            try
            {
                var tcs = new TaskCompletionSource<UNAuthorizationStatus>();
                UNUserNotificationCenter.Current.GetNotificationSettings(settings =>
                {
                    tcs.TrySetResult(settings.AuthorizationStatus);
                });
                var status = await tcs.Task;
                if (status == UNAuthorizationStatus.NotDetermined)
                {
                    return await RequestPermissionAsync();
                }
                return status == UNAuthorizationStatus.Authorized || status == UNAuthorizationStatus.Provisional;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationService] EnsurePermissionRequestedAsync error: {ex.Message}");
                return false;
            }
#else
            await Task.CompletedTask;
            return true;
#endif
        }

        public async Task<bool> RequestPermissionAsync()
        {
#if IOS || MACCATALYST
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                var center = UNUserNotificationCenter.Current;
                center.Delegate = NotificationDelegate;
                center.RequestAuthorization(
                    UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge,
                    (granted, error) =>
                    {
                        if (error != null)
                        {
                            Console.WriteLine($"[NotificationService] Authorization error: {error.LocalizedDescription}");
                        }
                        tcs.TrySetResult(granted);
                    });
                var result = await tcs.Task;
                IsEnabled = result;
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationService] RequestPermissionAsync error: {ex.Message}");
                return false;
            }
#else
            await Task.CompletedTask;
            return true;
#endif
        }

        public async Task<bool> SendNotificationAsync(string title, string body)
        {
            if (!IsEnabled) return false;

            // Trigger in-app toast event (zawsze w aplikacji)
            OnInAppNotification?.Invoke(title, body);

#if IOS || MACCATALYST
            try
            {
                var center = UNUserNotificationCenter.Current;
                center.Delegate = NotificationDelegate;

                var settingsTcs = new TaskCompletionSource<UNNotificationSettings>();
                center.GetNotificationSettings(settings => settingsTcs.TrySetResult(settings));
                var settings = await settingsTcs.Task;

                if (settings.AuthorizationStatus == UNAuthorizationStatus.NotDetermined)
                {
                    bool granted = await RequestPermissionAsync();
                    if (!granted) return false;
                }
                else if (settings.AuthorizationStatus == UNAuthorizationStatus.Denied)
                {
                    Console.WriteLine("[NotificationService] Uprawnienia do powiadomień zablokowane w ustawieniach systemu.");
                    return false;
                }

                var content = new UNMutableNotificationContent
                {
                    Title = title,
                    Body = body,
                    Sound = UNNotificationSound.Default,
                    Badge = 1
                };

                // null trigger oznacza natychmiastowe doręczenie powiadomienia
                var request = UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString(), content, null);

                var addTcs = new TaskCompletionSource<bool>();
                center.AddNotificationRequest(request, (error) =>
                {
                    if (error != null)
                    {
                        Console.WriteLine($"[NotificationService] Błąd dodawania powiadomienia: {error.LocalizedDescription}");
                        addTcs.TrySetResult(false);
                    }
                    else
                    {
                        Console.WriteLine($"[NotificationService] Powiadomienie wysłane pomyślnie: {title}");
                        addTcs.TrySetResult(true);
                    }
                });

                return await addTcs.Task;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationService] SendNotification error: {ex.Message}");
                return false;
            }
#else
            await Task.CompletedTask;
            return true;
#endif
        }

        public void NotifyLessonCanceled(Lesson lesson, string? reason = null)
        {
            if (!IsEnabled || !NotifyCancellations) return;
            var (title, body) = NotificationMessageLogic.FormatLessonCanceled(lesson, reason, DateTime.Now);
            _ = SendNotificationAsync(title, body);
        }

        public void NotifyLessonRoomChanged(Lesson lesson, string newRoom)
        {
            if (!IsEnabled || !NotifyRoomChanges) return;
            var (title, body) = NotificationMessageLogic.FormatLessonRoomChanged(lesson, newRoom, DateTime.Now);
            _ = SendNotificationAsync(title, body);
        }

        public void NotifyLessonRescheduled(Lesson lesson, DateTime newStart, DateTime newEnd)
        {
            if (!IsEnabled || !NotifyReschedules) return;
            var (title, body) = NotificationMessageLogic.FormatLessonRescheduled(lesson, newStart, newEnd);
            _ = SendNotificationAsync(title, body);
        }

        public void NotifyUrgentAnnouncement(AnnouncementItem announcement)
        {
            if (!IsEnabled || !NotifyAnnouncements) return;

            var isDev = announcement.IsSystem || string.Equals(announcement.Author, "dev", StringComparison.OrdinalIgnoreCase) || string.Equals(announcement.Author, "system", StringComparison.OrdinalIgnoreCase);
            if (isDev)
            {
                if (!NotifyDevAnnouncements) return;
            }
            else
            {
                if (!NotifyStarostaAnnouncements) return;
            }

            var (title, body) = NotificationMessageLogic.FormatAnnouncement(announcement);
            _ = SendNotificationAsync(title, body);
        }

        public void NotifyNewPoll(GroupPoll poll)
        {
            if (!IsEnabled || !NotifyPolls || poll == null) return;
            var (title, body) = NotificationMessageLogic.FormatNewPoll(poll);
            _ = SendNotificationAsync(title, body);
        }

        public async Task ScheduleClassReminderAsync(Lesson lesson, int? minutesBefore = null)
        {
            if (!IsEnabled || !NotifyUpcomingClasses || lesson == null || lesson.IsCanceled) return;

            int minutes = minutesBefore ?? ReminderMinutes;
            var triggerTime = lesson.Start.AddMinutes(-minutes);
            var secondsFromNow = (triggerTime - DateTime.Now).TotalSeconds;
            if (secondsFromNow <= 5) return;

#if IOS || MACCATALYST
            try
            {
                var content = new UNMutableNotificationContent
                {
                    Title = $"⏰ Zaraz zajęcia: {lesson.Title}",
                    Body = $"Zajęcia {lesson.Type} o godz. {lesson.Start:HH:mm} w sali: {lesson.Place}",
                    Sound = UNNotificationSound.Default,
                    Badge = 1
                };

                var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(secondsFromNow, repeats: false);
                var reqId = $"lesson_alarm_{lesson.Id}_{lesson.Start:yyyyMMddHHmm}";
                var request = UNNotificationRequest.FromIdentifier(reqId, content, trigger);
                UNUserNotificationCenter.Current.AddNotificationRequest(request, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotificationService] Błąd planowania przypomnienia o zajęciach: {ex.Message}");
            }
#else
            await Task.CompletedTask;
#endif
        }

        public async Task ScheduleUpcomingClassRemindersAsync(IEnumerable<Lesson> lessons)
        {
            if (!IsEnabled || !NotifyUpcomingClasses || lessons == null) return;
            var upcoming = lessons.Where(l => !l.IsCanceled && l.Start > DateTime.Now && l.Start < DateTime.Now.AddDays(3));
            foreach (var lesson in upcoming)
            {
                await ScheduleClassReminderAsync(lesson, ReminderMinutes);
            }
        }
    }

#if IOS || MACCATALYST
    public class ForegroundNotificationDelegate : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(
            UNUserNotificationCenter center,
            UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler)
        {
            try
            {
                var options = UNNotificationPresentationOptions.Sound | UNNotificationPresentationOptions.Badge;
                if (UIKit.UIDevice.CurrentDevice.CheckSystemVersion(14, 0))
                {
                    options |= UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List;
                }
                else
                {
                    options |= UNNotificationPresentationOptions.Alert;
                }
                completionHandler(options);
            }
            catch
            {
                completionHandler(UNNotificationPresentationOptions.Sound);
            }
        }

        public override void DidReceiveNotificationResponse(
            UNUserNotificationCenter center,
            UNNotificationResponse response,
            Action completionHandler)
        {
            try
            {
                completionHandler();
            }
            catch {}
        }
    }
#endif
}
