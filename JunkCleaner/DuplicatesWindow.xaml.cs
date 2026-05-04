using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JunkCleaner.Duplicates;
using JunkCleaner.Security;
using JunkCleaner.Services;
using JunkCleaner.Ui;
using WinForms = System.Windows.Forms;

namespace JunkCleaner;

public partial class DuplicatesWindow : Window
{
    private static readonly DuplicateScanEngine Engine = new();

    private readonly DeletionService _deletion = new();
    private readonly FileCleanupLogger _logger = new();

    private readonly ObservableCollection<string> _folders = new();
    private readonly ObservableCollection<DuplicateGroupVm> _groups = new();

    private CancellationTokenSource? _operationsCts;

    /// <summary>Полный результат последнего скана (обновляется после удаления файлов).</summary>
    private List<DuplicateScanGroup>? _fullScanOrdered;

    private string[]? _cachedProgramFilesPrefixes;

    private string? _cachedWindowsPrefix;

    public DuplicatesWindow()
    {
        InitializeComponent();
        MysticTitleBar.ApplyWhenReady(this);
        FoldersList.ItemsSource = _folders;
        DupGroupsList.ItemsSource = _groups;
        DupFilterMinWastedMbBox.Text = "0";
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = "Выберите папку для поиска дубликатов:",
            UseDescriptionForTitle = true,
        };

        if (dlg.ShowDialog() != WinForms.DialogResult.OK)
            return;

        if (string.IsNullOrWhiteSpace(dlg.SelectedPath))
            return;

