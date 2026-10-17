using System.Globalization;

namespace Portal_Studenta
{
    public partial class App : Application
    {
        public App()
        {
            var savedLang = Preferences.Default.Get("app_lang", "pl-PL");
            var culture = new CultureInfo(savedLang);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            InitializeComponent();

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[CRITICAL UNHANDLED EXCEPTION] {e.ExceptionObject}");
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[CRITICAL UNOBSERVED TASK EXCEPTION] {e.Exception}");
                e.SetObserved();
            };
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new MainPage()) { Title = "UniAvio" };
        }
    }
}
