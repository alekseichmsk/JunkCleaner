using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using IoPath = System.IO.Path;
using MessageBox = System.Windows.MessageBox;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using JunkCleaner.Ui;

namespace JunkCleaner.LargeFiles;

public partial class LargeFilesView : UserControl
{
    private readonly ObservableCollection<DriveChoiceVm> _drives = new();
    private readonly ObservableCollection<LargeFileDisplayRow> _rows = new();

    private CancellationTokenSource? _scanCts;
    private SizeChangedEventHandler? _treemapSizeListener;

    public LargeFilesView()
    {
        InitializeComponent();
        DrivesPanel.ItemsSource = _drives;
        FilesGrid.ItemsSource = _rows;
        ExtensionsBox.Text = ".iso,.zip,.mp4";
        MinMegabytesBox.Text = "100";
        FilesGrid.MouseDoubleClick += FilesGrid_MouseDoubleClick;
        FilesGrid.SelectionChanged += FilesGrid_SelectionChanged;
        Loaded += (_, _) =>
        {
            HydrateDrivesOnce();
            WireTreemapSizing();
            RedrawTreemapDeferred();
        };
        Unloaded += (_, _) =>
        {
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;

            FilesGrid.SelectionChanged -= FilesGrid_SelectionChanged;
            if (_treemapSizeListener != null && TreemapCanvas != null)
                TreemapCanvas.SizeChanged -= _treemapSizeListener;
        };
        SizeChanged += (_, _) => RedrawTreemapDeferred();
    }

