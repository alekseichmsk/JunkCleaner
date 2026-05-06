namespace JunkCleaner.Updates;

internal static class UpdateSettings
{
    public const string GitHubOwner = "alekseichmsk";

    public const string GitHubRepo = "JunkCleaner";

    public const bool IncludePrereleases = false;
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Debugging escape hatch for hostile TLS interception (some corporate proxies / broken AV setups).
    /// Toggle to true temporarily if HTTPS MITM certs are trusted in Windows store but dotnet still rejects them.
    /// </summary>
    public static readonly bool RelaxTlsValidation = false;

    public static string RepositoryUrl => $"https://github.com/{GitHubOwner}/{GitHubRepo}";

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(GitHubOwner) &&
        !string.IsNullOrWhiteSpace(GitHubRepo);
}
