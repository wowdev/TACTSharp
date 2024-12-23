using System.Buffers.Binary;

static class Extensions
{
    public static int ReadInt16BE(this Span<byte> source)
    {
        return BinaryPrimitives.ReadInt16BigEndian(source);
    }

    public static int ReadInt32BE(this Span<byte> source)
    {
        return BinaryPrimitives.ReadInt32BigEndian(source);
    }

    private static byte[] ReadInvertedBytes(this BinaryReader reader, int byteCount)
    {
        byte[] byteArray = reader.ReadBytes(byteCount);
        Array.Reverse(byteArray);

        return byteArray;
    }

    public static uint ReadUInt32(this BinaryReader reader, bool invertEndian = false)
    {
        if (invertEndian)
        {
            return BitConverter.ToUInt32(reader.ReadInvertedBytes(4), 0);
        }

        return reader.ReadUInt32();
    }
}