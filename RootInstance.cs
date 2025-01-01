using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;

namespace TACTIndexTestCSharp
{
    // THIS IS MOSTLY YOINKED FROM BUILDBACKUP, REMAKE AT SOME POINT
    public class RootInstance
    {
        private MemoryMappedFile rootFile;
        private MemoryMappedViewAccessor accessor;
        private SafeMemoryMappedViewHandle mmapViewHandle;

        private static readonly MultiDictionary<ulong, RootEntry> entriesLookup = [];
        private static readonly MultiDictionary<uint, RootEntry> entriesFDID = [];

        [Flags]
        public enum LocaleFlags : uint
        {
            All = 0xFFFFFFFF,
            None = 0,
            Unk_1 = 0x1,
            enUS = 0x2,
            koKR = 0x4,
            Unk_8 = 0x8,
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
            enSG = 0x20000000, // custom
            plPL = 0x40000000, // custom
            All_WoW = enUS | koKR | frFR | deDE | zhCN | esES | zhTW | enGB | esMX | ruRU | ptBR | itIT | ptPT
        }

        [Flags]
        public enum ContentFlags : uint
        {
            None = 0,
            F00000001 = 0x1,            // unused in 9.0.5
            F00000002 = 0x2,            // unused in 9.0.5
            F00000004 = 0x4,            // unused in 9.0.5
            LoadOnWindows = 0x8,        // added in 7.2.0.23436
            LoadOnMacOS = 0x10,         // added in 7.2.0.23436
            LowViolence = 0x80,         // many models have this flag
            DoNotLoad = 0x100,          // unused in 9.0.5
            F00000200 = 0x200,          // unused in 9.0.5
            F00000400 = 0x400,          // unused in 9.0.5
            UpdatePlugin = 0x800,       // UpdatePlugin.dll / UpdatePlugin.dylib only
            F00001000 = 0x1000,         // unused in 9.0.5
            F00002000 = 0x2000,         // unused in 9.0.5
            F00004000 = 0x4000,         // unused in 9.0.5
            F00008000 = 0x8000,         // unused in 9.0.5
            F00010000 = 0x10000,        // unused in 9.0.5
            F00020000 = 0x20000,        // 1173911 uses in 9.0.5        
            F00040000 = 0x40000,        // 1329023 uses in 9.0.5
            F00080000 = 0x80000,        // 682817 uses in 9.0.5
            F00100000 = 0x100000,       // 1231299 uses in 9.0.5
            F00200000 = 0x200000,       // 7398 uses in 9.0.5: updateplugin, .bls, .lua, .toc, .xsd
            F00400000 = 0x400000,       // 156302 uses in 9.0.5
            F00800000 = 0x800000,       // .skel & .wwf
            F01000000 = 0x1000000,      // unused in 9.0.5
            F02000000 = 0x2000000,      // 969369 uses in 9.0.5
            F04000000 = 0x4000000,      // 1101698 uses in 9.0.5
            Encrypted = 0x8000000,      // File is encrypted
            NoNames = 0x10000000,       // No lookup hash
            UncommonRes = 0x20000000,   // added in 7.0.3.21737
            Bundle = 0x40000000,        // unused in 9.0.5
            NoCompression = 0x80000000  // sounds have this flag
        }

        public struct RootEntry
        {
            public ContentFlags contentFlags;
            public LocaleFlags localeFlags;
            public ulong lookup;
            public uint fileDataID;
            public byte[] md5;
        }

        public List<RootEntry> GetEntryByFDID(uint fileDataID)
        {
            if (entriesFDID.TryGetValue(fileDataID, out var entries))
                return entries;

            return [];
        }

