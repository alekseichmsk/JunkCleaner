using JunkCleaner.Models;

namespace JunkCleaner.Services;

public sealed class DeletionService
{
    public Task<CleanupResult> DeleteFilesAsync(
        string categoryId,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => DeleteFilesWorker(categoryId, filePaths, cancellationToken), cancellationToken);
    }

    private static CleanupResult DeleteFilesWorker(
        string categoryId,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken)
    {
        long freed = 0;
        var errors = 0;
        var successCount = 0;
        var errorMsgs = new List<string>();

        foreach (var path in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                long size = 0;
                if (File.Exists(path))
                {
                    try
                    {
                        size = new FileInfo(path).Length;
                    }
                    catch
                    {
                        size = 0;
                    }

                    File.Delete(path);
                    freed += size;
                    successCount++;
                }
            }
            catch (Exception ex)
            {
                errors++;
                if (errorMsgs.Count < 32)
                    errorMsgs.Add($"{path}: {ex.Message}");
            }
        }

        return new CleanupResult
        {
            CategoryId = categoryId,
            FreedBytes = freed,
            FilesDeleted = successCount,
            Errors = errors,
            ErrorMessages = errorMsgs,
            Success = errors == 0,
        };
    }
}
