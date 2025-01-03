using System.CommandLine;
using System.Diagnostics;
using TACTSharp;

namespace TACTTool
{
    internal class Program
    {
        private static string OutputDir = "output";

        static async Task Main(string[] args)
        {
            #region CLI switches
            var rootCommand = new RootCommand("TACTTool - Extraction tool using the TACTSharp library");

            var buildConfigOption = new Option<string?>(name: "--buildconfig", description: "Build config to load (hex or file on disk)");
            buildConfigOption.AddAlias("-b");
            rootCommand.AddOption(buildConfigOption);

            var cdnConfigOption = new Option<string?>(name: "--cdnconfig", description: "CDN config to load (hex or file on disk)");
            cdnConfigOption.AddAlias("-c");
            rootCommand.AddOption(cdnConfigOption);

            var modeCommand = new Option<string>("--source", () => Settings.Source, "Data source: online or local");
            modeCommand.AddAlias("-s");
            rootCommand.AddOption(modeCommand);

            var productOption = new Option<string?>(name: "--product", () => Settings.Product, description: "TACT product to load");
            productOption.AddAlias("-p");
            rootCommand.AddOption(productOption);

            var regionOption = new Option<string?>(name: "--region", () => Settings.Region, description: "Region to use for patch service/build selection/CDNs");
            regionOption.AddAlias("-r");
            rootCommand.AddOption(regionOption);

            var outputDirOption = new Option<string>("--output", () => OutputDir, "Output directory for extracted files");
            outputDirOption.AddAlias("-o");
            rootCommand.AddOption(outputDirOption);

            var baseDirOption = new Option<string?>(name: "--basedir", description: "WoW installation folder (if available) (NYI)");
            rootCommand.AddOption(baseDirOption);

            rootCommand.SetHandler(async (product, buildConfig, cdnConfig, region, baseDir, outputDirectory) =>
            {
                if (region != null)
                    Settings.Region = region;

                if (buildConfig != null)
                    Settings.BuildConfig = buildConfig;

                if (cdnConfig != null)
                    Settings.CDNConfig = cdnConfig;

                if (outputDirectory != null)
                    OutputDir = outputDirectory;

                if (product != null)
                {
                    Settings.Product = product;

                    if (baseDir != null)
                    {
                        Settings.BaseDir = baseDir;
                        Settings.Source = "local";

                        // Load from build.info
                        var buildInfoPath = Path.Combine(baseDir, ".build.info");
                        if (!File.Exists(buildInfoPath))
                            throw new Exception("No build.info found in base directory");

                        var buildInfo = new BuildInfo(buildInfoPath);

                        if (!buildInfo.Entries.Any(x => x.Product == product))
                            throw new Exception("No build found for product " + product);

                        var build = buildInfo.Entries.First(x => x.Product == product);

                        if (buildConfig == null)
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
            }, productOption, buildConfigOption, cdnConfigOption, regionOption, baseDirOption, outputDirOption);

            await rootCommand.InvokeAsync(args);

            if(Settings.BuildConfig == null || Settings.CDNConfig == null)
            {
                Console.WriteLine("Missing build or CDN config, exiting..");
                return;
            }
            #endregion

            var buildTimer = new Stopwatch();
            var totalTimer = new Stopwatch();
            totalTimer.Start();

            if (!File.Exists("extract.txt"))
            {
                Console.WriteLine("No extract.txt found, skipping extraction..");
                return;
            }

            buildTimer.Start();
            var build = new BuildInstance(Settings.BuildConfig, Settings.CDNConfig);
            await build.Load();
            buildTimer.Stop();
            Console.WriteLine("Build " + build.BuildConfig.Values["build-name"][0] + " loaded in " + Math.Ceiling(buildTimer.Elapsed.TotalMilliseconds) + "ms");

            if (build.Encoding == null || build.Root == null || build.Install == null || build.GroupIndex == null)
                throw new Exception("Failed to load build");

            var extractionTargets = new List<(uint fileDataID, string fileName)>();
            foreach (var line in File.ReadAllLines("extract.txt"))
            {
                var parts = line.Split(';');
                if (parts.Length == 2)
                    extractionTargets.Add((uint.Parse(parts[0]), parts[1]));
                else
                    extractionTargets.Add((0, parts[0])); // Assume install
            }

            Parallel.ForEach(extractionTargets, (target) =>
            {
                var (fileDataID, fileName) = target;

                byte[] targetCKey;

                if (fileDataID == 0)
                {
                    var fileEntries = build.Install.Entries.Where(x => x.name.Equals(fileName, StringComparison.InvariantCultureIgnoreCase)).ToList();
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
                    var fileEntry = build.Root.GetEntryByFDID(fileDataID);
                    if (fileEntry == null)
                    {
                        Console.WriteLine("FileDataID " + fileDataID + " not found in root, skipping extraction..");
                        return;
                    }

                    targetCKey = fileEntry.Value.md5;
                }

                if (!build.Encoding.TryGetEKeys(targetCKey, out var fileEKeys) || fileEKeys == null)
                    throw new Exception("EKey not found in encoding");

                var (offset, size, archiveIndex) = build.GroupIndex.GetIndexInfo(fileEKeys.Value.eKeys[0]);
                byte[] fileBytes;
                if (offset == -1)
                    fileBytes = CDN.GetFile("wow", "data", Convert.ToHexStringLower(fileEKeys.Value.eKeys[0]), 0, fileEKeys.Value.decodedFileSize, true).Result;
                else
                    fileBytes = CDN.GetFileFromArchive(Convert.ToHexStringLower(fileEKeys.Value.eKeys[0]), "wow", build.CDNConfig.Values["archives"][archiveIndex], offset, size, fileEKeys.Value.decodedFileSize, true).Result;

                Directory.CreateDirectory(Path.Combine(OutputDir, Path.GetDirectoryName(fileName)!));

                File.WriteAllBytes(Path.Combine(OutputDir, fileName), fileBytes);
            });

            totalTimer.Stop();
            Console.WriteLine("Total time: " + totalTimer.Elapsed.TotalMilliseconds + "ms");
        }
    }
}