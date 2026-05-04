namespace JunkCleaner.Models;

public sealed class CleanupResult
{
    public required string CategoryId { get; init; }

    public long FreedBytes { get; init; }

    public int FilesDeleted { get; init; }

    public int Errors { get; init; }

    public IReadOnlyList<string> ErrorMessages { get; init; } = Array.Empty<string>();

    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }
}
