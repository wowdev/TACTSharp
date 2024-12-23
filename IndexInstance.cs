using System.IO.MemoryMappedFiles;

namespace TACTIndexTestCSharp
{
    public sealed class IndexInstance
    {
        private long indexSize;
        private IndexFooter footer;
        private short archiveIndex = -1;
        private string path;
        private bool isGroupArchive;
        private readonly List<byte[]> lastEKeys = [];

        public IndexInstance(string path, short archiveIndex = -1)
        {
            this.archiveIndex = archiveIndex;
            this.path = path;
            indexSize = new FileInfo(path).Length;

            using (var indexFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open, Path.GetFileNameWithoutExtension(path)))
            {
                using (var accessor = indexFile.CreateViewAccessor(indexSize - 20, 20, MemoryMappedFileAccess.Read))
                    accessor.Read(0, out footer);

                isGroupArchive = footer.offsetBytes == 6;

                var blockSizeBytes = footer.blockSizeKBytes << 10;
                var entrySize = footer.keyBytes + footer.sizeBytes + footer.offsetBytes;
                var entriesPerBlock = blockSizeBytes / entrySize;
                var numBlocks = (int)Math.Ceiling((double)footer.numElements / entriesPerBlock);

                var ofsStartOfToc = numBlocks * blockSizeBytes;
                var ofsEndOfTocEkeys = ofsStartOfToc + footer.keyBytes * numBlocks;

                var data = new byte[ofsEndOfTocEkeys - ofsStartOfToc];

                using (var accessor = indexFile.CreateViewAccessor(ofsStartOfToc, ofsEndOfTocEkeys - ofsStartOfToc, MemoryMappedFileAccess.Read))
                    accessor.ReadArray(0, data, 0, data.Length);

                var dataAsSpan = data.AsSpan();

                for (var i = 0; i < numBlocks; i++)
                {
                    var eKeyCompare = dataAsSpan.Slice(i * footer.keyBytes, footer.keyBytes);
                    lastEKeys.Add(eKeyCompare.ToArray());
                }
            }
        }

        public (int offset, int size, int archiveIndex) GetIndexInfo(string targetEKey)
        {
            var eKeyTarget = Convert.FromHexString(targetEKey).AsSpan();
            var targetBlock = 0;

            foreach (var lastEKey in lastEKeys)
            {
                if (lastEKey.AsSpan().SequenceCompareTo(eKeyTarget) > 0)
                    break;

                targetBlock++;
            }

            var blockIndexMaybeContainingEkey = targetBlock;

            var blockSizeBytes = footer.blockSizeKBytes << 10;
            var entrySize = footer.keyBytes + footer.sizeBytes + footer.offsetBytes;
            var entriesPerBlock = blockSizeBytes / entrySize;
            var numBlocks = (int)Math.Ceiling((double)footer.numElements / entriesPerBlock);
            var ofsStartOfCandidateBlock = blockIndexMaybeContainingEkey * blockSizeBytes;
            var entriesOfCandidateBlock = blockIndexMaybeContainingEkey != numBlocks - 1 ? entriesPerBlock : (footer.numElements - (numBlocks - 1) * entriesPerBlock);
            var ofsEndOfCandidateBlock = ofsStartOfCandidateBlock + entrySize * entriesOfCandidateBlock;

            if (ofsEndOfCandidateBlock >= indexSize)
            {
                return (-1, -1, -1);
            }

            byte[] candidateBlockData = new byte[ofsEndOfCandidateBlock - ofsStartOfCandidateBlock];

            using (var indexFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open, Path.GetFileNameWithoutExtension(path), 0, MemoryMappedFileAccess.Read))
            {
                using (var accessor = indexFile.CreateViewAccessor(ofsStartOfCandidateBlock, candidateBlockData.Length, MemoryMappedFileAccess.Read))
                {
                    accessor.ReadArray(0, candidateBlockData, 0, candidateBlockData.Length);
                }
            }
            //using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            //{
            //    fs.Position = ofsStartOfCandidateBlock;
            //    fs.ReadExactly(candidateBlockData, 0, (int)ofsEndOfCandidateBlock - ofsStartOfCandidateBlock);
            //}

            var candidateBlockDataAsSpan = candidateBlockData.AsSpan();

            for (int i = 0; i < entriesOfCandidateBlock; i++)
            {
                var entry = candidateBlockDataAsSpan.Slice(i * entrySize, entrySize);
                if (entry.Slice(0, footer.keyBytes).SequenceEqual(eKeyTarget))
                {
                    if (isGroupArchive)
                    {
                        var encodedSize = entry.Slice(footer.keyBytes, footer.sizeBytes).ReadInt32BE();
                        var fileArchiveIndex = entry.Slice(footer.keyBytes + footer.sizeBytes, 2).ReadInt16BE();
                        var offset = entry.Slice(footer.keyBytes + footer.sizeBytes + 2, 4).ReadInt32BE();
                        return (offset, encodedSize, fileArchiveIndex);
                    }
                    else
                    {
                        var encodedSize = entry.Slice(footer.keyBytes, footer.sizeBytes).ReadInt32BE();
                        var offset = entry.Slice(footer.keyBytes + footer.sizeBytes, footer.offsetBytes).ReadInt32BE();
                        return (offset, encodedSize, archiveIndex);
                    }
                }
            }

            return (-1, -1, -1);
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
