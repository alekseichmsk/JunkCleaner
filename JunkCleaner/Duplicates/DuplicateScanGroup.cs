namespace JunkCleaner.Duplicates;

/// <summary>Files sharing the same size and SHA-256 (minimum two entries).</summary>
public sealed class DuplicateScanGroup
{
    public DuplicateScanGroup(string sha256Hex, long fileSizeBytes, IReadOnlyList<DuplicateScanFileMeta> files)
    {
        Sha256Hex = sha256Hex;
        FileSizeBytes = fileSizeBytes;
        Files = files ?? throw new ArgumentNullException(nameof(files));
    }

    public string Sha256Hex { get; }

    public long FileSizeBytes { get; }

    public IReadOnlyList<DuplicateScanFileMeta> Files { get; }

    public long WastedBytesApprox => Files.Count > 0 ? FileSizeBytes * (Files.Count - 1) : 0;
}
