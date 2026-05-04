namespace JunkCleaner.Duplicates;

internal static class ExtensionPresetHelpers
{
    internal static HashSet<string>? ExtensionsForPreset(DuplicateExtensionPreset preset)
    {
        var list = preset switch
        {
            DuplicateExtensionPreset.All => (string[]?)null,
            DuplicateExtensionPreset.Images => [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff"],
            DuplicateExtensionPreset.Documents => [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt"],
            DuplicateExtensionPreset.Videos => [".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".mpeg", ".mpg"],
            DuplicateExtensionPreset.Archives => [".zip", ".rar", ".7z", ".tar", ".gz", ".bz2"],
            _ => null,
        };

        if (list is null)
            return null;

        return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
    }
}
