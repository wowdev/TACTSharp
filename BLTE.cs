using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace TACTIndexTestCSharp
{
    public static class BLTE
    {
        public static byte[] Decode(ReadOnlySpan<byte> data, ulong totalDecompSize = 0)
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

                HandleDataBlock((char)data[compOffset], data.Slice(compOffset + 1, compSize - 1), i, compSize, decompSize, compOffset, decompOffset, decompData);

                compOffset += compSize;
                decompOffset += decompSize;
            }

            return decompData;
        }

        private unsafe static void HandleDataBlock(char mode, ReadOnlySpan<byte> compData, int chunkIndex, int compSize, int decompSize, int compOffset, int decompOffset, byte[] decompData)
        {
            switch (mode)
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
                    var decompSpan = decompData.AsSpan(decompOffset);
                    try
                    {
                        if(TryDecrypt(compData, chunkIndex, out var decryptedData))
                            HandleDataBlock((char)decryptedData[0], decryptedData.AsSpan()[1..], chunkIndex, compSize, decompSize, compOffset, decompOffset, decompData);
                    }
                    catch (KeyNotFoundException e)
                    {
                        //Console.WriteLine(e.Message);
                        var empty = new byte[decompSize];
                        empty.CopyTo(decompSpan);
                    }

                    break;

                default:
                    throw new Exception("Invalid BLTE chunk mode: " + (char)mode);
            }
        }

        private static bool TryDecrypt(ReadOnlySpan<byte> data, int chunkIndex, out byte[] output)
        {
            byte keyNameSize = data[0];

            if (keyNameSize == 0 || keyNameSize != 8)
                throw new Exception("keyNameSize == 0 || keyNameSize != 8");

            ulong keyName = BinaryPrimitives.ReadUInt64LittleEndian(data[1..]);

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

            if (!KeyService.TryGetKey(keyName, out var key))
            {
                output = [];
                return false;
            }

            if (encType == 'S')
            {
                ICryptoTransform decryptor = KeyService.SalsaInstance.CreateDecryptor(key, IV);

                output = decryptor.TransformFinalBlock(data[1..].ToArray(), dataOffset, data.Length - 1 - dataOffset);
                return true;
            }
            else
            {
                throw new Exception("encType arc4 not implemented");
            }
        }
    }
}
