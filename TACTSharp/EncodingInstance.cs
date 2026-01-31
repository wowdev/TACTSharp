using Microsoft.Win32.SafeHandles;

using System.Diagnostics;
using System.IO.MemoryMappedFiles;

using TACTSharp.Extensions;

using static TACTSharp.Extensions.BinarySearchExtensions;

namespace TACTSharp
{
    public class EncodingInstance
    {
        private readonly string _filePath;
        private readonly int _fileSize;
        private readonly MemoryMappedFile encodingFile;
        private readonly MemoryMappedViewAccessor accessor;
        private readonly SafeMemoryMappedViewHandle mmapViewHandle;
        private EncodingSchema _header;
        private string[] _encodingSpecs = [];
        private readonly Lock _encodingSpecsLock = new();

        public static readonly EncodingResult Zero = new(0, [], 0);

        public EncodingInstance(string filePath, int fileSize = 0)
        {
            _filePath = filePath;

            if (fileSize != 0)
                _fileSize = fileSize;
            else
                _fileSize = (int)new FileInfo(filePath).Length;

            this.encodingFile = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            this.accessor = encodingFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            this.mmapViewHandle = accessor.SafeMemoryMappedViewHandle;

            (var version, _header) = ReadHeader();
            if (version != 1)
                throw new Exception("Unsupported encoding version");
        }

        public EncodingInstance(string path) : this(path, (int)new FileInfo(path).Length) { }

        unsafe private (byte Version, EncodingSchema Schema) ReadHeader()
        {
            byte* headerData = null;
            try
            {
                mmapViewHandle.AcquirePointer(ref headerData);

                var header = new ReadOnlySpan<byte>(headerData, 22);
                if (header[0] != 0x45 || header[1] != 0x4E)
                    throw new Exception("Invalid encoding file magic");

                var version = header[0x02];
                var hashSizeCKey = header[0x03];
                var hashSizeEKey = header[0x04];
                var ckeyPageSize = header[0x05..].ReadUInt16BE() * 1024;
                var ekeyPageSize = header[0x07..].ReadUInt16BE() * 1024;
                var ckeyPageCount = header[0x09..].ReadInt32BE();
                var ekeyPageCount = header[0x0D..].ReadInt32BE();
                Debug.Assert(header[0x11] == 0x00);
                var especBlockSize = header[0x12..].ReadInt32BE();

                var especRange = new Range(22, 22 + especBlockSize);
                var ckeyHeaderRange = new Range(especRange.End, especRange.End.Value + ckeyPageCount * (hashSizeCKey + 0x10));
                var ckeyRange = new Range(ckeyHeaderRange.End, ckeyHeaderRange.End.Value + ckeyPageSize * ckeyPageCount);
                var ekeyHeaderRange = new Range(ckeyRange.End, ckeyRange.End.Value + ekeyPageCount * (hashSizeEKey + 0x10));
                var ekeyRange = new Range(ekeyHeaderRange.End, ekeyHeaderRange.End.Value + ekeyPageSize * ekeyPageCount);

                return (version, new EncodingSchema(
                    hashSizeCKey,
                    hashSizeEKey,
                    especRange,
                    new(ckeyHeaderRange, ckeyRange, hashSizeCKey + 0x10, ckeyPageSize),
                    new(ekeyHeaderRange, ekeyRange, hashSizeEKey + 0x10, ekeyPageSize)
                ));
            }
            finally
            {
                if (headerData != null)
                    mmapViewHandle.ReleasePointer();
            }
        }

        /// <summary>
        /// Returns the result of a lookup for a specific content key.
        /// </summary>
        /// <param name="cKeyTarget">The content key to look for.</param>
        /// <returns></returns>
        public unsafe EncodingResult FindContentKey(ReadOnlySpan<byte> cKeyTarget)
        {
            byte* pageData = null;
            mmapViewHandle.AcquirePointer(ref pageData);

            ReadOnlySpan<byte> fileData = new(pageData, _fileSize);
            var targetPage = _header.CEKey.ResolvePage(fileData, cKeyTarget);
            while (targetPage.Length != 0)
            {
                var keyCount = targetPage[0];
                var recordData = targetPage.Slice(1, 5 + _header.CKeySize + _header.EKeySize * keyCount);

                // Advance iteration
                targetPage = targetPage[(recordData.Length + 1)..];

                if (keyCount == 0)
                    continue;

                var recordContentKey = recordData.Slice(5, _header.CKeySize);
                var recordEncodingKeys = recordData.Slice(5 + _header.CKeySize, _header.EKeySize * keyCount);

                if (recordContentKey.SequenceEqual(cKeyTarget))
                {
                    var decodedFileSize = (ulong)recordData.ReadInt40BE();
                    return new EncodingResult(keyCount, recordEncodingKeys, decodedFileSize);
                }
            }

            return EncodingResult.Empty;
        }

