using Foundation;
using UIKit;

namespace Portal_Studenta
{
    [Register("AppDelegate")]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
        {
            try
            {
                UserNotifications.UNUserNotificationCenter.Current.Delegate = Portal_Studenta.Services.NotificationService.NotificationDelegate;
#pragma warning disable CA1422
                UIApplication.SharedApplication.SetMinimumBackgroundFetchInterval(UIApplication.BackgroundFetchIntervalMinimum);
#pragma warning restore CA1422
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AppDelegate] Błąd rejestracji delegata powiadomień / fetch: {ex.Message}");
            }

            return base.FinishedLaunching(application, launchOptions);
        }

        public override void PerformFetch(UIApplication application, Action<UIBackgroundFetchResult> completionHandler)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var services = IPlatformApplication.Current?.Services;
                    if (services != null)
                    {
                        var scheduleService = services.GetService<Portal_Studenta.Services.ScheduleService>();
                        var notifService = services.GetService<Portal_Studenta.Services.NotificationService>();
                        if (scheduleService != null && notifService != null)
                        {
                            await scheduleService.SyncCloudEventsAsync();
                            var upcoming = scheduleService.GetAllEvents().Where(l => !l.IsCanceled && l.Start > DateTime.Now && l.Start < DateTime.Now.AddDays(2));
                            await notifService.ScheduleUpcomingClassRemindersAsync(upcoming);
                        }
                    }
                    completionHandler(UIBackgroundFetchResult.NewData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AppDelegate] PerformFetch error: {ex.Message}");
                    completionHandler(UIBackgroundFetchResult.Failed);
                }
            });
        }

        public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
        {
            if (Microsoft.Maui.ApplicationModel.Platform.OpenUrl(application, url, options))
                return true;

            return base.OpenUrl(application, url, options);
        }
    }
}
