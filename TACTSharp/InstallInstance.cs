using Microsoft.Win32.SafeHandles;
using System.Collections;
using System.IO.MemoryMappedFiles;

using TACTSharp.Extensions;

namespace TACTSharp
{
    public class InstallInstance
    {
        private readonly MemoryMappedFile installFile;
        private readonly MemoryMappedViewAccessor accessor;
        private readonly SafeMemoryMappedViewHandle mmapViewHandle;

        private readonly byte HashSize;
        private readonly ushort NumTags;
        private readonly uint NumEntries;

        public readonly List<InstallTagEntry> Tags;
        public readonly List<InstallFileEntry> Entries;
        public unsafe InstallInstance(string path)
        {
            this.installFile = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            this.accessor = installFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            this.mmapViewHandle = accessor.SafeMemoryMappedViewHandle;

            byte* fileData = null;
            mmapViewHandle.AcquirePointer(ref fileData);

            var fileLength = new FileInfo(path).Length;
            var installData = new ReadOnlySpan<byte>(fileData, (int)fileLength);

            if (installData[0] != 0x49 || installData[1] != 0x4E)
                throw new Exception("Invalid Install file magic");

            this.HashSize = installData[3];
            this.NumTags = installData.Slice(4, 2).ReadUInt16BE();
            this.NumEntries = installData.Slice(6, 4).ReadUInt32BE();

            var bytesPerTag = ((int)this.NumEntries + 7) / 8;

            var offs = 10;

            this.Tags = [];
            for (var i = 0; i < this.NumTags; i++)
            {
                var name = installData[offs..].ReadNullTermString();
                offs += name.Length + 1;

                var type = installData.Slice(offs, 2).ReadUInt16BE();
                offs += 2;

                var files = installData.Slice(offs, bytesPerTag).ToArray();
                offs += bytesPerTag;

                for (int j = 0; j < bytesPerTag; j++)
                    files[j] = (byte)((files[j] * 0x0202020202 & 0x010884422010) % 1023);

                this.Tags.Add(new InstallTagEntry() { name = name, type = type, files = new BitArray(files) });
            }

            this.Entries = [];
            for (var i = 0; i < this.NumEntries; i++)
            {
                var name = installData[offs..].ReadNullTermString();
                offs += name.Length + 1;

                var contentHash = installData.Slice(offs, this.HashSize).ToArray();
                offs += this.HashSize;

                var size = installData.Slice(offs, 4).ReadUInt32BE();
                offs += 4;

                var tags = new List<string>();
                for (var t = 0; t < NumTags; t++)
                {
                    if (Tags[t].files[i])
                        tags.Add(Tags[t].type + "=" + Tags[t].name);
                }

                this.Entries.Add(new InstallFileEntry() { name = name, md5 = contentHash, size = size, tags = [.. tags] });
            }

            mmapViewHandle.ReleasePointer();
        }
        public struct InstallTagEntry
        {
            public string name;
            public ushort type;
            public BitArray files;
        }

        public struct InstallFileEntry
        {
            public string name;
            public byte[] md5;
            public uint size;
            public string[] tags;
        }
    }
}
