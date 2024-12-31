using System.Diagnostics;

namespace TACTIndexTestCSharp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // mostly based on schlumpf's implementation, but with some changes because i dont know how to port some c++ things to c# properly

            var baseDir = "C:\\World of Warcraft\\data\\";

            var inputArchive = Path.Combine(baseDir, "indices\\ec39d1815cbe1434aa9c77db96ab4211.index");
            var cdnConfig = Path.Combine(baseDir, "config\\cd\\18\\cd18191b8928c33bf24b962e9330460f");

            IndexInstance groupIndex;
            var indices = new List<IndexInstance>();

            var groupArchiveIndex = "";
            var archives = new List<string>();

            foreach (var line in File.ReadAllLines(cdnConfig))
            {
                var parts = line.Split(" = ");
                if (parts[0] == "archive-group")
                    groupArchiveIndex = parts[1];
                else if (parts[0] == "archives")
                    archives.AddRange(parts[1].Split(" "));
            }

            var gaSW = new Stopwatch();
            var archiveSW = new Stopwatch();

            if (!File.Exists(Path.Combine(baseDir, "indices", groupArchiveIndex + ".index")))
            {
                // TODO: Make it? 
                throw new Exception("Group archive index not found");
            }

            gaSW.Start();
            groupIndex = new IndexInstance(Path.Combine(baseDir, "indices", groupArchiveIndex + ".index"));
            gaSW.Stop();
            Console.WriteLine("Loaded group archive index in " + gaSW.Elapsed.TotalMilliseconds + "ms");

            var ramAfterGroupIndex = Process.GetCurrentProcess().WorkingSet64;
            Console.WriteLine("RAM usage after group index: " + ramAfterGroupIndex / 1024 / 1024 + "MB");

            archiveSW.Start();
            for (var i = 0; i < archives.Count; i++)
            {
                var archive = archives[i];
                if (File.Exists(Path.Combine(baseDir, "indices", archive + ".index")))
                {
                    indices.Add(new IndexInstance(Path.Combine(baseDir, "indices", archive + ".index"), (short)i));
                }
            }
            archiveSW.Stop();
            Console.WriteLine("Loaded " + indices.Count + " indices in " + archiveSW.Elapsed.TotalMilliseconds + "ms");
            var ramDiffAfterLoose = Process.GetCurrentProcess().WorkingSet64 - ramAfterGroupIndex;
            Console.WriteLine("RAM usage difference loose indices: " + ramDiffAfterLoose / 1024 / 1024 + "MB");

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

            foreach (var line in File.ReadAllLines("D:\\Downloads\\11.1.encodingdump")) // https://old.wow.tools/pub/11.1.encodingdump
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

                archiveSW.Restart();
                Parallel.ForEach(indices, index =>
                {
                    if (looseFound)
                        return;

                    var (offset, size, archiveIndex) = index.GetIndexInfo(eKeyTarget.AsSpan());
                    if (offset != -1)
                    {
                        archiveSW.Stop();
                        looseFound = true;
                       // Console.WriteLine("Loose (" + offset + ", " + size + ", " + archiveIndex + " lookup took " + archiveSW.Elapsed.TotalMilliseconds + "ms");
                    }
                });

                if(looseFound && !gaFound)
                    Console.WriteLine("Loose found " + eKey + " but group didn't");
                else if (gaFound && !looseFound)
                    Console.WriteLine("Group found " + eKey + " but loose didn't");

                if (gaSW.Elapsed.TotalMilliseconds > archiveSW.Elapsed.TotalMilliseconds)
                    looseFaster++;
                else
                    groupFaster++;

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
