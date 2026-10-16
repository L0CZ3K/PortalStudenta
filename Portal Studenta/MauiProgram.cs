using Microsoft.Extensions.Logging;
using Portal_Studenta.Services;
using System.Globalization;

namespace Portal_Studenta
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddLocalization();

            // Singleton services
            builder.Services.AddSingleton<ScheduleService>();
            builder.Services.AddSingleton<AnnouncementService>();
            builder.Services.AddSingleton<Portal_Studenta.Services.NoteService>();
            builder.Services.AddSingleton<Portal_Studenta.Services.AppState>();
            builder.Services.AddSingleton<UsosAuthService>();
            builder.Services.AddSingleton<UsosApiService>();
            builder.Services.AddSingleton<ProfileService>();
            builder.Services.AddSingleton<FirebaseService>();
            builder.Services.AddSingleton<UniversityService>();
            builder.Services.AddSingleton<NotificationService>();
            builder.Services.AddSingleton<CalendarExportService>();
#if MACCATALYST
            builder.Services.AddSingleton<Portal_Studenta.Platforms.MacCatalyst.MacMenuBarService>();
#endif

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
