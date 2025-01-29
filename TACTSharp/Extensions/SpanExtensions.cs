using System;
using System.Collections.Generic;
using System.Linq;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TACTSharp.Extensions
{
    internal static class SpanExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StridedReadOnlySpan<T> WithStride<T>(this ReadOnlySpan<T> span, int stride)
            => new(span, stride);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StridedSpan<T> WithStride<T>(this Span<T> span, int stride)
            => new(span, stride);

        public static ulong ReadUInt64LE(this ReadOnlySpan<byte> span) => BinaryPrimitives.ReadUInt64LittleEndian(span);
        public static uint ReadUInt32LE(this ReadOnlySpan<byte> span) => BinaryPrimitives.ReadUInt32LittleEndian(span);
        public static ushort ReadUInt16LE(this ReadOnlySpan<byte> span) => BinaryPrimitives.ReadUInt16LittleEndian(span);

        public static long ReadInt64LE(this ReadOnlySpan<byte> span) => BinaryPrimitives.ReadInt64LittleEndian(span);
        public static int ReadInt32LE(this ReadOnlySpan<byte> span) => BinaryPrimitives.ReadInt32LittleEndian(span);
        public static short ReadInt16LE(this ReadOnlySpan<byte> span) => BinaryPrimitives.ReadInt16LittleEndian(span);

        
        public static ulong ReadUInt64BE(this ReadOnlySpan<byte> span) => BinaryPrimitives.ReadUInt64BigEndian(span);
        public static uint ReadUInt32BE(this ReadOnlySpan<byte> span) => BinaryPrimitives.ReadUInt32BigEndian(span);
        public static ushort ReadUInt16BE(this ReadOnlySpan<byte> span) => BinaryPrimitives.ReadUInt16BigEndian(span);

        public static long ReadInt64BE(this ReadOnlySpan<byte> span) => BinaryPrimitives.ReadInt64BigEndian(span);
        public static int ReadInt32BE(this ReadOnlySpan<byte> span) => BinaryPrimitives.ReadInt32BigEndian(span);
        public static short ReadInt16BE(this ReadOnlySpan<byte> span) => BinaryPrimitives.ReadInt16BigEndian(span);
    }
}
