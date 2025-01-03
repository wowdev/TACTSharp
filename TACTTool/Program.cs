using System.CommandLine;
using System.Diagnostics;
using System.IO;
using TACTSharp;

namespace TACTTool
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var rootCommand = new RootCommand("TACTTool - Extraction tool using the TACTSharp library");

            var modeCommand = new Option<string>("--source", () => Settings.Source, "Data source: online or local");
            modeCommand.AddAlias("-s");
            rootCommand.AddOption(modeCommand);
            
            var productOption = new Option<string?>(name: "--product", () => Settings.Product, description: "TACT product to load");
            productOption.AddAlias("-p");
            rootCommand.AddOption(productOption);

            var regionOption = new Option<string?>(name: "--region", () => Settings.Region, description: "Region to use for patch service/build selection/CDNs");
            rootCommand.AddOption(regionOption);

            var baseDirOption = new Option<string?>(name: "--basedir", () => Settings.BaseDir, description: "WoW installation folder (if available)");
            rootCommand.AddOption(baseDirOption);

            var buildConfigOption = new Option<string?>(name: "--buildconfig", description: "Build config to load (hex or file on disk)");
            rootCommand.AddOption(buildConfigOption);

            var cdnConfigOption = new Option<string?>(name: "--cdnconfig", description: "CDN config to load (hex or file on disk)");
            rootCommand.AddOption(cdnConfigOption);
      
            rootCommand.SetHandler(async (product, buildConfig, cdnConfig, region, baseDir) =>
            {
                if (region != null)
                    Settings.Region = region;

                if (buildConfig != null)
                    Settings.BuildConfig = buildConfig;

                if (cdnConfig != null)
                    Settings.CDNConfig = cdnConfig;

                if (product != null)
                {
                    Settings.Product = product;

                    if(baseDir != null)
                    {
                        Settings.BaseDir = baseDir;
                        Settings.Source = "local";

                        // Load from build.info
                        var buildInfoPath = Path.Combine(baseDir, ".build.info");
                        if(!File.Exists(buildInfoPath))
                            throw new Exception("No build.info found in base directory");

                        var buildInfo = new BuildInfo(buildInfoPath);

                        if(!buildInfo.Entries.Any(x => x.Product == product))
                            throw new Exception("No build found for product " + product);

                        var build = buildInfo.Entries.First(x => x.Product == product);

                        if(buildConfig == null)
                            Settings.BuildConfig = build.BuildConfig;

                        if (cdnConfig == null)
                            Settings.CDNConfig = build.CDNConfig;
                    }
                    else
                    {
                        var versions = await CDN.GetProductVersions(product);
                        foreach (var line in versions.Split('\n'))
                        {
                            if (!line.StartsWith(Settings.Region + "|"))
                                continue;

                            var splitLine = line.Split('|');

                            if (buildConfig == null)
                                Settings.BuildConfig = splitLine[1];

                            if (cdnConfig == null)
                                Settings.CDNConfig = splitLine[2];
                        }
                    }
                }
            }, productOption, buildConfigOption, cdnConfigOption, regionOption, baseDirOption);

            await rootCommand.InvokeAsync(args);

            #region Configs
            Config? buildConfig = null;
            if (File.Exists(Settings.BuildConfig))
                buildConfig = new Config(Settings.BuildConfig, true);
            else if (Settings.BuildConfig.Length == 32 && Settings.BuildConfig.All(c => "0123456789abcdef".Contains(c)))
                buildConfig = new Config(Settings.BuildConfig, false);

            Config? cdnConfig = null;
            if (File.Exists(Settings.CDNConfig))
                cdnConfig = new Config(Settings.CDNConfig, true);
            else if (Settings.CDNConfig.Length == 32 && Settings.BuildConfig.All(c => "0123456789abcdef".Contains(c)))
                cdnConfig = new Config(Settings.CDNConfig, false);


            if (buildConfig == null || cdnConfig == null)
                throw new Exception("Failed to load configs");

            if (!buildConfig.Values.TryGetValue("encoding", out var encodingKey))
                throw new Exception("No encoding key found in build config");

            if (!cdnConfig.Values.TryGetValue("archive-group", out var groupArchiveIndex))
                throw new Exception("No archive group found in cdn config");
            #endregion

            var totalTimer = new Stopwatch();
            totalTimer.Start();

            #region Encoding
            var eTimer = new Stopwatch();
            eTimer.Start();
            var encodingPath = await CDN.GetDecodedFilePath("wow", "data", encodingKey[1], ulong.Parse(buildConfig.Values["encoding-size"][1]), ulong.Parse(buildConfig.Values["encoding-size"][0]));
            eTimer.Stop();
            Console.WriteLine("Retrieved encoding in " + eTimer.Elapsed.TotalMilliseconds + "ms");

            eTimer.Restart();
            var encoding = new EncodingInstance(encodingPath);
            eTimer.Stop();
            Console.WriteLine("Loaded encoding in " + eTimer.Elapsed.TotalMilliseconds + "ms");
            #endregion

            #region Root
            if (!buildConfig.Values.TryGetValue("root", out var rootKey))
                throw new Exception("No root key found in build config");

            var root = Convert.FromHexString(rootKey[0]);
            eTimer.Restart();
            if (!encoding.TryGetEKeys(root, out var rootEKeys) || rootEKeys == null)
                throw new Exception("Root key not found in encoding");
            eTimer.Stop();

            var rootEKey = Convert.ToHexStringLower(rootEKeys.Value.eKeys[0]);

            eTimer.Restart();
            var rootPath = await CDN.GetDecodedFilePath("wow", "data", rootEKey, 0, rootEKeys.Value.decodedFileSize);
            eTimer.Stop();
            Console.WriteLine("Retrieved root in " + eTimer.Elapsed.TotalMilliseconds + "ms");

            eTimer.Restart();
            var rootInstance = new RootInstance(rootPath);
            eTimer.Stop();
            Console.WriteLine("Loaded root in " + eTimer.Elapsed.TotalMilliseconds + "ms");
            #endregion

            #region GroupIndex
            var groupIndexPath = Path.Combine("cache", "wow", "data", groupArchiveIndex[0] + ".index");
            if (!File.Exists(groupIndexPath))
                GroupIndex.Generate(groupArchiveIndex[0], cdnConfig.Values["archives"]);

            var gaSW = new Stopwatch();
            gaSW.Start();
            var groupIndex = new IndexInstance(groupIndexPath);
            gaSW.Stop();
            Console.WriteLine("Loaded group index in " + gaSW.Elapsed.TotalMilliseconds + "ms");
            #endregion

            #region Install
            if (!buildConfig.Values.TryGetValue("install", out var installKey))
                throw new Exception("No root key found in build config");

            if (!encoding.TryGetEKeys(Convert.FromHexString(installKey[0]), out var installEKeys) || installEKeys == null)
                throw new Exception("Install key not found in encoding");
            var installEKey = Convert.ToHexStringLower(installEKeys.Value.eKeys[0]);

            eTimer.Restart();
            var installPath = await CDN.GetDecodedFilePath("wow", "data", installEKey, 0, installEKeys.Value.decodedFileSize);
            eTimer.Stop();
            Console.WriteLine("Retrieved install in " + eTimer.Elapsed.TotalMilliseconds + "ms");

            eTimer.Restart();
            var installInstance = new InstallInstance(installPath);
            eTimer.Stop();
            Console.WriteLine("Loaded install in " + eTimer.Elapsed.TotalMilliseconds + "ms");
            #endregion

            if (!File.Exists("extract.txt"))
            {
                Console.WriteLine("No extract.txt found, skipping extraction..");
                return;
            }

            var extractionTargets = new List<(uint fileDataID, string fileName)>();
            foreach (var line in File.ReadAllLines("extract.txt"))
            {
                var parts = line.Split(';');
                if (parts.Length == 2)
                    extractionTargets.Add((uint.Parse(parts[0]), parts[1]));
                else
                    extractionTargets.Add((0, parts[0])); // Assume install
            }

            eTimer.Restart();

            Directory.CreateDirectory("output");

            Parallel.ForEach(extractionTargets, (target) =>
            {
                var (fileDataID, fileName) = target;

                byte[] targetCKey;

                if (fileDataID == 0)
                {
                    var fileEntries = installInstance.Entries.Where(x => x.name.Equals(fileName, StringComparison.InvariantCultureIgnoreCase)).ToList();
                    if (fileEntries.Count == 0)
                    {
                        Console.WriteLine("No results found in install for file " + fileName + ", skipping extraction..");
                        return;
                    }

                    if (fileEntries.Count > 1)
                    {
                        var filter = fileEntries.Where(x => x.tags.Contains("4=US")).Select(x => x.md5);
                        if (filter.Any())
                        {
                            Console.WriteLine("Multiple results found in install for file " + fileName + ", using US version..");
                            targetCKey = filter.First();
                        }
                        else
                        {
                            Console.WriteLine("Multiple results found in install for file " + fileName + ", using first result..");
                            targetCKey = fileEntries[0].md5;
                        }
                    }
                    else
                    {
                        targetCKey = fileEntries[0].md5;
                    }
                }
                else
                {
                    var fileEntry = rootInstance.GetEntryByFDID(fileDataID);
                    if (fileEntry == null)
                    {
                        Console.WriteLine("FileDataID " + fileDataID + " not found in root, skipping extraction..");
                        return;
                    }

                    targetCKey = fileEntry.Value.md5;
                }

                if (!encoding.TryGetEKeys(targetCKey, out var fileEKeys) || fileEKeys == null)
                    throw new Exception("EKey not found in encoding");

                var (offset, size, archiveIndex) = groupIndex.GetIndexInfo(fileEKeys.Value.eKeys[0]);
                byte[] fileBytes;
                if (offset == -1)
                    fileBytes = CDN.GetFile("wow", "data", Convert.ToHexStringLower(fileEKeys.Value.eKeys[0]), 0, fileEKeys.Value.decodedFileSize, true).Result;
                else
                    fileBytes = CDN.GetFileFromArchive(Convert.ToHexStringLower(fileEKeys.Value.eKeys[0]), "wow", cdnConfig.Values["archives"][archiveIndex], offset, size, fileEKeys.Value.decodedFileSize, true).Result;

                Directory.CreateDirectory(Path.Combine("output", Path.GetDirectoryName(fileName)!));

                File.WriteAllBytes(Path.Combine("output", fileName), fileBytes);
            });

            eTimer.Stop();

            Console.WriteLine("Extracted " + extractionTargets.Count + " files in " + eTimer.Elapsed.TotalMilliseconds + "ms (average of " + Math.Round(eTimer.Elapsed.TotalMilliseconds / extractionTargets.Count, 5) + "ms per file)");

            totalTimer.Stop();
            Console.WriteLine("Total time: " + totalTimer.Elapsed.TotalMilliseconds + "ms");
        }
    }
}