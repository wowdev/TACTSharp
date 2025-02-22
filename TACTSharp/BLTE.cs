using System.Buffers.Binary;
using System.IO.Compression;

namespace TACTSharp
{
    public static class BLTE
    {
        public static byte[] Decode(ReadOnlySpan<byte> data, ulong totalDecompSize = 0)
        {
            var fixedHeaderSize = 8;

            if (data[0] != 0x42 || data[1] != 0x4C || data[2] != 0x54 || data[3] != 0x45)
                throw new Exception("Invalid BLTE header");

            int headerSize = data[4..].ReadInt32BE();
            if (headerSize == 0)
            {
                if ((char)data[fixedHeaderSize] != 'N' && totalDecompSize == 0)
                    throw new Exception("totalDecompSize must be set for single non-normal BLTE block");
                else if ((char)data[fixedHeaderSize] == 'N' && totalDecompSize == 0)
                    totalDecompSize = (ulong)(data.Length - fixedHeaderSize - 1);

                var singleDecompData = new byte[totalDecompSize];
                HandleDataBlock((char)data[fixedHeaderSize], data[(fixedHeaderSize + 1)..], 0, singleDecompData.AsSpan());
                return singleDecompData;
            }

            var tableFormat = data[(fixedHeaderSize + 0)];
            if(tableFormat != 0xF)
                throw new Exception("Unexpected BLTE table format");

            // If tableFormat is 0x10 this might be 40 instead of 24. Only seen in Avowed (aqua) product. Likely another key.
            var blockInfoSize = 24;

            var chunkCount = data[(fixedHeaderSize + 1)..].ReadInt24BE();
            int infoStart = fixedHeaderSize + 4;

            if (totalDecompSize == 0)
            {
                int sizeScanOffset = infoStart + 4;
                for (var i = 0; i < chunkCount; i++)
                {
                    totalDecompSize += (ulong)data[sizeScanOffset..].ReadInt32BE();
                    sizeScanOffset += blockInfoSize;
                }
            }

            var decompData = new byte[totalDecompSize];
            var infoOffset = infoStart;
            int compOffset = headerSize;
            int decompOffset = 0;

            for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                var compSize = data[(infoOffset + 0)..].ReadInt32BE();
                var decompSize = data[(infoOffset + 4)..].ReadInt32BE();
                // var checkSum = data[(infoOffset+8)..(infoOffset+8+16)];

                HandleDataBlock((char)data[compOffset], data[(compOffset + 1)..(compOffset + compSize)], chunkIndex, decompData.AsSpan().Slice(decompOffset, decompSize));

                infoOffset += blockInfoSize;
                compOffset += compSize;
                decompOffset += decompSize;
            }

            return decompData;
        }

        private unsafe static void HandleDataBlock(char mode, ReadOnlySpan<byte> compData, int chunkIndex, Span<byte> decompData)
        {
            switch (mode)
            {
                case 'N':
                    compData.CopyTo(decompData);
                    break;

                case 'Z':
                    fixed (byte* compRaw = compData)
                        using (var stream = new UnmanagedMemoryStream(compRaw, compData.Length))
                        using (var zlibStream = new ZLibStream(stream, CompressionMode.Decompress))
                            zlibStream.ReadExactly(decompData);
                    break;

                case 'F':
                    throw new NotImplementedException("Frame decompression not implemented");

                case 'E':
                    if (TryDecrypt(compData, chunkIndex, out var decryptedData))
                        HandleDataBlock((char)decryptedData[0], decryptedData[1..], chunkIndex, decompData);
                    break;

                default:
                    throw new Exception("Invalid BLTE chunk mode: " + (char)mode);
            }
        }

        private static bool TryDecrypt(ReadOnlySpan<byte> data, int chunkIndex, out Span<byte> output)
        {
            byte keyNameSize = data[0];

            if (keyNameSize == 0 || keyNameSize != 8)
                throw new Exception("keyNameSize == 0 || keyNameSize != 8");

            ulong keyName = BinaryPrimitives.ReadUInt64LittleEndian(data[1..]);
            if (!KeyService.TryGetKey(keyName, out var key))
            {
                output = [];
                return false;
            }

            byte IVSize = data[keyNameSize + 1];

            if (IVSize != 4 || IVSize > 0x10)
                throw new Exception("IVSize != 4 || IVSize > 0x10");

            byte[] IV = data.Slice(keyNameSize + 2, IVSize).ToArray();
            // expand to 8 bytes
            Array.Resize(ref IV, 8);

            if (data.Length < keyNameSize + IVSize + 4)
                throw new Exception("data.Length < IVSize + keyNameSize + 4");

            int dataOffset = keyNameSize + IVSize + 2;

            var encType = (char)data[keyNameSize + 2 + IVSize];

            if (encType != 'S' && encType != 'A')
                throw new Exception("unhandled encryption type: " + encType);

            // magic
            for (int shift = 0, i = 0; i < sizeof(int); shift += 8, i++)
            {
                IV[i] ^= (byte)((chunkIndex >> shift) & 0xFF);
            }

            if (encType == 'S')
            {
                output = KeyService.SalsaInstance.CreateDecryptor(key, IV).TransformFinalBlock(data[1..], dataOffset, data.Length - 1 - dataOffset);
                return true;
            }
            else
            {
                throw new Exception("encType arc4 not implemented");
            }
        }
    }
}