    private void HydrateDrivesOnce()
    {
        if (_drives.Count > 0)
            return;

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                continue;

            string label;
            try
            {
                label = drive.VolumeLabel?.Trim() ?? string.Empty;
            }
            catch
            {
                label = string.Empty;
            }

            string root;
            try
            {
                root = drive.RootDirectory.FullName;
            }
            catch
            {
                continue;
            }

            string title = string.IsNullOrEmpty(label) ? root : $"{root} ({label})";
            _drives.Add(new DriveChoiceVm(root, title));
        }
    }

    private void WireTreemapSizing()
    {
        if (_treemapSizeListener != null)
            TreemapCanvas.SizeChanged -= _treemapSizeListener;

        _treemapSizeListener = (_, _) => RedrawTreemapDeferred();
        TreemapCanvas.SizeChanged += _treemapSizeListener;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (!ScanButton.IsEnabled)
            return;

        var roots = _drives.Where(d => d.IsIncluded).Select(static d => d.RootPath).ToList();
        if (roots.Count == 0)
        {
            MessageBox.Show(
                "Отметьте хотя бы один диск для обхода.",
                "Крупные файлы",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryParseMinMegabytes(MinMegabytesBox.Text, out var minMb))
        {
            MessageBox.Show(
                "Введите минимальный размер в мегабайтах — целое или дробное число ≥ 0.",
                "Крупные файлы",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var minBytes = (long)Math.Max(0d, Math.Min(minMb * 1024d * 1024d, (double)long.MaxValue));
        var extFilter = ParseExtensionFilter(ExtensionsBox.Text);

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        ScanButton.IsEnabled = false;
        CancelScanButton.IsEnabled = true;
        ScanProgressBar.Visibility = Visibility.Visible;
        ScanStatusText.Text = "Сканирование… первые сообщения статуса могут появиться с задержкой.";

        var progress = new Progress<(int FilesSeen, long SmallestBytesInTopOrMinValue)>(p =>
        {
            Dispatcher.Invoke(
                () =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    var tail = p.SmallestBytesInTopOrMinValue == long.MinValue
                        ? "(топ заполняется)"
                        : ByteFormat.Format(p.SmallestBytesInTopOrMinValue);
                    ScanStatusText.Text =
                        $"Просмотрено файлов (примерно): {p.FilesSeen:N0}. Минимум в топ-100 сейчас: {tail}.";
                },
                DispatcherPriority.Background);
        });

        try
        {
            var list = await LargeFileScanner.ScanAsync(roots, minBytes, extFilter, progress, token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                ScanStatusText.Text = "Сканирование отменено.";
                return;
            }

            ApplyResults(list);
            ScanStatusText.Text = $"Готово. В топ попало {list.Count} файлов (не более 100).";
        }
        catch (OperationCanceledException)
        {
            ScanStatusText.Text = "Сканирование отменено.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось завершить сканирование:\n\n" + ex.Message,
                "Крупные файлы",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ScanStatusText.Text = "Ошибка во время сканирования.";
        }
        finally
        {
            EndScanUiIdle();
        }
    }

    private void FilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesGrid.SelectedItem is LargeFileDisplayRow row)
            ExplorePath(row.Path);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        ScanStatusText.Text = "Отмена сканирования…";
    }

    private void EndScanUiIdle()
    {
        ScanButton.IsEnabled = true;
        CancelScanButton.IsEnabled = false;
        ScanProgressBar.Visibility = Visibility.Collapsed;
    }

    private void ApplyResults(IReadOnlyList<LargeFileEntry> list)
    {
        _rows.Clear();

        foreach (var e in list)
            _rows.Add(new LargeFileDisplayRow(e));

        RedrawTreemapDeferred();
    }

    private void RedrawTreemapDeferred()
    {
        Dispatcher.InvokeAsync(RefillTreemap, DispatcherPriority.Render);
    }

    private void RefillTreemap()
    {
        TreemapCanvas.Children.Clear();

        if (_rows.Count == 0)
            return;

        var w = TreemapCanvas.ActualWidth;
        var h = TreemapCanvas.ActualHeight;
        if (w <= 8 || h <= 8)
            return;

        var weights = _rows.Select(static r => (double)r.Bytes).ToList();
        var bounds = new Rect(0, 0, w, h);
        var rects = SquarifiedTreemapLayout.Layout(weights, bounds);

        for (var i = 0; i < _rows.Count; i++)
        {
            var r = rects.Length > i ? rects[i] : Rect.Empty;
            if (r.Width <= 2 || r.Height <= 2)
                continue;

            var row = _rows[i];
            var cell = CreateTreemapCell(r, row, i);
            ApplyTreemapCellChrome(cell, IsRowPathSelected(row.Path));

            Canvas.SetLeft(cell, r.X + 1);
            Canvas.SetTop(cell, r.Y + 1);
            TreemapCanvas.Children.Add(cell);
        }
    }

    private static Border CreateTreemapCell(Rect r, LargeFileDisplayRow row, int colorIndex)
    {
        var w = Math.Max(r.Width - 2, 0);
        var h = Math.Max(r.Height - 2, 0);

        var cell = new Border
        {
            Width = w,
            Height = h,
            Background = BrushForTreemapLeaf(colorIndex),
            CornerRadius = new CornerRadius(2),
            Cursor = Cursors.Hand,
            Tag = row,
            SnapsToDevicePixels = true,
        };

        ToolTipService.SetToolTip(cell, $"{row.Path}\n{row.SizeText}  ·  {row.Extension}");

        if (w >= 56 && h >= 22)
        {
            var name = IoPath.GetFileName(row.Path);
            if (string.IsNullOrEmpty(name))
                name = row.Path;

            if (name.Length > 48)
                name = name[..45] + "…";

            var font = Math.Clamp(h * 0.11, 9.0, 11.5);
            var tb = new TextBlock
            {
                Text = h >= 40 ? $"{name}\n{row.SizeText}" : name,
                Foreground = new SolidColorBrush(Color.FromArgb(230, 0xee, 0xe8, 0xf2)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(4, 3, 4, 2),
                FontSize = font,
            };

            cell.Child = tb;
        }

        return cell;
    }

    private void ApplyTreemapCellChrome(Border cell, bool selected)
    {
        var accent = TryFindResource("Brush.AccentMist") as Brush ??
                     new SolidColorBrush(Color.FromRgb(0x9a, 0xd4, 0xe6));
        var muted = TryFindResource("Brush.BorderMuted") as Brush ??
                    new SolidColorBrush(Color.FromRgb(0x52, 0x4d, 0x66));

        cell.BorderBrush = selected ? accent : muted;
        cell.BorderThickness = selected ? new Thickness(2.5) : new Thickness(1);
    }

    private bool IsRowPathSelected(string path)
    {
        return FilesGrid.SelectedItem is LargeFileDisplayRow r &&
               string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase);
    }

    private void SyncTreemapHighlightFromGrid()
    {
        foreach (var child in TreemapCanvas.Children)
        {
            if (child is not Border bd || bd.Tag is not LargeFileDisplayRow row)
                continue;

            ApplyTreemapCellChrome(bd, IsRowPathSelected(row.Path));
        }
    }

    private void FilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SyncTreemapHighlightFromGrid();

    private static Brush BrushForTreemapLeaf(int index)
    {
        var hsl = HueFromGolden(index);
        var rgb = ColorFromHsv(hsl.h, hsl.s, hsl.v);
        rgb.A = 210;
        return new SolidColorBrush(rgb);
    }

    private static (double h, double s, double v) HueFromGolden(int index)
    {
        var h = (index * 137.508) % 360.0;
        return (h, 0.55, 0.62);
    }

    private static Color ColorFromHsv(double hDegrees, double s, double v)
    {
        hDegrees = double.Ieee754Remainder(hDegrees, 360);
        if (hDegrees < 0)
            hDegrees += 360;

        double c = v * s;
        double hh = hDegrees / 60.0;
        double x = c * (1 - Math.Abs(hh % 2 - 1));
        double m = v - c;

        double r1 = 0, g1 = 0, b1 = 0;
        switch ((int)Math.Floor(hh))
        {
            case 0:
                r1 = c;
                g1 = x;
                break;
            case 1:
                r1 = x;
                g1 = c;
                break;
            case 2:
                g1 = c;
                b1 = x;
                break;
            case 3:
                g1 = x;
                b1 = c;
                break;
            case 4:
                r1 = x;
                b1 = c;
                break;
            default:
                r1 = c;
                b1 = x;
                break;
        }

        return Color.FromRgb(
            ClampByte((int)Math.Round((r1 + m) * 255)),
            ClampByte((int)Math.Round((g1 + m) * 255)),
            ClampByte((int)Math.Round((b1 + m) * 255)));
    }

    private static byte ClampByte(int v)
    {
        if (v < 0)
            return 0;
        if (v > 255)
            return byte.MaxValue;
        return (byte)v;
    }

    private void TreemapCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not Canvas canvas)
            return;

        var pt = e.GetPosition(canvas);
        if (TreemapLeafHit(canvas, pt)?.Tag is not LargeFileDisplayRow row)
            return;

        if (e.ClickCount == 2)
        {
            ExplorePath(row.Path);
            return;
        }

        FilesGrid.SelectedItem = row;
        FilesGrid.ScrollIntoView(row);
        SyncTreemapHighlightFromGrid();
    }

    private static Border? TreemapLeafHit(Canvas canvas, Point pt)
    {
        for (var i = canvas.Children.Count - 1; i >= 0; i--)
        {
            if (canvas.Children[i] is Border bd)
            {
                var left = Canvas.GetLeft(bd);
                var top = Canvas.GetTop(bd);
                var hitRect = new Rect(left, top, bd.Width, bd.Height);
                if (hitRect.Contains(pt))
                    return bd;
            }
        }

        return null;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is not LargeFileDisplayRow row)
        {
            MessageBox.Show(
                "Выберите строку списка, чтобы показать файл в проводнике.",
                "Крупные файлы",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ExplorePath(row.Path);
    }

    private void ExplorePath(string path)
    {
        try
        {
            if (path.Length == 0)
                return;

            string arguments;
            if (File.Exists(path))
                arguments = "/select,\"" + path + "\"";
            else if (Directory.Exists(path))
                arguments = "\"" + path + "\"";
            else
            {
                var dir = IoPath.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    MessageBox.Show(
                        "Не удалось открыть расположение: файл недоступен.",
                        "Крупные файлы",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                arguments = "\"" + dir + "\"";
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = arguments,
                    UseShellExecute = true,
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось открыть проводник Windows:\n\n" + ex.Message,
                "Крупные файлы",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    internal static HashSet<string>? ParseExtensionFilter(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var parts = raw.Split(',', ';');
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var blob in parts)
        {
            foreach (var frag in blob.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var t = frag.Trim();
                if (t.Length == 0)
                    continue;

                var key = t.StartsWith('.')
                    ? t.ToLowerInvariant()
                    : "." + t.ToLowerInvariant();
                set.Add(key);
            }
        }

        return set.Count > 0 ? set : null;
    }

    private static bool TryParseMinMegabytes(string text, out double megabytes)
    {
        megabytes = 0;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        return double.TryParse(
            text.Trim().Replace(',', '.'),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out megabytes) && megabytes >= 0;
    }
}
