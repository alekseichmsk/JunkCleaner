using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

namespace JunkCleaner.Updates;

public partial class UpdatesView : UserControl
{
    private readonly GitHubUpdateService _updates = new();
    private CancellationTokenSource? _cts;
    private UpdateCheckResult? _lastResult;

    public UpdatesView()
    {
        InitializeComponent();
        RepositoryText.Text = UpdateSettings.IsConfigured
            ? $"Источник Velopack: github.com/{UpdateSettings.GitHubOwner}/{UpdateSettings.GitHubRepo}/releases"
            : "Источник не настроен. Укажите GitHubOwner и GitHubRepo в Updates/UpdateSettings.cs.";
        StatusText.Text = "Нажмите «Проверить обновления».";
        Unloaded += (_, _) =>
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        };
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        RestartCts();
        SetBusy(true, downloading: false);
        _lastResult = null;
        AssetText.Text = string.Empty;
        ReleaseNotesBox.Text = "Проверяем Velopack-обновления в GitHub Releases…";
        Progress.Visibility = Visibility.Collapsed;

        try
        {
            var result = await _updates.CheckLatestAsync(_cts!.Token).ConfigureAwait(true);
            _lastResult = result;

            StatusText.Text = BuildStatus(result);
            ReleaseNotesBox.Text = BuildReleaseNotes(result);
            AssetText.Text = result.Update is null
                ? result.IsInstalled
                    ? "Пакет обновления не требуется."
                    : "Автообновление доступно только после установки через Velopack."
                : "Пакет: " + GitHubUpdateService.FormatUpdate(result.Update);

            DownloadButton.IsEnabled = result.IsNewerAvailable && (result.Update is not null || result.IsInstalled);
            ReleasePageButton.IsEnabled = !string.IsNullOrWhiteSpace(result.ReleaseUrl);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Проверка отменена.";
            ReleaseNotesBox.Text = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка проверки обновлений.";
            ReleaseNotesBox.Text = ex.Message;
        }
        finally
        {
            SetBusy(false, downloading: false);
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult is not { } result)
            return;

        RestartCts();
        SetBusy(true, downloading: true);
        Progress.Value = 0;
        Progress.Visibility = Visibility.Visible;

        var progress = new Progress<int>(percent =>
        {
            Dispatcher.Invoke(
                () =>
                {
                    Progress.IsIndeterminate = false;
                    Progress.Value = Math.Clamp(percent, 0, 100);
                    StatusText.Text = $"Скачивание и подготовка обновления: {Progress.Value:0}%…";
                },
                DispatcherPriority.Background);
        });

        try
        {
            if (result.Update is not null)
                await _updates.DownloadUpdateAsync(result.Update, progress, _cts!.Token).ConfigureAwait(true);

            Progress.Value = 100;
            StatusText.Text = "Обновление скачано и готово к применению.";

            var answer = MessageBox.Show(
                "Обновление готово. Приложение закроется, Velopack применит новую версию и запустит JunkCleaner снова.\n\nПродолжить?",
                "Обновление JunkCleaner",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            _updates.ApplyUpdateAndRestart(result);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Обновление отменено.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка скачивания обновления.";
            MessageBox.Show(
                ex.Message,
                "Обновление JunkCleaner",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            Progress.IsIndeterminate = false;
            SetBusy(false, downloading: false);
        }
    }

    private void ReleasePage_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult is not null)
            GitHubUpdateService.OpenReleasePage(_lastResult);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        StatusText.Text = "Отмена…";
    }

    private void RestartCts()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
    }

    private void SetBusy(bool busy, bool downloading)
    {
        CheckButton.IsEnabled = !busy;
        DownloadButton.IsEnabled = !busy &&
                                   _lastResult?.IsNewerAvailable == true &&
                                   (_lastResult.Update is not null || _lastResult.IsInstalled);
        ReleasePageButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_lastResult?.ReleaseUrl);
        CancelButton.IsEnabled = busy;
        if (!downloading)
            Progress.Visibility = Visibility.Collapsed;
    }

    private static string BuildStatus(UpdateCheckResult result)
    {
        if (!result.IsConfigured)
            return result.Message ?? "Обновления не настроены.";

        if (!result.IsInstalled)
            return $"Текущая версия: {result.CurrentVersion}. " + (result.Message ?? string.Empty);

        var latest = result.LatestTag ?? "неизвестно";
        return
            $"Текущая версия: {result.CurrentVersion}. Последний релиз: {latest}. " +
            (result.Message ?? string.Empty);
    }

    private static string BuildReleaseNotes(UpdateCheckResult result)
    {
        var lines = new List<string>
        {
            result.ReleaseName ?? result.LatestTag ?? "GitHub Release",
            new string('=', 48),
            string.Empty,
        };

        if (!string.IsNullOrWhiteSpace(result.ReleaseUrl))
            lines.Add("URL: " + result.ReleaseUrl);

        if (result.LatestVersion is not null)
            lines.Add("Версия: " + result.LatestVersion);

        lines.Add(string.Empty);
        lines.Add(string.IsNullOrWhiteSpace(result.Body)
            ? result.Message ?? "Описание релиза отсутствует."
            : result.Body);

        return string.Join(Environment.NewLine, lines);
    }
}
