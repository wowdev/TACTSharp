using System.Diagnostics;

namespace TACTIndexTestCSharp
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("\tTACTIndexTestCSharp <product>");
                Console.WriteLine("\tTACTIndexTestCSharp <buildconfig(path)> <cdnconfig(path)>");
                Console.WriteLine("Press enter to exit.");
                Console.ReadLine();
                return;
            }

            Config? buildConfig = null;
            Config? cdnConfig = null;

            if (args.Length == 1)
            {
                Console.WriteLine("Using product " + args[0]);
                var versions = await CDN.GetProductVersions(args[0]);
                foreach (var line in versions.Split('\n'))
                {
                    // TODO: Configurable?
                    if (!line.StartsWith("us|"))
                        continue;

                    var splitLine = line.Split('|');
                    if (splitLine.Length < 2)
                        continue;

                    Console.WriteLine("Using buildconfig " + splitLine[1] + " and cdnconfig " + splitLine[2]);
                    buildConfig = new Config(splitLine[1], false);
                    cdnConfig = new Config(splitLine[2], false);
                }
            }
            else if (args.Length == 2)
            {
                Console.WriteLine("Using buildconfig " + args[0] + " and cdnconfig " + args[1]);

                if (File.Exists(args[0]))
                    buildConfig = new Config(args[0], true);
                else if (args[0].Length == 32 && args[0].All(c => "0123456789abcdef".Contains(c)))
                    buildConfig = new Config(args[0], false);
                else
                    throw new Exception("Invalid buildconfig(path)");

                if (File.Exists(args[1]))
                    cdnConfig = new Config(args[1], true);
                else if (args[1].Length == 32 && args[1].All(c => "0123456789abcdef".Contains(c)))
                    cdnConfig = new Config(args[1], false);
                else
                    throw new Exception("Invalid buildconfig(path)");
            }
            else
            {
                throw new Exception("Invalid number of arguments");
            }

            if (buildConfig == null || cdnConfig == null)
                throw new Exception("Failed to load configs");

            if (!buildConfig.Values.TryGetValue("encoding", out var encodingKey))
                throw new Exception("No encoding key found in build config");

            if (!cdnConfig.Values.TryGetValue("archive-group", out var groupArchiveIndex))
                throw new Exception("No archive group found in cdn config");

            var totalTimer = new Stopwatch();
            totalTimer.Start();

            var eTimer = new Stopwatch();
            eTimer.Start();
            var encodingPath = await CDN.GetDecodedFilePath("wow", "data", encodingKey[1], ulong.Parse(buildConfig.Values["encoding-size"][1]), ulong.Parse(buildConfig.Values["encoding-size"][0]));
            eTimer.Stop();
            Console.WriteLine("Retrieved encoding in " + eTimer.Elapsed.TotalMilliseconds + "ms");

            eTimer.Restart();
            var encoding = new EncodingInstance(encodingPath);
            eTimer.Stop();
            Console.WriteLine("Loaded encoding in " + eTimer.Elapsed.TotalMilliseconds + "ms");

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

            var groupIndexPath = Path.Combine("cache", "wow", "data", groupArchiveIndex[0] + ".index");
            if (!File.Exists(groupIndexPath))
                GroupIndex.Generate(groupArchiveIndex[0], cdnConfig.Values["archives"]);

            var gaSW = new Stopwatch();
            gaSW.Start();
            var groupIndex = new IndexInstance(groupIndexPath);
            gaSW.Stop();
            Console.WriteLine("Loaded group index in " + gaSW.Elapsed.TotalMilliseconds + "ms");

            Directory.CreateDirectory("output");

            var extractionTargets = new List<(uint fileDataID, string fileName)>();
            foreach (var line in File.ReadAllLines("extract.txt"))
            {
                var parts = line.Split(';');
                if (parts.Length != 2)
                    continue;

                extractionTargets.Add((uint.Parse(parts[0]), parts[1]));
            }

            eTimer.Restart();

            Parallel.ForEach(extractionTargets, (target) =>
            {
                var (fileDataID, fileName) = target;
                var fileEntry = rootInstance.GetEntryByFDID(fileDataID);
                if (fileEntry == null)
                {
                    Console.WriteLine("FileDataID " + fileDataID + " not found in root, skipping extraction..");
                    return;
                }

                if (!encoding.TryGetEKeys(fileEntry.Value.md5, out var fileEKeys) || fileEKeys == null)
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
