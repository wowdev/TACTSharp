using System.Buffers.Binary;
using System.Text;

static class Extensions
{
    public static ushort ReadUInt16BE(this ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(source);
    }

    public static short ReadInt16BE(this ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadInt16BigEndian(source);
    }

    public static int ReadInt24BE(this ReadOnlySpan<byte> source)
    {
        return source[2] | source[1] << 8 | source[0] << 16;
    }

    public static int ReadInt32BE(this ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadInt32BigEndian(source);
    }

    public static uint ReadUInt32BE(this ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(source);
    }

    public static long ReadInt40BE(this ReadOnlySpan<byte> source)
    {
        return source[4] | source[3] << 8 | source[2] << 16 | source[1] << 24 | source[0] << 32;
    }

    public static string ReadNullTermString(this ReadOnlySpan<byte> source)
    {
        return Encoding.UTF8.GetString(source[..source.IndexOf((byte)0)]);
    }
}
