namespace JunkCleaner.LargeFiles;

/// <summary>Элемент топа крупнейших файлов.</summary>
public sealed class LargeFileEntry
{
    public LargeFileEntry(string fullPath, long lengthBytes)
    {
        FullPath = fullPath;
        LengthBytes = lengthBytes;
    }

    public string FullPath { get; }

    public long LengthBytes { get; }

    public string Extension
    {
        get
        {
            try
            {
                var e = Path.GetExtension(FullPath);
                return string.IsNullOrEmpty(e) ? "—" : e;
            }
            catch
            {
                return "—";
            }
        }
    }
}
