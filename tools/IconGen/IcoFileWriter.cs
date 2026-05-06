using System.IO;

namespace JunkCleaner.IconGen;

/// <summary>
/// Writes a Windows .ico containing multiple embedded PNG images (Vista+).
/// </summary>
internal static class IcoFileWriter
{
    public static void Write(string path, IReadOnlyList<(int Size, byte[] PngBytes)> images)
    {
        if (images.Count == 0)
            throw new ArgumentException("No icon images.", nameof(images));

        var ordered = images.OrderBy(static x => x.Size).ToList();
        var count = ordered.Count;
        const int dirHeaderBytes = 6;
        var dirSize = dirHeaderBytes + count * 16;

        using var ms = new MemoryStream();
        // ICONDIR
        ms.Write([0, 0, 1, 0]);
        WriteUInt16LE(ms, (ushort)count);

        var pngPayloads = new byte[count][];
        var dataOffset = dirSize;
        for (var i = 0; i < count; i++)
        {
            var png = ordered[i].PngBytes;
            pngPayloads[i] = png;

            var size = ordered[i].Size;
            var w = size >= 256 ? (byte)0 : (byte)size;
            var h = size >= 256 ? (byte)0 : (byte)size;

            ms.WriteByte(w);
            ms.WriteByte(h);
            ms.WriteByte(0);
            ms.WriteByte(0);
            WriteUInt16LE(ms, 1);
            WriteUInt16LE(ms, 32);
            WriteInt32LE(ms, png.Length);
            WriteInt32LE(ms, dataOffset);
            dataOffset += png.Length;
        }

        foreach (var png in pngPayloads)
            ms.Write(png);

        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void WriteUInt16LE(Stream s, ushort value)
    {
        s.WriteByte((byte)(value & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
    }

    private static void WriteInt32LE(Stream s, int value)
    {
        s.WriteByte((byte)(value & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
        s.WriteByte((byte)((value >> 16) & 0xFF));
        s.WriteByte((byte)((value >> 24) & 0xFF));
    }
}
