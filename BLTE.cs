using System.IO.Compression;

namespace TACTIndexTestCSharp
{
    public static class BLTE
    {
        public unsafe static byte[] Decode(ReadOnlySpan<byte> data, ulong totalDecompSize = 0)
        {
            var fixedHeaderSize = 8;

            if (data[0] != 0x42 || data[1] != 0x4C || data[2] != 0x54 || data[3] != 0x45)
                throw new Exception("Invalid BLTE header");

            var headerSize = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);

            var offset = fixedHeaderSize;

            if (headerSize == 0)
            {
                if ((char)data[offset] != 'N')
                    throw new NotImplementedException("Single-chunk BLTE, but data is not N!?");
                offset += 1;

                return data[offset..].ToArray();
            }

            var flags = data[offset];
            offset += 1;
            var chunkCount = (uint)((data[offset + 0] << 16) | (data[offset + 1] << 8) | (data[offset + 2] << 0));
            offset += 3;

            int infoOffset = offset;

            if (totalDecompSize == 0)
            {
                for (var i = 0; i < chunkCount; i++)
                {
                    infoOffset += 4;
                    totalDecompSize += (ulong)((data[infoOffset + 0] << 24) | (data[infoOffset + 1] << 16) | (data[infoOffset + 2] << 8) | (data[infoOffset + 3] << 0));
                    infoOffset += 20;
                }

                infoOffset = offset;
            }

            var decompData = new byte[totalDecompSize];
            int compOffset = (int)headerSize;
            int decompOffset = 0;

            for (var i = 0; i < chunkCount; i++)
            {
                var compSize = (data[infoOffset + 0] << 24) | (data[infoOffset + 1] << 16) | (data[infoOffset + 2] << 8) | (data[infoOffset + 3] << 0);
                infoOffset += 4;
                var decompSize = (data[infoOffset + 0] << 24) | (data[infoOffset + 1] << 16) | (data[infoOffset + 2] << 8) | (data[infoOffset + 3] << 0);
                infoOffset += 4;

                var checkSum = data.Slice(infoOffset, 16);
                infoOffset += 16;

                var compData = data.Slice(compOffset + 1, compSize - 1);

                switch ((char)data[compOffset])
                {
                    case 'N':
                        compData.CopyTo(decompData.AsSpan(decompOffset));
                        break;

                    case 'Z':
                        fixed (byte* compRaw = compData)
                            using (var stream = new UnmanagedMemoryStream(compRaw, compData.Length))
                            using (var zlibStream = new ZLibStream(stream, CompressionMode.Decompress))
                                zlibStream.ReadExactly(decompData, decompOffset, decompSize);
                        break;

                    case 'F':
                        throw new NotImplementedException("Frame decompression not implemented");

                    case 'E':
                        var empty = new byte[decompSize];
                        empty.CopyTo(decompData.AsSpan(decompOffset));
                        break;

                    default:
                        throw new Exception("Invalid BLTE chunk mode: " + (char)data[compOffset]);
                }

                compOffset += compSize;
                decompOffset += decompSize;
            }

            return decompData;
        }
    }
}
