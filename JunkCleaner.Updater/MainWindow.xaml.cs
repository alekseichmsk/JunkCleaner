using System.Diagnostics;
using System.IO;
using System.Windows;

namespace JunkCleaner.Updater;

public partial class MainWindow : Window
{
    private UpdaterArgs? _args;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RunUpdateAsync();
    }

    private async Task RunUpdateAsync()
    {
        try
        {
            _args = UpdaterArgs.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
            if (!_args.IsValid)
                throw new InvalidOperationException("Не переданы обязательные параметры обновления.");

            StatusText.Text = "Ожидаем закрытия JunkCleaner…";
            DetailsText.Text = _args.TargetDir;
            await WaitForProcessExitAsync(_args.WaitPid, TimeSpan.FromSeconds(45));

            StatusText.Text = "Копируем новые файлы…";
            var runningFromTarget = IsUnderTargetDirectory(Process.GetCurrentProcess().MainModule?.FileName, _args.TargetDir);
            var files = Directory
                .EnumerateFiles(_args.SourceDir, "*", SearchOption.AllDirectories)
                .ToList();

            for (var i = 0; i < files.Count; i++)
            {
                var source = files[i];
                var rel = Path.GetRelativePath(_args.SourceDir, source);
                if (!ShouldCopyFile(rel, runningFromTarget))
                    continue;

                var target = Path.Combine(_args.TargetDir, rel);

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);

                Progress.Value = files.Count == 0 ? 100 : (i + 1) * 100.0 / files.Count;
                DetailsText.Text = rel;
                await Task.Delay(5);
            }

            Progress.Value = 100;
            StatusText.Text = "Запускаем новую версию…";
            DetailsText.Text = _args.MainExe;
            StartMainApp(_args.MainExe);

            await Task.Delay(800);
            Close();
        }
        catch (Exception ex)
        {
            Progress.Value = 0;
            StatusText.Text = "Не удалось установить обновление.";
            DetailsText.Text = ex.Message;
        }
    }

    private static bool ShouldCopyFile(string relativePath, bool runningFromTarget)
    {
        if (!runningFromTarget)
            return true;

        var normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return !normalized.StartsWith("Updater" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderTargetDirectory(string? path, string targetDir)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(targetDir))
            return false;

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDir)) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task WaitForProcessExitAsync(int? pid, TimeSpan timeout)
    {
        if (pid is not { } value || value <= 0)
            return;

        try
        {
            using var process = Process.GetProcessById(value);
            var waitTask = process.WaitForExitAsync();
            var timeoutTask = Task.Delay(timeout);
            var completed = await Task.WhenAny(waitTask, timeoutTask);
            if (completed != waitTask && !process.HasExited)
                throw new TimeoutException("JunkCleaner не закрылся вовремя. Обновление остановлено, чтобы не повредить файлы приложения.");
        }
        catch (ArgumentException)
        {
            // Process is already gone.
        }
    }

    private static void StartMainApp(string mainExe)
    {
        if (string.IsNullOrWhiteSpace(mainExe) || !File.Exists(mainExe))
            throw new FileNotFoundException("Не найден основной исполняемый файл для запуска после обновления.", mainExe);

        Process.Start(
            new ProcessStartInfo
            {
                FileName = mainExe,
                WorkingDirectory = Path.GetDirectoryName(mainExe),
                UseShellExecute = true,
            });
    }

    private sealed class UpdaterArgs
    {
        public string SourceDir { get; private init; } = string.Empty;

        public string TargetDir { get; private init; } = string.Empty;

        public string MainExe { get; private init; } = string.Empty;

        public int? WaitPid { get; private init; }

        public bool IsValid =>
            Directory.Exists(SourceDir) &&
            Directory.Exists(TargetDir) &&
            !string.IsNullOrWhiteSpace(MainExe) &&
            File.Exists(MainExe);

        public static UpdaterArgs Parse(IReadOnlyList<string> args)
        {
            string? source = null;
            string? target = null;
            string? mainExe = null;
            int? pid = null;

            for (var i = 0; i < args.Count; i++)
            {
                var key = args[i];
                if (i + 1 >= args.Count)
                    break;

                var value = args[++i];
                switch (key)
                {
                    case "--source":
                        source = value;
                        break;
                    case "--target":
                        target = value;
                        break;
                    case "--main-exe":
                        mainExe = value;
                        break;
                    case "--wait-pid":
                        if (int.TryParse(value, out var parsed))
                            pid = parsed;
                        break;
                }
            }

            return new UpdaterArgs
            {
                SourceDir = source ?? string.Empty,
                TargetDir = target ?? string.Empty,
                MainExe = mainExe ?? string.Empty,
                WaitPid = pid,
            };
        }
    }
}