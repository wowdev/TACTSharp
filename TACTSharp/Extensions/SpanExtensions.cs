using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

using TACTSharp.Utils;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort ReadUInt16BE(this ReadOnlySpan<byte> source)
            => BinaryPrimitives.ReadUInt16BigEndian(source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static short ReadInt16BE(this ReadOnlySpan<byte> source)
            => BinaryPrimitives.ReadInt16BigEndian(source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadInt24BE(this ReadOnlySpan<byte> source)
            => source[2] | source[1] << 8 | source[0] << 16;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadInt32BE(this ReadOnlySpan<byte> source)
            => BinaryPrimitives.ReadInt32BigEndian(source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadInt32LE(this ReadOnlySpan<byte> source)
            => BinaryPrimitives.ReadInt32LittleEndian(source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadUInt32BE(this ReadOnlySpan<byte> source)
            => BinaryPrimitives.ReadUInt32BigEndian(source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ReadInt40BE(this ReadOnlySpan<byte> source)
            => source[4] | source[3] << 8 | source[2] << 16 | source[1] << 24 | source[0] << 32;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ReadNullTermString(this ReadOnlySpan<byte> source)
            => Encoding.UTF8.GetString(source[..source.IndexOf((byte)0)]);
    }
}
