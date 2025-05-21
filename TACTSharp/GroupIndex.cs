using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace TACTSharp
{
    // roughly based on schlumpf's implementation
    public class GroupIndex
    {
        private struct IndexEntry
        {
            public byte[] EKey;
            public uint Size;
            public ushort ArchiveIndex;
            public uint Offset;
        }

        private readonly List<IndexEntry> Entries = [];
        private readonly Lock entryLock = new();

        public string Generate(CDN CDN, Settings Settings, string? hash, string[] archives)
        {
            if (string.IsNullOrEmpty(hash))
                Console.WriteLine("Generating group index for unknown group-index");
            else
                Console.WriteLine("Generating group index for " + hash);

            Console.WriteLine("Loading " + archives.Length + " index files");

            Parallel.For(0, archives.Length, archiveIndex =>
            {
                var archiveName = archives[archiveIndex];
                string indexPath = "";
                if (!string.IsNullOrEmpty(Settings.BaseDir) && File.Exists(Path.Combine(Settings.BaseDir, "Data", "indices", archiveName + ".index")))
                {
                    indexPath = Path.Combine(Settings.BaseDir, "Data", "indices", archiveName + ".index");
                }
                else if(!string.IsNullOrEmpty(Settings.CDNDir) && File.Exists(Path.Combine(Settings.CDNDir, CDN.ProductDirectory, "data", $"{archiveName[0]}{archiveName[1]}", $"{archiveName[2]}{archiveName[3]}", archiveName + ".index")))
                {
                    indexPath = Path.Combine(Settings.CDNDir, CDN.ProductDirectory, "data", $"{archiveName[0]}{archiveName[1]}", $"{archiveName[2]}{archiveName[3]}", archiveName + ".index");
                }
                else
                {
                    _ = CDN.GetFile("data", archives[archiveIndex] + ".index");
                    indexPath = Path.Combine(Settings.CacheDir, CDN.ProductDirectory, "data", archives[archiveIndex] + ".index");
                }

                var index = new IndexInstance(indexPath);
                var allEntries = index.GetAllEntries();
                foreach (var (eKey, offset, size, _) in allEntries)
                {
                    lock (entryLock)
                    {
                        Entries.Add(new IndexEntry
                        {
                            EKey = eKey,
                            Size = (uint)size,
                            ArchiveIndex = (ushort)archiveIndex,
                            Offset = (uint)offset
                        });
                    }
                }
            });

            Console.WriteLine("Done loading index files, got " + Entries.Count + " entries");

            Console.WriteLine("Sorting entries by EKey");
            Entries.Sort((a, b) => a.EKey.AsSpan().SequenceCompareTo(b.EKey));
            Console.WriteLine("Done sorting entries");

            var outputFooter = new IndexFooter
            {
                formatRevision = 1,
                flags0 = 0,
                flags1 = 0,
                blockSizeKBytes = 4,
                offsetBytes = 6,
                sizeBytes = 4,
                keyBytes = 16,
                hashBytes = 8,
                numElements = (uint)Entries.Count
            };

            var outputBlockSizeBytes = outputFooter.blockSizeKBytes * 1024;
            var outputEntrySize = outputFooter.keyBytes + outputFooter.sizeBytes + outputFooter.offsetBytes;
            var outputEntriesPerBlock = outputBlockSizeBytes / outputEntrySize;
            var outputNumBlocks = (int)Math.Ceiling((double)outputFooter.numElements / outputEntriesPerBlock);
            var outputEntriesOfLastBlock = outputFooter.numElements - (outputNumBlocks - 1) * outputEntriesPerBlock;

            var totalSize = (outputNumBlocks * outputBlockSizeBytes) + ((outputFooter.keyBytes + outputFooter.hashBytes) * outputNumBlocks) + 28;

            using (var ms = new MemoryStream(totalSize))
            using (var br = new BinaryReader(ms))
            using (var bin = new BinaryWriter(ms))
            {
                var ofsStartOfTocEkeys = outputNumBlocks * outputBlockSizeBytes;
                var ofsStartOfTocBlockHashes = ofsStartOfTocEkeys + outputFooter.keyBytes * outputNumBlocks;

                // See https://github.com/dotnet/runtime/issues/115033 and GroupIndexBlockBench for why we're using a span over entries here.
                // Not that it matters a ton since groupindex gen should only run once per cdnconfig, but still.
                var entriesSpan = CollectionsMarshal.AsSpan(Entries);
                int totalBlocks = (entriesSpan.Length + outputEntriesPerBlock - 1) / outputEntriesPerBlock;

                for (int blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
                {
                    int start = blockIndex * outputEntriesPerBlock;
                    int length = Math.Min(outputEntriesPerBlock, entriesSpan.Length - start);
                    var blockSpan = entriesSpan.Slice(start, length);
                    bin.BaseStream.Position = blockIndex * outputBlockSizeBytes;

                    foreach (var entry in blockSpan)
                    {
                        bin.Write(entry.EKey);
                        bin.Write(BinaryPrimitives.ReverseEndianness(entry.Size));
                        bin.Write(BinaryPrimitives.ReverseEndianness((short)entry.ArchiveIndex));
                        bin.Write(BinaryPrimitives.ReverseEndianness(entry.Offset));
                    }
                    bin.BaseStream.Position = ofsStartOfTocEkeys + blockIndex * outputFooter.keyBytes;
                    bin.Write(blockSpan[^1].EKey);
                    bin.BaseStream.Position = ofsStartOfTocBlockHashes + blockIndex * outputFooter.hashBytes;
                    bin.Write(new byte[outputFooter.hashBytes]);
                }

                bin.BaseStream.Position = totalSize - 28;
                bin.Write(new byte[outputFooter.hashBytes]); // toc_hash
                bin.Write(outputFooter.formatRevision);
                bin.Write(outputFooter.flags0);
                bin.Write(outputFooter.flags1);
                bin.Write(outputFooter.blockSizeKBytes);
                bin.Write(outputFooter.offsetBytes);
                bin.Write(outputFooter.sizeBytes);
                bin.Write(outputFooter.keyBytes);
                bin.Write(outputFooter.hashBytes);
                bin.Write(outputFooter.numElements);
                bin.Write(new byte[outputFooter.hashBytes]); // footerHash

                for (var i = 0; i < outputNumBlocks; i++)
                {
                    var startOfBlock = i * outputBlockSizeBytes;
                    bin.BaseStream.Position = startOfBlock;
                    var blockBytes = br.ReadBytes(outputBlockSizeBytes);
                    var md5Hash = MD5.HashData(blockBytes);

                    bin.BaseStream.Position = ofsStartOfTocBlockHashes + (i * 8);
                    bin.Write(md5Hash.AsSpan(0, 8).ToArray());
                }

                // Generate TOC hash
                bin.BaseStream.Position = ofsStartOfTocEkeys;
                var tocBytes = br.ReadBytes((int)bin.BaseStream.Length - ofsStartOfTocEkeys - 28);
                var tocMD5Hash = MD5.HashData(tocBytes);
                bin.BaseStream.Position = totalSize - 28;
                bin.Write(tocMD5Hash.AsSpan(0, 8).ToArray());

                // Generate footer hash
                bin.BaseStream.Position = totalSize - 20;
                var footerBytes = br.ReadBytes(20);
                var footerMD5Hash = MD5.HashData(footerBytes);
                bin.BaseStream.Position = totalSize - 8;
                bin.Write(footerMD5Hash.AsSpan(0, 8).ToArray());

                // Generate full footer hash (filename)
                bin.BaseStream.Position = totalSize - 28;
                var fullFooterBytes = br.ReadBytes(28);
                var fullFooterMD5Hash = Convert.ToHexStringLower(MD5.HashData(fullFooterBytes));

                Directory.CreateDirectory(Path.Combine(Settings.CacheDir, "wow", "data"));

                if (!string.IsNullOrEmpty(hash))
                {
                    if (fullFooterMD5Hash != hash)
                        throw new Exception("Footer MD5 of group index does not match group index filename");

                    if (!Directory.Exists(Path.Combine(Settings.CacheDir, CDN.ProductDirectory, "data")))
                        Directory.CreateDirectory(Path.Combine(Settings.CacheDir, CDN.ProductDirectory, "data"));

                    File.WriteAllBytes(Path.Combine(Settings.CacheDir, CDN.ProductDirectory, "data", hash + ".index"), ms.ToArray());
                }
                else
                {
                    hash = fullFooterMD5Hash;
                    File.WriteAllBytes(Path.Combine(Settings.CacheDir, CDN.ProductDirectory, "data", fullFooterMD5Hash + ".index"), ms.ToArray());
                }

                return hash;
            }
        }

        private unsafe struct IndexFooter
        {
            public byte formatRevision;
            public byte flags0;
            public byte flags1;
            public byte blockSizeKBytes;
            public byte offsetBytes;
            public byte sizeBytes;
            public byte keyBytes;
            public byte hashBytes;
            public uint numElements;
            public fixed byte bytefooterHash[8];
        }
    }
}
