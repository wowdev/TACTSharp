namespace TACTSharp.Interfaces
{
    public interface IVersionService
    {
        public void Refresh();
        public Task<bool> RefreshAsync();

        public List<string> GetProductVariants();
        public Task<List<string>> GetProductVariantsAsync();

        public Dictionary<string, VersionConfigs> GetVersions(string product);
        public Task<Dictionary<string, VersionConfigs>> GetVersionsAsync(string product);

        public VersionConfigs GetVersion(string product, string region);
        public Task<VersionConfigs> GetVersionAsync(string product, string region);

        public List<string> GetCDNs(string product, string region);
        public Task<List<string>> GetCDNsAsync(string product, string region);

        public string GetCDNDirectory(string product);
        public Task<string> GetCDNDirectoryAsync(string product);
    }

    public class VersionConfigs
    {
        public required string BuildConfig;
        public required string CDNConfig;
        public required string ProductConfig;
        public required int VersionNumber;
        public required string VersionString;
    }
}
