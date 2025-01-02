using Microsoft.Win32.SafeHandles;
using System.IO.MemoryMappedFiles;

namespace TACTSharp
{
    public class EncodingInstance
    {
        private readonly MemoryMappedFile encodingFile;
        private readonly MemoryMappedViewAccessor accessor;
        private readonly SafeMemoryMappedViewHandle mmapViewHandle;
        private EncodingHeader header;
        private string[] ESpecs = [];
        private readonly Lock ESpecLock = new();

        public EncodingInstance(string path)
        {
            this.encodingFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            this.accessor = encodingFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            this.mmapViewHandle = accessor.SafeMemoryMappedViewHandle;

            this.header = ReadHeader();

            if (this.header.version != 1)
                throw new Exception("Unsupported encoding version");

            if (this.header.hashSizeCKey != 0x10)
                throw new Exception("Unsupported CKey hash size");

            if (this.header.hashSizeEKey != 0x10)
                throw new Exception("Unsupported EKey hash size");
        }

        unsafe private EncodingHeader ReadHeader()
        {
            byte* headerData = null;

            mmapViewHandle.AcquirePointer(ref headerData);

            var header = new ReadOnlySpan<byte>(headerData, 22);

            if (header[0] != 0x45 || header[1] != 0x4E)
                throw new Exception("Invalid encoding file magic");

            return new EncodingHeader
            {
                version = header[2],
                hashSizeCKey = header[3],
                hashSizeEKey = header[4],
                CKeyPageSizeKB = (ushort)((header[5] << 8) | header[6]),
                EKeySpecPageSizeKB = (ushort)((header[7] << 8) | header[8]),
                CEKeyPageTablePageCount = (uint)((header[9] << 24) | (header[10] << 16) | (header[11] << 8) | header[12]),
                EKeySpecPageTablePageCount = (uint)((header[13] << 24) | (header[14] << 16) | (header[15] << 8) | header[16]),
                unk11 = header[17],
                ESpecBlockSize = (uint)((header[18] << 24) | (header[19] << 16) | (header[20] << 8) | header[21])
            };
        }

