using System.Diagnostics;
using JunkCleaner.Contracts;
using JunkCleaner.Models;
using JunkCleaner.Security;

namespace JunkCleaner.Categories;

public sealed class IconCacheCategory : ICleanupCategory
{
    public string Id => "icon-cache";

    public string DisplayName => "База иконок Windows";

    public string Description =>
        "Сброс IconCache.db и iconcache_*.db. Нужен при белых, неправильных или устаревших иконках; при необходимости Проводник будет перезапущен.";

    public bool RequiresAdmin => false;

    public async Task<ScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (paths, total) = await Task.Run(
                    () => ScanWorker(cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            return new ScanResult
            {
                CategoryId = Id,
                Success = true,
                TotalBytes = total,
                FilePaths = paths,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ScanResult
            {
                CategoryId = Id,
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    public Task<CleanupResult> CleanAsync(ScanResult scanResult, CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                var first = DeleteFilesWorker(scanResult.FilePaths, cancellationToken);
                if (first.Errors == 0)
                    return first;

                RestartExplorer();
                var second = DeleteFilesWorker(scanResult.FilePaths, cancellationToken);

                return new CleanupResult
                {
                    CategoryId = Id,
                    FreedBytes = first.FreedBytes + second.FreedBytes,
                    FilesDeleted = first.FilesDeleted + second.FilesDeleted,
                    Errors = second.Errors,
                    ErrorMessages = second.ErrorMessages,
                    Success = second.Errors == 0,
                    ErrorMessage = second.Errors > 0
                        ? "Часть базы иконок не удалось удалить даже после перезапуска Проводника."
                        : "Проводник был перезапущен, база иконок будет перестроена Windows.",
                };
            },
            cancellationToken);
    }

    private static (List<string> paths, long totalBytes) ScanWorker(CancellationToken ct)
    {
        var paths = new List<string>();
        long total = 0;

        foreach (var file in CandidateFiles(ct).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            if (ProtectedPaths.IsBlocked(file))
                continue;

            try
            {
                if (!File.Exists(file))
                    continue;

                var fi = new FileInfo(file);
                paths.Add(fi.FullName);
                total += fi.Length;
            }
            catch
            {
                // Icon cache files can disappear during Explorer activity.
            }
        }

        return (paths, total);
    }

    private static IEnumerable<string> CandidateFiles(CancellationToken ct)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            yield break;

        var iconCache = Path.Combine(local, "IconCache.db");
        if (File.Exists(iconCache))
            yield return iconCache;

        var explorer = Path.Combine(local, "Microsoft", "Windows", "Explorer");
        foreach (var file in SafeEnumerateFiles(explorer, "iconcache*.db", ct))
            yield return file;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            yield break;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(
                root,
                pattern,
                new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                });
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            yield return file;
        }
    }

    private CleanupResult DeleteFilesWorker(IReadOnlyList<string> files, CancellationToken ct)
    {
        long freed = 0;
        var deleted = 0;
        var errors = 0;
        var errorMessages = new List<string>();

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(path))
                    continue;

                var len = 0L;
                try
                {
                    len = new FileInfo(path).Length;
                }
                catch
                {
                    // ignore
                }

                File.Delete(path);
                freed += len;
                deleted++;
            }
            catch (Exception ex)
            {
                errors++;
                if (errorMessages.Count < 32)
                    errorMessages.Add($"{path}: {ex.Message}");
            }
        }

        return new CleanupResult
        {
            CategoryId = Id,
            FreedBytes = freed,
            FilesDeleted = deleted,
            Errors = errors,
            ErrorMessages = errorMessages,
            Success = errors == 0,
        };
    }

    private static void RestartExplorer()
    {
        try
        {
            using var kill = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = "/f /im explorer.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
            kill?.WaitForExit(8000);
        }
        catch
        {
            // If Explorer was not running or taskkill failed, still try starting it below.
        }

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true,
                });
        }
        catch
        {
            // User can manually start Explorer if shell restart is blocked.
        }
    }
}
