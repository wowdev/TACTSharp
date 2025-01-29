using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using TACTSharp.Extensions;

namespace TACTSharp
{
    public class RootInstance
    {
        private readonly Page[] _pages;
        private readonly Dictionary<ulong, (int, int)> _hashes = [];

        [Flags]
        public enum LocaleFlags : uint
        {
            enUS = 0x2,
            koKR = 0x4,
            frFR = 0x10,
            deDE = 0x20,
            zhCN = 0x40,
            esES = 0x80,
            zhTW = 0x100,
            enGB = 0x200,
            enCN = 0x400,
            enTW = 0x800,
            esMX = 0x1000,
            ruRU = 0x2000,
            ptBR = 0x4000,
            itIT = 0x8000,
            ptPT = 0x10000,
        }

        private static readonly LocaleFlags AllWoW = LocaleFlags.enUS | LocaleFlags.koKR | LocaleFlags.frFR
            | LocaleFlags.deDE | LocaleFlags.zhCN | LocaleFlags.esES | LocaleFlags.zhTW | LocaleFlags.enGB
            | LocaleFlags.esMX | LocaleFlags.ruRU | LocaleFlags.ptBR | LocaleFlags.itIT | LocaleFlags.ptPT;

        [Flags]
        public enum ContentFlags : uint
        {
            None = 0,
            LoadOnWindows = 0x8,        // added in 7.2.0.23436
            LoadOnMacOS = 0x10,         // added in 7.2.0.23436
            LowViolence = 0x80,         // many models have this flag
            DoNotLoad = 0x100,          // unused in 9.0.5
            UpdatePlugin = 0x800,       // UpdatePlugin.dll / UpdatePlugin.dylib only
            Encrypted = 0x8000000,      // File is encrypted
            NoNames = 0x10000000,       // No lookup hash
            UncommonRes = 0x20000000,   // added in 7.0.3.21737
            Bundle = 0x40000000,        // unused in 9.0.5
            NoCompression = 0x80000000  // sounds have this flag
        }

        public unsafe RootInstance(string filePath)
        {
            var rootFile = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            var accessor = rootFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            var mmapViewHandle = accessor.SafeMemoryMappedViewHandle;

            byte* rawData = null;
            mmapViewHandle.AcquirePointer(ref rawData);

            var fileSize = new FileInfo(filePath).Length;
            var fileData = new ReadOnlySpan<byte>(rawData, (int)fileSize);

            var magic = fileData.ReadUInt32LE();
            var (format, version, headerSize, totalFileCount, namedFileCount) = magic switch
            {
                0x4D465354 => ParseMFST(fileData),
                _ => (Format.Legacy, 0, 0, 0, 0)
            };

            // Skip the header.
            fileData = fileData[headerSize..];

            var pages = new List<Page>();
            while (fileData.Length != 0)
            {
                var recordCount = fileData.ReadInt32LE();
                fileData = fileData[4..];

                var (contentFlags, localeFlags) = ParseManifestPageFlags(ref fileData, version);

                // No records in this file.
                if (recordCount == 0)
                    continue;

                // Calculate block size
                var blockSize = 4 * recordCount; // FileDataID[n]
                blockSize += MD5.Length * recordCount;
                if (format == Format.Legacy || !contentFlags.HasFlag(ContentFlags.NoNames))
                    blockSize += 8 * recordCount;

                var blockData = fileData[..blockSize];
                fileData = fileData[blockSize..];

                // Determine conditions related to keeping this page.
                var localeSkip = !localeFlags.HasFlag(AllWoW) && !localeFlags.HasFlag(Settings.Locale);
                var contentSkip = contentFlags.HasFlag(ContentFlags.LowViolence);

                if (localeSkip || contentSkip)
                    continue;

                // Read a FDID delta array from the file (+1 implied) and adjust instantly.
                var fdids = blockData.ReadUInt32LE(recordCount);
                for (var i = 1; i < fdids.Length; ++i)
                    fdids[i] += fdids[i - 1] + 1;

                // Get a span over the actual record block of this page
                var recordData = blockData[(4 * recordCount)..];
                var (records, nameHashRange) = format switch
                {
                    Format.Legacy => ParseLegacy(recordData, recordCount, fdids),
                    Format.MFST => ParseManifest(recordData, recordCount, contentFlags, fdids),
                    _ => throw new UnreachableException()
                };

                var nameHashes = nameHashRange.AsEnumerator(recordData);
                var page = new Page(records, contentFlags, localeFlags);

                // TODO: This happens. What do we do?!
                // System.ArgumentException: An item with the same key has already been added. Key: 11470997404861800962
                for (var i = 0; i < nameHashes.Count; ++i)
                    _hashes.TryAdd(nameHashes[i], (pages.Count, i));

                pages.Add(page);
            }
            
            _pages = [.. pages];
            _hashes.TrimExcess();
        }

        private static (ContentFlags contentFlags, LocaleFlags localeFlags) ParseManifestPageFlags(ref ReadOnlySpan<byte> fileData, int version)
        {
            switch (version)
            {
                case 0:
                case 1:
                    {
                        var contentFlags = (ContentFlags)fileData.ReadUInt32LE();
                        var localeFlags = (LocaleFlags)fileData[4..].ReadUInt32LE();

                        fileData = fileData[(4 + 4) ..];

                        return (contentFlags, localeFlags);
                    }
                case 2:
                    {
                        var localeFlags = (LocaleFlags)fileData.ReadUInt32LE();

                        var unk1 = fileData[4..].ReadUInt32LE();
                        var unk2 = fileData[8..].ReadUInt32LE();
                        var unk3 = ((uint)fileData[12]) << 17;

                        fileData = fileData[13..];

                        var contentFlags = (ContentFlags)(unk1 | unk2 | unk3);

                        return (contentFlags, localeFlags);
                    }
                default:
                    throw new NotImplementedException($"MFST version {version} is not supported");
            }
        }

