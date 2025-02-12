using System.Collections.Concurrent;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using TACTSharp;

namespace TACTTool
{
    internal class Program
    {
        private enum InputMode
        {
            List,
            EKey,
            CKey,
            FDID,
            FileName
        };

        private static InputMode? Mode;
        private static string? Input;
        private static string? Output;
        private static readonly ConcurrentBag<(byte[] eKey, ulong decodedSize, string fileName)> extractionTargets = [];
        private static BuildInstance build;
        private static Listfile listfile = new();

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

            var productOption = new Option<string?>(name: "--product", () => "wow", description: "TACT product to load");
            productOption.AddAlias("-p");
            rootCommand.AddOption(productOption);

            var regionOption = new Option<string?>(name: "--region", () => "us", description: "Region to use for patch service/build selection/CDNs");
            regionOption.AddAlias("-r");
            rootCommand.AddOption(regionOption);

            var localeOption = new Option<string?>(name: "--locale", () => "enUS", description: "Locale to use for file retrieval");
            localeOption.AddAlias("-l");
            rootCommand.AddOption(localeOption);

            var inputModeOption = new Option<string>("--mode", "Input mode: list, ekey (or ehash), ckey (or chash), id (or fdid), name (or filename)");
            inputModeOption.AddAlias("-m");
            rootCommand.AddOption(inputModeOption);

            var inputValueOption = new Option<string>("--inputvalue", "Input value for extraction");
            inputValueOption.AddAlias("-i");
            rootCommand.AddOption(inputValueOption);

            var outputDirOption = new Option<string>("--output", "Output path for extracted files, folder for list mode (defaults to 'extract' folder), output filename for other input modes (defaults to input value as filename)");
            outputDirOption.AddAlias("-o");
            rootCommand.AddOption(outputDirOption);

            var baseDirOption = new Option<string?>(name: "--basedir", description: "WoW installation folder to use as source for build info and read-only file cache (if available)");
            baseDirOption.AddAlias("-d");
            rootCommand.AddOption(baseDirOption);

            rootCommand.SetHandler(CommandLineArgHandler);

            build = new BuildInstance();

            await rootCommand.InvokeAsync(args);

            if (build.Settings.BuildConfig == null || build.Settings.CDNConfig == null)
            {
                Console.WriteLine("Missing build or CDN config, exiting..");
                return;
            }

            build.LoadConfigs(build.Settings.BuildConfig, build.Settings.CDNConfig);

            #endregion

            if (Input == null)
            {
                Console.WriteLine("No input given, skipping extraction..");
                return;
            }

            var buildTimer = new Stopwatch();
            var totalTimer = new Stopwatch();
            totalTimer.Start();

            buildTimer.Start();
            build.Load();
            buildTimer.Stop();
            Console.WriteLine("Build " + build.BuildConfig.Values["build-name"][0] + " loaded in " + Math.Ceiling(buildTimer.Elapsed.TotalMilliseconds) + "ms");

            if (build.Encoding == null || build.Root == null || build.Install == null || build.GroupIndex == null)
                throw new Exception("Failed to load build");

            // Handle input modes
            switch (Mode)
            {
                case InputMode.List:
                    HandleList(build, Input);
                    break;
                case InputMode.EKey:
                    HandleEKey(Input, Output);
                    break;
                case InputMode.CKey:
                    HandleCKey(build, Input, Output);
                    break;
                case InputMode.FDID:
                    HandleFDID(build, Input, Output);
                    break;
                case InputMode.FileName:
                    HandleFileName(build, Input, Output);
                    break;
            }

            if (extractionTargets.IsEmpty)
            {
                Console.WriteLine("No files to extract, exiting..");
                return;
            }

            Console.WriteLine("Extracting " + extractionTargets.Count + " file" + (extractionTargets.Count > 1 ? "s" : "") + "..");

            Parallel.ForEach(extractionTargets, target =>
            {
                var (eKey, decodedSize, fileName) = target;

                fileName = fileName.Replace('\\', Path.DirectorySeparatorChar);
                fileName = fileName.Replace('/', Path.DirectorySeparatorChar);

                Console.WriteLine("Extracting " + Convert.ToHexStringLower(eKey) + " to " + fileName);

                try
                {
                    var fileBytes = build.OpenFileByEKey(eKey, decodedSize);

                    if (Mode == InputMode.List)
                    {
                        Directory.CreateDirectory(Path.Combine(Output, Path.GetDirectoryName(fileName)!));
                        File.WriteAllBytes(Path.Combine(Output, fileName), fileBytes);
                    }
                    else
                    {
                        var dirName = Path.GetDirectoryName(fileName);
                        if (!string.IsNullOrEmpty(dirName))
                            Directory.CreateDirectory(dirName);

                        File.WriteAllBytes(fileName, fileBytes);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to extract " + fileName + " (" + Convert.ToHexStringLower(eKey) + "): " + e.Message);
                    return;
                }
            });

            totalTimer.Stop();
            Console.WriteLine("Total time: " + totalTimer.Elapsed.TotalMilliseconds + "ms");
        }

