using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using TACTSharp.Interfaces;
using static TACTSharp.RootInstance;

namespace TACTSharp
{
    // Note: This implementation of TVFS is unfinished and only sorta works for WoW TVFS. It won't work at all on other games nor is it a good implementation of TVFS. 
    public class TVFSInstance : IRootInstance
    {
        private readonly Dictionary<string, FileSpan> rootManifestEntries = [];
        private readonly ConcurrentDictionary<uint, RootEntry> entriesFDID = [];
        private readonly ConcurrentDictionary<uint, List<RootEntry>> entriesFDIDFull = [];

        private LoadMode loadedWith;

        public List<RootEntry> GetEntriesByFDID(uint fileDataID)
        {
            if (loadedWith == LoadMode.Normal)
            {
                if (entriesFDID.TryGetValue(fileDataID, out var entry))
                    return [entry];
            }
            else
            {
                if (entriesFDIDFull.TryGetValue(fileDataID, out var entries))
                    return entries;
            }

            return [];
        }

        public List<RootEntry> GetEntriesByLookup(ulong lookup)
        {
            // TVFS does not support lookups?
            return [];
        }

        public uint[] GetAvailableFDIDs()
        {
            return [.. entriesFDIDFull.Keys];
        }

        public ulong[] GetAvailableLookups()
        {
            // TVFS does not support lookups?
            return [];
        }

        public bool FileExists(ulong lookup)
        {
            // TVFS does not support lookups?
            return false;
        }

        public bool FileExists(uint fileDataID)
        {
            if (loadedWith == LoadMode.Normal)
                return entriesFDID.ContainsKey(fileDataID);
            else
                return entriesFDIDFull.ContainsKey(fileDataID);
        }

        public TVFSInstance(Dictionary<uint, (string cKey, string eKey)> vfsKeys, Dictionary<uint, (uint decodedSize, uint encodedSize)> vfsSizes, Config cdnConfig, IndexInstance groupIndex, IndexInstance? fileIndex, CDN cdn, Settings settings)
        {
            if (!vfsKeys.TryGetValue(1, out var vfsRootKeys))
                throw new Exception("Root VFS (vfs-1) not found in VFS keys");

            var sw = Stopwatch.StartNew();
            var rootVFSBytes = GetTVFSManifestBytes(vfsRootKeys.eKey, vfsSizes[1].decodedSize, groupIndex, fileIndex, cdn, cdnConfig);
            ParseTVFSManifest(rootVFSBytes, 0, 0);

            // TODO: I feel like this step shouldn't be neccesary so I'm still missing something here in regards to partial/full ekeys
            var partialEKeyToFullEKey = new Dictionary<string, byte[]>();
            foreach (var vfsKey in vfsKeys.Values)
            {
                var eKeySpan = Convert.FromHexString(vfsKey.eKey).AsSpan();
                var partialEKey = Convert.ToHexStringLower(eKeySpan.Slice(0, 9));
                partialEKeyToFullEKey[partialEKey] = eKeySpan.ToArray();
            }

            Parallel.ForEach(rootManifestEntries, entry =>
            {
                // .root goes to old TACT root?
                if (entry.Key == ".root")
                    return;

                var flagsParsed = Convert.FromHexString(entry.Key).AsSpan();
                var localeFlags = (LocaleFlags)BinaryPrimitives.ReadInt32BigEndian(flagsParsed);
                var contentFlags = (ContentFlags)(entry.Key.Length == 16 ? BinaryPrimitives.ReadInt32BigEndian(flagsParsed.Slice(4)) : BinaryPrimitives.ReadInt16BigEndian(flagsParsed.Slice(4)));

                var localeSkip = !localeFlags.HasFlag(LocaleFlags.All_WoW) && !localeFlags.HasFlag(settings.Locale);
                var contentSkip = (contentFlags & ContentFlags.LowViolence) != 0;
                var skipFile = localeSkip || contentSkip;

                loadedWith = settings.RootMode;

                var fullMode = settings.RootMode == LoadMode.Full;
                if (fullMode)
                    skipFile = false;

                if (skipFile)
                {
                    if (Settings.LogLevel <= TSLogLevel.Debug)
                        Console.WriteLine("Skipping TVFS manifest " + entry.Key + " with locale flags " + localeFlags + " and content flags " + contentFlags);

                    return;
                }

                var partialEKey = Convert.ToHexStringLower(entry.Value.EKey).Substring(0, 18);
                if (!partialEKeyToFullEKey.TryGetValue(partialEKey, out var fullEKey))
                {
                    if (Settings.LogLevel <= TSLogLevel.Warn)
                        Console.WriteLine("Could not find full EKey for partial EKey " + Convert.ToHexStringLower(entry.Value.EKey) + " of file " + entry.Key + ", skipping");

                    return;
                }

                var tvfsBytes = GetTVFSManifestBytes(Convert.ToHexStringLower(fullEKey), entry.Value.ContentLength, groupIndex, fileIndex, cdn, cdnConfig);

                ParseTVFSManifest(tvfsBytes, (int)contentFlags, (int)localeFlags);
            });

            Console.WriteLine("Done loading TVFS manifest in " + (int)sw.Elapsed.TotalMilliseconds + "ms, loaded " + (loadedWith == LoadMode.Normal ? entriesFDID.Count : entriesFDIDFull.Count) + " entries");
        }

