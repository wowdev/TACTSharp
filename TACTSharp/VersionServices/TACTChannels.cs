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

        public async Task<string> GetFileAsync(string hash)
        {
            // TODO: Caching

            return await client.GetStringAsync("https://distribution.version.battle.net/" + Path + "/" + hash[0..2] + "/" + hash[2..4] + "/" + hash);
        }

        public string GetFile(string hash)
        {
            // TODO: Caching

            var catalogRequest = new HttpRequestMessage(HttpMethod.Get, "https://distribution.version.battle.net/" + Path + "/" + hash[0..2] + "/" + hash[2..4] + "/" + hash);
            var catalogResult = client.Send(catalogRequest);
            using (var catalogMS = new MemoryStream())
            {
                catalogResult.Content.ReadAsStream().CopyTo(catalogMS);
                return System.Text.Encoding.UTF8.GetString(catalogMS.ToArray());
            }
        }

        public List<string> GetCDNs(string product, string region)
        {
            var cdns = new List<string>();

            if (Catalog == null)
                Refresh();

            var productResults = Catalog!.Products.Where(p => p.Variant == product);
            if (!productResults.Any())
                return cdns;

            var regionResults = productResults.First().Regions.Where(r => r.Branches.Contains(region));

            // If no region is found, fall back to the first one (e.g. wow_classic_titan might be requested for non-CN regions but only has CN CDNs)
            if (!regionResults.Any())
                regionResults = productResults.First().Regions;

            if (!regionResults.Any())
                return cdns;

            var cdnDefinitionHash = regionResults.First().CDNs;
            var cdnDefinitionContent = GetFile(cdnDefinitionHash);
            var cdnDefinitionJSON = JsonSerializer.Deserialize(cdnDefinitionContent, ChannelCDNDefinitionContext.Default.ChannelCDNDefinition);
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

            var regionResults = productResults.First().Regions.Where(r => r.Branches.Contains(region));

            // If no region is found, fall back to the first one (e.g. wow_classic_titan might be requested for non-CN regions but only has CN CDNs)
            if (!regionResults.Any())
                regionResults = productResults.First().Regions;

            if (!regionResults.Any())
                return cdns;

            var cdnDefinitionHash = regionResults.First().CDNs;
            var cdnDefinitionContent = await GetFileAsync(cdnDefinitionHash);
            var cdnDefinitionJSON = JsonSerializer.Deserialize(cdnDefinitionContent, ChannelCDNDefinitionContext.Default.ChannelCDNDefinition);
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

            var regionResults = productResults.First().Builds.Where(r => r.Branches.Contains(region));
            if (!regionResults.Any())
                return versionResult;

            var buildDefinitionHash = regionResults.First().Definition;
            var buildDefinitionContent = GetFile(buildDefinitionHash);
            var buildDefinitionJSON = JsonSerializer.Deserialize(buildDefinitionContent, ChannelBuildDefinitionContext.Default.ChannelBuildDefinition);
            if (buildDefinitionJSON == null)
                return versionResult;

            versionResult.BuildConfig = buildDefinitionJSON.BuildKey;
            versionResult.CDNConfig = buildDefinitionJSON.CDNKey;
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

            var regionResults = productResults.First().Builds.Where(r => r.Branches.Contains(region));
            if (!regionResults.Any())
                return versionResult;

            var buildDefinitionHash = regionResults.First().Definition;
            var buildDefinitionContent = await GetFileAsync(buildDefinitionHash);
            var buildDefinitionJSON = JsonSerializer.Deserialize(buildDefinitionContent, ChannelBuildDefinitionContext.Default.ChannelBuildDefinition);
            if (buildDefinitionJSON == null)
                return versionResult;

            versionResult.BuildConfig = buildDefinitionJSON.BuildKey;
            versionResult.CDNConfig = buildDefinitionJSON.CDNKey;
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

            var buildDefinitionHash = productResults.First().Builds.First().Definition;
            var buildDefinitionContent = GetFile(buildDefinitionHash);
            var buildDefinitionJSON = JsonSerializer.Deserialize(buildDefinitionContent, ChannelBuildDefinitionContext.Default.ChannelBuildDefinition);
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

            var buildDefinitionHash = productResults.First().Builds.First().Definition;
            var buildDefinitionContent = await GetFileAsync(buildDefinitionHash);
            var buildDefinitionJSON = JsonSerializer.Deserialize(buildDefinitionContent, ChannelBuildDefinitionContext.Default.ChannelBuildDefinition);
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
                foreach (var build in productResult.Builds)
                {
                    foreach (var branch in build.Branches)
                    {
                        var buildDefinitionHash = build.Definition;

                        var buildDefinitionContent = GetFile(buildDefinitionHash);
                        var buildDefinitionJSON = JsonSerializer.Deserialize(buildDefinitionContent, ChannelBuildDefinitionContext.Default.ChannelBuildDefinition);
                        if (buildDefinitionJSON == null)
                            continue;

                        versionResult[branch] = new VersionConfigs()
                        {
                            BuildConfig = buildDefinitionJSON.BuildKey,
                            CDNConfig = buildDefinitionJSON.CDNKey,
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
                foreach (var build in productResult.Builds)
                {
                    foreach (var branch in build.Branches)
                    {
                        var buildDefinitionHash = build.Definition;

                        var buildDefinitionContent = await GetFileAsync(buildDefinitionHash);
                        var buildDefinitionJSON = JsonSerializer.Deserialize(buildDefinitionContent, ChannelBuildDefinitionContext.Default.ChannelBuildDefinition);
                        if (buildDefinitionJSON == null)
                            continue;

                        versionResult[branch] = new VersionConfigs()
                        {
                            BuildConfig = buildDefinitionJSON.BuildKey,
                            CDNConfig = buildDefinitionJSON.CDNKey,
                            ProductConfig = buildDefinitionJSON.ProductConfig.Hash,
                            VersionNumber = buildDefinitionJSON.VersionNumber,
                            VersionString = buildDefinitionJSON.VersionString
                        };
                    }
                }
            }

            return versionResult;
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
        public required List<ChannelProductBuild> Builds { get; set; }

        // TODO: preloads
        // TODO: alternates
        // TODO: pipeline
        // TODO: subscription

        [JsonPropertyName("infrastructure")]
        public required bool Infrastructure { get; set; }

        [JsonPropertyName("product")]
        public required string Product { get; set; }

        [JsonPropertyName("regions")]
        public required List<ChannelProductRegion> Regions { get; set; }

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

        [JsonPropertyName("cdnKey")]
        public required string CDNKey { get; set; }

        [JsonPropertyName("keyRing")]
        public required string KeyRing { get; set; }

        [JsonPropertyName("path")]
        public required string Path { get; set; }

        [JsonPropertyName("pipelineId")]
        public required int PipelineId { get; set; }

        [JsonPropertyName("productConfig")]
        public required ChannelBuildDefinitionProductConfig ProductConfig { get; set; }

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
        public required string CDNPath { get; set; }

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
}
