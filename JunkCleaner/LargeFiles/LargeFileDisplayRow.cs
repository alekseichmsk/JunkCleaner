using JunkCleaner.Ui;

namespace JunkCleaner.LargeFiles;

/// <summary>Строка списка крупнейших файлов для привязки в UI.</summary>
public sealed class LargeFileDisplayRow
{
    public LargeFileDisplayRow(LargeFileEntry entry)
    {
        Entry = entry;
    }

    public LargeFileEntry Entry { get; }

    public string Path => Entry.FullPath;

    public string Extension => Entry.Extension;

    public string SizeText => ByteFormat.Format(Entry.LengthBytes);

    public long Bytes => Entry.LengthBytes;

    public override string ToString() => Path;
}