        // TODO: I feel like this should be a generic function elsewhere, it exists in Build but Build isn't a required path into TACTSharp so we can't rely on it there
        public static byte[] GetTVFSManifestBytes(string eKeyString, uint decodedSize, IndexInstance groupIndex, IndexInstance? fileIndex, CDN cdn, Config cdnConfig)
        {
            var eKey = Convert.FromHexString(eKeyString);

            var (offset, size, archiveIndex) = groupIndex.GetIndexInfo(eKey);
            if (offset != -1)
                return cdn.GetFileFromArchive(Convert.ToHexStringLower(eKey), cdnConfig!.Values["archives"][archiveIndex], offset, size, decodedSize, true);

            if (fileIndex != null)
            {
                var fi = fileIndex.GetIndexInfo(eKey);
                if (fi.size != -1)
                    return cdn.GetFile("data", Convert.ToHexStringLower(eKey), (ulong)fi.size, decodedSize, true);
            }

            if (Settings.LogLevel <= TSLogLevel.Warn)
                Console.WriteLine("File index not available, fetching from CDN regardless");

            return cdn.GetFile("data", Convert.ToHexStringLower(eKey), 0, decodedSize, true);
        }

        // As per https://wowdev.wiki/TVFS
        public struct FileManifestHeader
        {
            public uint Magic;                  // Always TVFS
            public byte Version;                // Format version. Always 1.
            public byte HeaderSize;             // Header size in bytes.
            public byte EKeySize;               // EKey size in bytes. Should be 9.
            public byte PKeySize;               // PKey size in bytes. Should be 9.
            public FileManifestFlags Flags;     // FileManifestFlags, see below.
            public uint PathTableOffset;        // Offset to the path table.
            public uint PathTableSize;          // Size of the path table.
            public uint FileTableOffset;        // Offset to the VFS table.
            public uint FileTableSize;          // Size of the VFS table.
            public uint ContainerTableOffset;   // Offset to the container file table.
            public uint ContainerTableSize;     // Size of the container file table.
            public ushort PathTableMaxDepth;    // The maximum depth of the path prefix tree stored in the path table.
            public uint ESpecTableOffset;       // Offset to ESpec table. Only present if write support (flags & 2) is enabled.
            public uint ESpecTableSize;         // Size of ESpec table. Only present if write support (flags & 2) is enabled.

            public int ContainerTableOffsetSize;   // Dynamic size of offsets in the container file table.
            public int ESpecTableOffsetSize;       // Dynamic size of offsets in the ESpec table.
        }
        public readonly struct FileSpan(byte[] eKey, uint contentOffset, uint contentLength)
        {
            public readonly byte[] EKey = eKey; // note: partial
            public readonly uint ContentOffset = contentOffset;
            public readonly uint ContentLength = contentLength;

            public override string ToString()
            {
                return "FileSpan(EKey=" + Convert.ToHexStringLower(EKey) + ", ContentOffset=" + ContentOffset + ", ContentLength=" + ContentLength + ")";
            }
        }

        // I would use RootEntry here but TVFS doesn't have lookups (yet)?
        public readonly struct TVFSRootEntry(/*uint localeFlags, uint contentFlags,*/uint fdid, MD5 cKey)
        {
            //public readonly uint LocaleFlags = localeFlags;
            //public readonly uint ContentFlags = contentFlags;
            public readonly uint FileDataId = fdid;
            public readonly MD5 CKey = cKey;
        }

