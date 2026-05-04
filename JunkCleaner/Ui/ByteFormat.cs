using System.Globalization;

namespace JunkCleaner.Ui;

internal static class ByteFormat
{
    private static readonly string[] Suffixes = { "B", "KB", "MB", "GB", "TB" };

    public static string Format(long bytes)
    {
        double n = bytes;
        var order = 0;
        while (n >= 1024 && order < Suffixes.Length - 1)
        {
            order++;
            n /= 1024;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", n, Suffixes[order]);
    }
}
