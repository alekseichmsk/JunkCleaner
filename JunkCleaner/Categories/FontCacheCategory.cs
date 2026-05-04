using JunkCleaner.Contracts;
using JunkCleaner.Models;
using JunkCleaner.Security;
using JunkCleaner.Services;

namespace JunkCleaner.Categories;

public sealed class FontCacheCategory : ICleanupCategory
{
    public string Id => "font-cache";

    public string DisplayName => "Кэш шрифтов Windows";

    public string Description =>
        "Сброс C:\\Windows\\ServiceProfiles\\LocalService\\AppData\\Local\\FontCache. " +
        "Нужно при битых/устаревших шрифтах и превью; обычно требует администратора.";

    public bool RequiresAdmin => true;

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
                ServiceCommand.TrySc("FontCache", "stop");
                try
                {
                    return DeleteFilesWorker(scanResult.FilePaths, cancellationToken);
                }
                finally
                {
                    ServiceCommand.TrySc("FontCache", "start");
                }
            },
            cancellationToken);
    }

    private static (List<string> paths, long totalBytes) ScanWorker(CancellationToken ct)
    {
        var root = GetFontCacheRoot();
        var paths = new List<string>();
        long total = 0;

        foreach (var file in SafeEnumerateFiles(root, ct))
        {
            ct.ThrowIfCancellationRequested();

            if (ProtectedPaths.IsBlocked(file))
                continue;

            try
            {
                var fi = new FileInfo(file);
                if (!fi.Exists)
                    continue;

                paths.Add(fi.FullName);
                total += fi.Length;
            }
            catch
            {
                // Cache files can be locked by the font cache service.
            }
        }

        return (paths, total);
    }

    private static string GetFontCacheRoot()
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return Path.Combine(win, "ServiceProfiles", "LocalService", "AppData", "Local", "FontCache");
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            yield break;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(
                root,
                "*",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
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
            ErrorMessage = errors > 0 ? "Часть кэша шрифтов не удалось удалить. Попробуйте запуск от администратора." : null,
        };
    }
}