        [Flags]
        public enum FileManifestFlags : uint
        {
            INCLUDE_CKEY = 0x1,         // Include C-key in content file record.
            WRITE_SUPPORT = 0x2,        // Write support. Include a table of encoding specifiers. This is required for writing files to the underlying storage. This bit is implied by the patch-support bit.
            PATCH_SUPPORT = 0x4,        // Patch support. Include patch records in the content file records.
            LOWERCASE_MANIFEST = 0x8    // Lowercase manifest. All paths in the path table have been converted to ASCII lowercase (i.e. [A-Z] converted to [a-z]).
        }

        public static int GetTableEntrySize(int tableSize)
        {
            return tableSize switch
            {
                > 0xffffff => 4,
                > 0xffff => 3,
                > 0xff => 2,
                _ => 1
            };
        }

        public void ParseTVFSManifest(ReadOnlySpan<byte> bytes, int contentFlags, int localeFlags)
        {
            #region TVFS header
            var header = new FileManifestHeader();

            header.Magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0, 4));
            if (header.Magic != 0x53465654)
                throw new Exception("Invalid TVFS manifest");

            header.Version = bytes[4];
            if (header.Version != 1)
                throw new Exception("Unsupported TVFS manifest version: " + header.Version);

            header.HeaderSize = bytes[5];

            header.EKeySize = bytes[6];
            if (header.EKeySize != 9)
                throw new Exception("Unsupported encoding key size in TVFS manifest: " + header.EKeySize);

            header.PKeySize = bytes[7];
            if (header.PKeySize != 9)
                throw new Exception("Unsupported patch key size in TVFS manifest: " + header.PKeySize);

