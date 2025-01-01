using System.IO.Compression;

namespace TACTIndexTestCSharp
{
    public static class BLTE
    {
        public static byte[] Decode(ReadOnlySpan<byte> data)
        {
            if (data[0] != 0x42 || data[1] != 0x4C || data[2] != 0x54 || data[3] != 0x45)
                throw new Exception("Invalid BLTE header");


            var headerSize = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);

            BLTEChunkInfo[] chunkInfos;

            if (headerSize > 0)
            {
                var flags = data[8];
                var chunkCount = (uint)((data[9] << 16) | (data[10] << 8) | data[11]);

                chunkInfos = new BLTEChunkInfo[chunkCount];

                var offset = 12;
                for (var i = 0; i < chunkCount; i++)
                {
                    chunkInfos[i].isFullChunk = true;
                    chunkInfos[i].compSize = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
                    offset += 4;
                    chunkInfos[i].decompSize = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
                    offset += 4;

                    chunkInfos[i].checkSum = new byte[16];
                    data.Slice(offset, 16).CopyTo(chunkInfos[i].checkSum);
                    offset += 16;
                }

                if(offset != headerSize)
                    throw new Exception("Incomplete BLTE header read");
            }
            else
            {
                chunkInfos = new BLTEChunkInfo[1];
                chunkInfos[0].isFullChunk = false;
                chunkInfos[0].compSize = data.Length - 8;
                chunkInfos[0].decompSize = data.Length - 8 - 1;
                chunkInfos[0].checkSum = new byte[16];
            }


            var decompData = new byte[chunkInfos.Sum(x => x.decompSize)];

            for(var i = 0; i < chunkInfos.Length; i++)
            {
                long offset = headerSize + chunkInfos.Where((x, index) => index < i).Sum(x => x.compSize);
                var mode = (char)data[(int)offset];
                offset++;

                var chunk = chunkInfos[i];

                switch (mode)
                {
                    case 'N':
                        var compData = data.Slice((int)offset, chunk.compSize);

                        compData.CopyTo(decompData.AsSpan(chunkInfos.Where((x, index) => index < i).Sum(x => x.decompSize)));
                        offset += chunk.compSize;

                        break;
                    case 'Z':
                        var startOffset = (int)headerSize + chunkInfos.Where((x, index) => index < i).Sum(x => x.compSize) + 1;

                        using (var stream = new MemoryStream(data.Slice(startOffset, chunk.compSize - 1).ToArray()))
                        using (var zlibStream = new ZLibStream(stream, CompressionMode.Decompress))
                        {
                            zlibStream.ReadExactly(decompData, chunkInfos.Where((x, index) => index < i).Sum(x => x.decompSize), chunk.decompSize);
                        }
                        break;
                    case 'F':
                        throw new NotImplementedException("Frame decompression not implemented");
                    case 'E':
                        var empty = new byte[chunk.decompSize];
                        empty.CopyTo(decompData.AsSpan(chunkInfos.Where((x, index) => index < i).Sum(x => x.decompSize)));
                        Console.WriteLine("Encountered encrypted chunk, skipping");
                        break;
                    default:
                        throw new Exception("Invalid BLTE chunk mode: " + mode);
                }
            }

            return decompData;
        }
        private struct BLTEChunkInfo
        {
            public bool isFullChunk;
            public int compSize;
            public int decompSize;
            public byte[] checkSum;
        }
    }
}
