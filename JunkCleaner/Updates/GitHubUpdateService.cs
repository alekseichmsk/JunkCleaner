using System.Diagnostics;
using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace JunkCleaner.Updates;

public sealed class GitHubUpdateService
{
    private readonly UpdateManager? _manager;

    public GitHubUpdateService()
    {
        if (UpdateSettings.IsConfigured)
        {
            _manager = new UpdateManager(
                new GithubSource(
                    UpdateSettings.RepositoryUrl,
                    accessToken: null,
                    prerelease: UpdateSettings.IncludePrereleases));
        }
    }

    public async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken)
    {
        var current = GetCurrentVersionText();
        if (!UpdateSettings.IsConfigured)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                IsConfigured = false,
                IsInstalled = false,
                Message = "GitHub Releases не настроены: задайте GitHubOwner и GitHubRepo в UpdateSettings.cs.",
            };
        }

        if (_manager is null)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                IsConfigured = true,
                IsInstalled = false,
                Message = "Velopack UpdateManager не инициализирован.",
            };
        }

        if (!_manager.IsInstalled)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                IsConfigured = true,
                IsInstalled = false,
                ReleaseUrl = GetReleasesUrl(),
                Message =
                    "Текущий запуск не установлен через Velopack. " +
                    "Для автообновлений установите приложение через JunkCleaner-win-Setup.exe из GitHub Releases.",
            };
        }

        var pending = _manager.UpdatePendingRestart;
        if (pending is not null)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                LatestVersion = pending.Version.ToString(),
                LatestTag = "v" + pending.Version,
                ReleaseName = "JunkCleaner " + pending.Version,
                ReleaseUrl = GetReleasesUrl(),
                Body = GetReleaseNotes(pending) ?? "Обновление уже скачано и будет применено при перезапуске.",
                IsConfigured = true,
                IsInstalled = true,
                IsNewerAvailable = true,
                Message = $"Обновление {pending.Version} уже скачано и ожидает перезапуска.",
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (update is not null)
        {
            var target = update.TargetFullRelease;
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                LatestVersion = target.Version.ToString(),
                LatestTag = "v" + target.Version,
                ReleaseName = "JunkCleaner " + target.Version,
                ReleaseUrl = GetReleasesUrl(),
                Body = GetReleaseNotes(target),
                Update = update,
                IsConfigured = true,
                IsInstalled = true,
                IsNewerAvailable = true,
                Message = $"Доступна версия {target.Version}.",
            };
        }

        return new UpdateCheckResult
        {
            CurrentVersion = current,
            IsConfigured = true,
            IsInstalled = true,
            ReleaseUrl = GetReleasesUrl(),
            Message = $"Установлена актуальная версия ({current}).",
        };
    }

    public async Task DownloadUpdateAsync(
        UpdateInfo update,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        if (_manager is null)
            throw new InvalidOperationException("Velopack UpdateManager не инициализирован.");

        Action<int>? progressCallback = progress is null ? null : progress.Report;
        await _manager.DownloadUpdatesAsync(update, progressCallback, cancellationToken).ConfigureAwait(false);
    }

    public void ApplyUpdateAndRestart(UpdateCheckResult result)
    {
        if (_manager is null)
            throw new InvalidOperationException("Velopack UpdateManager не инициализирован.");

        var asset = result.Update?.TargetFullRelease ?? _manager.UpdatePendingRestart;
        if (asset is null)
            throw new InvalidOperationException("Нет скачанного обновления для применения.");

        _manager.WaitExitThenApplyUpdates(asset, silent: false, restart: true);
    }

    public static void OpenReleasePage(UpdateCheckResult result)
    {
        var url = string.IsNullOrWhiteSpace(result.ReleaseUrl)
            ? GetReleasesUrl()
            : result.ReleaseUrl;

        Process.Start(
            new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
    }

    public static string FormatUpdate(UpdateInfo update)
    {
        var target = update.TargetFullRelease;
        return $"{target.FileName} ({target.Size / 1024d / 1024d:0.0} MB)";
    }

    private string GetCurrentVersionText()
    {
        if (_manager?.CurrentVersion is not null)
            return _manager.CurrentVersion.ToString();

        return (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0)).ToString();
    }

    private static string? GetReleaseNotes(VelopackAsset asset)
    {
        return string.IsNullOrWhiteSpace(asset.NotesMarkdown)
            ? asset.NotesHTML
            : asset.NotesMarkdown;
    }

    private static string GetReleasesUrl() => $"{UpdateSettings.RepositoryUrl}/releases";
}
