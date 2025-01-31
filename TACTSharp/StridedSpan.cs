using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TACTSharp
{
    internal readonly ref struct StridedSpan<T>(Span<T> data, int stride)
    {
        private readonly Span<T> _data = data;
        private readonly int _stride = stride;

        public readonly int Count { get; } = data.Length / stride;
        public readonly Span<T> this[int index] => _data.Slice(index * _stride, _stride);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator StridedReadOnlySpan<T> (StridedSpan<T> self)
            => new (self._data, self._stride);
    }

    internal readonly ref struct StridedReadOnlySpan<T>(ReadOnlySpan<T> data, int stride)
    {
        private readonly ReadOnlySpan<T> _data = data;
        private readonly int _stride = stride;

        public readonly int Count { get; } = data.Length / stride;
        public readonly ReadOnlySpan<T> this[int index] => _data.Slice(index * _stride, _stride);
    }
}
