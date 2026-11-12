using System.Text.Json;
using System.Text.Json.Serialization;
using TACTSharp.Interfaces;

namespace TACTSharp.VersionServices
{
    public class TACTChannels : IVersionService
    {
        private HttpClient client = new();

        private string LastCatalog = "";
        private string Path = "";

        private ChannelCatalog? Catalog;
        private Lock CatalogLock = new();

        public void Refresh()
        {
            var catalogDefinitionRequest = new HttpRequestMessage(HttpMethod.Get, "https://distribution.version.battle.net/summary");
            var catalogDefitionResult = client.Send(catalogDefinitionRequest);

            using (var ms = new MemoryStream())
            {
                catalogDefitionResult.Content.ReadAsStream().CopyTo(ms);

                var summary = JsonSerializer.Deserialize(ms.ToArray(), ChannelDefinitionContext.Default.ChannelDefinition);
                if (summary != null)
                {
                    Path = summary.Path;
                    var catalogHash = summary.PublicChannel.Channel;
                    if (catalogHash != null && catalogHash != LastCatalog)
                    {
                        var catalogRequest = new HttpRequestMessage(HttpMethod.Get, "https://distribution.version.battle.net/" + summary.Path + "/" + catalogHash[0..2] + "/" + catalogHash[2..4] + "/" + catalogHash);
                        var catalogResult = GetFile(catalogHash);
                        Catalog = JsonSerializer.Deserialize(catalogResult, ChannelCatalogContext.Default.ChannelCatalog);
                        LastCatalog = catalogHash;
                    }
                }
            }
        }

        public async Task<bool> RefreshAsync()
        {
            if (Settings.LogLevel <= TSLogLevel.Debug)
                Console.WriteLine("Refreshing TACT channels...");

            var catalogDefinitionResult = await client.GetStringAsync("https://distribution.version.battle.net/summary");
            var summary = JsonSerializer.Deserialize(catalogDefinitionResult, ChannelDefinitionContext.Default.ChannelDefinition);
            if (summary != null)
            {
                Path = summary.Path;
                var catalogHash = summary.PublicChannel.Channel;
                if (catalogHash != null && catalogHash != LastCatalog)
                {
                    var catalogResult = await GetFileAsync(catalogHash);
                    lock (CatalogLock)
                    {
                        Catalog = JsonSerializer.Deserialize(catalogResult, ChannelCatalogContext.Default.ChannelCatalog);
                        LastCatalog = catalogHash;
                    }
                }
            }

            return true;
        }

        public async Task<string> GetFileAsync(string hash, bool isFullPath = false)
        {
            if(Settings.LogLevel <= TSLogLevel.Debug)
                Console.WriteLine("Downloading TACT Channel file " + hash + "...");

            // TODO: Setting
            var cacheDir = "cache";

            Directory.CreateDirectory(cacheDir);

            var cacheFile = System.IO.Path.Combine(cacheDir, Path, hash[0..2], hash[2..4], hash);
            if(isFullPath)
                cacheFile = System.IO.Path.Combine(cacheDir, hash);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cacheFile)!);

            if (System.IO.File.Exists(cacheFile))
                return await System.IO.File.ReadAllTextAsync(cacheFile);

            var url = "https://distribution.version.battle.net/" + Path + "/" + hash[0..2] + "/" + hash[2..4] + "/" + hash;
            if(isFullPath)
                url = "https://distribution.version.battle.net/" + hash;

