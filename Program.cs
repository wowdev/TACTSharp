using System;
using System.Diagnostics;

namespace TACTIndexTestCSharp
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var buildConfig = new Config("D:\\Projects\\wow.tools.local\\fakebuildconfig");
            if(!buildConfig.Values.TryGetValue("build-name", out var buildName))
                throw new Exception("No build name found in build config");

            if(!buildConfig.Values.TryGetValue("encoding", out var encodingKey))
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
            var encodingPath = await CDN.GetFilePath("wow", "data", encodingKey[1], true);
            eTimer.Stop();
            Console.WriteLine("Retrieved encoding in " + eTimer.Elapsed.TotalMilliseconds + "ms");

            eTimer.Restart();
            var encoding = new EncodingInstance(encodingPath);
            eTimer.Stop();
            Console.WriteLine("Loaded encoding in " + eTimer.Elapsed.TotalMilliseconds + "ms");

            if(!buildConfig.Values.TryGetValue("root", out var rootKey))
                throw new Exception("No root key found in build config");

            string rootEKey;
            if(rootKey.Length == 1)
            {
                var root = Convert.FromHexString(rootKey[0]);
                eTimer.Restart();
                if (!encoding.TryGetEKeys(root, out var rootEKeys) || rootEKeys == null)
                    throw new Exception("Root key not found in encoding");
                eTimer.Stop();

                rootEKey = Convert.ToHexStringLower(rootEKeys.Value.eKeys[0]);
            }
            else
            {
                rootEKey = rootKey[1];
            }

            eTimer.Restart();
            var rootPath = await CDN.GetFilePath("wow", "data", rootEKey, true);
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

            var xyzm2 = rootInstance.GetEntryByFDID(189077) ?? throw new Exception("xyz.m2 not found in root");

            eTimer.Restart();
            if (!encoding.TryGetEKeys(xyzm2[0].md5, out var xyzEKeys) || xyzEKeys == null)
                throw new Exception("EKey not found in encoding");
            eTimer.Stop();
            Console.WriteLine("EKey lookup in encoding took " + eTimer.Elapsed.TotalMilliseconds + "ms");

            eTimer.Restart();
            var (offset, size, archiveIndex) = groupIndex.GetIndexInfo(xyzEKeys.Value.eKeys[0]);
 
            eTimer.Stop();
            Console.WriteLine("EKey lookup in group index took " + eTimer.Elapsed.TotalMilliseconds + "ms");

            if (offset == -1)
                throw new Exception("EKey not found in group index");

            var targetArchive = cdnConfig.Values["archives"][archiveIndex];
            Console.WriteLine("File found in archive " + targetArchive + " at offset " + offset + ", " + size + " bytes");

            eTimer.Restart();
            var xyzPath = await CDN.GetFilePathFromArchive(Convert.ToHexStringLower(xyzEKeys.Value.eKeys[0]), "wow", targetArchive, offset, size, true);
            eTimer.Stop();
            Console.WriteLine("Retrieved xyz.m2 in " + eTimer.Elapsed.TotalMilliseconds + "ms");

            Console.WriteLine("Available at " + xyzPath);
            totalTimer.Stop();
            Console.WriteLine("Total time: " + totalTimer.Elapsed.TotalMilliseconds + "ms");

            return;

            //archiveSW.Start();
            //for (var i = 0; i < archives.Count; i++)
            //{
            //    var archive = archives[i];
            //    if (File.Exists(Path.Combine(baseDir, "indices", archive + ".index")))
            //    {
            //        indices.Add(new IndexInstance(Path.Combine(baseDir, "indices", archive + ".index"), (short)i));
            //    }
            //}
            //archiveSW.Stop();
            //Console.WriteLine("Loaded " + indices.Count + " indices in " + archiveSW.Elapsed.TotalMilliseconds + "ms");
            //var ramDiffAfterLoose = Process.GetCurrentProcess().WorkingSet64 - ramAfterGroupIndex;
            //Console.WriteLine("RAM usage difference loose indices: " + ramDiffAfterLoose / 1024 / 1024 + "MB");

            //var oldIndexSW = new Stopwatch();
            //oldIndexSW.Start();
            //GetIndexes(baseDir, archives.ToArray());
            //oldIndexSW.Stop();
            //Console.WriteLine("Loaded " + indexDictionary.Count + " indices in " + oldIndexSW.Elapsed.TotalMilliseconds + "ms");
            //var ramDiffAfterOld = Process.GetCurrentProcess().WorkingSet64 - ramAfterGroupIndex - ramAfterGroupIndex;
            //Console.WriteLine("RAM usage difference old indices: " + ramDiffAfterOld / 1024 / 1024 + "MB");

            var checkedKeys = 0;
            var looseFaster = 0;
            var groupFaster = 0;

            var eKeysToCheck = new List<string>();

            foreach (var line in File.ReadAllLines("C:\\Users\\ictma\\Downloads\\11.1.encodingdump")) // https://old.wow.tools/pub/11.1.encodingdump
            {
                var parts = line.Split(' ');

                if (parts[0] == "ENCODINGESPEC")
                    continue;

                eKeysToCheck.Add(parts[1].Trim());
            }

            gaSW.Restart();
            foreach (var eKey in eKeysToCheck) {
                var eKeyTarget = Convert.FromHexString(eKey);

                var gaFound = false;

                gaSW.Restart();
                var (gaOffset, gaSize, gaArchiveIndex) = groupIndex.GetIndexInfo(eKeyTarget.AsSpan());
                gaSW.Stop();

                if (gaOffset != -1)
                {
                    //Console.WriteLine("Group (" + gaOffset + ", " + gaSize + ", " + gaArchiveIndex + " lookup took " + gaSW.Elapsed.TotalMilliseconds + "ms");
                    gaFound = true;
                }

                var looseFound = false;

                //archiveSW.Restart();
                //Parallel.ForEach(indices, index =>
                //{
                //    if (looseFound)
                //        return;

                //    var (offset, size, archiveIndex) = index.GetIndexInfo(eKeyTarget.AsSpan());
                //    if (offset != -1)
                //    {
                //        archiveSW.Stop();
                //        looseFound = true;
                //       // Console.WriteLine("Loose (" + offset + ", " + size + ", " + archiveIndex + " lookup took " + archiveSW.Elapsed.TotalMilliseconds + "ms");
                //    }
                //});

                if(looseFound && !gaFound)
                    Console.WriteLine("Loose found " + eKey + " but group didn't");
                else if (gaFound && !looseFound)
                    Console.WriteLine("Group found " + eKey + " but loose didn't");

                //if (gaSW.Elapsed.TotalMilliseconds > archiveSW.Elapsed.TotalMilliseconds)
                //    looseFaster++;
                //else
                //    groupFaster++;

                checkedKeys++;

                if (checkedKeys % 1000 == 0)
                {
                    Console.WriteLine("Checked " + checkedKeys + " keys");
                }
            }
            gaSW.Stop();
            Console.WriteLine("Group lookup took " + gaSW.Elapsed.TotalMilliseconds + "ms");
        }
    }
}
