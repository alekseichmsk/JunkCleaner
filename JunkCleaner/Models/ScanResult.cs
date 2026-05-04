namespace JunkCleaner.Models;

public sealed class ScanResult
{
    public required string CategoryId { get; init; }

    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>Total size in bytes reported at scan time (best effort).</summary>
    public long TotalBytes { get; init; }

    /// <summary>Files to delete; empty when <see cref="UsesNativeCleanup"/> is true.</summary>
    public IReadOnlyList<string> FilePaths { get; init; } = Array.Empty<string>();

    /// <summary>Recycle bin and similar: clean via native APIs, not individual paths.</summary>
    public bool UsesNativeCleanup { get; init; }

    /// <summary>Recycle bin query: number of objects (hint for reporting).</summary>
    public long NativeItemCount { get; init; }
}