        unsafe static private byte* LowerBoundEkey(byte* begin, byte* end, long dataSize, ReadOnlySpan<byte> needle)
        {
            var count = (end - begin) / dataSize;

            while (count > 0)
            {
                var it = begin;
                var step = count / 2;
                it += step * dataSize;

                if (new ReadOnlySpan<byte>(it, needle.Length).SequenceCompareTo(needle) <= 0)
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

        public bool TryGetEKeys(Span<byte> cKeyTarget, out EncodingResult? result)
        {
            result = GetEKeys(cKeyTarget);
            return result.HasValue;
        }

        public unsafe EncodingResult? GetEKeys(Span<byte> cKeyTarget)
        {
            byte* pageData = null;
            mmapViewHandle.AcquirePointer(ref pageData);

            var eKeyPageSize = header.EKeySpecPageSizeKB * 1024;

            byte* startOfPageKeys = pageData + 22 + header.ESpecBlockSize;
            byte* endOfPageKeys = startOfPageKeys + (header.CEKeyPageTablePageCount * 32);

            byte* lastPageKey = LowerBoundEkey(startOfPageKeys, endOfPageKeys, 32, cKeyTarget);
            if (lastPageKey == startOfPageKeys)
                return null;

            var pageIndex = ((lastPageKey - startOfPageKeys) / 32) - 1;
            var startOfPageEKeys = endOfPageKeys + ((int)pageIndex * eKeyPageSize);
            var targetPage = new ReadOnlySpan<byte>(startOfPageEKeys, eKeyPageSize);
            var offs = 0;
            while (true)
            {
                if (offs >= eKeyPageSize)
                    break;

                var eKeyCount = targetPage[offs];
                offs++;

                if (eKeyCount == 0)
                    continue;

                if (targetPage.Slice(offs + 5, header.hashSizeCKey).SequenceEqual(cKeyTarget))
                {
                    var decodedFileSize = (ulong)targetPage.Slice(offs, 5).ReadInt40BE();
                    offs += 5;

                    offs += header.hashSizeCKey; // +ckey

                    var eKeys = new List<byte[]>(eKeyCount);
                    for (var i = 0; i < eKeyCount; i++)
                    {
                        var eKey = new byte[header.hashSizeEKey];
                        targetPage.Slice(offs, header.hashSizeEKey).CopyTo(eKey);
                        offs += header.hashSizeEKey;
                        eKeys.Add(eKey);
                    }

                    return new EncodingResult()
                    {
                        eKeyCount = eKeyCount,
                        decodedFileSize = decodedFileSize,
                        eKeys = eKeys
                    };
                }
                else
                {
                    offs += 5; //+size
                    offs += header.hashSizeCKey; // +ckey
                    offs += header.hashSizeEKey * eKeyCount; // +ekeys
                }
            }

            Console.WriteLine("EKey not found for CKey " + Convert.ToHexStringLower(cKeyTarget) + " but should have been in page index " + pageIndex);

            return null;
        }

        public unsafe (string eSpec, ulong encodedFileSize)? GetESpec(Span<byte> eKeyTarget)
        {
            byte* pageData = null;
            mmapViewHandle.AcquirePointer(ref pageData);
            lock (ESpecLock)
            {
                if (ESpecs.Length == 0)
                {
                    var timer = new System.Diagnostics.Stopwatch();
                    timer.Start();
                    var eSpecs = new List<string>();

                    var eSpecTable = new ReadOnlySpan<byte>(pageData + 22, (int)header.ESpecBlockSize);
                    var eSpecOffs = 0;
                    while (true)
                    {
                        if (eSpecOffs >= header.ESpecBlockSize)
                            break;

                        var eSpecString = eSpecTable[eSpecOffs..].ReadNullTermString();
                        eSpecOffs += eSpecString.Length + 1;
                        eSpecs.Add(eSpecString);
                    }

                    ESpecs = [.. eSpecs];
                    timer.Stop();
                    Console.WriteLine("Loaded " + ESpecs.Length + " ESpecs in " + timer.Elapsed.TotalMilliseconds + "ms");
                }
            }

            var eKeyPageSize = header.EKeySpecPageSizeKB * 1024;

            byte* startOfESpecPageKeys = pageData + 22 + header.ESpecBlockSize + (header.CEKeyPageTablePageCount * 32) + (header.CEKeyPageTablePageCount * (header.CKeyPageSizeKB * 1024));
            byte* endOfESpecPageKeys = startOfESpecPageKeys + (header.EKeySpecPageTablePageCount * 32);

            byte* firstESpecPageKey = LowerBoundEkey(startOfESpecPageKeys, endOfESpecPageKeys, 32, eKeyTarget);
            if (firstESpecPageKey == startOfESpecPageKeys)
                return null;

            var pageIndex = ((firstESpecPageKey - startOfESpecPageKeys) / 32) - 1;
            var startOfPageESpec = endOfESpecPageKeys + ((int)pageIndex * eKeyPageSize);
            var targetPage = new ReadOnlySpan<byte>(startOfPageESpec, eKeyPageSize);
            var offs = 0;
            while (true)
            {
                if (offs >= eKeyPageSize)
                    break;

                if (targetPage.Slice(offs, header.hashSizeEKey).SequenceEqual(eKeyTarget))
                {
                    offs += header.hashSizeEKey; // +ekey

                    var eSpecIndex = targetPage.Slice(offs, 4).ReadInt32BE();
                    offs += 4;

                    var encodedFileSize = (ulong)targetPage.Slice(offs, 5).ReadInt40BE();

                    return (ESpecs[eSpecIndex], encodedFileSize);
                }
                else
                {
                    offs += header.hashSizeEKey; // +ekey
                    offs += 4; // +encodedFileSize
                    offs += 5; // +encodedFileSize
                }
            }

            Console.WriteLine("ESpec not found for EKey " + Convert.ToHexStringLower(eKeyTarget) + " but should have been in page index " + pageIndex);
            return null;
        }

        private unsafe struct EncodingHeader
        {
            public fixed byte magic[2];
            public byte version;
            public byte hashSizeEKey;
            public byte hashSizeCKey;
            public ushort CKeyPageSizeKB;
            public ushort EKeySpecPageSizeKB;
            public uint CEKeyPageTablePageCount;
            public uint EKeySpecPageTablePageCount;
            public byte unk11;
            public uint ESpecBlockSize;
        }

        public struct EncodingResult
        {
            public byte eKeyCount;
            public ulong decodedFileSize;
            public List<byte[]> eKeys;
        }
    }
}
