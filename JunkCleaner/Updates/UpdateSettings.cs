namespace JunkCleaner.Updates;

internal static class UpdateSettings
{
    public const string GitHubOwner = "alekseichmsk";

    public const string GitHubRepo = "JunkCleaner";

    public const bool IncludePrereleases = false;

    /// <summary>Preferred installer/update assets. First matching asset wins.</summary>
    public static readonly string[] PreferredAssetExtensions =
    {
        ".appinstaller",
        ".msix",
        ".zip",
        ".msi",
        ".exe",
    };

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(GitHubOwner) &&
        !string.IsNullOrWhiteSpace(GitHubRepo);
}
