namespace JunkCleaner.Security;

/// <summary>Blocks traversal/deletion outside safe temp zones.</summary>
public static class ProtectedPaths
{
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly StringComparer PathStringComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Prefix blacklist: normalized full paths matching any entry are blocked.</summary>
    internal static readonly IReadOnlyList<string> ForbiddenDirectoryPrefixes =
        NormalizePrefixList(CreateDefaultForbiddenPrefixes());

    public static bool IsBlocked(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        var full = NormalizePath(path);

        foreach (var bad in ForbiddenDirectoryPrefixes)
        {
            if (full.StartsWith(bad, PathComparison))
                return true;
        }

        return false;
    }

    /// <summary>Returns true when <paramref name="path"/> is equal to or nested under any <paramref name="allowedRoots"/>.</summary>
    public static bool IsUnderAllowedRoots(string path, IEnumerable<string> allowedRoots)
    {
        var full = NormalizePath(path);
        foreach (var root in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            var r = NormalizePath(root);
            if (string.Equals(full, r, PathComparison))
                return true;

            var prefix = r + Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, PathComparison))
                return true;
        }

        return false;
    }

    internal static List<string> NormalizePrefixList(IEnumerable<string> raw)
    {
        return raw.Select(NormalizePath).Distinct(PathStringComparer).ToList();
    }

    public static string NormalizePath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static IEnumerable<string> CreateDefaultForbiddenPrefixes()
    {
        if (!OperatingSystem.IsWindows())
            yield break;

        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(win))
        {
            foreach (var sub in new[] { "System32", "SysWOW64", "WinSxS", "assembly", "ServiceState" })
                yield return SafeCombine(win, sub);
        }

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(pf))
            yield return NormalizePath(pf);

        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(pfx86))
            yield return NormalizePath(pfx86);
    }

    private static string SafeCombine(string root, string sub)
    {
        try
        {
            return NormalizePath(Path.Combine(root, sub));
        }
        catch
        {
            return NormalizePath(root + "\\" + sub);
        }
    }
}
