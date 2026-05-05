using Velopack;

namespace JunkCleaner.Updates;

public sealed class UpdateCheckResult
{
    public required string CurrentVersion { get; init; }

    public string? LatestVersion { get; init; }

    public string? LatestTag { get; init; }

    public string? ReleaseName { get; init; }

    public string? ReleaseUrl { get; init; }

    public string? Body { get; init; }

    public bool IsNewerAvailable { get; init; }

    public bool IsConfigured { get; init; }

    public bool IsInstalled { get; init; }

    public string? Message { get; init; }

    public UpdateInfo? Update { get; init; }
}
