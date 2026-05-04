using JunkCleaner.Security;
using Microsoft.Win32;

namespace JunkCleaner.ProgramLeftovers;

public sealed class ProgramLeftoverScanner
{
    private static readonly HashSet<string> RegistryTopLevelSkip = new(StringComparer.OrdinalIgnoreCase)
    {
        "Classes", "Clients", "Microsoft", "Policies", "RegisteredApplications", "ODBC",
        "OLE", "Wow6432Node", "Windows", "Windows NT", "DirectShow", "Cryptography",
        "Description", "Drivers", "EventSystem", "HTMLHelp", "Secure", "TabletTip",
        "Tracing", "Wbem", "WinRAR SFX", "MozillaPlugins",
    };

    private static readonly HashSet<string> FolderSkip = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "Packages", "Package Cache", "Temp", "CrashDumps",
        "ConnectedDevicesPlatform", "Comms", "Diagnostics", "History", "Logs",
        "PeerDistRepub", "USOShared", "USOPrivate", "NVIDIA", "NVIDIA Corporation",
    };

    public static async Task<ProgramLeftoverScanResult> ScanAsync(
        bool scanRegistry,
        bool scanFolders,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(
                () => ScanCore(scanRegistry, scanFolders, progress, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ProgramLeftoverScanResult ScanCore(
        bool scanRegistry,
        bool scanFolders,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        progress?.Report("Читаем список установленных программ из Uninstall-ключей…");
        var installed = InstalledProgramIndex.Build(ct);

        var items = new List<ProgramLeftoverItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var registryChecked = 0;
        var folderChecked = 0;

        if (scanRegistry)
        {
            progress?.Report("Сканируем HKCU\\Software и HKLM\\Software…");
            registryChecked = ScanRegistrySoftware(installed, items, seen, ct);
        }

        if (scanFolders)
        {
            progress?.Report("Проверяем %AppData%, %LocalAppData%, %ProgramData%…");
            folderChecked = ScanDataFolders(installed, items, seen, ct);
        }

        var ordered = items
            .OrderByDescending(static i => i.SizeBytes ?? -1)
            .ThenBy(static i => i.KindText, StringComparer.Ordinal)
            .ThenBy(static i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static i => i.Location, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProgramLeftoverScanResult(ordered, installed.InstalledProgramCount, registryChecked, folderChecked);
    }

    private static int ScanRegistrySoftware(
        InstalledProgramIndex installed,
        List<ProgramLeftoverItem> items,
        HashSet<string> seen,
        CancellationToken ct)
    {
        var checkedCount = 0;

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in RegistryViewsForCurrentOs())
            {
                ct.ThrowIfCancellationRequested();

                RegistryKey? baseKey = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var software = baseKey.OpenSubKey("Software");
                    if (software is null)
                        continue;

                    foreach (var topName in software.GetSubKeyNames())
                    {
                        ct.ThrowIfCancellationRequested();

                        if (ShouldSkipRegistryTopLevel(topName))
                            continue;

                        using var topKey = software.OpenSubKey(topName);
                        if (topKey is null)
                            continue;

                        checkedCount++;
                        var topLocation = RegistryLocation(hive, view, "Software\\" + topName);
                        AddRegistryCandidateIfOrphan(installed, items, seen, topName, topLocation, "ключ верхнего уровня в Software", "Средняя");

                        foreach (var childName in SafeSubKeyNames(topKey).Take(80))
                        {
                            ct.ThrowIfCancellationRequested();

                            if (ShouldSkipRegistryChild(childName))
                                continue;

                            checkedCount++;
                            var childLocation = topLocation + "\\" + childName;
                            AddRegistryCandidateIfOrphan(installed, items, seen, childName, childLocation, $"подключ {topName}\\{childName}", "Низкая");
                        }
                    }
                }
                catch
                {
                    // Reading HKLM/HKCU may fail for individual views/keys; ignore and continue.
                }
                finally
                {
                    baseKey?.Dispose();
                }
            }
        }

        return checkedCount;
    }

    private static int ScanDataFolders(
        InstalledProgramIndex installed,
        List<ProgramLeftoverItem> items,
        HashSet<string> seen,
        CancellationToken ct)
    {
        var checkedCount = 0;

        foreach (var root in CandidateDataRoots())
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            foreach (var dir in SafeEnumerateDirectories(root).Take(1000))
            {
                ct.ThrowIfCancellationRequested();

                if (ProtectedPaths.IsBlocked(dir))
                    continue;

                var name = Path.GetFileName(dir);
                if (ShouldSkipFolderName(name))
                    continue;

                checkedCount++;
                if (!installed.LooksInstalled(name, dir))
                {
                    AddFolderCandidate(items, seen, name, dir, "папка не сопоставилась с установленными программами", "Средняя");
                    continue;
                }

                foreach (var childDir in SafeEnumerateDirectories(dir).Take(120))
                {
                    ct.ThrowIfCancellationRequested();

                    if (ProtectedPaths.IsBlocked(childDir))
                        continue;

                    var childName = Path.GetFileName(childDir);
                    if (ShouldSkipFolderName(childName))
                        continue;

                    checkedCount++;
                    if (!installed.LooksInstalled(childName, childDir))
                    {
                        AddFolderCandidate(
                            items,
                            seen,
                            childName,
                            childDir,
                            $"подпапка внутри {name}, но название не найдено среди Uninstall-ключей",
                            "Низкая");
                    }
                }
            }
        }

        return checkedCount;
    }

    private static void AddRegistryCandidateIfOrphan(
        InstalledProgramIndex installed,
        List<ProgramLeftoverItem> items,
        HashSet<string> seen,
        string name,
        string location,
        string context,
        string confidence)
    {
        if (string.IsNullOrWhiteSpace(name) || installed.LooksInstalled(name))
            return;

        var key = "reg|" + location;
        if (!seen.Add(key))
            return;

        items.Add(
            new ProgramLeftoverItem(
                ProgramLeftoverKind.RegistryKey,
                name,
                location,
                $"{context}: имя не найдено среди установленных программ",
                confidence));
    }

    private static void AddFolderCandidate(
        List<ProgramLeftoverItem> items,
        HashSet<string> seen,
        string name,
        string location,
        string reason,
        string confidence)
    {
        var key = "dir|" + location;
        if (!seen.Add(key))
            return;

        var sizeBytes = TryEstimateDirectorySize(location);
        items.Add(new ProgramLeftoverItem(ProgramLeftoverKind.Folder, name, location, reason, confidence, sizeBytes));
    }

    private static IEnumerable<string> CandidateDataRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        IEnumerable<string>? dirs = null;
        try
        {
            dirs = Directory.EnumerateDirectories(root);
        }
        catch
        {
            yield break;
        }

        foreach (var dir in dirs)
            yield return dir;
    }

    private static long? TryEstimateDirectorySize(string root)
    {
        try
        {
            if (!Directory.Exists(root))
                return null;

            long total = 0;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MaxRecursionDepth = int.MaxValue,
            };

            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // Individual files can disappear or be locked while scanning.
                }
            }

            return total;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeSubKeyNames(RegistryKey key)
    {
        string[] names;
        try
        {
            names = key.GetSubKeyNames();
        }
        catch
        {
            yield break;
        }

        foreach (var n in names)
            yield return n;
    }

    private static bool ShouldSkipRegistryTopLevel(string name) =>
        RegistryTopLevelSkip.Contains(name) || !HasUsefulToken(name);

    private static bool ShouldSkipRegistryChild(string name) =>
        RegistryTopLevelSkip.Contains(name) || name.StartsWith('{') || !HasUsefulToken(name);

    private static bool ShouldSkipFolderName(string name) =>
        FolderSkip.Contains(name) || name.StartsWith('.') || !HasUsefulToken(name);

    private static bool HasUsefulToken(string name) =>
        InstalledProgramIndex.Tokenize(name).Any(static t => t.Length >= 3);

    private static string RegistryLocation(RegistryHive hive, RegistryView view, string path)
    {
        var root = hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
        var suffix = view switch
        {
            RegistryView.Registry32 => " (32-bit)",
            RegistryView.Registry64 => " (64-bit)",
            _ => string.Empty,
        };
        return root + "\\" + path + suffix;
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
