using TACTSharp.Interfaces;

namespace TACTSharp.VersionServices
{
    public class Ribbit : IVersionService
    {
        private readonly HttpClient Client = new();

        private readonly Dictionary<string, string> RibbitCache = new();

        public List<string> GetCDNs(string product, string region)
        {
            var cdns = new List<string>();

            if (!RibbitCache.TryGetValue("v2/products/" + product + "/cdns", out var cdnString))
            {
                var cdnsRequest = new HttpRequestMessage(HttpMethod.Get, $"https://{region}.version.battle.net/v2/products/{product}/cdns");
                var cdnsResponse = Client.Send(cdnsRequest);
                cdnString = "";

                using (var ms = new MemoryStream())
                {
                    cdnsResponse.Content.ReadAsStream().CopyTo(ms);
                    cdnString = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                }

                RibbitCache["v2/products/" + product + "/cdns"] = cdnString;
            }

            foreach (var line in cdnString.Split('\n'))
            {
                if (line.Length == 0)
                    continue;

                if (!line.StartsWith(region + "|"))
                    continue;

                var splitLine = line.Split('|');
                if (splitLine.Length < 2)
                    continue;

                cdns.AddRange(splitLine[2].Trim().Split(' '));
            }

            return cdns;
        }

        public async Task<List<string>> GetCDNsAsync(string product, string region)
        {
            var cdns = new List<string>();

            if(!RibbitCache.TryGetValue("v2/products/" + product + "/cdns", out var cdnString))
            {
                cdnString = await Client.GetStringAsync($"https://{region}.version.battle.net/v2/products/{product}/cdns");
                RibbitCache["v2/products/" + product + "/cdns"] = cdnString;
            }

            foreach (var line in cdnString.Split('\n'))
            {
                if (line.Length == 0)
                    continue;

                if (!line.StartsWith(region + "|"))
                    continue;

                var splitLine = line.Split('|');
                if (splitLine.Length < 2)
                    continue;

                cdns.AddRange(splitLine[2].Trim().Split(' '));
            }

            return cdns;
        }

        public string GetCDNDirectory(string product)
        {
            var cdns = new List<string>();

            if (!RibbitCache.TryGetValue("v2/products/" + product + "/cdns", out var cdnString))
            {
                var cdnsRequest = new HttpRequestMessage(HttpMethod.Get, $"https://us.version.battle.net/v2/products/{product}/cdns");
                var cdnsResponse = Client.Send(cdnsRequest);
                cdnString = "";

                using (var ms = new MemoryStream())
                {
                    cdnsResponse.Content.ReadAsStream().CopyTo(ms);
                    cdnString = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                }
            }

            foreach (var line in cdnString.Split('\n'))
            {
                if (line.Length == 0)
                    continue;

                if (line.StartsWith('#') || line.StartsWith("Name"))
                    continue;

                var splitLine = line.Split('|');
                if (splitLine.Length < 2)
                    continue;

                return splitLine[1].Trim();
            }

            return "";
        }

        public async Task<string> GetCDNDirectoryAsync(string product)
        {
            var cdns = new List<string>();

            if(!RibbitCache.TryGetValue("v2/products/" + product + "/cdns", out var cdnString))
            {
                cdnString = await Client.GetStringAsync($"https://us.version.battle.net/v2/products/{product}/cdns");
                RibbitCache["v2/products/" + product + "/cdns"] = cdnString;
            }

            foreach (var line in cdnString.Split('\n'))
            {
                if (line.Length == 0)
                    continue;

                if (line.StartsWith('#') || line.StartsWith("Name"))
                    continue;

                var splitLine = line.Split('|');
                if (splitLine.Length < 2)
                    continue;

                return splitLine[1].Trim();
            }

            return "";
        }

        public VersionConfigs GetVersion(string product, string region)
        {
            var version = new VersionConfigs() { BuildConfig = "", CDNConfig = "", ProductConfig = "", VersionNumber = 0, VersionString = "" };

            if(!RibbitCache.TryGetValue("v2/products/" + product + "/versions", out var versionsString))
            {
                var versionsRequest = new HttpRequestMessage(HttpMethod.Get, $"https://{region}.version.battle.net/v2/products/{product}/versions");
                var versionsResponse = Client.Send(versionsRequest);
                versionsString = "";

                using (var ms = new MemoryStream())
                {
                    versionsResponse.Content.ReadAsStream().CopyTo(ms);
                    versionsString = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                }

                RibbitCache["v2/products/" + product + "/versions"] = versionsString;
            }

            foreach (var line in versionsString.Split('\n'))
            {
                if (line.Length == 0)
                    continue;

                if (!line.StartsWith(region + "|"))
                    continue;

                var splitLine = line.Split('|');
                if (splitLine.Length < 6)
                    continue;

                version.BuildConfig = splitLine[1];
                version.CDNConfig = splitLine[2];

                version.VersionNumber = int.Parse(splitLine[4]);
                version.VersionString = splitLine[5];
                version.ProductConfig = splitLine[6];
            }

            return version;
        }