        unsafe public RootInstance(string path)
        {
            this.rootFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            this.accessor = rootFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            this.mmapViewHandle = accessor.SafeMemoryMappedViewHandle;

            var namedCount = 0;
            var unnamedCount = 0;
            uint totalFiles = 0;
            uint namedFiles = 0;
            var newRoot = false;

            uint dfHeaderSize = 0;
            uint dfVersion = 0;

            byte* fileData = null;

            mmapViewHandle.AcquirePointer(ref fileData);

            var rootLength = new FileInfo(path).Length;
            var rootdata = new ReadOnlySpan<byte>(fileData, (int)rootLength);

            var header = BinaryPrimitives.ReadUInt32LittleEndian(rootdata);
            int offset = 12;

            if (header == 1296454484)
            {
                totalFiles = BinaryPrimitives.ReadUInt32LittleEndian(rootdata.Slice(4, 4));
                namedFiles = BinaryPrimitives.ReadUInt32LittleEndian(rootdata.Slice(8, 4));

                if (namedFiles == 1 || namedFiles == 2)
                {
                    // Post 10.1.7
                    dfHeaderSize = totalFiles;
                    dfVersion = namedFiles;

                    if (dfVersion == 1 || dfVersion == 2)
                    {
                        totalFiles = BinaryPrimitives.ReadUInt32LittleEndian(rootdata.Slice(12, 4));
                        namedFiles = BinaryPrimitives.ReadUInt32LittleEndian(rootdata.Slice(16, 4));
                    }

                    offset = (int)dfHeaderSize;
                }

                newRoot = true;
            }
            else
            {
                offset = 0;
            }

            var blockCount = 0;

            while (offset < rootLength)
            {
                ContentFlags contentFlags = 0;
                LocaleFlags localeFlags = 0;

                var count = BinaryPrimitives.ReadUInt32LittleEndian(rootdata.Slice(offset, 4));
                offset += 4;

                if (dfVersion == 2)
                {
                    localeFlags = (LocaleFlags)BinaryPrimitives.ReadUInt32LittleEndian(rootdata.Slice(offset, 4));
                    offset += 4;

                    var unkFlags = BinaryPrimitives.ReadUInt32LittleEndian(rootdata.Slice(offset, 4));
                    offset += 4;

                    var unkFlags2 = BinaryPrimitives.ReadUInt32LittleEndian(rootdata.Slice(offset, 4));
                    offset += 4;

                    var unkByte = rootdata[offset];
                    offset++;

                    contentFlags = (ContentFlags)(unkFlags | unkFlags2 | (uint)(unkByte << 17));
                }
                else
                {
                    contentFlags = (ContentFlags)BinaryPrimitives.ReadUInt32LittleEndian(rootdata.Slice(offset, 4));
                    offset += 4;

                    localeFlags = (LocaleFlags)BinaryPrimitives.ReadUInt32LittleEndian(rootdata.Slice(offset, 4));
                    offset += 4;
                }

                var entries = new RootEntry[count];
                var filedataIds = new int[count];

                var fileDataIndex = 0;
                for (var i = 0; i < count; ++i)
                {
                    entries[i].localeFlags = localeFlags;
                    entries[i].contentFlags = contentFlags;

                    var fileDataIDOffset = BinaryPrimitives.ReadInt32LittleEndian(rootdata.Slice(offset, 4));
                    offset += 4;

                    filedataIds[i] = fileDataIndex + fileDataIDOffset;
                    entries[i].fileDataID = (uint)filedataIds[i];
                    fileDataIndex = filedataIds[i] + 1;
                }

                if (!newRoot)
                {
                    for (var i = 0; i < count; ++i)
                    {
                        entries[i].md5 = rootdata.Slice(offset, 16).ToArray();
                        offset += 16;

                        entries[i].lookup = BinaryPrimitives.ReadUInt64LittleEndian(rootdata.Slice(offset, 8));
                        offset += 8;

                        entriesLookup.Add(entries[i].lookup, entries[i]);
                        entriesFDID.Add(entries[i].fileDataID, entries[i]);
                    }
                }
                else
                {
                    for (var i = 0; i < count; ++i)
                    {
                        entries[i].md5 = rootdata.Slice(offset, 16).ToArray();
                        offset += 16;
                    }

                    for (var i = 0; i < count; ++i)
                    {
                        if (contentFlags.HasFlag(ContentFlags.NoNames))
                        {
                            entries[i].lookup = 0;
                            unnamedCount++;
                        }
                        else
                        {
                            entries[i].lookup = BinaryPrimitives.ReadUInt64LittleEndian(rootdata.Slice(offset, 8));
                            offset += 8;
                            entriesLookup.Add(entries[i].lookup, entries[i]);
                            namedCount++;
                        }

                        entriesFDID.Add(entries[i].fileDataID, entries[i]);
                    }
                }

                blockCount++;
            }

            if ((namedFiles > 0) && namedFiles != namedCount)
                throw new Exception("Didn't read correct amount of named files! Read " + namedCount + " but expected " + namedFiles);

            if ((totalFiles > 0) && totalFiles != (namedCount + unnamedCount))
                throw new Exception("Didn't read correct amount of total files! Read " + (namedCount + unnamedCount) + " but expected " + totalFiles);
        }
    }
}
