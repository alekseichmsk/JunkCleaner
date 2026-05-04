namespace JunkCleaner.Duplicates;

public sealed class DuplicateScanOptions
{
    public DuplicateScanOptions(
        IReadOnlyList<string> rootFolders,
        long minFileSizeBytes,
        DuplicateExtensionPreset extensionPreset)
    {
        RootFolders = rootFolders ?? throw new ArgumentNullException(nameof(rootFolders));
        MinFileSizeBytes = minFileSizeBytes;
        ExtensionPreset = extensionPreset;
    }

    public IReadOnlyList<string> RootFolders { get; }

    public long MinFileSizeBytes { get; }

    public DuplicateExtensionPreset ExtensionPreset { get; }

    /// <summary>Returns null when all extensions are allowed; otherwise distinct normalized extensions including leading dot, lower-case invariant.</summary>
    public HashSet<string>? ResolveExtensionFilter()
        => ExtensionPresetHelpers.ExtensionsForPreset(ExtensionPreset);
}