        /// <summary>
        /// Finds a file given a file data ID.
        /// </summary>
        /// <param name="fileDataID">The file data ID to look for.</param>
        /// <returns>An optional record.</returns>
        public ref Record FindFileDataID(uint fileDataID)
        {
            foreach (ref readonly var page in _pages.AsSpan())
            {
                var fdidIndex = page.Records.BinarySearch((ref Record record, int fileDataID) => ((int) record.FileDataID - fileDataID).ToOrdering(), (int) fileDataID);
                if (fdidIndex < 0)
                    continue;

                return ref page.Records.UnsafeIndex(fdidIndex);
            }

            return ref Unsafe.NullRef<Record>();
        }

        /// <summary>
        /// Finds a record as identified by its name hash (also known as lookup).
        /// </summary>
        /// <param name="nameHash">The hash of the file's complete path in the game's file structure.</param>
        /// <returns>An optional record.</returns>
        public ref Record FindHash(ulong nameHash)
        {
            if (_hashes.TryGetValue(nameHash, out (int pageIndex, int recordIndex) value))
            {
                var page = _pages.UnsafeIndex(value.pageIndex);
                return ref page.Records.UnsafeIndex(value.recordIndex);
            }

            return ref Unsafe.NullRef<Record>();
        }

        private static (Record[], NameHashRange) ParseLegacy(ReadOnlySpan<byte> dataStream, int recordCount, uint[] fdids)
        {
            var records = GC.AllocateUninitializedArray<Record>(recordCount);
            for (var i = 0; i < records.Length; ++i)
            {
                var contentKey = new MD5(dataStream[.. MD5.Length]);
                dataStream = dataStream[(8 + MD5.Length) ..]; // Skip the name hash, we parse it externally

                records[i] = new(contentKey, fdids[i]);
            }

            return (records, new NameHashRange(0, recordCount * (MD5.Length + 8), MD5.Length + 8, MD5.Length));
        }

        private static (Record[], NameHashRange) ParseManifest(ReadOnlySpan<byte> dataStream, int recordCount, ContentFlags contentFlags, uint[] fdids)
        {
            // Promote the check to an integer (0 or 1) and then multiply by 8. Equivalent to (bool ? 8 : 0) * recordCount.
            var nameHashSize = (!contentFlags.HasFlag(ContentFlags.NoNames)).UnsafePromote() << 3;
            nameHashSize *= recordCount;

            var ckr = new Range(0, recordCount * MD5.Length); // Content key range
            var nhr = new Range(ckr.End.Value, ckr.End.Value + nameHashSize); // Name hash range

            var contentKeys = MemoryMarshal.Cast<byte, MD5>(dataStream[ckr]);

            var records = GC.AllocateUninitializedArray<Record>(recordCount);
            for (var i = 0; i < recordCount; ++i)
                records[i] = new(contentKeys[i], fdids[i]);

            return (records, new NameHashRange(nhr.Start.Value, nhr.End.Value, sizeof(ulong)));
        }

        private static (Format, int Version, int HeaderSize, int TotalFileCount, int NamedFileCount) ParseMFST(ReadOnlySpan<byte> dataStream)
        {
            // Skip over magic at dataStream[0]
            Debug.Assert(dataStream.ReadUInt32LE() == 0x4D465354);

            var headerSize = dataStream[4..].ReadInt32LE();
            var version = dataStream[8..].ReadInt32LE();
            if (headerSize > 1000)
                return (Format.MFST, 0, 4 * 4, headerSize, version);

            var totalFileCount = dataStream[12..].ReadInt32LE();
            var namedFileCount = dataStream[16..].ReadInt32LE();

            return (Format.MFST, version, headerSize, totalFileCount, namedFileCount);
        }

        [InlineArray(16)]
        public struct MD5
        {
            public const int Length = 16;

            private byte _element;

            public MD5(ReadOnlySpan<byte> sourceData) => sourceData.CopyTo(MemoryMarshal.CreateSpan(ref _element, Length));

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Span<byte> AsSpan() => MemoryMarshal.CreateSpan(ref _element, Length);
        }

        public readonly struct Record(MD5 contentKey, uint fileDataID)
        {
            public readonly MD5 ContentKey = contentKey;
            public readonly uint FileDataID = fileDataID;
        }

        private readonly record struct Page(Record[] Records, ContentFlags ContentFlags, LocaleFlags LocaleFlags);

        private enum Format
        {
            Legacy,
            MFST
        }

        // A strided range for name hashes.
        private readonly struct NameHashRange(int start, int end, int stride, int offset = 0)
        {
            public readonly int Start = start;
            public readonly int End = end;
            public readonly int Stride = stride;
            public readonly int Offset = offset;

            public NameHashEnumerator AsEnumerator(ReadOnlySpan<byte> data)
                => new(data[Start .. End], Stride, Offset);
        }

        // Enumerates over a span with a specific stride.
        private readonly ref struct NameHashEnumerator(ReadOnlySpan<byte> Data, int Stride, int Offset)
        {
            private readonly ReadOnlySpan<byte> _data = Data;
            public readonly int Count => _data.Length / Stride;

            public readonly ulong this[int index] => _data[((Stride * index) + Offset) ..].ReadUInt64LE();
        }
    }
}
