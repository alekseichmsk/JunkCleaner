namespace JunkCleaner.Services;

public sealed class FileCleanupLogger
{
    private readonly string _logDirectory;
    private readonly object _sync = new();

    public FileCleanupLogger()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JunkCleaner",
            "logs");
        _logDirectory = baseDir;
        try
        {
            Directory.CreateDirectory(_logDirectory);
        }
        catch
        {
            // ignore
        }
    }

    public void Append(string line)
    {
        var name = $"cleanup-{DateTime.UtcNow:yyyy-MM-dd}.log";
        var path = Path.Combine(_logDirectory, name);
        var text = $"{DateTime.UtcNow:O}\t{line}{Environment.NewLine}";
        lock (_sync)
        {
            try
            {
                File.AppendAllText(path, text);
            }
            catch
            {
                // ignore
            }
        }
    }
}
