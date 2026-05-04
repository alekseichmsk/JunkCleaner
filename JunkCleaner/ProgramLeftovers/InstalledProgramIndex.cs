using System.Text;
using Microsoft.Win32;

namespace JunkCleaner.ProgramLeftovers;

internal sealed class InstalledProgramIndex
{
    private static readonly string[] UninstallRoots =
    {
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "apps", "application", "software", "program", "programs",
        "update", "updater", "helper", "service", "services", "runtime",
        "setup", "install", "installer", "uninstall", "client", "server",
        "x64", "x86", "win32", "win64", "windows",
        "inc", "ltd", "llc", "corp", "corporation", "company", "co",
    };

    private readonly HashSet<string> _phrases = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _installLocations = new();

    public int InstalledProgramCount { get; private set; }

    public static InstalledProgramIndex Build(CancellationToken ct)
    {
        var index = new InstalledProgramIndex();
        var seenDisplayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in RegistryViewsForCurrentOs())
            {
                ct.ThrowIfCancellationRequested();

                RegistryKey? baseKey = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                    foreach (var rootPath in UninstallRoots)
                    {
                        using var root = baseKey.OpenSubKey(rootPath);
                        if (root is null)
                            continue;

                        foreach (var subName in root.GetSubKeyNames())
                        {
                            ct.ThrowIfCancellationRequested();

                            using var appKey = root.OpenSubKey(subName);
                            if (appKey is null)
                                continue;

                            var displayName = ReadString(appKey, "DisplayName");
                            if (string.IsNullOrWhiteSpace(displayName))
                                continue;

                            if (seenDisplayNames.Add(displayName))
                                index.InstalledProgramCount++;

                            index.AddPhraseAndTokens(displayName);
                            index.AddPhraseAndTokens(ReadString(appKey, "Publisher"));

                            var installLocation = ReadString(appKey, "InstallLocation");
                            index.AddInstallLocation(installLocation);

                            var displayIcon = ReadString(appKey, "DisplayIcon");
                            index.AddInstallLocation(TryExtractDirectory(displayIcon));
                        }
                    }
                }
                catch
                {
                    // Registry access may fail per-view/per-hive; keep scanning other roots.
                }
                finally
                {
                    baseKey?.Dispose();
                }
            }
        }

        return index;
    }

    public bool LooksInstalled(string candidateName, string? candidatePath = null)
    {
        var phrase = NormalizePhrase(candidateName);
        if (phrase.Length == 0)
            return false;

        if (_phrases.Contains(phrase))
            return true;

        if (_phrases.Any(p => p.Length >= 4 && (phrase.Contains(p, StringComparison.OrdinalIgnoreCase) || p.Contains(phrase, StringComparison.OrdinalIgnoreCase))))
            return true;

        var tokens = Tokenize(candidateName).Where(static t => !StopWords.Contains(t)).ToList();
        if (tokens.Count == 0)
            return false;

        var overlap = tokens.Count(t => _tokens.Contains(t));
        if (tokens.Count == 1)
            return overlap == 1 && tokens[0].Length >= 4;

        if (overlap >= 2)
            return true;

        if (overlap == 1 && tokens.Any(t => t.Length >= 7 && _tokens.Contains(t)))
            return true;

        if (!string.IsNullOrWhiteSpace(candidatePath))
        {
            var full = NormalizePath(candidatePath);
            if (_installLocations.Any(loc => full.StartsWith(loc, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    internal static IEnumerable<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                continue;
            }

            if (sb.Length > 0)
            {
                var token = sb.ToString();
                sb.Clear();
                if (IsUsefulToken(token))
                    yield return token;
            }
        }

        if (sb.Length > 0)
        {
            var token = sb.ToString();
            if (IsUsefulToken(token))
                yield return token;
        }
    }

    private void AddPhraseAndTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var phrase = NormalizePhrase(value);
        if (phrase.Length >= 3)
            _phrases.Add(phrase);

        foreach (var t in Tokenize(value))
        {
            if (!StopWords.Contains(t))
                _tokens.Add(t);
        }
    }

    private void AddInstallLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return;

        var clean = location.Trim().Trim('"');
        if (clean.Length == 0)
            return;

        try
        {
            if (File.Exists(clean))
                clean = Path.GetDirectoryName(clean) ?? clean;
            clean = NormalizePath(clean);
        }
        catch
        {
            return;
        }

        if (!Directory.Exists(clean))
            return;

        _installLocations.Add(clean);
        AddPhraseAndTokens(Path.GetFileName(clean));
    }

    private static bool IsUsefulToken(string token) =>
        token.Length >= 3 && !StopWords.Contains(token);

    private static string NormalizePhrase(string? value) =>
        string.Join(" ", Tokenize(value));

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string? ReadString(RegistryKey key, string valueName)
    {
        try
        {
            return key.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryExtractDirectory(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
            return null;

        var s = displayIcon.Trim().Trim('"');
        var comma = s.LastIndexOf(',');
        if (comma > 1)
            s = s[..comma].Trim().Trim('"');

        try
        {
            if (File.Exists(s))
                return Path.GetDirectoryName(s);
        }
        catch
        {
            // ignore malformed DisplayIcon
        }

        return null;
    }

    private static IEnumerable<RegistryView> RegistryViewsForCurrentOs()
    {
        yield return RegistryView.Default;

        if (!Environment.Is64BitOperatingSystem)
            yield break;

        yield return RegistryView.Registry64;
        yield return RegistryView.Registry32;
    }
}
