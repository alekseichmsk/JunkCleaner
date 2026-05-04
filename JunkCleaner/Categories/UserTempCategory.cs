using JunkCleaner.Contracts;
using JunkCleaner.Models;
using JunkCleaner.Services;

namespace JunkCleaner.Categories;

public sealed class UserTempCategory : ICleanupCategory
{
    private readonly DeletionService _deletion;

    public UserTempCategory(DeletionService deletion) => _deletion = deletion;

    public string Id => "user-temp";

    public string DisplayName => "Временные файлы пользователя";

    public string Description => "Папки %TEMP% / %TMP% и стандартный каталог временных файлов профиля.";

    public bool RequiresAdmin => false;

    public async Task<ScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (paths, total) = await FolderScanner.ScanAsync(GetRoots(), cancellationToken).ConfigureAwait(false);
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

    private static IEnumerable<string> GetRoots()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in new[]
                 {
                     Environment.GetEnvironmentVariable("TEMP"),
                     Environment.GetEnvironmentVariable("TMP"),
                     Path.GetTempPath(),
                 })
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var t = raw.Trim();
            if (set.Add(t))
                yield return t;
        }
    }
}
