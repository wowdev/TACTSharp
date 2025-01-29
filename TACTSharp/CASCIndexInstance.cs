using Microsoft.Win32.SafeHandles;
using System.IO.MemoryMappedFiles;

using TACTSharp.Extensions;

namespace TACTSharp
{
    public sealed class CASCIndexInstance
    {
        private readonly long indexSize;

        private readonly short archiveIndex = -1;

        private readonly string path;
        private readonly IndexHeader header;

        private readonly MemoryMappedFile indexFile;
        private readonly MemoryMappedViewAccessor accessor;
        private readonly SafeMemoryMappedViewHandle mmapViewHandle;

        private readonly int entrySize;

        private readonly int ofsStartOfEntries;
        private readonly int ofsEndOfEntries;

        public CASCIndexInstance(string path)
        {
            this.path = path;
            this.indexSize = new FileInfo(path).Length;

            // create from filestream instead so battle.net doesn't freak out
            this.indexFile = MemoryMappedFile.CreateFromFile(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
            this.accessor = indexFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            this.mmapViewHandle = accessor.SafeMemoryMappedViewHandle;

            this.accessor.Read(0, out header);

            this.entrySize = header.entrySizeBytes + header.entryOffsetBytes + header.entryKeyBytes;

            this.ofsStartOfEntries = 40;
            this.ofsEndOfEntries = (int)(this.ofsStartOfEntries + header.entriesSize);
        }

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

        unsafe public (int offset, int size, int archiveIndex) GetIndexInfo(Span<byte> eKeyTarget)
        {
            byte* fileData = null;

            var partialEKeyTarget = eKeyTarget[..header.entryKeyBytes];

            try
            {
                mmapViewHandle.AcquirePointer(ref fileData);

                byte* startOfEntries = fileData + this.ofsStartOfEntries;
                byte* endofEntries = fileData + this.ofsEndOfEntries;

                byte* lastEkey = LowerBoundEkey(startOfEntries, endofEntries, this.entrySize, partialEKeyTarget);
                if (lastEkey == startOfEntries)
                    return (-1, -1, -1);

                var entryIndex = (lastEkey - startOfEntries) / this.entrySize;
                var entryOffset = startOfEntries + (entryIndex * this.entrySize);
                var entrySpan = new ReadOnlySpan<byte>(entryOffset, this.entrySize);

                if (!entrySpan[..header.entryKeyBytes].SequenceEqual(partialEKeyTarget))
                    return (-1, -1, -1);

                var indexHigh = entrySpan[header.entryKeyBytes];
                var indexLow = entrySpan.Slice(header.entryKeyBytes + 1, 4).ReadInt32BE();
                var indexSize = entrySpan.Slice(header.entryKeyBytes + 5, header.entrySizeBytes).ReadInt32LE() - 30;

                var archiveIndex = (indexHigh << 2 | (byte)((indexLow & 0xC0000000) >> 30));
                var archiveOffset = (indexLow & 0x3FFFFFFF) + 30;

                return (archiveOffset, indexSize, archiveIndex);
            }
            finally
            {
                if (fileData != null)
                    mmapViewHandle.ReleasePointer();
            }
        }

        private unsafe struct IndexHeader
        {
            public uint headerHashSize;
            public uint headerHash;
            public ushort version;
            public byte bucketIndex;
            public byte extraBytes;
            public byte entrySizeBytes;
            public byte entryOffsetBytes;
            public byte entryKeyBytes;
            public byte entryOffsetBits;
            public ulong maxArchiveSize;
            public fixed byte padding[8];
            public uint entriesSize;
            public uint entriesHash;
        }
    }
}