            var result = await client.GetStringAsync(url);
            await System.IO.File.WriteAllTextAsync(cacheFile, result);
            return result;
        }

        public string GetFile(string hash, bool isFullPath = false)
        {
            if(Settings.LogLevel <= TSLogLevel.Debug)
                Console.WriteLine("Downloading TACT Channel file " + hash + "...");

            // TODO: Setting
            var cacheDir = "cache";

            Directory.CreateDirectory(cacheDir);

            var cacheFile = System.IO.Path.Combine(cacheDir, Path, hash[0..2], hash[2..4], hash);
            if(isFullPath)
                cacheFile = System.IO.Path.Combine(cacheDir, hash);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cacheFile)!);

            if (System.IO.File.Exists(cacheFile))
                return System.IO.File.ReadAllText(cacheFile);

            var catalogRequest = new HttpRequestMessage(HttpMethod.Get, "https://distribution.version.battle.net/" + Path + "/" + hash[0..2] + "/" + hash[2..4] + "/" + hash);
            if(isFullPath)
                catalogRequest = new HttpRequestMessage(HttpMethod.Get, "https://distribution.version.battle.net/" + hash);

            var catalogResult = client.Send(catalogRequest);
            using (var catalogMS = new MemoryStream())
            {
                catalogResult.Content.ReadAsStream().CopyTo(catalogMS);
                var arr = catalogMS.ToArray();
                File.WriteAllBytes(cacheFile, arr);
                return System.Text.Encoding.UTF8.GetString(arr);
            }
        }

        public List<string> GetCDNs(string product, string region)
        {
            var cdns = new List<string>();

            if (Catalog == null)
                Refresh();

            var version = GetVersion(product, region);

            var productResults = Catalog!.Products.Where(p => p.Variant == product);
            if (!productResults.Any())
                return cdns;

            if (productResults.First().Builds == null || productResults.First().Builds!.Count == 0)
                return cdns;

            var buildRegionResults = productResults.First().Builds!.Where(r => r.Branches.Contains(region));
            if (!buildRegionResults.Any())
                return cdns;

            var buildDefinitionHash = buildRegionResults.First().Definition;
            var buildDefinitionJSON = GetChannelBuildDefinition(buildDefinitionHash);
            if (buildDefinitionJSON == null)
                return cdns;

            var regionResults = buildDefinitionJSON.Regions.Where(r => r.Branches.Contains(region));

            // If no region is found, fall back to the first one (e.g. wow_classic_titan might be requested for non-CN regions but only has CN CDNs)
            if (!regionResults.Any())
                regionResults = buildDefinitionJSON.Regions;

            if (!regionResults.Any())
                return cdns;

            var cdnDefinitionHash = regionResults.First().CDNs;
            var cdnDefinitionJSON = GetChannelCDNDefinition(cdnDefinitionHash);
            if (cdnDefinitionJSON == null)
                return cdns;

            // Disregard get params, Blizzard puts ?maxhosts=4 and other things in here
            return cdnDefinitionJSON.Hosts.Select(x => x.Host.Split('?')[0].Split("://")[1]).Distinct().ToList();
        }

        public async Task<List<string>> GetCDNsAsync(string product, string region)
        {
            var cdns = new List<string>();

            if (Catalog == null)
                await RefreshAsync();

            var productResults = Catalog!.Products.Where(p => p.Variant == product);
            if (!productResults.Any())
                return cdns;

            var buildRegionResults = productResults.First().Builds!.Where(r => r.Branches.Contains(region));
            if (!buildRegionResults.Any())
                return cdns;

            var buildDefinitionHash = buildRegionResults.First().Definition;
            var buildDefinitionJSON = await GetChannelBuildDefinitionAsync(buildDefinitionHash);
            if (buildDefinitionJSON == null)
                return cdns;

            var regionResults = buildDefinitionJSON.Regions.Where(r => r.Branches.Contains(region));

            if (!regionResults.Any())
                return cdns;

            var cdnDefinitionHash = regionResults.First().CDNs;
            var cdnDefinitionJSON = await GetChannelCDNDefinitionAsync(cdnDefinitionHash);
            if (cdnDefinitionJSON == null)
                return cdns;

            // Disregard get params, Blizzard puts ?maxhosts=4 and other things in here
            return cdnDefinitionJSON.Hosts.Select(x => x.Host.Split('?')[0].Split("://")[1]).Distinct().ToList();
        }

        public VersionConfigs GetVersion(string product, string region)
        {
            var versionResult = new VersionConfigs() { BuildConfig = "", CDNConfig = "", ProductConfig = "", VersionNumber = 0, VersionString = "" };

            if (Catalog == null)
                Refresh();

            var productResults = Catalog!.Products.Where(p => p.Variant == product);
            if (!productResults.Any())
                return versionResult;

            if (productResults.First().Builds == null || productResults.First().Builds!.Count == 0)
                return versionResult;

            var regionResults = productResults.First().Builds!.Where(r => r.Branches.Contains(region));
            if (!regionResults.Any())
                return versionResult;

            var buildDefinitionHash = regionResults.First().Definition;
            var buildDefinitionJSON = GetChannelBuildDefinition(buildDefinitionHash);
            if (buildDefinitionJSON == null)
                return versionResult;

            var cdnKeyFileJSON = GetChannelBuildCDNKey(buildDefinitionJSON.CDNKeyFile.Name);
            if (cdnKeyFileJSON == null)
                return versionResult;

            versionResult.BuildConfig = buildDefinitionJSON.BuildKey;
            versionResult.CDNConfig = cdnKeyFileJSON.CDNKey;
            versionResult.ProductConfig = buildDefinitionJSON.ProductConfig.Hash;
            versionResult.VersionNumber = buildDefinitionJSON.VersionNumber;
            versionResult.VersionString = buildDefinitionJSON.VersionString;

            return versionResult;
        }

        public async Task<VersionConfigs> GetVersionAsync(string product, string region)
        {
            var versionResult = new VersionConfigs() { BuildConfig = "", CDNConfig = "", ProductConfig = "", VersionNumber = 0, VersionString = "" };

            if (Catalog == null)
                await RefreshAsync();

            var productResults = Catalog!.Products.Where(p => p.Variant == product);
            if (!productResults.Any())
                return versionResult;

            if (productResults.First().Builds == null || productResults.First().Builds!.Count == 0)
                return versionResult;

            var regionResults = productResults.First().Builds!.Where(r => r.Branches.Contains(region));
            if (!regionResults.Any())
                return versionResult;

            var buildDefinitionHash = regionResults.First().Definition;
            var buildDefinitionJSON = await GetChannelBuildDefinitionAsync(buildDefinitionHash);
            if (buildDefinitionJSON == null)
                return versionResult;

            var cdnKeyFileJSON = await GetChannelBuildCDNKeyAsync(buildDefinitionJSON.CDNKeyFile.Name);
            if (cdnKeyFileJSON == null)
                return versionResult;

            versionResult.BuildConfig = buildDefinitionJSON.BuildKey;
            versionResult.CDNConfig = cdnKeyFileJSON.CDNKey;
            versionResult.ProductConfig = buildDefinitionJSON.ProductConfig.Hash;
            versionResult.VersionNumber = buildDefinitionJSON.VersionNumber;
            versionResult.VersionString = buildDefinitionJSON.VersionString;

            return versionResult;
        }

        public string GetCDNDirectory(string product)
        {
            if (Catalog == null)
                Refresh();

            var productResults = Catalog!.Products.Where(p => p.Variant == product);
            if (!productResults.Any())
                return "";

            if (productResults.First().Builds == null || productResults.First().Builds!.Count == 0)
                return "";

            var buildDefinitionHash = productResults.First().Builds!.First().Definition;
            var buildDefinitionJSON = GetChannelBuildDefinition(buildDefinitionHash);
            if (buildDefinitionJSON == null)
                return "";

            return buildDefinitionJSON.Path;
        }

        public async Task<string> GetCDNDirectoryAsync(string product)
        {
            if (Catalog == null)
                await RefreshAsync();

            var productResults = Catalog!.Products.Where(p => p.Variant == product);
            if (!productResults.Any())
                return "";

            if (productResults.First().Builds == null || productResults.First().Builds!.Count == 0)
                return "";

            var buildDefinitionHash = productResults.First().Builds!.First().Definition;
            var buildDefinitionJSON = await GetChannelBuildDefinitionAsync(buildDefinitionHash);
            if (buildDefinitionJSON == null)
                return "";

            return buildDefinitionJSON.Path;
        }

        public Dictionary<string, VersionConfigs> GetVersions(string product)
        {
            var versionResult = new Dictionary<string, VersionConfigs>();

            if (Catalog == null)
                Refresh();

            var productResults = Catalog!.Products.Where(p => p.Variant == product);
            if (!productResults.Any())
                return versionResult;

            foreach (var productResult in productResults)
            {
                if (productResult.Builds == null)
                    continue;

                foreach (var build in productResult.Builds)
                {
                    foreach (var branch in build.Branches)
                    {
                        var buildDefinitionHash = build.Definition;

                        var buildDefinitionJSON = GetChannelBuildDefinition(buildDefinitionHash);
                        if (buildDefinitionJSON == null)
                            continue;

                        if (buildDefinitionJSON.CDNKeyFile == null)
                            continue;

                        var cdnKeyFileJSON = GetChannelBuildCDNKey(buildDefinitionJSON.CDNKeyFile.Name);
                        if(cdnKeyFileJSON == null)
                            continue;

                        versionResult[branch] = new VersionConfigs()
                        {
                            BuildConfig = buildDefinitionJSON.BuildKey,
                            CDNConfig = cdnKeyFileJSON.CDNKey,
                            ProductConfig = buildDefinitionJSON.ProductConfig.Hash,
                            VersionNumber = buildDefinitionJSON.VersionNumber,
                            VersionString = buildDefinitionJSON.VersionString
                        };
                    }
                }
            }

            return versionResult;
        }

        public async Task<Dictionary<string, VersionConfigs>> GetVersionsAsync(string product)
        {
            var versionResult = new Dictionary<string, VersionConfigs>();

            if (Catalog == null)
                await RefreshAsync();

            var productResults = Catalog!.Products.Where(p => p.Variant == product);
            if (!productResults.Any())
                return versionResult;

            foreach (var productResult in productResults)
            {
                if (productResult.Builds == null)
                    continue;

                foreach (var build in productResult.Builds)
                {
                    foreach (var branch in build.Branches)
                    {
                        var buildDefinitionHash = build.Definition;

                        var buildDefinitionJSON = await GetChannelBuildDefinitionAsync(buildDefinitionHash);
                        if (buildDefinitionJSON == null)
                            continue;

                        var cdnKeyFileJSON = await GetChannelBuildCDNKeyAsync(buildDefinitionJSON.CDNKeyFile.Name);
                        if (cdnKeyFileJSON == null)
                            continue;

                        versionResult[branch] = new VersionConfigs()
                        {
                            BuildConfig = buildDefinitionJSON.BuildKey,
                            CDNConfig = cdnKeyFileJSON.CDNKey,
                            ProductConfig = buildDefinitionJSON.ProductConfig.Hash,
                            VersionNumber = buildDefinitionJSON.VersionNumber,
                            VersionString = buildDefinitionJSON.VersionString
                        };
                    }
                }
            }

            return versionResult;
        }

        public List<string> GetProductVariants()
        {
            var products = new List<string>();

            if (Catalog == null)
                Refresh();

            foreach (var product in Catalog!.Products)
            {
                if(product.Builds == null || product.Builds.Count == 0)
                    continue;

                if (!products.Contains(product.Variant))
                    products.Add(product.Variant);
            }

            return products;
        }

        public async Task<List<string>> GetProductVariantsAsync()
        {
            var products = new List<string>();

            if (Catalog == null)
                await RefreshAsync();

            foreach (var product in Catalog!.Products)
            {
                if (product.Builds == null || product.Builds.Count == 0)
                    continue;

                if (!products.Contains(product.Variant))
                    products.Add(product.Variant);
            }

            return products;
        }

        private ChannelBuildDefinition? GetChannelBuildDefinition(string buildDefinitionHash)
        {
            var buildDefinitionContent = GetFile(buildDefinitionHash);
            var buildDefinitionJSON = JsonSerializer.Deserialize(buildDefinitionContent, ChannelBuildDefinitionContext.Default.ChannelBuildDefinition);
            return buildDefinitionJSON;
        }

        private async Task<ChannelBuildDefinition?> GetChannelBuildDefinitionAsync(string buildDefinitionHash)
        {
            var buildDefinitionContent = await GetFileAsync(buildDefinitionHash);
            var buildDefinitionJSON = JsonSerializer.Deserialize(buildDefinitionContent, ChannelBuildDefinitionContext.Default.ChannelBuildDefinition);
            return buildDefinitionJSON;
        }

        private ChannelCDNDefinition? GetChannelCDNDefinition(string cdnDefinitionHash)
        {
            var cdnDefinitionContent = GetFile(cdnDefinitionHash);
            var cdnDefinitionJSON = JsonSerializer.Deserialize(cdnDefinitionContent, ChannelCDNDefinitionContext.Default.ChannelCDNDefinition);
            return cdnDefinitionJSON;
        }

        private async Task<ChannelCDNDefinition?> GetChannelCDNDefinitionAsync(string cdnDefinitionHash)
        {
            var cdnDefinitionContent = await GetFileAsync(cdnDefinitionHash);
            var cdnDefinitionJSON = JsonSerializer.Deserialize(cdnDefinitionContent, ChannelCDNDefinitionContext.Default.ChannelCDNDefinition);
            return cdnDefinitionJSON;
        }

        private ChannelBuildCDNKey? GetChannelBuildCDNKey(string cdnKeyFileHash)
        {
            var cdnKeyFileContent = GetFile(cdnKeyFileHash, true);
            var cdnKeyFileJSON = JsonSerializer.Deserialize(cdnKeyFileContent, ChannelBuildCDNKeyContext.Default.ChannelBuildCDNKey);
            return cdnKeyFileJSON;
        }

        private async Task<ChannelBuildCDNKey?> GetChannelBuildCDNKeyAsync(string cdnKeyFileHash)
        {
            var cdnKeyFileContent = await GetFileAsync(cdnKeyFileHash, true);
            var cdnKeyFileJSON = JsonSerializer.Deserialize(cdnKeyFileContent, ChannelBuildCDNKeyContext.Default.ChannelBuildCDNKey);
            return cdnKeyFileJSON;
        }
    }

    public class ChannelDefinition
    {
        [JsonPropertyName("path")]
        public required string Path { get; set; }

        [JsonPropertyName("public")]
        public required ChannelDefinitionChannel PublicChannel { get; set; }
    }

    public class ChannelDefinitionChannel
    {
        [JsonPropertyName("channel")]
        public required string Channel { get; set; }
    }

    [JsonSerializable(typeof(ChannelDefinition))]
    public partial class ChannelDefinitionContext : JsonSerializerContext
    {
    }

    public class ChannelCatalog
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("products")]
        public required List<ChannelProductDefinition> Products { get; set; }
    }

    public class ChannelProductDefinition
    {
        [JsonPropertyName("builds")]
        public List<ChannelProductBuild>? Builds { get; set; }

        // TODO: preloads
        // TODO: alternates
        // TODO: pipeline
        // TODO: subscription

        [JsonPropertyName("infrastructure")]
        public required bool Infrastructure { get; set; }

        [JsonPropertyName("product")]
        public required string Product { get; set; }

        [JsonPropertyName("variant")]
        public required string Variant { get; set; }
    }

    public class ChannelProductBuild
    {
        [JsonPropertyName("branches")]
        public required List<string> Branches { get; set; }

        [JsonPropertyName("definition")]
        public required string Definition { get; set; }
    }

    public class ChannelProductRegion
    {
        [JsonPropertyName("branches")]
        public required List<string> Branches { get; set; }

        [JsonPropertyName("cdns")]
        public required string CDNs { get; set; }
    }

    [JsonSerializable(typeof(ChannelCatalog))]
    public partial class ChannelCatalogContext : JsonSerializerContext
    {
    }

    public class ChannelBuildDefinition
    {
        [JsonPropertyName("armadilloKey")]
        public required string ArmadilloKey { get; set; }

        [JsonPropertyName("build")]
        public required ChannelBuildDefinitionBuild Build { get; set; }

        [JsonPropertyName("buildKey")]
        public required string BuildKey { get; set; }

        [JsonPropertyName("cdnKeyFile")]
        public required ChannelBuildCDNKeyFile CDNKeyFile { get; set; }

        [JsonPropertyName("keyRing")]
        public required string KeyRing { get; set; }

        [JsonPropertyName("path")]
        public required string Path { get; set; }

        [JsonPropertyName("productConfig")]
        public required ChannelBuildDefinitionProductConfig ProductConfig { get; set; }

        [JsonPropertyName("regions")]
        public required List<ChannelProductRegion> Regions { get; set; }

        [JsonPropertyName("versionNumber")]
        public required int VersionNumber { get; set; }

        [JsonPropertyName("versionString")]
        public required string VersionString { get; set; }
    }

    public class ChannelBuildDefinitionBuild
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("token")]
        public required string Token { get; set; }
    }

    public class ChannelBuildDefinitionProductConfig
    {
        // TODO: Encryption bits
        [JsonPropertyName("cdnPath")]
        public string? CDNPath { get; set; }

        [JsonPropertyName("hash")]
        public required string Hash { get; set; }
    }

    [JsonSerializable(typeof(ChannelBuildDefinition))]
    public partial class ChannelBuildDefinitionContext : JsonSerializerContext
    {
    }

    public class ChannelCDNDefinition
    {
        [JsonPropertyName("hosts")]
        public required List<ChannelCDNDefinitionHost> Hosts { get; set; }
    }

    public class ChannelCDNDefinitionHost
    {
        [JsonPropertyName("host")]
        public required string Host { get; set; }
    }

    [JsonSerializable(typeof(ChannelCDNDefinition))]
    public partial class ChannelCDNDefinitionContext : JsonSerializerContext
    {
    }

    public class ChannelBuildCDNKeyFile
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }
    }

    public class ChannelBuildCDNKey
    {
        [JsonPropertyName("cdnKey")]
        public required string CDNKey { get; set; }
    }

    [JsonSerializable(typeof(ChannelBuildCDNKey))]
    public partial class ChannelBuildCDNKeyContext : JsonSerializerContext
    {
    }
}
