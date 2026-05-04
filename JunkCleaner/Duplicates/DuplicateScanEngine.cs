using System.Collections.Concurrent;

namespace JunkCleaner.Duplicates;

public sealed class DuplicateScanEngine
{
    private const int PrefixSampleBytes = 64 * 1024;

    /// <summary>Ограничивает конкуренцию SHA-256, чтобы освободить потоки под UI и диск.</summary>
    private static int HashParallelismDegrees =>
        Math.Clamp(Math.Max(1, Environment.ProcessorCount / 2), 1, 8);

    public async Task<IReadOnlyList<DuplicateScanGroup>> ScanAsync(
        DuplicateScanOptions options,
        IProgress<(string Phase, int Current, int Total)>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        cancellationToken.ThrowIfCancellationRequested();

        var extFilter = options.ResolveExtensionFilter();

        progress?.Report(("Сбор файлов…", 0, 0));

        var discovered = await Task.Run(
                () => DiscoverFiles(options, extFilter, progress, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var byLength = discovered
            .GroupBy(static f => f.LengthBytes)
            .Where(static g => g.Key > 0 && g.Count() > 1)
            .SelectMany(static g => g)
            .ToList();

        cancellationToken.ThrowIfCancellationRequested();

        if (byLength.Count == 0)
            return Array.Empty<DuplicateScanGroup>();

        var totalPrefix = byLength.Count;
        var prefixProgressBox = new int[1];

        progress?.Report(("Быстрый отбор (начало файла)…", 0, totalPrefix));

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = HashParallelismDegrees,
            CancellationToken = cancellationToken,
        };

        var prefixBuckets =
            new ConcurrentDictionary<(long Length, string PrefixHex), ConcurrentBag<DuplicateScanFileMeta>>();

        await Parallel.ForEachAsync(
                byLength,
                parallelOptions,
                async (meta, token) =>
                {
                    try
                    {
                        var hex = await FileHashing.TryComputePrefixSha256HexAsync(meta.FullPath, PrefixSampleBytes, token)
                            .ConfigureAwait(false);

                        if (string.IsNullOrEmpty(hex))
                            return;

                        var key = (meta.LengthBytes, hex);
                        var bag = prefixBuckets.GetOrAdd(key, static _ => new ConcurrentBag<DuplicateScanFileMeta>());
                        bag.Add(meta);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // пропуск файла при отборе
                    }

                    var v = Interlocked.Increment(ref prefixProgressBox[0]);
                    if (v % 512 == 0 || v == totalPrefix)
                        progress?.Report(("Быстрый отбор (начало файла)…", v, totalPrefix));
                })
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var groups = new List<DuplicateScanGroup>();
        var fullShaWork = new List<DuplicateScanFileMeta>();

        foreach (var kv in prefixBuckets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var flat = kv.Value.ToArray();
            if (flat.Length < 2)
                continue;

            if (kv.Key.Length <= PrefixSampleBytes)
            {
                var sorted = flat.OrderBy(static f => f.FullPath, StringComparer.OrdinalIgnoreCase).ToList();
                groups.Add(new DuplicateScanGroup(kv.Key.PrefixHex, kv.Key.Length, sorted));
            }
            else
                fullShaWork.AddRange(flat);
        }

        var bagsByHash = new ConcurrentDictionary<string, ConcurrentBag<DuplicateScanFileMeta>>(StringComparer.Ordinal);
        var totalFull = fullShaWork.Count;
        var fullProgressBox = new int[1];

        if (totalFull > 0)
        {
            progress?.Report(("SHA-256 полного файла…", 0, totalFull));

            await Parallel.ForEachAsync(
                    fullShaWork,
                    parallelOptions,
                    async (meta, token) =>
                    {
                        try
                        {
                            var hex = await FileHashing.TryComputeSha256HexAsync(meta.FullPath, token).ConfigureAwait(false);

                            if (!string.IsNullOrEmpty(hex))
                            {
                                var bag = bagsByHash.GetOrAdd(hex, static _ => new ConcurrentBag<DuplicateScanFileMeta>());
                                bag.Add(meta);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                            // пропуск при полном хеше
                        }

                        var v = Interlocked.Increment(ref fullProgressBox[0]);
                        if (v % 512 == 0 || v == totalFull)
                            progress?.Report(("SHA-256 полного файла…", v, totalFull));
                    })
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var kv in bagsByHash)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var flat = kv.Value.ToArray();
            if (flat.Length < 2)
                continue;

            var sorted = flat
                .OrderBy(static f => f.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var size = sorted[0].LengthBytes;

            groups.Add(new DuplicateScanGroup(kv.Key, size, sorted));
        }

        var ordered = groups
            .OrderByDescending(static g => g.WastedBytesApprox)
            .ThenByDescending(static g => g.Files.Count)
            .ToList();

        return ordered;
    }

    private static List<DuplicateScanFileMeta> DiscoverFiles(
        DuplicateScanOptions options,
        HashSet<string>? extFilter,
        IProgress<(string Phase, int Current, int Total)>? progress,
        CancellationToken ct)
    {
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MaxRecursionDepth = int.MaxValue,
        };

        var list = new List<DuplicateScanFileMeta>();

        foreach (var rootRaw in options.RootFolders)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(rootRaw))
                continue;

            string root;
            try
            {
                root = Path.GetFullPath(rootRaw.Trim());
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(root))
                continue;

            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*", enumerationOptions))
                {
                    ct.ThrowIfCancellationRequested();

                    if (!TryInspectFile(path, options.MinFileSizeBytes, extFilter, out var meta))
                        continue;

                    list.Add(meta);
                    if (list.Count % 2048 == 0)
                        progress?.Report(("Сбор файлов…", list.Count, 0));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // skip unreadable trees under this root
            }
        }

        return list;
    }

    private static bool TryInspectFile(
        string path,
        long minSize,
        HashSet<string>? extFilter,
        out DuplicateScanFileMeta meta)
    {
        meta = null!;

        try
        {
            var fi = new FileInfo(path);
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
                return false;

            var length = fi.Length;
            if (length < minSize)
                return false;

            if (extFilter is not null)
            {
                var ext = fi.Extension;
                if (string.IsNullOrWhiteSpace(ext) || ext.Length == 1)
                    return false;

                var key = ext.StartsWith(".", StringComparison.Ordinal) ? ext : "." + ext;
                if (!extFilter.Contains(key))
                    return false;
            }

            meta = new DuplicateScanFileMeta(path, length, fi.LastWriteTimeUtc);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
