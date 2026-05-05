namespace JunkCleaner.Updates;

internal static class UpdateSettings
{
    public const string GitHubOwner = "alekseichmsk";

    public const string GitHubRepo = "JunkCleaner";

    public const bool IncludePrereleases = false;

    public static string RepositoryUrl => $"https://github.com/{GitHubOwner}/{GitHubRepo}";

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(GitHubOwner) &&
        !string.IsNullOrWhiteSpace(GitHubRepo);
}
