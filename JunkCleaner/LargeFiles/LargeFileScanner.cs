using System.Collections.Generic;
using System.IO;
using JunkCleaner.Security;

namespace JunkCleaner.LargeFiles;

/// <summary>Ищет крупнейшие файлы среди указанных корней.</summary>
public sealed class LargeFileScanner
{
    private const int TopCount = 100;

    /// <remarks>Расширения — ключи вида ".mp4"; null или пустой набор означает «любое расширение».</remarks>
    public static async Task<IReadOnlyList<LargeFileEntry>> ScanAsync(
        IReadOnlyList<string> rootFolders,
        long minBytes,
        HashSet<string>? extensionsOrNullMeansAll,
        IProgress<(int FilesSeen, long SmallestBytesInTopOrMinValue)>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rootFolders);

        return await Task.Run(
                () => ScanCore(rootFolders, minBytes, extensionsOrNullMeansAll, progress, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static List<LargeFileEntry> ScanCore(
        IReadOnlyList<string> rootFolders,
        long minBytes,
        HashSet<string>? extFilter,
        IProgress<(int FilesSeen, long SmallestBytesInTopOrMinValue)>? progress,
        CancellationToken ct)
    {
        var keeper = new LargeFileKeeper(TopCount);
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MaxRecursionDepth = int.MaxValue,
        };

        var seenBox = new int[1];

        foreach (var raw in rootFolders)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string root;
            try
            {
                root = Path.GetFullPath(raw.Trim());
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(root))
                continue;

            foreach (var path in EnumerateCandidateFilesSafe(root, enumerationOptions))
            {
                ct.ThrowIfCancellationRequested();

                if (ProtectedPaths.IsBlocked(path))
                    continue;

                if (!TryInspectFile(path, minBytes, extFilter, out var bytes))
                    continue;

                keeper.Add(bytes, path);

                var n = Interlocked.Increment(ref seenBox[0]);
                if (n % 1024 == 0)
                    progress?.Report((n, keeper.SmallestIncludedSizeExclusive));
            }
        }

        return keeper.BuildSortedDescending();
    }

    /// <returns>Поток только файлов; само начало дереву не передаём.</returns>
    private static IEnumerable<string> EnumerateCandidateFilesSafe(string root, EnumerationOptions options)
    {
        IEnumerable<string>? seq = null;

        try
        {
            seq = Directory.EnumerateFiles(root, "*", options);
        }
        catch
        {
            yield break;
        }

        IEnumerator<string>? en = null;
        try
        {
            en = seq.GetEnumerator();
        }
        catch
        {
            yield break;
        }

        try
        {
            while (true)
            {
                string cur;
                try
                {
                    if (!en.MoveNext())
                        break;
                    cur = en.Current;
                }
                catch
                {
                    break;
                }

                yield return cur;
            }
        }
        finally
        {
            try
            {
                en.Dispose();
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <remarks>
    /// <paramref name="extFilter"/> элементы нижним регистром с ведущей точкой или без.
    /// </remarks>
    private static bool TryInspectFile(
        string path,
        long minBytes,
        HashSet<string>? extFilter,
        out long bytes)
    {
        bytes = 0;

        try
        {
            var fi = new FileInfo(path);
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
                return false;

            var len = fi.Length;
            if (len < minBytes)
                return false;

            if (extFilter is { Count: > 0 })
            {
                var ext = fi.Extension;
                if (string.IsNullOrEmpty(ext))
                    return false;

                var key = ext.StartsWith('.')
                    ? ext.ToLowerInvariant()
                    : "." + ext.ToLowerInvariant();
                if (!extFilter.Contains(key))
                    return false;
            }

            bytes = len;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Хранит K крупнейших файлов через отсортированное множество «худшие» минимальных размеров.</summary>
    private sealed class LargeFileKeeper
    {
        private readonly int _capacity;
        private readonly SortedSet<(long Len, string Path)> _ordered;

        internal LargeFileKeeper(int capacity)
        {
            _capacity = Math.Max(1, capacity);

            static int Cmp((long Len, string Path) a, (long Len, string Path) b) =>
                a.Len != b.Len ? a.Len.CompareTo(b.Len) :
                string.CompareOrdinal(a.Path, b.Path);

            _ordered = new SortedSet<(long Len, string Path)>(Comparer<(long Len, string Path)>.Create(Cmp));
        }

        /// <summary>Минимальный размер среди топа; если набор полон — заменять только большие этого.</summary>
        internal long SmallestIncludedSizeExclusive => Count < _capacity ? long.MinValue : _ordered.Min.Len;

        private int Count => _ordered.Count;

        internal void Add(long lengthBytes, string path)
        {
            if (_ordered.Count < _capacity)
                _ordered.Add((lengthBytes, path));
            else if (lengthBytes > _ordered.Min.Len)
            {
                var minVal = _ordered.Min;
                _ordered.Remove(minVal);
                _ordered.Add((lengthBytes, path));
            }
        }

        internal List<LargeFileEntry> BuildSortedDescending()
        {
            return _ordered
                .OrderByDescending(static x => x.Len)
                .ThenBy(static x => x.Path, StringComparer.OrdinalIgnoreCase)
                .Select(static x => new LargeFileEntry(x.Path, x.Len))
                .ToList();
        }
    }
}