            header.Flags = (FileManifestFlags)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(8, 4));
            header.PathTableOffset = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(12, 4));
            header.PathTableSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
            header.FileTableOffset = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
            header.FileTableSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(24, 4));
            header.ContainerTableOffset = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(28, 4));
            header.ContainerTableSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(32, 4));
            header.PathTableMaxDepth = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(36, 2));

            if (header.Flags.HasFlag(FileManifestFlags.WRITE_SUPPORT))
            {
                header.ESpecTableOffset = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(38, 4));
                header.ESpecTableSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(42, 4));
            }

            header.ContainerTableOffsetSize = GetTableEntrySize((int)header.ContainerTableSize);
            header.ESpecTableOffsetSize = GetTableEntrySize((int)header.ESpecTableSize);

            #endregion

            var pathTable = bytes.Slice((int)header.PathTableOffset, (int)header.PathTableSize);
            var vfsTable = bytes.Slice((int)header.FileTableOffset, (int)header.FileTableSize);
            var cftTable = bytes.Slice((int)header.ContainerTableOffset, (int)header.ContainerTableSize);

            WalkDirectory(pathTable, vfsTable, cftTable, header, new StringBuilder(), contentFlags, localeFlags);
        }


        private void WalkDirectory(ReadOnlySpan<byte> dir, ReadOnlySpan<byte> vfsTable, ReadOnlySpan<byte> cftTable, FileManifestHeader header, StringBuilder path, int contentFlags, int localeFlags)
        {
            var pos = 0;
            while (pos < dir.Length)
            {
                var savedLen = path.Length;

                ReadPathEntry(dir, ref pos, out var nameSlice, out int nodeFlags, out uint nodeValue, out bool hasNodeValue);

                if ((nodeFlags & 0x1) != 0) path.Append('/');
                if (!nameSlice.IsEmpty) path.Append(Encoding.ASCII.GetString(nameSlice));
                if ((nodeFlags & 0x2) != 0) path.Append('/');

                if (hasNodeValue)
                {
                    // 0x80000000 designates another directory/folder
                    if ((nodeValue & 0x80000000) != 0)
                    {
                        int childLen = (int)(nodeValue & 0x7FFFFFFF) - sizeof(uint);
                        if (childLen < 0 || pos + childLen > dir.Length)
                            throw new InvalidDataException("TVFS folder node overruns directory");
                        WalkDirectory(dir.Slice(pos, childLen), vfsTable, cftTable, header, path, contentFlags, localeFlags);
                        pos += childLen;
                    }
                    else
                    {
                        var spans = ReadSpans(vfsTable, cftTable, in header, (int)nodeValue);
                        if (spans != null)
                            RecordFile(path.ToString(), spans, contentFlags, localeFlags);
                    }

                    path.Length = savedLen;
                }
            }
        }

        private static void ReadPathEntry(ReadOnlySpan<byte> dir, ref int pos, out ReadOnlySpan<byte> name, out int nodeFlags, out uint nodeValue, out bool hasNodeValue)
        {
            nodeFlags = 0;
            nodeValue = 0;
            hasNodeValue = false;
            name = new ReadOnlySpan<byte>();

            // optional separator before name
            if (pos < dir.Length && dir[pos] == 0x00)
            {
                nodeFlags |= 0x1;
                pos++;
            }

            // length byte + name of said length
            if (pos < dir.Length && dir[pos] != 0xFF)
            {
                int len = dir[pos++];
                name = dir.Slice(pos, len);
                pos += len;
            }

            // optional separator after name
            if (pos < dir.Length && dir[pos] == 0x00)
            {
                nodeFlags |= 0x2;
                pos++;
            }

            // if 0xFF then we hit a node, otherwise we hit another seperator
            if (pos < dir.Length && dir[pos] == 0xFF)
            {
                nodeValue = BinaryPrimitives.ReadUInt32BigEndian(dir.Slice(pos + 1));
                hasNodeValue = true;
                pos += 5;
            }
            else
                nodeFlags |= 0x2;
        }

        private static FileSpan[]? ReadSpans(ReadOnlySpan<byte> vfsTable, ReadOnlySpan<byte> cftTable, in FileManifestHeader header, int vfsOffset)
        {
            int p = vfsOffset;
            int spanCount = vfsTable[p++];

            // As per ladik: 1–224 = normal file, 225–254 = other, 255 = deleted
            if (spanCount < 1 || spanCount > 224)
                return null;

            int spanEntrySize = 4 + 4 + header.ContainerTableOffsetSize;
            var spans = new FileSpan[spanCount];

            for (int i = 0; i < spanCount; i++)
            {
                uint contentOffset = BinaryPrimitives.ReadUInt32BigEndian(vfsTable.Slice(p));
                uint contentLength = BinaryPrimitives.ReadUInt32BigEndian(vfsTable.Slice(p + 4));

                var cftOffsetSlice = vfsTable.Slice(p + 8);

                // variable length integer based on table size
                uint cftOffset = header.ContainerTableOffsetSize switch
                {
                    1 => cftOffsetSlice[0],
                    2 => BinaryPrimitives.ReadUInt16BigEndian(cftOffsetSlice),
                    3 => (uint)cftOffsetSlice.ReadInt24BE(),
                    _ => BinaryPrimitives.ReadUInt32BigEndian(cftOffsetSlice),
                };
                p += spanEntrySize;

                var eKey = cftTable.Slice((int)cftOffset, header.EKeySize).ToArray();
                spans[i] = new FileSpan(eKey, contentOffset, contentLength);
            }

            return spans;
        }

        private void RecordFile(string path, FileSpan[] spans, int contentFlags, int localeFlags)
        {
            if (!ParseWoWName(path, out var generic))
            {
                if (spans.Length > 1 && Settings.LogLevel <= TSLogLevel.Warn)
                    Console.WriteLine("Unexpected non-WoW name in TVFS with multiple spans: " + path);

                rootManifestEntries[path] = spans[0];
                return;
            }

            if(loadedWith == LoadMode.Full)
            {
                if (!entriesFDIDFull.TryGetValue(generic.FileDataId, out var list))
                    entriesFDIDFull[generic.FileDataId] = list = new List<RootEntry>();

                foreach (var span in spans)
                {
                    var entry = new RootEntry
                    {
                        fileDataID = generic.FileDataId,
                        md5 = new MD5(span.EKey),
                        localeFlags = (LocaleFlags)localeFlags,
                        contentFlags = (ContentFlags)contentFlags,
                    };

                    list.Add(entry);
                }
            }
            else
            {
                var entry = new RootEntry
                {
                    fileDataID = generic.FileDataId,
                    md5 = new MD5(spans[0].EKey),
                    localeFlags = (LocaleFlags)localeFlags,
                    contentFlags = (ContentFlags)contentFlags,
                };

                entriesFDID.TryAdd(generic.FileDataId, entry);
            }
        }

        public static bool ParseWoWName(string name, out TVFSRootEntry entry)
        {
            entry = new();

            if (name.Length != 40)
                return false;

            var fdidCKeyParsed = Convert.FromHexString(name).AsSpan();
            var fdid = BinaryPrimitives.ReadUInt32BigEndian(fdidCKeyParsed);
            var cKey = new MD5(fdidCKeyParsed.Slice(4, 16));
            entry = new(fdid, cKey);

            return true;
        }
    }
}