using JunkCleaner.Models;

namespace JunkCleaner.Contracts;

public interface ICleanupCategory
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    bool RequiresAdmin { get; }

    Task<ScanResult> ScanAsync(CancellationToken cancellationToken);

    Task<CleanupResult> CleanAsync(ScanResult scanResult, CancellationToken cancellationToken);
}
