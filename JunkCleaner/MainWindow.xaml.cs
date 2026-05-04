using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using JunkCleaner.Categories;
using JunkCleaner.Contracts;
using JunkCleaner.Models;
using JunkCleaner.Services;
using JunkCleaner.Ui;

namespace JunkCleaner;

public partial class MainWindow : Window
{
    private readonly DeletionService _deletion = new();
    private readonly ScanOrchestrator _orchestrator = new();
    private readonly FileCleanupLogger _logger = new();
    private readonly ObservableCollection<CategoryPresentation> _rows = new();
    private CancellationTokenSource? _operationsCts;

    public MainWindow()
    {
        InitializeComponent();
        MysticTitleBar.ApplyWhenReady(this);

        ICleanupCategory[] cats =
        {
            new UserTempCategory(_deletion),
            new WindowsTempCategory(_deletion),
            new WindowsStoreCacheCategory(_deletion),
            new FontCacheCategory(),
            new IconCacheCategory(),
            new UserCrashDumpsCategory(_deletion),
            new SystemCrashDumpsCategory(_deletion),
            new RecycleBinCategory(),
        };

        foreach (var c in cats)
        {
            var row = new CategoryPresentation(c);
            if (IsRepairCategory(c.Id))
                row.IsIncluded = false;
            _rows.Add(row);
        }

        CategoryList.ItemsSource = _rows;
    }

