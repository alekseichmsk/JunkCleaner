using System.IO;
using System.Security.Cryptography;

namespace JunkCleaner.Duplicates;

internal static class FileHashing
{
    /// <summary>Вычисляет SHA-256 или возвращает <c>null</c>, если файл недоступен (занят, нет прав и т.д.).</summary>
    internal static async Task<string?> TryComputeSha256HexAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            // ReadWrite — чаще удаётся открыть логи/временные файлы, удерживаемые ОС с монопольной записью.
            var opts = new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.SequentialScan | FileOptions.Asynchronous,
            };

            await using var fs = new FileStream(path, opts);
            cancellationToken.ThrowIfCancellationRequested();

            var hashBytes = await SHA256.HashDataAsync(fs, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            return Convert.ToHexString(hashBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// SHA-256 первых <paramref name="maxPrefixBytes"/> байт (или всего файла, если он короче).
    /// Для файлов длиной ≤ <paramref name="maxPrefixBytes"/> совпадает с полным SHA-256 содержимого.
    /// </summary>
    internal static async Task<string?> TryComputePrefixSha256HexAsync(
        string path,
        int maxPrefixBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (maxPrefixBytes <= 0)
            maxPrefixBytes = 64 * 1024;

        try
        {
            var opts = new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.SequentialScan | FileOptions.Asynchronous,
            };

            await using var fs = new FileStream(path, opts);
            cancellationToken.ThrowIfCancellationRequested();

            var len = fs.Length;
            if (len <= 0)
                return null;

            var toRead = (int)Math.Min(len, maxPrefixBytes);
            var buffer = new byte[toRead];
            var read = 0;
            while (read < toRead)
            {
                var n = await fs.ReadAsync(buffer.AsMemory(read, toRead - read), cancellationToken).ConfigureAwait(false);
                if (n == 0)
                    return null;
                read += n;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var hashBytes = SHA256.HashData(buffer.AsSpan(0, read));
            return Convert.ToHexString(hashBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
