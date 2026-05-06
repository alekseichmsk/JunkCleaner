using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using JunkCleaner.Ui;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace JunkCleaner.ProgramLeftovers;

public partial class ProgramLeftoversView : UserControl
{
    private readonly ObservableCollection<ProgramLeftoverItem> _items = new();
    private readonly ICollectionView _itemsView;
    private CancellationTokenSource? _scanCts;
    private ProgramLeftoverScanResult? _lastScanResult;

    public ProgramLeftoversView()
    {
        InitializeComponent();
        _itemsView = CollectionViewSource.GetDefaultView(_items);
        _itemsView.Filter = FilterItem;
        ResultsList.ItemsSource = _itemsView;
        ResultsList.MouseDoubleClick += ResultsList_MouseDoubleClick;
        SearchBox.TextChanged += (_, _) => RefreshFilters();
        KindFilterBox.SelectionChanged += (_, _) => RefreshFilters();
        ShowLowConfidenceBox.Checked += (_, _) => RefreshFilters();
        ShowLowConfidenceBox.Unchecked += (_, _) => RefreshFilters();
        ResultsList.SizeChanged += (_, _) => ResizeColumnsToFit();
        Loaded += (_, _) => ResizeColumnsToFit();
        Unloaded += (_, _) =>
        {
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;
            ResultsList.MouseDoubleClick -= ResultsList_MouseDoubleClick;
        };
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (!ScanButton.IsEnabled)
            return;

        var scanRegistry = ScanRegistryBox.IsChecked == true;
        var scanFolders = ScanFoldersBox.IsChecked == true;
        if (!scanRegistry && !scanFolders)
        {
            MessageBox.Show(
                "Выберите хотя бы одну область сканирования.",
                "Остатки программ",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        SetBusy(true);
        _items.Clear();
        _lastScanResult = null;
        SummaryText.Text = string.Empty;
        StatusText.Text = "Подготовка сканирования…";

        var progress = new Progress<string>(message =>
        {
            Dispatcher.Invoke(
                () =>
                {
                    if (!token.IsCancellationRequested)
                        StatusText.Text = message;
                },
                DispatcherPriority.Background);
        });

        try
        {
            var result = await ProgramLeftoverScanner.ScanAsync(scanRegistry, scanFolders, progress, token)
                .ConfigureAwait(true);

            _lastScanResult = result;
            foreach (var item in result.Items)
                _items.Add(item);

            StatusText.Text =
                $"Готово. Найдено кандидатов: {result.Items.Count}. Установленных программ в индексе: {result.InstalledProgramCount}. " +
                $"Проверено ключей: {result.RegistryCandidatesChecked}, папок: {result.FolderCandidatesChecked}.";
            RefreshFilters();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Сканирование отменено.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка сканирования.";
            MessageBox.Show(
                ex.Message,
                "Остатки программ",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        StatusText.Text = "Отмена сканирования…";
    }

    private void SetBusy(bool busy)
    {
        ScanButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        ScanRegistryBox.IsEnabled = !busy;
        ScanFoldersBox.IsEnabled = !busy;
        SearchBox.IsEnabled = !busy;
        KindFilterBox.IsEnabled = !busy;
        ShowLowConfidenceBox.IsEnabled = !busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool FilterItem(object obj)
    {
        if (obj is not ProgramLeftoverItem item)
            return false;

        if (ShowLowConfidenceBox.IsChecked != true &&
            string.Equals(item.Confidence, "Низкая", StringComparison.OrdinalIgnoreCase))
            return false;

        var kind = (KindFilterBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (string.Equals(kind, "Папки", StringComparison.OrdinalIgnoreCase) &&
            item.Kind != ProgramLeftoverKind.Folder)
            return false;
        if (string.Equals(kind, "Реестр", StringComparison.OrdinalIgnoreCase) &&
            item.Kind != ProgramLeftoverKind.RegistryKey)
            return false;

        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.Location.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.Reason.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshFilters()
    {
        _itemsView.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var visible = _itemsView.Cast<ProgramLeftoverItem>().ToList();
        var totalVisibleBytes = visible.Sum(static i => i.SizeBytes ?? 0);
        var visibleFolders = visible.Count(static i => i.Kind == ProgramLeftoverKind.Folder);
        var visibleRegistry = visible.Count(static i => i.Kind == ProgramLeftoverKind.RegistryKey);
        var hidden = Math.Max(0, _items.Count - visible.Count);

        var allBytes = _lastScanResult?.TotalFolderBytes ?? _items.Sum(static i => i.SizeBytes ?? 0);
        var top = visible
            .Where(static i => i.SizeBytes is > 0)
            .OrderByDescending(static i => i.SizeBytes)
            .Take(3)
            .Select(static i => $"{i.Name}: {i.SizeText}")
            .ToList();

        SummaryText.Text =
            $"Показано: {visible.Count} из {_items.Count} (скрыто: {hidden}). " +
            $"Папок: {visibleFolders}, реестр: {visibleRegistry}. " +
            $"Размер показанных папок: {ByteFormat.Format(totalVisibleBytes)}; всего найденных папок: {ByteFormat.Format(allBytes)}." +
            (top.Count > 0 ? " Самые крупные: " + string.Join("; ", top) + "." : string.Empty);
    }

    private void OpenSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not ProgramLeftoverItem item)
        {
            MessageBox.Show(
                "Выберите строку результата.",
                "Остатки программ",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        OpenItem(item);
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not ProgramLeftoverItem item)
        {
            MessageBox.Show(
                "Выберите строку результата.",
                "Остатки программ",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(item.Location);
        StatusText.Text = "Путь скопирован в буфер обмена.";
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is ProgramLeftoverItem item)
            OpenItem(item);
    }

    private void OpenItem(ProgramLeftoverItem item)
    {
        if (item.Kind == ProgramLeftoverKind.RegistryKey)
        {
            Clipboard.SetText(item.Location);
            StatusText.Text = "Путь ключа реестра скопирован. Открываем regedit: вставьте путь в адресную строку редактора.";
            TryStart("regedit.exe", string.Empty);
            return;
        }

        try
        {
            if (Directory.Exists(item.Location))
            {
                TryStart("explorer.exe", "\"" + item.Location + "\"");
                return;
            }

            var parent = Path.GetDirectoryName(item.Location);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                TryStart("explorer.exe", "\"" + parent + "\"");
                return;
            }

            MessageBox.Show(
                "Папка больше недоступна.",
                "Остатки программ",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось открыть расположение:\n\n" + ex.Message,
                "Остатки программ",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static void TryStart(string fileName, string arguments)
    {
        Process.Start(
            new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
            });
    }

    private void ResizeColumnsToFit()
    {
        var availableWidth = ResultsList.ActualWidth - 36;
        if (availableWidth <= 0)
            return;

        // Keep columns readable while fitting the whole table inside the control width.
        var layout = new[]
        {
            (column: TypeColumn, share: 0.09, min: 64d),
            (column: ConfidenceColumn, share: 0.12, min: 84d),
            (column: SizeColumn, share: 0.10, min: 78d),
            (column: FileTypeColumn, share: 0.10, min: 78d),
            (column: NameColumn, share: 0.15, min: 100d),
            (column: ReasonColumn, share: 0.20, min: 120d),
            (column: LocationColumn, share: 0.24, min: 140d),
        };

        var widthForShares = availableWidth;
        foreach (var (_, _, min) in layout)
            widthForShares -= min;
        if (widthForShares < 0)
            widthForShares = 0;

        foreach (var (column, share, min) in layout)
            column.Width = min + widthForShares * share;
    }
}
