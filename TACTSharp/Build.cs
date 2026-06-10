using System.Text.Json;
using System.Text.Json.Nodes;
using TACTSharp.Interfaces;

namespace TACTSharp
{
    public class BuildInstance
    {
        public Config? BuildConfig { get; private set; }
        public Config? CDNConfig { get; private set; }
        public string? ProductConfig { get; private set; }

        public EncodingInstance? Encoding { get; private set; }
        public IRootInstance? Root { get; private set; }
        public InstallInstance? Install { get; private set; }
        public IndexInstance? GroupIndex { get; private set; }
        public IndexInstance? FileIndex { get; private set; }

        public CDN cdn { get; private set; }

        public Settings Settings { get; private set; } = new Settings();

        public BuildInstance()
        {
            cdn = new(Settings);
        }

        public void ResetCDN() => cdn = new(Settings);

        public void LoadConfigs(string buildConfig, string cdnConfig)
        {
            Settings.BuildConfig = buildConfig;
            Settings.CDNConfig = cdnConfig;

            var timer = new System.Diagnostics.Stopwatch();
            timer.Start();

            if (!cdn.HasLocal && !string.IsNullOrEmpty(Settings.BaseDir))
                cdn.OpenLocal();

            if (File.Exists(buildConfig))
                BuildConfig = new Config(cdn, buildConfig, true);
            else if (buildConfig.Length == 32 && buildConfig.All(c => "0123456789abcdef".Contains(c)))
                BuildConfig = new Config(cdn, buildConfig, false);
            else
                throw new Exception("Invalid build config, must be a file path or a 32 character hex string");

            if (File.Exists(cdnConfig))
                CDNConfig = new Config(cdn, cdnConfig, true);
            else if (cdnConfig.Length == 32 && cdnConfig.All(c => "0123456789abcdef".Contains(c)))
                CDNConfig = new Config(cdn, cdnConfig, false);
            else
                throw new Exception("Invalid CDN config, must be a file path or a 32 character hex string");

            if (BuildConfig == null || CDNConfig == null)
                throw new Exception("Failed to load configs");
            timer.Stop();
            if (Settings.LogLevel <= TSLogLevel.Info)
                Console.WriteLine("Configs loaded in " + Math.Ceiling(timer.Elapsed.TotalMilliseconds) + "ms");
        }

        public void LoadConfigs(string buildConfig, string cdnConfig, string productConfig)
        {
            Settings.ProductConfig = productConfig;

            var timer = new System.Diagnostics.Stopwatch();
            timer.Start();

            if (!cdn.HasLocal && !string.IsNullOrEmpty(Settings.BaseDir))
                cdn.OpenLocal();

            ProductConfig = cdn.GetProductConfig(productConfig);

            if (ProductConfig == null)
                throw new Exception("Failed to load product config");

            try
            {
                var parsedJSON = JsonSerializer.Deserialize<JsonNode>(ProductConfig);
                if (parsedJSON == null || parsedJSON["all"] == null || parsedJSON["all"]!["config"] == null)
                    throw new Exception("Product config has missing keys");

                if (parsedJSON["all"]!["config"]!["decryption_key_name"] != null && !string.IsNullOrEmpty(parsedJSON["all"]!["config"]!["decryption_key_name"]!.GetValue<string>()))
                {
                    cdn.ArmadilloKeyName = parsedJSON["all"]!["config"]!["decryption_key_name"]!.GetValue<string>();
                    if (Settings.LogLevel <= TSLogLevel.Info)
                        Console.WriteLine("Set Armadillo key name to " + cdn.ArmadilloKeyName);
                }
            }
            catch (Exception ex)
            {
                if (Settings.LogLevel <= TSLogLevel.Warn)
                    Console.WriteLine("Failed to parse product config: " + ex.Message);
            }

            timer.Stop();

            if (Settings.LogLevel <= TSLogLevel.Info)
                Console.WriteLine("Product config loaded in " + Math.Ceiling(timer.Elapsed.TotalMilliseconds) + "ms");

            LoadConfigs(buildConfig, cdnConfig);
        }

