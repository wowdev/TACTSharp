using System.Diagnostics;
using System.Drawing;
using System.Linq.Expressions;

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

            foreach (var line in File.ReadAllLines("D:\\Downloads\\11.1.encodingdump")) // https://old.wow.tools/pub/11.1.encodingdump
            {
                var parts = line.Split(' ');

                gaSW.Restart();
                var (gaOffset, gaSize, gaArchiveIndex) = groupIndex.GetIndexInfo(parts[1]);
                gaSW.Stop();

                if (gaOffset != -1)
                    Console.WriteLine("Group (" + gaOffset + ", " + gaSize + ", " + gaArchiveIndex + " lookup took " + gaSW.Elapsed.TotalMilliseconds + "ms");

                var found = false;

                archiveSW.Restart();

                Parallel.ForEach(indices, index =>
                {
                    if(found)
                        return;
                    var (offset, size, archiveIndex) = index.GetIndexInfo(parts[1]);
                    if (offset != -1)
                    {
                        archiveSW.Stop();
                        found = true;
                        Console.WriteLine("Loose (" + offset + ", " + size + ", " + archiveIndex + " lookup took " + archiveSW.Elapsed.TotalMilliseconds + "ms");
                    }
                });

                //oldIndexSW.Restart();
                //if (indexDictionary.TryGetValue(parts[1].ToUpper(), out var indexEntry))
                //{
                //    oldIndexSW.Stop();
                //    Console.WriteLine("Old (" + indexEntry.offset + ", " + indexEntry.size + ", " + indexEntry.index + " lookup took " + oldIndexSW.Elapsed.TotalMilliseconds + "ms");
                //}
                //oldIndexSW.Stop();

                if (gaSW.Elapsed.TotalMilliseconds > archiveSW.Elapsed.TotalMilliseconds)
                {
                    looseFaster++;
                }
                else
                {
                    groupFaster++;
                }

                checkedKeys++;

                if(checkedKeys % 1000 == 0)
                {
                    Console.WriteLine("Checked " + checkedKeys + " keys");
                    Console.WriteLine("Group faster: " + groupFaster);
                    Console.WriteLine("Loose faster: " + looseFaster);
                }
            }
        }

        public struct IndexEntry
        {
            public short index;
            public uint offset;
            public uint size;
        }

        private static ReaderWriterLockSlim cacheLock = new ReaderWriterLockSlim();
        private static Dictionary<string, IndexEntry> indexDictionary = new Dictionary<string, IndexEntry>();

        private static void GetIndexes(string basePath, string[] archives)
        {
            Parallel.ForEach(archives, (archive, state, i) =>
            {
                try
                {
                    byte[] indexContent = File.ReadAllBytes(Path.Combine(basePath, "indices", archive + ".index"));

                    using (BinaryReader bin = new BinaryReader(new MemoryStream(indexContent)))
                    {
                        int indexEntries = indexContent.Length / 4096;

                        for (var b = 0; b < indexEntries; b++)
                        {
                            for (var bi = 0; bi < 170; bi++)
                            {
                                var headerHash = Convert.ToHexString(bin.ReadBytes(16));

                                var entry = new IndexEntry()
                                {
                                    index = (short)i,
                                    size = bin.ReadUInt32(true),
                                    offset = bin.ReadUInt32(true)
                                };

                                cacheLock.EnterUpgradeableReadLock();
                                try
                                {
                                    if (!indexDictionary.ContainsKey(headerHash))
                                    {
                                        cacheLock.EnterWriteLock();
                                        try
                                        {
                                            if (!indexDictionary.TryAdd(headerHash, entry))
                                            {
                                                Console.WriteLine("Duplicate index entry for " + headerHash + " " + "(index: " + archives[i] + ", size: " + entry.size + ", offset: " + entry.offset);
                                            }
                                        }
                                        finally
                                        {
                                            cacheLock.ExitWriteLock();
                                        }
                                    }
                                }
                                finally
                                {
                                    cacheLock.ExitUpgradeableReadLock();
                                }
                            }
                            bin.ReadBytes(16);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error retrieving index: " + e.Message);
                }
            });
        }
    }
}