        private static async Task CommandLineArgHandler(InvocationContext context)
        {
            var command = context.ParseResult.CommandResult;
            var modeOption = command.Command.Options.FirstOrDefault(option => option.Name == "mode");
            foreach (var option in command.Command.Options)
            {
                var optionValue = command.GetValueForOption(option);
                if (optionValue == null)
                    continue;

                switch (option.Name)
                {
                    case "buildconfig":
                        build.Settings.BuildConfig = (string)optionValue;
                        break;
                    case "cdnconfig":
                        build.Settings.CDNConfig = (string)optionValue;
                        break;
                    case "region":
                        build.Settings.Region = (string)optionValue;
                        break;
                    case "product":
                        build.Settings.Product = (string)optionValue;
                        break;
                    case "locale":
                        build.Settings.Locale = ((string)optionValue).ToLower() switch
                        {
                            "dede" => RootInstance.LocaleFlags.deDE,
                            "enus" => RootInstance.LocaleFlags.enUS,
                            "engb" => RootInstance.LocaleFlags.enGB,
                            "ruru" => RootInstance.LocaleFlags.ruRU,
                            "zhcn" => RootInstance.LocaleFlags.zhCN,
                            "zhtw" => RootInstance.LocaleFlags.zhTW,
                            "entw" => RootInstance.LocaleFlags.enTW,
                            "eses" => RootInstance.LocaleFlags.esES,
                            "esmx" => RootInstance.LocaleFlags.esMX,
                            "frfr" => RootInstance.LocaleFlags.frFR,
                            "itit" => RootInstance.LocaleFlags.itIT,
                            "kokr" => RootInstance.LocaleFlags.koKR,
                            "ptbr" => RootInstance.LocaleFlags.ptBR,
                            "ptpt" => RootInstance.LocaleFlags.ptPT,
                            _ => throw new Exception("Invalid locale. Available locales: deDE, enUS, enGB, ruRU, zhCN, zhTW, enTW, esES, esMX, frFR, itIT, koKR, ptBR, ptPT"),
                        };
                        break;
                    case "basedir":
                        build.Settings.BaseDir = (string)optionValue;
                        break;
                    case "inputvalue":
                        Input = (string)optionValue;
                        break;
                    case "output":
                        Output = (string)optionValue;
                        break;
                    case "mode":
                        Mode = ((string)optionValue).ToLower() switch
                        {
                            "list" => InputMode.List,
                            "ehash" => InputMode.EKey,
                            "ekey" => InputMode.EKey,
                            "chash" => InputMode.CKey,
                            "ckey" => InputMode.CKey,
                            "id" => InputMode.FDID,
                            "fdid" => InputMode.FDID,
                            "install" => InputMode.FileName,
                            "filename" => InputMode.FileName,
                            "name" => InputMode.FileName,
                            _ => throw new Exception("Invalid input mode. Available modes: list, ekey/ehash, ckey/chash, fdid/id, filename/name"),
                        };
                        break;

                    case "version":
                    case "help":
                        break;
                    default:
                        Console.WriteLine("Unhandled command line option " + option.Name);
                        break;
                }
            }

            if (Mode == null)
            {
                Console.WriteLine("No input mode specified. Available modes: list, ekey, ckey, fdid, filename. Run with -h or --help for more information.");
                return;
            }

            if (Mode == InputMode.List)
                Output ??= "extract";

            if (build.Settings.BaseDir != null)
            {
                // Load from build.info
                var buildInfoPath = Path.Combine(build.Settings.BaseDir, ".build.info");
                if (!File.Exists(buildInfoPath))
                    throw new Exception("No build.info found in base directory, is this a valid WoW installation?");

                var buildInfo = new BuildInfo(buildInfoPath, build.Settings, build.cdn);

                if (!buildInfo.Entries.Any(x => x.Product == build.Settings.Product))
                    throw new Exception("No build found for product " + build.Settings.Product + " in .build.info, are you sure this product is installed?");

                var buildInfoEntry = buildInfo.Entries.First(x => x.Product == build.Settings.Product);

                build.Settings.BuildConfig ??= buildInfoEntry.BuildConfig;
                build.Settings.CDNConfig ??= buildInfoEntry.CDNConfig;
            }
            else
            {
                // Load from patch service
                var versions = await build.cdn.GetProductVersions(build.Settings.Product);
                foreach (var line in versions.Split('\n'))
                {
                    if (!line.StartsWith(build.Settings.Region + "|"))
                        continue;

                    var splitLine = line.Split('|');

                    build.Settings.BuildConfig ??= splitLine[1];
                    build.Settings.CDNConfig ??= splitLine[2];
                }
            }
        }

        private static void HandleList(BuildInstance build, string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("Input file list " + path + " not found, skipping extraction..");
                return;
            }

