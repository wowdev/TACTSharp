using Microsoft.Win32.SafeHandles;
using System.IO.MemoryMappedFiles;

namespace TACTSharp
{
    // mostly based on schlumpf's implementation, but with some changes because i dont know how to port some c++ things to c# properly
    public sealed class IndexInstance
    {
        private readonly long indexSize;
        private IndexFooter footer;
        private readonly short archiveIndex = -1;
        private readonly bool isGroupArchive;

        private readonly MemoryMappedFile indexFile;
        private readonly MemoryMappedViewAccessor accessor;
        private readonly SafeMemoryMappedViewHandle mmapViewHandle;

        private readonly int blockSizeBytes;
        private readonly int entrySize;
        private readonly int entriesPerBlock;
        private readonly int entriesInLastBlock;
        private readonly int numBlocks;
        private readonly int ofsStartOfToc;
        private readonly int ofsEndOfTocEkeys;

        public IndexInstance(string path, short archiveIndex = -1)
        {
            this.archiveIndex = archiveIndex;
            this.indexSize = new FileInfo(path).Length;

            this.indexFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            this.accessor = indexFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            this.mmapViewHandle = accessor.SafeMemoryMappedViewHandle;

            using (var accessor = this.indexFile.CreateViewAccessor(this.indexSize - 20, 20, MemoryMappedFileAccess.Read))
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
        unsafe static private byte* LowerBoundEkey(byte* begin, byte* end, long dataSize, ReadOnlySpan<byte> needle)
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

        public unsafe List<(byte[] eKey, int offset, int size)> GetAllEntries()
        {
            var entries = new List<(byte[] eKey, int offset, int size)>();

            byte* fileData = null;
            try
            {
                mmapViewHandle.AcquirePointer(ref fileData);
                for (var i = 0; i < this.numBlocks; i++)
                {
                    byte* startOfBlock = fileData + (i * this.blockSizeBytes);
                    var blockSpan = new ReadOnlySpan<byte>(startOfBlock, this.blockSizeBytes);
                    for(var j = 0; j < this.entriesPerBlock; j++)
                    {
                        var entry = blockSpan.Slice(j * this.entrySize, this.entrySize);
                        var eKey = entry[..footer.keyBytes].ToArray();
                        var offset = entry.Slice(footer.keyBytes + footer.sizeBytes, footer.offsetBytes).ReadInt32BE();
                        var size = entry.Slice(footer.keyBytes, footer.sizeBytes).ReadInt32BE();

                        if(size != 0)
                            entries.Add((eKey, offset, size));
                    }
                }
            }
            finally
            {
                if (fileData != null)
                    mmapViewHandle.ReleasePointer();
            }

            return entries;
        }

        unsafe public (int offset, int size, int archiveIndex) GetIndexInfo(Span<byte> eKeyTarget)
        {
            byte* fileData = null;
            try
            {
                mmapViewHandle.AcquirePointer(ref fileData);

                byte* startOfToc = fileData + this.ofsStartOfToc;
                byte* endOfTocEkeys = fileData + this.ofsEndOfTocEkeys;

                byte* lastEkey = LowerBoundEkey(startOfToc, endOfTocEkeys, footer.keyBytes, eKeyTarget);
                if (lastEkey == endOfTocEkeys)
                    return (-1, -1, -1);

                var blockIndexMaybeContainingEkey = (lastEkey - startOfToc) / footer.keyBytes;

                var ofsStartOfCandidateBlock = blockIndexMaybeContainingEkey * this.blockSizeBytes;
                var entriesOfCandidateBlock = blockIndexMaybeContainingEkey != this.numBlocks - 1 ? this.entriesPerBlock : this.entriesInLastBlock;
                var ofsEndOfCandidateBlock = ofsStartOfCandidateBlock + this.entrySize * entriesOfCandidateBlock;

                byte* startOfCandidateBlock = fileData + ofsStartOfCandidateBlock;
                byte* endOfCandidateBlock = fileData + ofsEndOfCandidateBlock;

                byte* candidate = LowerBoundEkey(startOfCandidateBlock, endOfCandidateBlock, this.entrySize, eKeyTarget);

                if (candidate == endOfCandidateBlock)
                    return (-1, -1, -1);

                var entry = new ReadOnlySpan<byte>(candidate, this.entrySize);
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