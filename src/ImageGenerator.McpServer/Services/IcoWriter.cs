namespace ImageGenerator.McpServer.Services;

// Writes a multi-size ICO container with PNG-compressed entries (supported since Windows Vista)
public static class IcoWriter
{
    public const int MaxSize = 256;

    private const int HeaderSize = 6;

    private const int EntrySize = 16;

    public static byte[] Write(IReadOnlyList<(int Size, byte[] Png)> entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // ICONDIR
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)entries.Count);

        // ICONDIRENTRY
        var offset = HeaderSize + (EntrySize * entries.Count);
        foreach (var (size, png) in entries)
        {
            var dimension = (byte)(size >= MaxSize ? 0 : size);
            writer.Write(dimension);
            writer.Write(dimension);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in entries)
        {
            writer.Write(png);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
