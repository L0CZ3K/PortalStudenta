namespace Portal_Studenta.Services
{
    public static partial class UsosSecrets
    {
        // Te właściwości są nadpisywane przez plik UsosSecrets.Local.cs (ignorowany w Git)
        public static string ConsumerKey { get; set; } = "DUMMY_CONSUMER_KEY";
        public static string ConsumerSecret { get; set; } = "DUMMY_CONSUMER_SECRET";

        static UsosSecrets()
        {
            LoadLocalSecrets();
        }

        static partial void LoadLocalSecrets();
    }
}
