using JunkCleaner.Contracts;
using JunkCleaner.Models;
using JunkCleaner.Services;

namespace JunkCleaner.Categories;

public sealed class WindowsTempCategory : ICleanupCategory
{
    private readonly DeletionService _deletion;

    public WindowsTempCategory(DeletionService deletion) => _deletion = deletion;

    public string Id => "windows-temp";

    public string DisplayName => "Временные файлы Windows";

    public string Description => "Папка Windows\\Temp. Может потребоваться запуск от имени администратора.";

    public bool RequiresAdmin => true;

    public async Task<ScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(win))
            {
                return new ScanResult
                {
                    CategoryId = Id,
                    Success = false,
                    ErrorMessage = "Не удалось определить каталог Windows.",
                };
            }

            var root = Path.Combine(win, "Temp");
            var (paths, total) = await FolderScanner.ScanAsync(new[] { root }, cancellationToken)
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
                ErrorMessage = $"Нет доступа (попробуйте запуск от администратора): {ex.Message}",
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
}
