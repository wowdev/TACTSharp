using System;
using System.Collections.Generic;
using System.Linq;
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
    }
}
