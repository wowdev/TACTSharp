using BenchmarkDotNet.Attributes;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace TACTBench
{
    [MemoryDiagnoser]
    public class GroupIndexBlockBench
    {
        private List<Entry> Entries = [];
        private const int outputEntriesPerBlock = 157;

        /* On .NET 9: 
| Method                | Mean     | Error     | StdDev    | Gen0     | Allocated |
|---------------------- |---------:|----------:|----------:|---------:|----------:|
| LINQ                  | 4.294 ms | 0.0396 ms | 0.0370 ms |   7.8125 |  611520 B |
| EnumerableChunk       | 6.672 ms | 0.0760 ms | 0.0674 ms | 156.2500 | 8156433 B |
| CollectionMarshalSpan | 2.199 ms | 0.0224 ms | 0.0199 ms |        - |         - |
        */

        // Someone figure out how to run this on .NET 10 :)

        [GlobalSetup]
        public void Setup()
        {
            Entries = [];
            var random = new Random();
            for (var i = 0; i < 1_000_000; i++)
            {
                var entry = new Entry()
                {
                    Size = i,
                    ArchiveIndex = (short)(i / 10000),
                    Offset = i
                };

                random.NextBytes(entry.EKey);

                Entries.Add(entry);
            }
        }

        [Benchmark]
        public void LINQ()
        {
            var totalBlocks = (Entries.Count + outputEntriesPerBlock - 1) / outputEntriesPerBlock;
            for (int i = 0; i < totalBlocks; i++)
            {
                var block = Entries.Skip(i * outputEntriesPerBlock).Take(outputEntriesPerBlock);
                foreach (var entry in block)
                {
                    Process(entry);
                }
            }
        }

        [Benchmark]
        public void EnumerableChunk()
        {
            foreach (var block in Entries.Chunk(outputEntriesPerBlock))
            {
                foreach (var entry in block)
                {
                    Process(entry);
                }
            }
        }

        [Benchmark]
        public void CollectionMarshalSpan()
        {
            var span = CollectionsMarshal.AsSpan(Entries);
            int totalBlocks = (span.Length + outputEntriesPerBlock - 1) / outputEntriesPerBlock;

            for (int i = 0; i < totalBlocks; i++)
            {
                int start = i * outputEntriesPerBlock;
                int length = Math.Min(outputEntriesPerBlock, span.Length - start);
                var block = span.Slice(start, length);

                foreach (var entry in block)
                {
                    Process(entry);
                }
            }
        }

        private static void Process(Entry entry)
        {
            // Close enough 
            BinaryPrimitives.ReverseEndianness(entry.Size);
            BinaryPrimitives.ReverseEndianness((short)entry.ArchiveIndex);
            BinaryPrimitives.ReverseEndianness(entry.Offset);
        }

        public class Entry
        {
            public byte[] EKey = new byte[16];
            public int Size;
            public short ArchiveIndex;
            public int Offset;
        }
    }
}
