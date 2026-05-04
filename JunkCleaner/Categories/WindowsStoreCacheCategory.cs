using JunkCleaner.Contracts;
using JunkCleaner.Models;
using JunkCleaner.Services;

namespace JunkCleaner.Categories;

public sealed class WindowsStoreCacheCategory : ICleanupCategory
{
    private readonly DeletionService _deletion;

    public WindowsStoreCacheCategory(DeletionService deletion) => _deletion = deletion;

    public string Id => "windows-store-cache";

    public string DisplayName => "Кэш приложений Windows Store";

    public string Description =>
        "LocalCache у UWP/MSIX-пакетов и временные файлы доставки обновлений Microsoft Store / Delivery Optimization.";

    public bool RequiresAdmin => true;

    public async Task<ScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            var roots = GetRoots().ToList();
            var (paths, total) = await FolderScanner.ScanAsync(roots, cancellationToken).ConfigureAwait(false);

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
                ErrorMessage = $"Нет доступа к части кэша Store/Delivery Optimization: {ex.Message}",
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

    private static IEnumerable<string> GetRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var localCache in EnumeratePackageLocalCaches())
        {
            if (seen.Add(localCache))
                yield return localCache;
        }

        foreach (var deliveryRoot in GetDeliveryOptimizationCacheRoots())
        {
            if (seen.Add(deliveryRoot))
                yield return deliveryRoot;
        }
    }

    private static IEnumerable<string> EnumeratePackageLocalCaches()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            yield break;

        var packagesRoot = Path.Combine(localAppData, "Packages");
        if (!Directory.Exists(packagesRoot))
            yield break;

        IEnumerable<string> packageDirs;
        try
        {
            packageDirs = Directory.EnumerateDirectories(packagesRoot);
        }
        catch
        {
            yield break;
        }

        foreach (var packageDir in packageDirs)
        {
            string localCache;
            try
            {
                localCache = Path.Combine(packageDir, "LocalCache");
            }
            catch
            {
                continue;
            }

            if (Directory.Exists(localCache))
                yield return localCache;
        }
    }

    private static IEnumerable<string> GetDeliveryOptimizationCacheRoots()
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(win))
        {
            yield return Path.Combine(
                win,
                "ServiceProfiles",
                "NetworkService",
                "AppData",
                "Local",
                "Microsoft",
                "Windows",
                "DeliveryOptimization",
                "Cache");
        }

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData))
        {
            yield return Path.Combine(programData, "Microsoft", "Windows", "DeliveryOptimization", "Cache");
            yield return Path.Combine(programData, "Microsoft", "Windows", "DeliveryOptimization", "State");
        }
    }
}
