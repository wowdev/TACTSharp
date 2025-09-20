﻿namespace TACTSharp
{
    public class Settings
    {
        public string Region = "us";
        public string Product = "wow";
        public RootInstance.LocaleFlags Locale = RootInstance.LocaleFlags.enUS;
        public RootInstance.LoadMode RootMode = RootInstance.LoadMode.Normal;
        public string? BaseDir;
        public string? BuildConfig;
        public string? CDNConfig;
        public string? ProductConfig;
        public string CacheDir = "cache";
        public string CDNDir = "";
        public bool ListfileFallback = true;
        public bool TryCDN = true;
        public string ListfileURL = "https://github.com/wowdev/wow-listfile/releases/latest/download/community-listfile.csv";
        public List<string> AdditionalCDNs = [];
    }
}