    private void SetOperating(bool busy)
    {
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ScanButton.IsEnabled = !busy;
        CleanButton.IsEnabled = !busy;
        DnsButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        CategoryList.IsEnabled = !busy;
        MinAgeDaysBox.IsEnabled = !busy;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        var selected = IncludedCategories();
        if (selected.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Отметьте хотя бы одну категорию.", "JunkCleaner", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        if (!TryParseMinAgeDays(out var minAgeDays))
            return;

        RestartCts();
        var ct = _operationsCts!.Token;

        try
        {
            SetOperating(true);
            StatusText.Text = "Сканирование…";

            foreach (var vm in IncludedPresentation())
            {
                vm.LastScan = null;
                vm.SizeText = "…";
            }

            var results = await _orchestrator.ScanAsync(selected, ct).ConfigureAwait(true);

            foreach (var (category, scan) in results)
            {
                var row = _rows.FirstOrDefault(r => r.Category.Id == category.Id);
                if (row is null || !row.IsIncluded)
                    continue;

                var filteredScan = scan.Success ? ApplyAgeFilter(scan, minAgeDays) : scan;

                row.LastScan = filteredScan;
                row.SizeText = filteredScan.Success
                    ? ByteFormat.Format(filteredScan.TotalBytes)
                    : $"ошибка: {scan.ErrorMessage}";
            }

            StatusText.Text = minAgeDays > 0
                ? $"Сканирование завершено. Учтены только файлы старше {minAgeDays.ToString("0.##", CultureInfo.InvariantCulture)} дн.; корзина фильтр возраста не поддерживает."
                : "Сканирование завершено. Проверьте размеры и при необходимости выполните очистку.";
            _logger.Append(
                $"Scan completed: {results.Count(r => r.Result.Success)} ok, failed {results.Count(r => !r.Result.Success)}");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Сканирование отменено.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка сканирования.";
            System.Windows.MessageBox.Show(this, ex.Message, "JunkCleaner", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            SetOperating(false);
            DisposeCts();
        }
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseMinAgeDays(out var minAgeDays))
            return;

        var payload = new List<(ICleanupCategory Category, Models.ScanResult Scan)>();
        foreach (var row in _rows.Where(r => r.IsIncluded))
        {
            if (row.LastScan is not { } scan)
            {
                System.Windows.MessageBox.Show(this, "Сначала выполните сканирование для выбранных категорий.", "JunkCleaner",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (!scan.Success)
            {
                System.Windows.MessageBox.Show(this, $"Категория «{row.Category.DisplayName}» не была успешно отсканирована.",
                    "JunkCleaner", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var effectiveScan = scan.Success ? ApplyAgeFilter(scan, minAgeDays) : scan;

            if (!effectiveScan.UsesNativeCleanup && effectiveScan.FilePaths.Count == 0)
                continue;

            payload.Add((row.Category, effectiveScan));
        }

        if (payload.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Нет данных для очистки: отметьте категории с ненулевым объёмом.", "JunkCleaner",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var preview = new CleanupPreviewWindow { Owner = this };
        preview.SetBody(BuildCleanupPreview(payload, minAgeDays));
        if (preview.ShowDialog() != true)
            return;

        RestartCts();
        var ct = _operationsCts!.Token;

        try
        {
            SetOperating(true);
            StatusText.Text = "Очистка…";

            var results = await _orchestrator.CleanAsync(payload, ct).ConfigureAwait(true);

            var sb = new StringBuilder();
            long total = 0;
            foreach (var (category, res) in results)
            {
                total += Math.Max(res.FreedBytes, 0);
                _logger.Append(
                    $"Clean [{category.Id}]: success={res.Success}, freed={res.FreedBytes}, deleted={res.FilesDeleted}, errors={res.Errors}");

                sb.AppendLine($"— {category.DisplayName}");
                sb.AppendLine(
                    $"  результат: {(res.Success ? "успех" : "с ошибками")}, освобождено ≈ {ByteFormat.Format(Math.Max(res.FreedBytes, 0))}");

                if (res.FilesDeleted > 0)
                    sb.AppendLine($"  элементов: {res.FilesDeleted.ToString(CultureInfo.InvariantCulture)}");

                if (!string.IsNullOrEmpty(res.ErrorMessage))
                    sb.AppendLine($"  сообщение: {res.ErrorMessage}");

                if (res.ErrorMessages.Count > 0)
                {
                    sb.AppendLine("  ошибки (фрагмент):");
                    foreach (var err in res.ErrorMessages.Take(8))
                        sb.AppendLine("    · " + err);
                }

                sb.AppendLine();
            }

            sb.AppendLine($"Итого освобождено (оценка): {ByteFormat.Format(total)}");

            StatusText.Text = "Очистка завершена. См. отчёт.";

            var report = new ReportWindow { Owner = this };
            report.SetBody(sb.ToString());
            report.ShowDialog();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Очистка отменена.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка очистки.";
            System.Windows.MessageBox.Show(this, ex.Message, "JunkCleaner", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            SetOperating(false);
            DisposeCts();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _operationsCts?.Cancel();
    }

    private void Duplicates_Click(object sender, RoutedEventArgs e)
    {
        var duplicatesWindow = new DuplicatesWindow { Owner = this };

        duplicatesWindow.ShowDialog();
    }

    private async void DnsFlush_Click(object sender, RoutedEventArgs e)
    {
        if (!ScanButton.IsEnabled)
            return;

        try
        {
            DnsButton.IsEnabled = false;
            StatusText.Text = "Сброс DNS‑кэша…";
            var (ok, text) = await DnsFlushService.FlushAsync().ConfigureAwait(true);
            StatusText.Text = ok ? "DNS‑кэш сброшен." : "Сброс DNS‑кэша завершился с ошибкой.";
            System.Windows.MessageBox.Show(
                this,
                text,
                ok ? "DNS" : "Ошибка",
                System.Windows.MessageBoxButton.OK,
                ok ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
            _logger.Append($"DNS flush: ok={ok}, output={text}");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "JunkCleaner", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            DnsButton.IsEnabled = true;
        }
    }

    private void RestartCts()
    {
        DisposeCts();
        _operationsCts = new CancellationTokenSource();
    }

    private void DisposeCts()
    {
        _operationsCts?.Dispose();
        _operationsCts = null;
    }

    private IReadOnlyList<ICleanupCategory> IncludedCategories() =>
        _rows.Where(r => r.IsIncluded).Select(r => r.Category).ToList();

    private IEnumerable<CategoryPresentation> IncludedPresentation() =>
        _rows.Where(r => r.IsIncluded);

    private bool TryParseMinAgeDays(out double days)
    {
        days = 0;

        var text = MinAgeDaysBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return true;

        if (double.TryParse(
                text.Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out days) &&
            days >= 0 &&
            days <= 3650)
        {
            return true;
        }

        System.Windows.MessageBox.Show(
            this,
            "Введите возраст файлов в днях: число от 0 до 3650. Например: 0, 1, 3 или 7.",
            "JunkCleaner",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
        return false;
    }

    private static ScanResult ApplyAgeFilter(ScanResult scan, double minAgeDays)
    {
        if (scan.UsesNativeCleanup || minAgeDays <= 0 || scan.FilePaths.Count == 0)
            return scan;

        if (scan.CategoryId is "font-cache" or "icon-cache")
            return scan;

        var cutoffUtc = DateTime.UtcNow - TimeSpan.FromDays(minAgeDays);
        var kept = new List<string>(scan.FilePaths.Count);
        long total = 0;

        foreach (var path in scan.FilePaths)
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                var fi = new FileInfo(path);
                if (fi.LastWriteTimeUtc > cutoffUtc)
                    continue;

                kept.Add(path);
                total += fi.Length;
            }
            catch
            {
                // Safety first: files we cannot inspect are not included after age filtering.
            }
        }

        return new ScanResult
        {
            CategoryId = scan.CategoryId,
            Success = scan.Success,
            ErrorMessage = scan.ErrorMessage,
            TotalBytes = total,
            FilePaths = kept,
            UsesNativeCleanup = scan.UsesNativeCleanup,
            NativeItemCount = scan.NativeItemCount,
        };
    }

    private static string BuildCleanupPreview(
        IReadOnlyList<(ICleanupCategory Category, ScanResult Scan)> payload,
        double minAgeDays)
    {
        var sb = new StringBuilder();
        long total = 0;

        sb.AppendLine("ПРЕДПРОСМОТР ОЧИСТКИ");
        sb.AppendLine();
        sb.AppendLine("Действие нельзя отменить штатными средствами приложения.");
        if (minAgeDays > 0)
        {
            sb.AppendLine(
                $"Фильтр безопасности: файловые категории удалят только файлы старше {minAgeDays.ToString("0.##", CultureInfo.InvariantCulture)} дн.");
            sb.AppendLine("Примечание: корзина очищается через Windows Shell API и не поддерживает фильтр возраста.");
        }
        else
        {
            sb.AppendLine("Фильтр возраста отключён.");
        }

        sb.AppendLine();

        foreach (var (category, scan) in payload)
        {
            total += Math.Max(scan.TotalBytes, 0);
            sb.AppendLine($"— {category.DisplayName}");
            sb.AppendLine($"  описание: {category.Description}");
            sb.AppendLine($"  объём: {ByteFormat.Format(Math.Max(scan.TotalBytes, 0))}");

            if (scan.UsesNativeCleanup)
            {
                sb.AppendLine(
                    $"  native-очистка: да; элементов (оценка): {scan.NativeItemCount.ToString(CultureInfo.InvariantCulture)}");
            }
            else
            {
                sb.AppendLine($"  файлов: {scan.FilePaths.Count.ToString(CultureInfo.InvariantCulture)}");
                if (scan.FilePaths.Count > 0)
                {
                    sb.AppendLine("  примеры:");
                    foreach (var path in scan.FilePaths.Take(12))
                        sb.AppendLine("    " + path);

                    if (scan.FilePaths.Count > 12)
                        sb.AppendLine($"    … ещё {scan.FilePaths.Count - 12} файлов");
                }
            }

            if (category.RequiresAdmin)
                sb.AppendLine("  может потребоваться запуск от имени администратора");

            sb.AppendLine();
        }

        sb.AppendLine($"ИТОГО К ОЧИСТКЕ (оценка): {ByteFormat.Format(total)}");
        sb.AppendLine();
        sb.AppendLine("Нажмите «Удалить», чтобы продолжить, или «Отмена», чтобы вернуться.");
        return sb.ToString();
    }

    private static bool IsRepairCategory(string categoryId) =>
        categoryId is "font-cache" or "icon-cache";

}
