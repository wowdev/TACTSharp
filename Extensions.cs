using System.Buffers.Binary;

static class Extensions
{
    public static int ReadInt16BE(this ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadInt16BigEndian(source);
    }

    public static int ReadInt32BE(this ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadInt32BigEndian(source);
    }
}
