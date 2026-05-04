using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using JunkCleaner.Ui;

namespace JunkCleaner.Updates;

public sealed class GitHubUpdateService
{
    private static readonly HttpClient Http = CreateClient();

    public async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken)
    {
        var current = GetCurrentVersion();
        if (!UpdateSettings.IsConfigured)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                IsConfigured = false,
                Message = "GitHub Releases не настроены: задайте GitHubOwner и GitHubRepo в UpdateSettings.cs.",
            };
        }

        var url = $"https://api.github.com/repos/{UpdateSettings.GitHubOwner}/{UpdateSettings.GitHubRepo}/releases";
        using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                IsConfigured = true,
                Message = $"GitHub API вернул {(int)response.StatusCode} {response.ReasonPhrase}.",
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var release in doc.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                continue;

            if (!UpdateSettings.IncludePrereleases &&
                release.TryGetProperty("prerelease", out var prerelease) &&
                prerelease.GetBoolean())
            {
                continue;
            }

            var tag = GetString(release, "tag_name");
            if (!TryParseVersion(tag, out var latest))
                continue;

            var asset = SelectAsset(release);
            var releaseUrl = GetString(release, "html_url");

            return new UpdateCheckResult
            {
                CurrentVersion = current,
                LatestVersion = latest,
                LatestTag = tag,
                ReleaseName = GetString(release, "name"),
                ReleaseUrl = releaseUrl,
                Body = GetString(release, "body"),
                Asset = asset,
                IsConfigured = true,
                IsNewerAvailable = latest > current,
                Message = latest > current
                    ? $"Доступна версия {tag}."
                    : $"Установлена актуальная версия ({current}).",
            };
        }

        return new UpdateCheckResult
        {
            CurrentVersion = current,
            IsConfigured = true,
            Message = "Не найден подходящий релиз GitHub Releases.",
        };
    }

    public async Task<string> DownloadAssetAsync(
        GitHubReleaseAsset asset,
        IProgress<(long Received, long? Total)>? progress,
        CancellationToken cancellationToken)
    {
        var downloadDir = Path.Combine(Path.GetTempPath(), "JunkCleaner", "Updates");
        Directory.CreateDirectory(downloadDir);

        var safeName = MakeSafeFileName(asset.Name);
        var target = Path.Combine(downloadDir, safeName);

        using var response = await Http.GetAsync(
                asset.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength > 0
            ? response.Content.Headers.ContentLength
            : asset.SizeBytes > 0 ? asset.SizeBytes : null;

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(target);

        var buffer = new byte[128 * 1024];
        long received = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report((received, total));
        }

        return target;
    }

    public static void LaunchDownloadedUpdate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Файл обновления не найден.", path);

        var extension = Path.GetExtension(path);
        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + path + "\"",
                    UseShellExecute = true,
                });
            return;
        }

        Process.Start(
            new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
    }

    public static void OpenReleasePage(UpdateCheckResult result)
    {
        if (string.IsNullOrWhiteSpace(result.ReleaseUrl))
            return;

        Process.Start(
            new ProcessStartInfo
            {
                FileName = result.ReleaseUrl,
                UseShellExecute = true,
            });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("JunkCleaner-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Version GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null
            ? new Version(0, 0, 0)
            : new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    private static GitHubReleaseAsset? SelectAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<GitHubReleaseAsset>();
        foreach (var asset in assets.EnumerateArray())
        {
            var name = GetString(asset, "name");
            var url = GetString(asset, "browser_download_url");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;

            long size = 0;
            if (asset.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var parsed))
                size = parsed;

            list.Add(new GitHubReleaseAsset { Name = name, DownloadUrl = url, SizeBytes = size });
        }

        foreach (var ext in UpdateSettings.PreferredAssetExtensions)
        {
            var found = list.FirstOrDefault(a =>
                a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("JunkCleaner", StringComparison.OrdinalIgnoreCase));
            if (found is not null)
                return found;

            found = list.FirstOrDefault(a => a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
                return found;
        }

        return null;
    }

    private static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var cleaned = tag.Trim();
        if (cleaned.StartsWith('v') || cleaned.StartsWith('V'))
            cleaned = cleaned[1..];

        var suffixIndex = cleaned.IndexOfAny(['-', '+', ' ']);
        if (suffixIndex >= 0)
            cleaned = cleaned[..suffixIndex];

        return Version.TryParse(cleaned, out version!);
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    public static string FormatAsset(GitHubReleaseAsset asset)
    {
        return asset.SizeBytes > 0
            ? $"{asset.Name} ({ByteFormat.Format(asset.SizeBytes)})"
            : asset.Name;
    }
}
