using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using TACTSharp;

static class Extensions
{
    /// <summary>
    /// Returns a given element in an array, bypassing bounds check automatically inserted by the JITter.
    /// 
    /// This is semantically equivalen to <pre>arr[index]</pre> but prevents the JIT from emitting bounds checks.
    /// 
    /// Note that in return no guarantees are made and you should always make sure the <paramref name="index"/> is within bounds
    /// yourself.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="arr">The array to index.</param>
    /// <param name="index">The index of the element to return.</param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T UnsafeIndex<T>(this T[] arr, int index)
        => ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(arr), index);

    public static int UnsafePromote(this bool value)
    {
#pragma warning disable CS0162 // Unreachable code detected
        if (sizeof(bool) == sizeof(byte))
            return (int)Unsafe.As<bool, byte>(ref value);
        
        if (sizeof(bool) == sizeof(short))
            return (int)Unsafe.As<bool, short>(ref value); // ? 8 : 0;
        
        if (sizeof(bool) == sizeof(int))
            return (int)Unsafe.As<bool, int>(ref value); // ? 8 : 0;
        
        return value ? 1 : 0;
#pragma warning restore CS0162 // Unreachable code detected
    }

    public static int ReadInt24BE(this ReadOnlySpan<byte> source)
        => source[2] | source[1] << 8 | source[0] << 16;

    public static long ReadInt40BE(this ReadOnlySpan<byte> source)
        => source[4] | source[3] << 8 | source[2] << 16 | source[1] << 24 | source[0] << 32;

    public static int[] ReadInt32LE(this ReadOnlySpan<byte> source, int count)
    {
        var data = MemoryMarshal.Cast<byte, int>(source[0..(count * 4)]).ToArray();

        if (!BitConverter.IsLittleEndian)
        {
            for (var i = 0; i < data.Length; ++i)
                data[i] = BinaryPrimitives.ReverseEndianness(data[i]);
        }
        return data;
    }

    public static uint[] ReadUInt32LE(this ReadOnlySpan<byte> source, int count)
    {
        var data = MemoryMarshal.Cast<byte, uint>(source[0..(count * 4)]).ToArray();

        if (!BitConverter.IsLittleEndian)
        {
            for (var i = 0; i < data.Length; ++i)
                data[i] = BinaryPrimitives.ReverseEndianness(data[i]);
        }
        return data;
    }

    public static string ReadNullTermString(this ReadOnlySpan<byte> source)
    {
        return Encoding.UTF8.GetString(source[..source.IndexOf((byte)0)]);
    }
}