        public void Load()
        {
            if (BuildConfig == null || CDNConfig == null)
                throw new Exception("Configs not loaded");

            var timer = new System.Diagnostics.Stopwatch();

            if (!cdn.HasLocal && !string.IsNullOrEmpty(Settings.BaseDir))
                cdn.OpenLocal();

            timer.Start();
            if (!CDNConfig.Values.TryGetValue("archive-group", out var groupArchiveIndex))
            {
                if (Settings.LogLevel <= TSLogLevel.Info)
                    Console.WriteLine("No group index found in CDN config, generating fresh group index...");
                var groupIndex = new GroupIndex();
                var groupIndexHash = groupIndex.Generate(cdn, Settings, "", CDNConfig.Values["archives"]);
                var groupIndexPath = Path.Combine(Settings.CacheDir, cdn.ProductDirectory, "data", groupIndexHash + ".index");
                GroupIndex = new IndexInstance(groupIndexPath);
            }
            else
            {
                if (!string.IsNullOrEmpty(Settings.BaseDir) && File.Exists(Path.Combine(Settings.BaseDir, "Data", "indices", groupArchiveIndex[0] + ".index")))
                {
                    GroupIndex = new IndexInstance(Path.Combine(Settings.BaseDir, "Data", "indices", groupArchiveIndex[0] + ".index"));
                }
                else
                {
                    var groupIndexPath = Path.Combine(Settings.CacheDir, cdn.ProductDirectory, "data", groupArchiveIndex[0] + ".index");
                    if (!File.Exists(groupIndexPath))
                    {
                        var groupIndex = new GroupIndex();
                        groupIndex.Generate(cdn, Settings, groupArchiveIndex[0], CDNConfig.Values["archives"], true);
                    }
                    GroupIndex = new IndexInstance(groupIndexPath);
                }
            }
            timer.Stop();
            if (Settings.LogLevel <= TSLogLevel.Info)
                Console.WriteLine("Group index loaded in " + Math.Ceiling(timer.Elapsed.TotalMilliseconds) + "ms");

            ulong decodedEncodingSize = 0;
            ulong encodedEncodingSize = 0;
            if (BuildConfig.Values.TryGetValue("encoding-size", out var bcEncodingSize))
            {
                decodedEncodingSize = ulong.Parse(bcEncodingSize[0]);
                encodedEncodingSize = ulong.Parse(bcEncodingSize[1]);
            }

            timer.Restart();
            Encoding = new EncodingInstance(cdn.GetDecodedFilePath("data", BuildConfig.Values["encoding"][1], encodedEncodingSize, decodedEncodingSize), (int)decodedEncodingSize);
            timer.Stop();
            if (Settings.LogLevel <= TSLogLevel.Info)
                Console.WriteLine("Encoding loaded in " + Math.Ceiling(timer.Elapsed.TotalMilliseconds) + "ms");

            timer.Restart();
            if (CDNConfig.Values.TryGetValue("file-index", out var fileIndex))
            {
                if (!string.IsNullOrEmpty(Settings.BaseDir) && File.Exists(Path.Combine(Settings.BaseDir, "Data", "indices", fileIndex[0] + ".index")))
                {
                    FileIndex = new IndexInstance(Path.Combine(Settings.BaseDir, "Data", "indices", fileIndex[0] + ".index"));
                }
                else
                {
                    try
                    {
                        var fileIndexPath = cdn.GetFilePath("data", fileIndex[0] + ".index");
                        FileIndex = new IndexInstance(fileIndexPath);

                    }
                    catch (Exception e)
                    {
                        if (Settings.LogLevel <= TSLogLevel.Warn)
                            Console.WriteLine("Failed to load file index: " + e.Message);
                    }
                }

                timer.Stop();
                if (Settings.LogLevel <= TSLogLevel.Info)
                    Console.WriteLine("File index loaded in " + Math.Ceiling(timer.Elapsed.TotalMilliseconds) + "ms");
            }
            else
            {
                // TODO: We might want to manually build up file-index based on encoding entries not present in indexes.
                if (Settings.LogLevel <= TSLogLevel.Info)
                    Console.WriteLine("No file index found in CDN config, skipping file index loading.");
            }

            timer.Restart();

            var useTVFS = Settings.ManifestType == AssetManifestType.TVFS;
            if (!BuildConfig.Values.TryGetValue("root", out var rootKey) || rootKey[0] == "00000000000000000000000000000000")
            {
                if (Settings.LogLevel <= TSLogLevel.Info)
                    Console.WriteLine("No root key found in build config, falling back to TVFS.");

                useTVFS = true;
            }

            if (!useTVFS)
            {
                var rootEncodingKeys = Encoding.FindContentKey(Convert.FromHexString(rootKey[0]));
                if (!rootEncodingKeys)
                    throw new Exception("Root key not found in encoding");

                Root = new RootInstance(cdn.GetDecodedFilePath("data", Convert.ToHexStringLower(rootEncodingKeys[0]), 0, rootEncodingKeys.DecodedFileSize), Settings);

                if (Settings.LogLevel <= TSLogLevel.Info)
                    Console.WriteLine("Root loaded in " + Math.Ceiling(timer.Elapsed.TotalMilliseconds) + "ms");
            }
            else
            {
                var vfsKeys = new Dictionary<uint, (string cKey, string eKey)>();
                var vfsSizes = new Dictionary<uint, (uint decodedSize, uint encodedSize)>();

                foreach (var bcKey in BuildConfig.Values)
                {
                    if (bcKey.Key.StartsWith("vfs") && !bcKey.Key.StartsWith("vfs-root")) // vfs-1 always seems to be the same as vfs-root
                    {
                        if (bcKey.Key.EndsWith("-size"))
                        {
                            if (uint.TryParse(bcKey.Key.Replace("-size", "").Replace("vfs-", ""), out var vfsIndex))
                                vfsSizes.Add(vfsIndex, (uint.Parse(bcKey.Value[0]), uint.Parse(bcKey.Value[1])));
                            else
                                if (Settings.LogLevel <= TSLogLevel.Warn)
                                    Console.WriteLine("Warning: Failed to parse buildconfig VFS size " + bcKey.Key + ", skipping..");
                        }
                        else
                        {
                            if (uint.TryParse(bcKey.Key.Replace("vfs-", ""), out var vfsIndex))
                                vfsKeys.Add(vfsIndex, (bcKey.Value[0], bcKey.Value[1]));
                            else
                                if (Settings.LogLevel <= TSLogLevel.Warn)
                                    Console.WriteLine("Warning: Failed to parse buildconfig VFS key " + bcKey.Key + ", skipping..");
                        }
                    }
                }

                // TODO: Not great to feed all this stuff into it
                Root = new TVFSInstance(vfsKeys, vfsSizes, CDNConfig, GroupIndex, FileIndex, cdn, Settings);

                if (Settings.LogLevel <= TSLogLevel.Info)
                    Console.WriteLine("TVFS loaded in " + Math.Ceiling(timer.Elapsed.TotalMilliseconds) + "ms");
            }
          
            timer.Stop();
         

            timer.Restart();
            if (!BuildConfig.Values.TryGetValue("install", out var installKey))
                throw new Exception("No root key found in build config");

            var installEncodingKeys = Encoding.FindContentKey(Convert.FromHexString(installKey[0]));
            if (!installEncodingKeys)
                throw new Exception("Install key not found in encoding");

            Install = new InstallInstance(cdn.GetDecodedFilePath("data", Convert.ToHexStringLower(installEncodingKeys[0]), 0, installEncodingKeys.DecodedFileSize));
            timer.Stop();

            if (Settings.LogLevel <= TSLogLevel.Info)
                Console.WriteLine("Install loaded in " + Math.Ceiling(timer.Elapsed.TotalMilliseconds) + "ms");
        }

