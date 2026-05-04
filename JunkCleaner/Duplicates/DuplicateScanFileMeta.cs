namespace JunkCleaner.Duplicates;

public sealed class DuplicateScanFileMeta
{
    public DuplicateScanFileMeta(string fullPath, long lengthBytes, DateTime lastWriteTimeUtc)
    {
        FullPath = fullPath;
        LengthBytes = lengthBytes;
        LastWriteTimeUtc = lastWriteTimeUtc;
    }

    public string FullPath { get; }

    public long LengthBytes { get; }

    public DateTime LastWriteTimeUtc { get; }
}
