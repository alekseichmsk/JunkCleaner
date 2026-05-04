using JunkCleaner.Security;

namespace JunkCleaner.Services;

internal static class FolderScanner
{
    private const int ProgressEvery = 200;

    public static Task<(List<string> paths, long totalBytes)> ScanAsync(
        IEnumerable<string> roots,
        CancellationToken cancellationToken,
        Action<long>? approximateProgress = null)
    {
        return Task.Run(() => ScanWorker(roots, cancellationToken, approximateProgress), cancellationToken);
    }

    private static (List<string> paths, long totalBytes) ScanWorker(
        IEnumerable<string> roots,
        CancellationToken cancellationToken,
        Action<long>? approximateProgress)
    {
        var files = new List<string>();
        long total = 0;
        long reported = 0;

        foreach (var rootRaw in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(rootRaw))
                continue;

            string root;
            try
            {
                root = ProtectedPaths.NormalizePath(rootRaw);
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(root))
                continue;

            if (ProtectedPaths.IsBlocked(root))
                continue;

            try
            {
                foreach (var file in EnumerateFilesSafe(root, cancellationToken))
                {
                    if (ProtectedPaths.IsBlocked(file))
                        continue;

                    cancellationToken.ThrowIfCancellationRequested();

                    long len = 0;
                    try
                    {
                        len = new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Locked or vanished; skip from delete list still may fail later
                        continue;
                    }

                    files.Add(file);
                    total += len;

                    reported++;
                    if (reported % ProgressEvery == 0)
                        approximateProgress?.Invoke(total);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Unauthorized or transient; caller shows category warning
            }
        }

        return (files, total);
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, CancellationToken ct)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var dir in children)
            {
                ct.ThrowIfCancellationRequested();
                if (ProtectedPaths.IsBlocked(dir))
                    continue;
                pending.Push(dir);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                yield return f;
            }
        }
    }
}