        string normalized;
        try
        {
            normalized = ProtectedPaths.NormalizePath(dlg.SelectedPath);
        }
        catch
        {
            System.Windows.MessageBox.Show(this, "Не удалось распознать путь к папке.", "Дубликаты",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (ProtectedPaths.IsBlocked(normalized))
        {
            var warn = System.Windows.MessageBox.Show(
                this,
                "Выбранная папка пересекается с защищаемыми системными зонами программы. Это опасно.\n\nПродолжить добавление?",
                "Предупреждение",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (warn != System.Windows.MessageBoxResult.Yes)
                return;
        }

        if (_folders.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return;

        _folders.Add(normalized);
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FoldersList.SelectedItem is not string path)
        {
            System.Windows.MessageBox.Show(this, "Выберите папку в списке.", "Дубликаты", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        _folders.Remove(path);
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_folders.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Добавьте хотя бы одну папку для сканирования.", "Дубликаты",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        if (!TryParseMinKb(MinSizeKbBox.Text, out var minKb))
        {
            System.Windows.MessageBox.Show(this, "Введите минимальный размер в килобайтах (целое число ≥ 0).", "Дубликаты",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var preset = MapPreset(PresetCombo.SelectedItem as ComboBoxItem);

        RestartCts();
        var ct = _operationsCts!.Token;

        _groups.Clear();
        _fullScanOrdered = null;
        DupGroupsList.SelectedItem = null;
        UpdateDupDetailForSelection();
        DupFilterPanel.IsEnabled = false;

        try
        {
            SetDupOperating(true, scanning: true);
            DupScanStatsPanel.Visibility = Visibility.Collapsed;

            var minBytes = checked((long)minKb * 1024L);
            var roots = _folders.ToArray();
            var options = new DuplicateScanOptions(roots, minBytes, preset);

            var progress = new Progress<(string Phase, int Current, int Total)>(p =>
            {
                DupStatusText.Text = p.Total <= 0 ? p.Phase : $"{p.Phase} {p.Current}/{p.Total}";

                var determinateKnownTotal =
                    p.Total > 0 &&
                    (string.Equals(p.Phase, "Быстрый отбор (начало файла)…", StringComparison.Ordinal) ||
                     string.Equals(p.Phase, "SHA-256 полного файла…", StringComparison.Ordinal));

                if (determinateKnownTotal)
                {
                    DupProgressIndeterminate.Visibility = Visibility.Collapsed;
                    DupProgressDeterminate.Visibility = Visibility.Visible;
                    DupProgressDeterminate.Maximum = p.Total;
                    DupProgressDeterminate.Value = Math.Min(p.Current, p.Total);
                }
                else
                {
                    DupProgressDeterminate.Visibility = Visibility.Collapsed;
                    DupProgressIndeterminate.Visibility = Visibility.Visible;
                }
            });

            var scanSw = Stopwatch.StartNew();
            var scan = await Engine.ScanAsync(options, progress, ct).ConfigureAwait(true);
            scanSw.Stop();

            _fullScanOrdered = scan.ToList();
            InvalidatePathFilterCaches();

            DupFilterMinWastedMbBox.Text = "0";
            DupFilterPathContainsBox.Text = string.Empty;
            DupFilterExcludeProgramFilesCheck.IsChecked = false;
            DupFilterExcludeWindowsCheck.IsChecked = false;
            DupFilterPanel.IsEnabled = _fullScanOrdered.Count > 0;

            await RebuildFilteredGroupsAsync(useYield: true, ct).ConfigureAwait(true);

            DupStatusText.Text =
                scan.Count == 0
                    ? "Дубликаты не найдены по заданным параметрам."
                    : $"Готово. Найдено групп: {scan.Count.ToString(CultureInfo.InvariantCulture)}. Слева — список с учётом фильтров; откройте группу для просмотра файлов.";

            DupProgressIndeterminate.Visibility = Visibility.Collapsed;
            DupProgressDeterminate.Visibility = Visibility.Visible;
            DupProgressDeterminate.Maximum = 1;
            DupProgressDeterminate.Value = 1;

            SmartSelectButton.IsEnabled = _groups.Count > 0;
            DeleteDupButton.IsEnabled = _groups.Count > 0;

            var totalWastedApprox = scan.Sum(static g => g.WastedBytesApprox);
            DupScanStatsText.Text = FormatDupScanSummary(scanSw.Elapsed, scan.Count, totalWastedApprox);
            DupScanStatsPanel.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            DupStatusText.Text = "Сканирование отменено.";
            DupScanStatsPanel.Visibility = Visibility.Collapsed;
            DupFilterPanel.IsEnabled = false;
        }
        catch (Exception ex)
        {
            DupStatusText.Text = "Ошибка сканирования.";
            DupScanStatsPanel.Visibility = Visibility.Collapsed;
            DupFilterPanel.IsEnabled = false;
            System.Windows.MessageBox.Show(this, ex.Message, "Дубликаты", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            SetDupOperating(false);
            DisposeCts();
            DupProgressIndeterminate.Visibility = Visibility.Collapsed;

            if (_groups.Count > 0 && DupProgressDeterminate.Visibility != Visibility.Visible)
            {
                DupProgressDeterminate.Visibility = Visibility.Visible;
                DupProgressDeterminate.Maximum = 1;
                DupProgressDeterminate.Value = 1;
            }
            else if (_groups.Count == 0)
            {
                DupProgressDeterminate.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void SmartSelect_Click(object sender, RoutedEventArgs e)
    {
        foreach (var g in _groups)
            g.ApplyRetentionHeuristic();

        UpdateDupDetailForSelection();
    }

    private async void DupApplyFilters_Click(object sender, RoutedEventArgs e)
    {
        if (_fullScanOrdered is null)
            return;

        if (!TryParseMinWastedMb(DupFilterMinWastedMbBox.Text, out _))
        {
            System.Windows.MessageBox.Show(this, "Введите неотрицательное число для «Мин. лишних данных (МБ)» или оставьте 0 / пусто.", "Фильтры",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        try
        {
            DupApplyFiltersButton.IsEnabled = false;
            await RebuildFilteredGroupsAsync(useYield: true, CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            DupApplyFiltersButton.IsEnabled = _fullScanOrdered is { Count: > 0 };
        }
    }

    private void DupGroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateDupDetailForSelection();

    private void UpdateDupDetailForSelection()
    {
        if (DupGroupsList.SelectedItem is DuplicateGroupVm vm)
        {
            DupDetailPanel.DataContext = vm;
            DupDetailPanel.Visibility = Visibility.Visible;
            DupDetailPlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            DupDetailPanel.DataContext = null;
            DupDetailPanel.Visibility = Visibility.Collapsed;
            DupDetailPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void InvalidatePathFilterCaches()
    {
        _cachedProgramFilesPrefixes = null;
        _cachedWindowsPrefix = null;
    }

    private string[] GetProgramFilesPrefixes()
    {
        if (_cachedProgramFilesPrefixes is not null)
            return _cachedProgramFilesPrefixes;

        var list = new List<string>(4);
        void add(Environment.SpecialFolder f)
        {
            try
            {
                var p = Environment.GetFolderPath(f);
                if (!string.IsNullOrWhiteSpace(p))
                    list.Add(NormalizeDirPrefix(p));
            }
            catch
            {
                // ignore
            }
        }

        add(Environment.SpecialFolder.ProgramFiles);
        add(Environment.SpecialFolder.ProgramFilesX86);

        _cachedProgramFilesPrefixes = list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return _cachedProgramFilesPrefixes;
    }

    private string GetWindowsPrefix()
    {
        if (_cachedWindowsPrefix is not null)
            return _cachedWindowsPrefix;

        try
        {
            var w = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            _cachedWindowsPrefix = string.IsNullOrWhiteSpace(w) ? string.Empty : NormalizeDirPrefix(w);
        }
        catch
        {
            _cachedWindowsPrefix = string.Empty;
        }

        return _cachedWindowsPrefix;
    }

    private static string NormalizeDirPrefix(string path)
    {
        try
        {
            var full = Path.GetFullPath(path.Trim());
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        }
        catch
        {
            return path.EndsWith('\\') ? path : path + '\\';
        }
    }

    private static bool PathStartsWithAnyPrefix(string fullPath, string[] prefixes)
    {
        if (prefixes.Length == 0)
            return false;

        string normalized;
        try
        {
            normalized = Path.GetFullPath(fullPath.Trim());
        }
        catch
        {
            normalized = fullPath;
        }

        normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;

        foreach (var p in prefixes)
        {
            if (p.Length > 0 && normalized.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private bool PassesFilters(DuplicateScanGroup g, long minWastedBytes, string search, bool excludePf, bool excludeWin)
    {
        if (g.WastedBytesApprox < minWastedBytes)
            return false;

        if (search.Length > 0 &&
            !g.Files.Any(f => f.FullPath.Contains(search, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (excludePf)
        {
            var pf = GetProgramFilesPrefixes();
            if (g.Files.Any(f => PathStartsWithAnyPrefix(f.FullPath, pf)))
                return false;
        }

        if (excludeWin)
        {
            var win = GetWindowsPrefix();
            if (win.Length > 0 && g.Files.Any(f => PathStartsWithAnyPrefix(f.FullPath, new[] { win })))
                return false;
        }

        return true;
    }

    private async Task RebuildFilteredGroupsAsync(bool useYield, CancellationToken ct)
    {
        DupGroupsList.SelectedItem = null;
        UpdateDupDetailForSelection();
        _groups.Clear();

        if (_fullScanOrdered is null)
        {
            UpdateFilterSummaryText();
            return;
        }

        if (!TryParseMinWastedMb(DupFilterMinWastedMbBox.Text, out var minWastedBytes))
            minWastedBytes = 0;

        var search = DupFilterPathContainsBox.Text.Trim();
        var excludePf = DupFilterExcludeProgramFilesCheck.IsChecked == true;
        var excludeWin = DupFilterExcludeWindowsCheck.IsChecked == true;

        var added = 0;
        foreach (var g in _fullScanOrdered)
        {
            ct.ThrowIfCancellationRequested();

            if (!PassesFilters(g, minWastedBytes, search, excludePf, excludeWin))
                continue;

            _groups.Add(new DuplicateGroupVm(g));
            added++;
            if (useYield && added % 150 == 0)
                await Dispatcher.Yield(DispatcherPriority.Background);
        }

        UpdateFilterSummaryText();

        SmartSelectButton.IsEnabled = _groups.Count > 0;
        DeleteDupButton.IsEnabled = _groups.Count > 0;
    }

    private void UpdateFilterSummaryText()
    {
        if (_fullScanOrdered is null)
        {
            DupFilterSummaryText.Text = "После скана здесь появится сводка по фильтрам.";
            return;
        }

        DupFilterSummaryText.Text =
            $"В полном результате: {_fullScanOrdered.Count.ToString(CultureInfo.InvariantCulture)} групп. "
            + $"В списке слева с учётом фильтров: {_groups.Count.ToString(CultureInfo.InvariantCulture)} групп.";
    }

    private static bool TryParseMinWastedMb(string raw, out long minWastedBytes)
    {
        minWastedBytes = 0;
        raw = raw.Trim();
        if (raw.Length == 0)
            return true;

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var mb) || mb < 0 || double.IsNaN(mb) || double.IsInfinity(mb))
            return false;

        try
        {
            minWastedBytes = checked((long)(mb * 1024d * 1024d));
        }
        catch (OverflowException)
        {
            return false;
        }

        return true;
    }

    private static void PruneFullScanAfterDelete(IReadOnlyList<DuplicateScanGroup> source, HashSet<string> deletedNormalized,
        out List<DuplicateScanGroup> next)
    {
        next = new List<DuplicateScanGroup>();

        foreach (var g in source)
        {
            var kept = new List<DuplicateScanFileMeta>();
            foreach (var f in g.Files)
            {
                string? norm = null;
                try
                {
                    norm = Path.GetFullPath(f.FullPath);
                }
                catch
                {
                    norm = f.FullPath;
                }

                if (norm is not null && deletedNormalized.Contains(norm))
                {
                    try
                    {
                        if (!File.Exists(f.FullPath))
                            continue;
                    }
                    catch
                    {
                        // при ошибке доступа оставляем запись в группе
                    }
                }

                kept.Add(f);
            }

            if (kept.Count < 2)
                continue;

            next.Add(new DuplicateScanGroup(g.Sha256Hex, g.FileSizeBytes, kept));
        }
    }

    private void DuplicateRow_OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DuplicateFileRowVm row })
            return;

        TryShowInExplorer(row.FullPath);
    }

    private static void TryShowInExplorer(string rawPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return;

            var path = rawPath.Trim();

            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                // оставить как есть — Explorer сам разберётся или покажем сообщение ниже
            }

            string? arguments;
            if (File.Exists(path))
            {
                arguments = "/select,\"" + path + "\"";
            }
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    System.Windows.MessageBox.Show(
                        "Не удалось открыть папку: файл не найден и каталог недоступен.",
                        "Дубликаты",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }

                arguments = "\"" + dir + "\"";
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                "Не удалось открыть проводник Windows.\n\n" + ex.Message,
                "Дубликаты",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var toDelete = _groups
            .SelectMany(static g => g.Rows)
            .Where(static r => r.MarkedForDeletion)
            .Select(static r => r.FullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (toDelete.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "Нет файлов, отмеченных к удалению.", "Дубликаты", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        foreach (var g in _groups)
        {
            if (g.Rows.All(static r => r.MarkedForDeletion))
            {
                System.Windows.MessageBox.Show(
                    this,
                    "В каждой группе должен остаться хотя бы один файл без отметки удаления. Снимите отметку одного файла в группе или нажмите «Умное выделение».",
                    "Дубликаты",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
        }

        long bytesApprox = 0;
        foreach (var path in toDelete)
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists)
                    bytesApprox += fi.Length;
            }
            catch
            {
                // ignore sizing errors
            }
        }

        var confirm = System.Windows.MessageBox.Show(
            this,
            $"Удалить отмеченные файлы безвозвратно?\n\nФайлов: {toDelete.Count.ToString(CultureInfo.InvariantCulture)}\nПримерный объём: {ByteFormat.Format(bytesApprox)}\n\nУчитываются только группы, видимые в списке слева (после фильтров).\nОтмены в Windows не предусмотрено.",
            "Подтверждение удаления",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes)
            return;

        RestartCts();
        var ct = _operationsCts!.Token;

        try
        {
            SetDupOperating(true, scanning: false);

            var del = await _deletion.DeleteFilesAsync("duplicates", toDelete, ct).ConfigureAwait(true);

            _logger.Append(
                $"Duplicates delete: success={del.Success}, deleted={del.FilesDeleted}, errors={del.Errors}, freedBytes={del.FreedBytes}");

            var msg =
                $"Готово.\nУдалено файлов (успешно): {del.FilesDeleted.ToString(CultureInfo.InvariantCulture)}\nОшибок: {del.Errors.ToString(CultureInfo.InvariantCulture)}\nПримерно освобождено: {ByteFormat.Format(del.FreedBytes)}";

            System.Windows.MessageBox.Show(this, msg, "Дубликаты", System.Windows.MessageBoxButton.OK,
                del.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);

            PruneDeletedFromUi(toDelete); // async void — обновит полный список и фильтрованный список
            DupStatusText.Text = del.Success ? "Выбранные дубликаты удалены." : "Удаление завершено с ошибками.";
        }
        catch (OperationCanceledException)
        {
            DupStatusText.Text = "Удаление отменено.";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Дубликаты", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            SetDupOperating(false);
            DisposeCts();
        }
    }

    private async void PruneDeletedFromUi(IReadOnlyCollection<string> deletedPaths)
    {
        static string? NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }
        }

        var normalizedTargets = deletedPaths
            .Select(NormalizePath)
            .Where(static s => s is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_fullScanOrdered is not null)
        {
            PruneFullScanAfterDelete(_fullScanOrdered, normalizedTargets, out var pruned);
            _fullScanOrdered = pruned;
        }

        await RebuildFilteredGroupsAsync(useYield: true, CancellationToken.None).ConfigureAwait(true);
        UpdateDupDetailForSelection();

        SmartSelectButton.IsEnabled = _groups.Count > 0;
        DeleteDupButton.IsEnabled = _groups.Count > 0;
        DupApplyFiltersButton.IsEnabled = _fullScanOrdered is { Count: > 0 };

        if (_groups.Count == 0)
            DupStatusText.Text = "Список дубликатов пуст после удаления.";
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e)
    {
        _operationsCts?.Cancel();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _operationsCts?.Cancel();
        Close();
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

    private void SetDupOperating(bool busy, bool scanning = false)
    {
        FoldersList.IsEnabled = !busy;
        ScanDupButton.IsEnabled = !busy;
        SmartSelectButton.IsEnabled = !busy && !scanning && _groups.Count > 0;
        DeleteDupButton.IsEnabled = !busy && !scanning && _groups.Count > 0;
        var hasScanData = _fullScanOrdered is { Count: > 0 };
        DupFilterPanel.IsEnabled = !busy && hasScanData;
        DupApplyFiltersButton.IsEnabled = !busy && hasScanData;
        CancelDupButton.IsEnabled = busy;
        PresetCombo.IsEnabled = !busy;
        MinSizeKbBox.IsEnabled = !busy;

        if (!busy && _groups.Count > 0 && !scanning)
        {
            SmartSelectButton.IsEnabled = true;
            DeleteDupButton.IsEnabled = true;
        }

        if (busy)
        {
            if (scanning)
            {
                DupProgressDeterminate.Value = 0;
                DupProgressDeterminate.Maximum = 1;
                DupProgressDeterminate.Visibility = Visibility.Collapsed;
                DupProgressIndeterminate.Visibility = Visibility.Visible;
            }

            DupStatusText.Text = scanning ? "Подождите…" : "Удаление…";
        }
    }

    private static bool TryParseMinKb(string raw, out long kbResult)
    {
        kbResult = 0;

        raw = raw.Trim();
        if (raw.Length == 0)
            return false;

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return false;

        if (v < 0)
            return false;

        kbResult = v;
        return true;
    }

    private static string FormatDupScanSummary(TimeSpan elapsed, int duplicateGroupCount, long totalWastedBytesApprox)
    {
        var time = FormatScanDuration(elapsed);

        if (duplicateGroupCount == 0)
            return $"Время скана: {time}. Дубликатов по содержимому не найдено — оценивать объём нечего.";

        var size = ByteFormat.Format(totalWastedBytesApprox);
        return $"Время скана: {time}.\n"
               + $"Потенциально освободимо до ≈ {size}, если в каждой группе оставить один файл и удалить остальные копии "
               + $"(оценка по {duplicateGroupCount.ToString(CultureInfo.InvariantCulture)} группам; фактически зависит от отметок и прав доступа).";
    }

    private static string FormatScanDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds < 500)
            return $"{Math.Max(1, (int)elapsed.TotalMilliseconds)} мс";

        if (elapsed.TotalHours >= 1)
        {
            var h = (int)elapsed.TotalHours;
            return string.Format(CultureInfo.InvariantCulture, "{0} ч {1} мин {2} с", h, elapsed.Minutes, elapsed.Seconds);
        }

        if (elapsed.TotalMinutes >= 1)
            return string.Format(CultureInfo.InvariantCulture, "{0} мин {1} с", elapsed.Minutes, elapsed.Seconds);

        return string.Format(CultureInfo.InvariantCulture, "{0:0.#} с", elapsed.TotalSeconds);
    }

    private static DuplicateExtensionPreset MapPreset(ComboBoxItem? item)
    {
        if (item?.Tag is not string tag || tag.Length == 0)
            return DuplicateExtensionPreset.All;

        return tag switch
        {
            "Images" => DuplicateExtensionPreset.Images,
            "Documents" => DuplicateExtensionPreset.Documents,
            "Videos" => DuplicateExtensionPreset.Videos,
            "Archives" => DuplicateExtensionPreset.Archives,
            _ => DuplicateExtensionPreset.All,
        };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _operationsCts?.Cancel();

        base.OnClosing(e);
    }
}