            Parallel.ForEach(File.ReadAllLines(path), (line) =>
            {
                var parts = line.Split(';');
                if (parts[0] == "ckey" || parts[0] == "chash")
                {
                    HandleCKey(build, parts[1], parts.Length == 3 ? parts[2] : null);
                }
                else if (parts[0] == "ekey" || parts[0] == "ehash")
                {
                    HandleEKey(parts[1], parts.Length == 3 ? parts[2] : null);
                }
                else if (parts[0] == "install")
                {
                    HandleFileName(build, parts[1], parts.Length == 3 ? parts[2] : null);
                }
                else if (uint.TryParse(parts[0], out var fileDataID))
                {
                    HandleFDID(build, parts[0], parts.Length == 2 ? parts[1] : null);
                }
                else if (parts.Length == 1 || parts.Length == 2)
                {
                    // Assume filename
                    HandleFileName(build, parts[0], parts.Length == 2 ? parts[1] : parts[0]);
                }
            });
        }

        private static void HandleEKey(string eKey, string? filename)
        {
            if (eKey.Length != 32 || !eKey.All("0123456789abcdef".Contains))
            {
                Console.WriteLine("Skipping " + eKey + ", invalid formatting for EKey (expected 32 character hex string).");
                return;
            }

            extractionTargets.Add((Convert.FromHexString(eKey), 0, !string.IsNullOrEmpty(filename) ? filename : eKey));
        }

        private static void HandleCKey(BuildInstance build, string cKey, string? filename)
        {
            if (cKey.Length != 32 || !cKey.All("0123456789abcdef".Contains))
            {
                Console.WriteLine("Skipping " + cKey + ", invalid formatting for CKey (expected 32 character hex string).");
                return;
            }

            var cKeyBytes = Convert.FromHexString(cKey);
            var fileEncodingKeys = build.Encoding.FindContentKey(cKeyBytes);
            if (!fileEncodingKeys)
            {
                Console.WriteLine("Skipping " + cKey + ", CKey not found in encoding.");
                return;
            }

            extractionTargets.Add((fileEncodingKeys[0].ToArray(), fileEncodingKeys.DecodedFileSize, !string.IsNullOrEmpty(filename) ? filename : cKey));
        }

        private static void HandleFDID(BuildInstance build, string fdid, string? filename)
        {
            if (!uint.TryParse(fdid, out var fileDataID))
            {
                Console.WriteLine("Skipping FDID " + fdid + ", invalid input format (expected unsigned integer).");
                return;
            }

            var fileEntry = build.Root.GetEntryByFDID(fileDataID);
            if (fileEntry == null)
            {
                Console.WriteLine("Skipping FDID " + fdid + ", not found in root.");
                return;
            }

            var fileEncodingKeys = build.Encoding.FindContentKey(fileEntry.Value.md5.AsSpan());
            if (!fileEncodingKeys)
            {
                Console.WriteLine("Skipping FDID " + fdid + ", CKey not found in encoding.");
                return;
            }

            extractionTargets.Add((fileEncodingKeys[0].ToArray(), fileEncodingKeys.DecodedFileSize, !string.IsNullOrEmpty(filename) ? filename : fdid));
        }

        private static void HandleFileName(BuildInstance build, string filename, string? outputFilename)
        {
            var fileEntries = build.Install.Entries.Where(x => x.name.Equals(filename.Replace('/', '\\'), StringComparison.InvariantCultureIgnoreCase)).ToList();
            if (fileEntries.Count == 0)
            {
                using (var hasher = new Jenkins96())
                {
                    var entryByLookup = build.Root.GetEntryByLookup(hasher.ComputeHash(filename, true));
                    if (entryByLookup != null)
                    {
                        HandleCKey(build, Convert.ToHexStringLower(entryByLookup.Value.md5.AsSpan()), filename);
                        return;
                    }
                }

                if (build.Settings.ListfileFallback)
                {
                    Console.WriteLine("No file by name \"" + filename + "\" found in install. Checking listfile.");
                    if (!listfile.Initialized)
                        listfile.Initialize(build.cdn, build.Settings);

                    var listfileID = listfile.GetFDID(filename);
                    if (listfileID == 0)
                    {
                        Console.WriteLine("No file by name \"" + filename + "\" found in listfile. Skipping..");
                        return;
                    }

                    HandleFDID(build, listfileID.ToString(), filename);
                    return;
                }
                else
                {
                    Console.WriteLine("No file by name \"" + filename + "\" found in install and listfile fallback is disabled. Skipping..");
                    return;
                }
            }

            byte[] targetCKey;
            if (fileEntries.Count > 1)
            {
                var filter = fileEntries.Where(x => x.tags.Contains("4=US")).Select(x => x.md5);
                if (filter.Any())
                {
                    Console.WriteLine("Multiple results found in install for file " + filename + ", using US version..");
                    targetCKey = filter.First();
                }
                else
                {
                    Console.WriteLine("Multiple results found in install for file " + filename + ", using first result..");
                    targetCKey = fileEntries[0].md5;
                }
            }
            else
            {
                targetCKey = fileEntries[0].md5;
            }

            var fileEncodingKeys = build.Encoding.FindContentKey(targetCKey);
            if (!fileEncodingKeys)
                throw new Exception("EKey not found in encoding");

            extractionTargets.Add((fileEncodingKeys[0].ToArray(), fileEncodingKeys.DecodedFileSize, !string.IsNullOrEmpty(outputFilename) ? outputFilename : filename));
        }
    }
}