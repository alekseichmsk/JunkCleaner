namespace JunkCleaner.Updates;

public sealed class UpdateCheckResult
{
    public required Version CurrentVersion { get; init; }

    public Version? LatestVersion { get; init; }

    public string? LatestTag { get; init; }

    public string? ReleaseName { get; init; }

    public string? ReleaseUrl { get; init; }

    public string? Body { get; init; }

    public bool IsNewerAvailable { get; init; }

    public bool IsConfigured { get; init; }

    public string? Message { get; init; }

    public GitHubReleaseAsset? Asset { get; init; }
}

public sealed class GitHubReleaseAsset
{
    public required string Name { get; init; }

    public required string DownloadUrl { get; init; }

    public long SizeBytes { get; init; }
}
