using System.Buffers.Binary;
using System.Security.Cryptography;

namespace TACTIndexTestCSharp
{
    // roughly based on schlumpf's implementation
    public static class GroupIndex
    {
        private struct IndexEntry
        {
            public byte[] EKey;
            public uint Size;
            public ushort ArchiveIndex;
            public uint Offset;
        }

        private static readonly List<IndexEntry> Entries = [];
        private static readonly Lock entryLock = new();

        public static void Generate(string hash, string[] archives)
        {
            Console.WriteLine("Generating group index for " + hash);
            Console.WriteLine("Loading " + archives.Length + " index files");

            Parallel.For(0, archives.Length, archiveIndex =>
            {
                _ = CDN.GetFile("wow", "data", archives[archiveIndex] + ".index").Result;
                var index = new IndexInstance(Path.Combine("cache", "wow", "data", archives[archiveIndex] + ".index"));
                var allEntries = index.GetAllEntries();
                foreach (var (eKey, offset, size) in allEntries)
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

                for (var i = 0; i < outputNumBlocks; i++)
                {
                    var startOfBlock = i * outputBlockSizeBytes;
                    bin.BaseStream.Position = startOfBlock;

                    var blockEntries = Entries.Skip(i * outputEntriesPerBlock).Take(outputEntriesPerBlock).ToArray();
                    for (var j = 0; j < blockEntries.Length; j++)
                    {
                        var entry = blockEntries[j];
                        bin.Write(entry.EKey);
                        bin.Write(BinaryPrimitives.ReverseEndianness(entry.Size));
                        bin.Write(BinaryPrimitives.ReverseEndianness((short)entry.ArchiveIndex));
                        bin.Write(BinaryPrimitives.ReverseEndianness(entry.Offset));
                    }
                    bin.BaseStream.Position = ofsStartOfTocEkeys + i * outputFooter.keyBytes;
                    bin.Write(blockEntries.Last().EKey);
                    bin.BaseStream.Position = ofsStartOfTocBlockHashes + i * outputFooter.hashBytes;
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
                var fullFooterMD5Hash = MD5.HashData(fullFooterBytes);
                if (Convert.ToHexStringLower(fullFooterMD5Hash) != hash)
                    throw new Exception("Footer MD5 of group index does not match group index filename");

                File.WriteAllBytes(Path.Combine("cache", "wow", "data", hash + ".index"), ms.ToArray());
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
