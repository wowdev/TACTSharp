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
            indexSize = new FileInfo(path).Length;

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
        unsafe static private byte* lowerBoundEkey(byte* begin, byte* end, long dataSize, ReadOnlySpan<byte> needle)
        {
            var count = (end - begin) / dataSize;

            while (count > 0)
            {
                var it = begin;
                var step = count / 2;
                it += step * dataSize;

                if (new ReadOnlySpan<byte>(it, needle.Length).SequenceCompareTo(needle) < 0)
                {
                    it += dataSize;
                    begin = it;
                    count -= step + 1;
                }
                else
                {
                    count = step;
                }
            }

            return begin;
        }

        unsafe public (int offset, int size, int archiveIndex) GetIndexInfo(string targetEKey)
        {
            var eKeyTarget = Convert.FromHexString(targetEKey).AsSpan();

            byte* fileData = null;
            try
            {
                mmapViewHandle.AcquirePointer(ref fileData);

                byte* startOfToc = fileData + this.ofsStartOfToc;
                byte* endOfTocEkeys = fileData + this.ofsEndOfTocEkeys;

                byte* lastEkey = lowerBoundEkey(startOfToc, endOfTocEkeys, footer.keyBytes, eKeyTarget);
                if (lastEkey == endOfTocEkeys)
                {
                    //Console.WriteLine("toc: no block with keys <= target");
                    return (-1, -1, -1);
                }

                var blockIndexMaybeContainingEkey = (lastEkey - startOfToc) / footer.keyBytes;

                var ofsStartOfCandidateBlock = blockIndexMaybeContainingEkey * this.blockSizeBytes;
                var entriesOfCandidateBlock = blockIndexMaybeContainingEkey != this.numBlocks - 1 ? this.entriesPerBlock : this.entriesInLastBlock;
                var ofsEndOfCandidateBlock = ofsStartOfCandidateBlock + this.entrySize * entriesOfCandidateBlock;

                byte* startOfCandidateBlock = fileData + ofsStartOfCandidateBlock;
                byte* endOfCandidateBlock = fileData + ofsEndOfCandidateBlock;

                byte* candidate = lowerBoundEkey(startOfCandidateBlock, endOfCandidateBlock, this.entrySize, eKeyTarget);

                if (candidate == endOfCandidateBlock)
                {
                    //Console.WriteLine("block: no key in block <= target");
                    return (-1, -1, -1);
                }

                var entry = new ReadOnlySpan<byte>(candidate, this.entrySize);
                if (entry.Slice(0, footer.keyBytes).SequenceCompareTo(eKeyTarget) != 0)
                {
                    //Console.WriteLine("block: candidate does not match");
                    return (-1, -1, -1);
                }

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