        public async Task<VersionConfigs> GetVersionAsync(string product, string region)
        {
            var version = new VersionConfigs() { BuildConfig = "", CDNConfig = "", ProductConfig = "", VersionNumber = 0, VersionString = "" };

            if(!RibbitCache.TryGetValue("v2/products/" + product + "/versions", out var versionsString))
            {
                versionsString = await Client.GetStringAsync($"https://{region}.version.battle.net/v2/products/{product}/versions");
                RibbitCache["v2/products/" + product + "/versions"] = versionsString;
            }

            foreach (var line in versionsString.Split('\n'))
            {
                if (line.Length == 0)
                    continue;

                if (!line.StartsWith(region + "|"))
                    continue;

                var splitLine = line.Split('|');
                if (splitLine.Length < 6)
                    continue;

                version.BuildConfig = splitLine[1];
                version.CDNConfig = splitLine[2];

                version.VersionNumber = int.Parse(splitLine[4]);
                version.VersionString = splitLine[5];
                version.ProductConfig = splitLine[6];
            }

            return version;
        }

        public Dictionary<string, VersionConfigs> GetVersions(string product)
        {
            var versions = new Dictionary<string, VersionConfigs>();

            if(!RibbitCache.TryGetValue("v2/products/" + product + "/versions", out var versionsString))
            {
                var versionsRequest = new HttpRequestMessage(HttpMethod.Get, $"https://us.version.battle.net/v2/products/{product}/versions");
                var versionsResponse = Client.Send(versionsRequest);
                versionsString = "";
                using (var ms = new MemoryStream())
                {
                    versionsResponse.Content.ReadAsStream().CopyTo(ms);
                    versionsString = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                }

                RibbitCache["v2/products/" + product + "/versions"] = versionsString;
            }

            foreach (var line in versionsString.Split('\n'))
            {
                if (line.Length == 0)
                    continue;

                if (line.StartsWith('#') || line.StartsWith("Region"))
                    continue;

                var splitLine = line.Split('|');
                if (splitLine.Length < 6)
                    continue;

                var version = new VersionConfigs() { BuildConfig = "", CDNConfig = "", ProductConfig = "", VersionNumber = 0, VersionString = "" };

                version.BuildConfig = splitLine[1];
                version.CDNConfig = splitLine[2];

                version.VersionNumber = int.Parse(splitLine[4]);
                version.VersionString = splitLine[5];
                version.ProductConfig = splitLine[6];

                versions[splitLine[0]] = version;
            }

            return versions;
        }

        public async Task<Dictionary<string, VersionConfigs>> GetVersionsAsync(string product)
        {
            var versions = new Dictionary<string, VersionConfigs>();

            if(!RibbitCache.TryGetValue("v2/products/" + product + "/versions", out var versionsString))
            {
                versionsString = await Client.GetStringAsync($"https://us.version.battle.net/v2/products/{product}/versions");
                RibbitCache["v2/products/" + product + "/versions"] = versionsString;
            }

            foreach (var line in versionsString.Split('\n'))
            {
                if (line.Length == 0)
                    continue;

                if (line.StartsWith('#') || line.StartsWith("Region"))
                    continue;

                var splitLine = line.Split('|');
                if (splitLine.Length < 6)
                    continue;

                var version = new VersionConfigs() { BuildConfig = "", CDNConfig = "", ProductConfig = "", VersionNumber = 0, VersionString = "" };

                version.BuildConfig = splitLine[1];
                version.CDNConfig = splitLine[2];

                version.VersionNumber = int.Parse(splitLine[4]);
                version.VersionString = splitLine[5];
                version.ProductConfig = splitLine[6];

                versions[splitLine[0]] = version;
            }

            return versions;
        }


        public List<string> GetProductVariants()
        {
            var products = new List<string>();

            if(!RibbitCache.TryGetValue("v2/summary", out var summaryString))
            {
                var summaryRequest = new HttpRequestMessage(HttpMethod.Get, $"https://us.version.battle.net/v2/summary");
                var summaryResponse = Client.Send(summaryRequest);
                summaryString = "";
                using (var ms = new MemoryStream())
                {
                    summaryResponse.Content.ReadAsStream().CopyTo(ms);
                    summaryString = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                }
                RibbitCache["v2/summary"] = summaryString;
            }

            foreach (var summaryLine in summaryString.Split('\n'))
            {
                if (summaryLine.StartsWith('#') || summaryLine.StartsWith("Product") || string.IsNullOrWhiteSpace(summaryLine))
                    continue;

                var product = summaryLine.Split('|');

                // Skip products with no versions
                if (product[2] != "")
                    continue;

                products.Add(product[0]);
            }

            return products;
        }

        public async Task<List<string>> GetProductVariantsAsync()
        {
            var products = new List<string>();

            if(!RibbitCache.TryGetValue("v2/summary", out var summaryString))
            {
                summaryString = await Client.GetStringAsync($"https://us.version.battle.net/v2/summary");
                RibbitCache["v2/summary"] = summaryString;
            }

            foreach (var summaryLine in summaryString.Split('\n'))
            {
                if (summaryLine.StartsWith('#') || summaryLine.StartsWith("Product") || string.IsNullOrWhiteSpace(summaryLine))
                    continue;

                var product = summaryLine.Split('|');

                // Skip products with no versions
                if (product[2] != "")
                    continue;

                products.Add(product[0]);
            }

            return products;
        }

        public void Refresh()
        {
            RibbitCache.Clear();
        }

        public async Task<bool> RefreshAsync()
        {
            RibbitCache.Clear();
            return true;
        }
    }
}
