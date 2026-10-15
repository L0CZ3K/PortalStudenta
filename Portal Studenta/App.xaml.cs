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
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new MainPage()) { Title = "UniAvio" };
        }
    }
}
