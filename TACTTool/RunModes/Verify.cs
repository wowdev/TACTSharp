using System.Security.Cryptography;
using TACTSharp;

namespace TACTTool.RunModes
{
    public static class Verify
    {
        public static void Run(BuildInstance buildInstance)
        {
            Console.WriteLine("Starting TACT CDN directory verification...");
            Console.WriteLine("!!!");
            Console.WriteLine("NOTE: This is currently a very basic implementation that only checks archive/loose file existence & sizes.");
            Console.WriteLine("!!!");

            var badFiles = new List<string>();
            var missingFiles = new List<string>();

            var buildConfigs = new List<string>();
            var cdnConfigs = new List<string>();
            var patchConfigs = new List<string>();
            var keyRings = new List<string>();

            var checkedFiles = new HashSet<string>();

            Console.WriteLine("Scanning configs...");
            var configPath = Path.Combine(buildInstance.Settings.CDNDir, "tpr", "wow", "config");
            foreach (var file in Directory.GetFiles(configPath, "*", SearchOption.AllDirectories))
            {
                using (var stream = File.OpenRead(file))
                {
                    using (var reader = new StreamReader(stream))
                    {
                        var firstLine = reader.ReadLine();
                        if (firstLine == "# Build Configuration")
                            buildConfigs.Add(file);
                        else if (firstLine == "# CDN Configuration")
                            cdnConfigs.Add(file);
                        else if (firstLine == "# Patch Configuration")
                            patchConfigs.Add(file);
                        else if (firstLine!.StartsWith("key"))
                            keyRings.Add(file);
                        else
                            Console.WriteLine($"!!! Warning, unknown config file {file} with first line of : " + firstLine);
                    }
                }

                var md5 = Convert.ToHexStringLower(MD5.HashData(File.ReadAllBytes(file)));
                if (md5 != Path.GetFileNameWithoutExtension(file))
                {
                    Console.WriteLine($"!!! Warning, config file {file} has incorrect MD5 hash! Expected {Path.GetFileNameWithoutExtension(file)} but got {md5}.");
                    badFiles.Add(file);
                }

                checkedFiles.Add(file);
            }

            Console.WriteLine("Found " + buildConfigs.Count + " build configs, " + cdnConfigs.Count + " CDN configs and " + patchConfigs.Count + " patch configs.");

            for (var c = 0; c < cdnConfigs.Count; c++)
            {
                {
                    var cdnConfig = cdnConfigs[c];
                    var config = new TACTSharp.Config(buildInstance.cdn, cdnConfig, true);
                    var configName = Path.GetFileNameWithoutExtension(cdnConfig);

                    Console.Write("Checking cdnconfigs.. " + (c + 1) + "/" + cdnConfigs.Count + "\r");

                    var archiveCount = config.Values["archives"].Length;
                    for (var i = 0; i < archiveCount; i++)
                    {
                        var archiveIndex = config.Values["archives"][i];
                        if (checkedFiles.Contains(archiveIndex))
                            continue;

                        var indexPath = Path.Combine(buildInstance.Settings.CDNDir, "tpr", "wow", "data", archiveIndex[0..2], archiveIndex[2..4], archiveIndex + ".index");
                        if (!File.Exists(indexPath))
                        {
                            Console.WriteLine($"!!! [{configName}] Archive index {archiveIndex} is missing!\n");
                            missingFiles.Add(indexPath);
                            checkedFiles.Add(indexPath);
                            continue;
                        }

                        var index = new IndexInstance(indexPath);

                        // TODO: Check index integrity

                        var allFiles = index.GetAllEntries();
                        var highestOffset = allFiles.Select(x => x.offset + x.size).Max();

                        var archiveFileInfo = new FileInfo(indexPath.Replace(".index", ""));
                        if (archiveFileInfo.Exists)
                        {
                            var archiveLength = archiveFileInfo.Length;

                            if (highestOffset != archiveLength)
                            {
                                Console.WriteLine($"!!! [{configName}] Archive {archiveIndex} has wrong size! Expected {highestOffset} bytes but only found {archiveLength} bytes.\n");
                                badFiles.Add(archiveFileInfo.FullName);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"!!! [{configName}] Archive file {archiveIndex} is missing!\n");
                            missingFiles.Add(indexPath);
                        }

                        checkedFiles.Add(archiveIndex);

                        //Console.Write("Checking archives.. " + (i + 1) + "/" + archiveCount + "\r");
                    }

                    if (config.Values.TryGetValue("file-index", out string[]? fileIndexName))
                    {
                        var fileIndexPath = Path.Combine(buildInstance.Settings.CDNDir, "tpr", "wow", "data", fileIndexName[0][0..2], fileIndexName[0][2..4], fileIndexName[0] + ".index");
                        var fileIndex = new IndexInstance(fileIndexPath);
                        var allFiles = fileIndex.GetAllEntries();
                        var looseFileCount = allFiles.Count;
                        for (var i = 0; i < looseFileCount; i++)
                        {
                            //Console.Write("Checking loose files.. " + (i + 1) + "/" + looseFileCount + "\r");

                            var looseFile = allFiles[i];
                            var looseFileName = Convert.ToHexStringLower(looseFile.eKey);

                            if (checkedFiles.Contains(looseFileName))
                                continue;

                            var looseFilePath = Path.Combine(buildInstance.Settings.CDNDir, "tpr", "wow", "data", looseFileName[0..2], looseFileName[2..4], looseFileName);
                            var looseFileInfo = new FileInfo(looseFilePath);
                            if (looseFileInfo.Exists)
                            {
                                var looseFileSize = looseFileInfo.Length;
                                var looseFileSupposedSize = looseFile.size;

                                // bad assumption here
                                //var looseFileMD5 = Convert.ToHexStringLower(MD5.HashData(File.ReadAllBytes(looseFilePath)));
                                //if (looseFileMD5 != looseFileName)
                                //{
                                //    Console.WriteLine($"!!! MD5 for file {looseFileName} is incorrect ({looseFileName} != {looseFileMD5}!");
                                //    badFiles.Add(looseFilePath);
                                //}

                                if (looseFileSize != looseFileSupposedSize)
                                {
                                    Console.WriteLine($"!!! [{configName}] Loose file {looseFileName} has wrong size! Expected {looseFileSupposedSize} bytes but only found {looseFileSize} bytes.\n");
                                    badFiles.Add(looseFileName);
                                }
                            }
                            else
                            {
                                Console.WriteLine($"!!! [{configName}] Loose file {looseFileName} is missing!\n");
                                missingFiles.Add(looseFileName);
                            }

                            checkedFiles.Add(looseFileName);
                        }
                    }
                    else
                    {
                        // TODO: No file index, list loose files from connected builds => encodings?
                        //Console.WriteLine($"!!! [{configName}] No file index specified, skipping loose file checks.\n");
                    }

                    // TODO: Other cdnconfig listed things such as patch archives
                }
            }

            Console.WriteLine("Verification complete! " + (badFiles.Count + missingFiles.Count) + " total issues found (" + badFiles.Count + " bad files, " + missingFiles.Count + " missing files).");
            File.WriteAllLines("badFiles.txt", badFiles);
            File.WriteAllLines("missingFiles.txt", missingFiles);
        }
    }
}
