using JunkCleaner.Contracts;
using JunkCleaner.Models;
using JunkCleaner.Security;
using JunkCleaner.Services;

namespace JunkCleaner.Categories;

public sealed class UserCrashDumpsCategory : ICleanupCategory
{
    private readonly DeletionService _deletion;

    public UserCrashDumpsCategory(DeletionService deletion) => _deletion = deletion;

    public string Id => "user-crash-dumps";

    public string DisplayName => "Дампы сбоев приложений";

    public string Description =>
        "%LocalAppData%\\CrashDumps (*.dmp, *.hdmp). Диагностические файлы: удаляйте, если не планируете разбирать падения программ.";

    public bool RequiresAdmin => false;

    public async Task<ScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (paths, total) = await Task.Run(
                    () => CrashDumpScanHelpers.ScanFiles(UserDumpCandidates(cancellationToken), cancellationToken),
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
        return _deletion.DeleteFilesAsync(Id, scanResult.FilePaths, cancellationToken);
    }

    private static IEnumerable<string> UserDumpCandidates(CancellationToken ct)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            yield break;

        var crashDumps = Path.Combine(local, "CrashDumps");
        foreach (var file in CrashDumpScanHelpers.SafeEnumerateFiles(crashDumps, recursive: false, ct))
            yield return file;
    }
}

public sealed class SystemCrashDumpsCategory : ICleanupCategory
{
    private readonly DeletionService _deletion;

    public SystemCrashDumpsCategory(DeletionService deletion) => _deletion = deletion;

    public string Id => "system-crash-dumps";

    public string DisplayName => "Системные дампы Windows";

    public string Description =>
        "C:\\Windows\\MEMORY.DMP и C:\\Windows\\Minidump. Нужны для анализа BSOD; обычно требуют запуск от администратора.";

    public bool RequiresAdmin => true;

    public async Task<ScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (paths, total) = await Task.Run(
                    () => CrashDumpScanHelpers.ScanFiles(SystemDumpCandidates(cancellationToken), cancellationToken),
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
        catch (UnauthorizedAccessException ex)
        {
            return new ScanResult
            {
                CategoryId = Id,
                Success = false,
                ErrorMessage = $"Нет доступа к системным дампам (попробуйте запуск от администратора): {ex.Message}",
            };
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
        return _deletion.DeleteFilesAsync(Id, scanResult.FilePaths, cancellationToken);
    }

    private static IEnumerable<string> SystemDumpCandidates(CancellationToken ct)
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(win))
            yield break;

        var memoryDump = Path.Combine(win, "MEMORY.DMP");
        if (File.Exists(memoryDump))
            yield return memoryDump;

        var minidump = Path.Combine(win, "Minidump");
        foreach (var file in CrashDumpScanHelpers.SafeEnumerateFiles(minidump, recursive: false, ct))
            yield return file;
    }
}

internal static class CrashDumpScanHelpers
{
    private static readonly HashSet<string> DumpExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dmp",
        ".hdmp",
        ".mdmp",
    };

    public static (List<string> paths, long totalBytes) ScanFiles(IEnumerable<string> candidates, CancellationToken ct)
    {
        var paths = new List<string>();
        long total = 0;

        foreach (var file in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(file) || ProtectedPaths.IsBlocked(file))
                continue;

            try
            {
                if (!File.Exists(file))
                    continue;

                var fi = new FileInfo(file);
                if (!DumpExtensions.Contains(fi.Extension))
                    continue;

                paths.Add(fi.FullName);
                total += fi.Length;
            }
            catch
            {
                // File can be locked, deleted or inaccessible while scanning.
            }
        }

        return (paths, total);
    }

    public static IEnumerable<string> SafeEnumerateFiles(string root, bool recursive, CancellationToken ct)
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
                    RecurseSubdirectories = recursive,
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
}
