using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
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
            if (asset is null)
                continue;

            var releaseUrl = GetString(release, "html_url");
            var latestComparable = NormalizeVersion(latest);
            var currentComparable = NormalizeVersion(current);

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
                IsNewerAvailable = latestComparable > currentComparable,
                Message = latestComparable > currentComparable
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

    public static void LaunchSelfUpdateFromZip(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            throw new FileNotFoundException("Архив обновления не найден.", zipPath);

        var targetDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var updaterPath = FindInstalledUpdater(targetDir);
        if (updaterPath is null)
        {
            throw new FileNotFoundException(
                "Локальный JunkCleaner.Updater.exe не найден рядом с приложением. " +
                "Установите релиз вручную один раз, после этого автообновление сможет заменять старую папку автоматически.");
        }

        var extractRoot = Path.Combine(
            Path.GetTempPath(),
            "JunkCleaner",
            "Updates",
            "install-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(extractRoot);
        ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);

        var extractedMainExe = Path.Combine(extractRoot, "JunkCleaner.exe");
        if (!File.Exists(extractedMainExe))
            throw new FileNotFoundException("В архиве обновления не найден JunkCleaner.exe.");

        var mainExe = Path.Combine(targetDir, "JunkCleaner.exe");
        if (!File.Exists(mainExe))
        {
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(currentExe) && File.Exists(currentExe))
                mainExe = currentExe;
        }

        var args =
            "--source " + Quote(extractRoot) + " " +
            "--target " + Quote(targetDir) + " " +
            "--main-exe " + Quote(mainExe) + " " +
            "--wait-pid " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);

        Process.Start(
            new ProcessStartInfo
            {
                FileName = updaterPath,
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(updaterPath),
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
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("JunkCleaner-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string? FindInstalledUpdater(string targetDir)
    {
        var direct = Path.Combine(targetDir, "Updater", "JunkCleaner.Updater.exe");
        if (File.Exists(direct))
            return direct;

        var root = Path.Combine(targetDir, "JunkCleaner.Updater.exe");
        if (File.Exists(root))
            return root;

        return null;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static Version GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return NormalizeVersion(version ?? new Version(0, 0, 0, 0));
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
            var found = list.FirstOrDefault(a => IsPreferredAsset(a, ext));
            if (found is not null)
                return found;
        }

        return null;
    }

    private static bool IsPreferredAsset(GitHubReleaseAsset asset, string extension)
    {
        if (!asset.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!asset.Name.Contains("JunkCleaner", StringComparison.OrdinalIgnoreCase))
            return false;

        return extension is ".appinstaller" or ".exe" or ".msi" ||
               asset.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase);
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

        if (!Version.TryParse(cleaned, out var parsed))
            return false;

        version = NormalizeVersion(parsed);
        return true;
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
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