        public byte[] OpenFileByFDID(uint fileDataID)
        {
            if (Root == null)
                throw new Exception("Root not loaded");

            var fileData = Root.GetEntriesByFDID(fileDataID);
            if (fileData.Count == 0)
                throw new Exception("File not found in root");

            return OpenFileByCKey(fileData[0].md5.AsSpan());
        }

        public byte[] OpenFileByCKey(string cKey) => OpenFileByCKey(Convert.FromHexString(cKey));

        public byte[] OpenFileByCKey(ReadOnlySpan<byte> cKey)
        {
            if (Encoding == null)
                throw new Exception("Encoding not loaded");

            var encodingResult = Encoding.FindContentKey(cKey);
            if (encodingResult.Length == 0)
                throw new Exception("File not found in encoding");

            return OpenFileByEKey(encodingResult[0], encodingResult.DecodedFileSize);
        }

        public byte[] OpenFileByEKey(string eKey, ulong decodedSize = 0) => OpenFileByEKey(Convert.FromHexString(eKey), decodedSize);

        public byte[] OpenFileByEKey(ReadOnlySpan<byte> eKey, ulong decodedSize = 0)
        {
            if (GroupIndex == null)
                throw new Exception("Indexes not loaded");

            var (offset, size, archiveIndex) = GroupIndex.GetIndexInfo(eKey);
            if (offset == -1)
            {
                if (FileIndex != null)
                {
                    var fileIndexEntry = FileIndex.GetIndexInfo(eKey);
                    if (fileIndexEntry.size != -1)
                        return cdn.GetFile("data", Convert.ToHexStringLower(eKey), (ulong)fileIndexEntry.size, decodedSize, true);
                }

                if (Settings.LogLevel <= TSLogLevel.Warn)
                    Console.WriteLine("Warning: EKey " + Convert.ToHexStringLower(eKey) + " not found in group or file index and might not be available on CDN.");

                return cdn.GetFile("data", Convert.ToHexStringLower(eKey), 0, decodedSize, true);
            }
            else
            {
                return cdn.GetFileFromArchive(Convert.ToHexStringLower(eKey), CDNConfig!.Values["archives"][archiveIndex], offset, size, decodedSize, true);
            }
        }
    }
}
