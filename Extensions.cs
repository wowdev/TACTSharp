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

    public static long ReadInt40BE(this ReadOnlySpan<byte> source)
    {
        return source[4] | source[3] << 8 | source[2] << 16 | source[1] << 24 | source[0] << 32;
    }
}
public class MultiDictionary<K, V> : Dictionary<K, List<V>>
{
    public void Add(K key, V value)
    {
        List<V> hset;
        if (TryGetValue(key, out hset))
        {
            hset.Add(value);
        }
        else
        {
            hset = new List<V>();
            hset.Add(value);
            base[key] = hset;
        }
    }

    public new void Clear()
    {
        foreach (var kv in this)
        {
            kv.Value.Clear();
        }

        base.Clear();
    }
}