        public unsafe (string eSpec, ulong encodedFileSize) GetESpec(ReadOnlySpan<byte> eKeyTarget)
        {
            byte* pageData = null;
            mmapViewHandle.AcquirePointer(ref pageData);

            ReadOnlySpan<byte> fileData = new(pageData, _fileSize);

            lock (_encodingSpecsLock)
            {
                if (_encodingSpecs.Length == 0)
                {
                    var timer = new System.Diagnostics.Stopwatch();
                    timer.Start();
                    var eSpecs = new List<string>();

                    var eSpecTable = fileData[_header.EncodingSpec];

                    while (eSpecTable.Length != 0)
                    {
                        var eSpecString = eSpecTable.ReadNullTermString();
                        eSpecTable = eSpecTable[(eSpecString.Length + 1)..];
                        eSpecs.Add(eSpecString);
                    }

                    _encodingSpecs = [.. eSpecs];
                    timer.Stop();

                    if (Settings.LogLevel <= TSLogLevel.Info)
                        Console.WriteLine("Loaded " + _encodingSpecs.Length + " ESpecs in " + timer.Elapsed.TotalMilliseconds + "ms");
                }
            }

            var targetPage = _header.EKeySpec.ResolvePage(fileData, eKeyTarget);
            while (targetPage.Length != 0)
            {
                if (targetPage[.._header.EKeySize].SequenceEqual(eKeyTarget))
                {
                    var specIndex = targetPage.Slice(_header.EKeySize).ReadInt32BE();
                    var encodedFileSize = (ulong)targetPage.Slice(_header.EKeySize + 4).ReadInt40BE();

                    return (_encodingSpecs[specIndex], encodedFileSize);
                }
                else
                {
                    targetPage = targetPage[(_header.EKeySize + 4 + 5)..];
                }
            }

            return (string.Empty, 0);
        }

        public readonly struct EncodingResult(byte keyCount, ReadOnlySpan<byte> keys, ulong fileSize)
        {
            private readonly byte[] _keys = keys.ToArray();
            private readonly int _keyLength = keys.Length / keyCount;

            public readonly ulong DecodedFileSize = fileSize;
            public readonly ReadOnlySpan<byte> this[int index] => _keys.AsSpan().Slice(_keyLength * index, _keyLength);
            public readonly int Length => keyCount;

            public static readonly EncodingResult Empty = new(0, [], 0);

            public static implicit operator bool(EncodingResult self) => self._keyLength != 0;
        }

        private readonly record struct EncodingSchema(int CKeySize, int EKeySize, Range EncodingSpec, TableSchema CEKey, TableSchema EKeySpec);
        private readonly struct TableSchema(Range header, Range pages, int headerEntrySize, int pageSize)
        {
            private readonly Range _header = header;
            private readonly Range _pages = pages;
            private readonly int _headerEntrySize = headerEntrySize;
            private readonly int _pageSize = pageSize;

            public readonly ReadOnlySpan<byte> ResolvePage(ReadOnlySpan<byte> fileData, ReadOnlySpan<byte> xKey)
            {
                var entryIndex = fileData[_header].WithStride(_headerEntrySize).LowerBound((itr, needle) =>
                {
                    var ordering = itr[..needle.Length].SequenceCompareTo(needle).ToOrdering();
                    return ordering switch
                    {
                        Ordering.Equal => Ordering.Less,
                        _ => ordering
                    };
                }, xKey) - 1;

                if (entryIndex * _headerEntrySize > _header.End.Value)
                    return [];

                return fileData.Slice(_pages.Start.Value + _pageSize * entryIndex, _pageSize);
            }
        }
    }
}
