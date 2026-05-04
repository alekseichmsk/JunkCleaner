using JunkCleaner.Contracts;
using JunkCleaner.Models;

namespace JunkCleaner.Services;

public sealed class ScanOrchestrator
{
    public async Task<IReadOnlyList<(ICleanupCategory Category, ScanResult Result)>> ScanAsync(
        IReadOnlyList<ICleanupCategory> categories,
        CancellationToken cancellationToken)
    {
        if (categories.Count == 0)
            return Array.Empty<(ICleanupCategory, ScanResult)>();

        var tasks = categories.Select(async c =>
        {
            var r = await c.ScanAsync(cancellationToken).ConfigureAwait(false);
            return (Category: c, Result: r);
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(ICleanupCategory Category, CleanupResult Result)>> CleanAsync(
        IReadOnlyList<(ICleanupCategory Category, ScanResult Scan)> items,
        CancellationToken cancellationToken)
    {
        var list = new List<(ICleanupCategory, CleanupResult)>();

        foreach (var (category, scan) in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!scan.Success)
            {
                list.Add((category,
                    new CleanupResult
                    {
                        CategoryId = category.Id,
                        Success = false,
                        ErrorMessage = scan.ErrorMessage ?? "Сканирование не удалось.",
                    }));
                continue;
            }

            if (!scan.UsesNativeCleanup && scan.FilePaths.Count == 0)
            {
                list.Add((category,
                    new CleanupResult
                    {
                        CategoryId = category.Id,
                        Success = true,
                        FreedBytes = 0,
                        FilesDeleted = 0,
                    }));
                continue;
            }

            var result = await category.CleanAsync(scan, cancellationToken).ConfigureAwait(false);
            list.Add((category, result));
        }

        return list;
    }
}
