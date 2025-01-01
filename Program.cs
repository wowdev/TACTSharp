using System.Diagnostics;

namespace TACTIndexTestCSharp
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var buildConfig = new Config("D:\\Projects\\wow.tools.local\\fakebuildconfig");

            if (!buildConfig.Values.TryGetValue("encoding", out var encodingKey))
                throw new Exception("No encoding key found in build config");

            var cdnConfig = new Config(Path.Combine(Settings.BaseDir, "config", "cd", "18", "cd18191b8928c33bf24b962e9330460f"));
            if (!cdnConfig.Values.TryGetValue("archive-group", out var groupArchiveIndex))
                throw new Exception("No archive group found in cdn config");

            var archives = new List<string>();
            if (cdnConfig.Values.TryGetValue("archives", out var archiveList))
                archives.AddRange(archiveList);

            var totalTimer = new Stopwatch();
            totalTimer.Start();
            var eTimer = new Stopwatch();
            eTimer.Start();
            var encodingPath = await CDN.GetFilePath("wow", "data", encodingKey[1], ulong.Parse(buildConfig.Values["encoding-size"][0]), ulong.Parse(buildConfig.Values["encoding-size"][1]), true);
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
            var rootPath = await CDN.GetFilePath("wow", "data", rootEKey, rootEKeys.Value.decodedFileSize, 0, true);
            eTimer.Stop();
            Console.WriteLine("Retrieved root in " + eTimer.Elapsed.TotalMilliseconds + "ms");

            eTimer.Restart();
            var rootInstance = new RootInstance(rootPath);
            eTimer.Stop();
            Console.WriteLine("Loaded root in " + eTimer.Elapsed.TotalMilliseconds + "ms");

            if (!File.Exists(Path.Combine(Settings.BaseDir, "indices", groupArchiveIndex[0] + ".index")))
            {
                // TODO: Make it? 
                throw new Exception("Group archive index not found");
            }

            var gaSW = new Stopwatch();
            gaSW.Start();
            var groupIndex = new IndexInstance(Path.Combine(Settings.BaseDir, "indices", groupArchiveIndex[0] + ".index"));
            gaSW.Stop();
            Console.WriteLine("Loaded group index in " + gaSW.Elapsed.TotalMilliseconds + "ms");

            if (!Directory.Exists("output"))
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

            foreach (var (fileDataID, fileName) in extractionTargets)
            {
                var fileEntry = rootInstance.GetEntryByFDID(fileDataID) ?? throw new FileNotFoundException("File " + fileDataID + " (" + fileName + ") not found in root");

                if (!encoding.TryGetEKeys(fileEntry.md5, out var fileEKeys) || fileEKeys == null)
                    throw new Exception("EKey not found in encoding");

                var (offset, size, archiveIndex) = groupIndex.GetIndexInfo(fileEKeys.Value.eKeys[0]);
                string filePath;
                if (offset == -1)
                {
                    // File is unarchived
                    filePath = await CDN.GetFilePath("wow", "data", Convert.ToHexStringLower(fileEKeys.Value.eKeys[0]), fileEKeys.Value.decodedFileSize, 0, true);
                }
                else
                {
                    // File is archived
                    filePath = await CDN.GetFilePathFromArchive(Convert.ToHexStringLower(fileEKeys.Value.eKeys[0]), "wow", cdnConfig.Values["archives"][archiveIndex], offset, size, fileEKeys.Value.decodedFileSize, true);
                }

                if (!Directory.Exists(Path.Combine("output", Path.GetDirectoryName(fileName))))
                    Directory.CreateDirectory(Path.Combine("output", Path.GetDirectoryName(fileName)));

                File.Copy(filePath, Path.Combine("output", fileName), true);
            }

            eTimer.Stop();

            Console.WriteLine("Extracted " + extractionTargets.Count + " files in " + eTimer.Elapsed.TotalMilliseconds + "ms (average of " + Math.Round(eTimer.Elapsed.TotalMilliseconds / extractionTargets.Count, 5) + "ms per file)");

            totalTimer.Stop();
            Console.WriteLine("Total time: " + totalTimer.Elapsed.TotalMilliseconds + "ms");
        }
    }
}
