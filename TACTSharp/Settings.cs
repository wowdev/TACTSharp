namespace TACTSharp
{
    public class Settings
    {
        public string Region = "us";
        public string Product = "wow";
        public RootInstance.LocaleFlags Locale = RootInstance.LocaleFlags.enUS;
        public string? BaseDir;
        public string? BuildConfig;
        public string? CDNConfig;
        public string CacheDir = "cache";
        public bool ListfileFallback = true;
        public string ListfileURL = "https://github.com/wowdev/wow-listfile/releases/latest/download/community-listfile.csv";
    }
}
