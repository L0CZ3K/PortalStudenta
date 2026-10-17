namespace Portal_Studenta
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();

#if ANDROID
            blazorWebView.BlazorWebViewInitialized += (sender, args) =>
            {
                try
                {
                    var webView = args.WebView;
                    webView.Settings.JavaScriptEnabled = true;
                    webView.Settings.DomStorageEnabled = true;
                    webView.Settings.DatabaseEnabled = true;
                    webView.Settings.AllowFileAccess = true;
                    webView.Settings.AllowContentAccess = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainPage] BlazorWebViewInitialized config error: {ex.Message}");
                }
            };
#endif
        }
    }
}
