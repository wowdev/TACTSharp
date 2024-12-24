using Microsoft.Win32.SafeHandles;
using System.IO.MemoryMappedFiles;

namespace TACTIndexTestCSharp
{
    public sealed class IndexInstance
    {
        private long indexSize;
        private IndexFooter footer;
        private short archiveIndex = -1;
        private bool isGroupArchive;

        private MemoryMappedFile indexFile;
        private MemoryMappedViewAccessor accessor;
        private SafeMemoryMappedViewHandle mmapViewHandle;

        private int blockSizeBytes;
        private int entrySize;
        private int entriesPerBlock;
        private int entriesInLastBlock;
        private int numBlocks;
        private int ofsStartOfToc;
        private int ofsEndOfTocEkeys;

        public IndexInstance(string path, short archiveIndex = -1)
        {
            this.archiveIndex = archiveIndex;
            this.indexSize = new FileInfo(path).Length;

            this.indexFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            this.accessor = indexFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            this.mmapViewHandle = accessor.SafeMemoryMappedViewHandle;

            using (var accessor = this.indexFile.CreateViewAccessor(indexSize - 20, 20, MemoryMappedFileAccess.Read))
                accessor.Read(0, out footer);

            isGroupArchive = footer.offsetBytes == 6;

            this.blockSizeBytes = footer.blockSizeKBytes << 10;
            this.entrySize = footer.keyBytes + footer.sizeBytes + footer.offsetBytes;
            this.entriesPerBlock = this.blockSizeBytes / this.entrySize;
            this.numBlocks = (int)Math.Ceiling((double)footer.numElements / this.entriesPerBlock);
            this.entriesInLastBlock = (int)footer.numElements - (this.numBlocks - 1) * this.entriesPerBlock;

            this.ofsStartOfToc = this.numBlocks * this.blockSizeBytes;
            this.ofsEndOfTocEkeys = ofsStartOfToc + footer.keyBytes * this.numBlocks;
        }

        // Binary search pointing to the first element **not** comparing SequenceCompareTo < 0 anymore.
        // [1 3 4 6]: 0 -> 1; 1 -> 1; 2 -> 3; 3 -> 3; 4 -> 4; 5 -> 6; 6 -> 6; 7 -> end.
        private static int lowerBoundEkey(ReadOnlySpan<byte> dataSpan, int dataSize, ReadOnlySpan<byte> needle)
        {
            var left = 0;
            int right = dataSpan.Length / dataSize;

            while (left < right)
            {
                int middle = left + (right - left) / 2;
                var entry = dataSpan.Slice((middle * dataSize), needle.Length);

                if (entry.SequenceCompareTo(needle) < 0)
                    left = middle + 1;
                else
                    right = middle;
            }

            return left * dataSize;
        }

        unsafe public (int offset, int size, int archiveIndex) GetIndexInfo(string targetEKey)
        {
            var eKeyTarget = Convert.FromHexString(targetEKey).AsSpan();

            // i dont think this pointer can be rid of, only way is to make a span over a byte array read from the accessor which kinda defeats the point
            byte* fileData = null;
            try
            {
                mmapViewHandle.AcquirePointer(ref fileData);

                var fileSpan = new ReadOnlySpan<byte>(fileData, (int)indexSize);
                var tocSpan = fileSpan[ofsStartOfToc..ofsEndOfTocEkeys];

                var lastEkey = lowerBoundEkey(tocSpan, footer.keyBytes, eKeyTarget);
                if (lastEkey >= tocSpan.Length)
                    return (-1, -1, -1);

                var blockIndexMaybeContainingEkey = lastEkey / footer.keyBytes;

                var ofsStartOfCandidateBlock = blockIndexMaybeContainingEkey * this.blockSizeBytes;
                var entriesOfCandidateBlock = blockIndexMaybeContainingEkey != this.numBlocks - 1 ? this.entriesPerBlock : this.entriesInLastBlock;
                var ofsEndOfCandidateBlock = ofsStartOfCandidateBlock + this.entrySize * entriesOfCandidateBlock;

                var candidateBlockSpan = fileSpan[ofsStartOfCandidateBlock..ofsEndOfCandidateBlock];

                var candidateOffset = lowerBoundEkey(tocSpan, this.entrySize, eKeyTarget);

                if (candidateOffset >= candidateBlockSpan.Length)
                    return (-1, -1, -1);

                var entry = candidateBlockSpan.Slice(candidateOffset, this.entrySize);
                if (entry[..footer.keyBytes].SequenceCompareTo(eKeyTarget) != 0)
                    return (-1, -1, -1);

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
            finally
            {
                if (fileData != null)
                    mmapViewHandle.ReleasePointer();
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
