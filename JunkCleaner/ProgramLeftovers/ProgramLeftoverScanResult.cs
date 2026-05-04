namespace JunkCleaner.ProgramLeftovers;

public sealed class ProgramLeftoverScanResult
{
    public ProgramLeftoverScanResult(
        IReadOnlyList<ProgramLeftoverItem> items,
        int installedProgramCount,
        int registryCandidatesChecked,
        int folderCandidatesChecked)
    {
        Items = items;
        InstalledProgramCount = installedProgramCount;
        RegistryCandidatesChecked = registryCandidatesChecked;
        FolderCandidatesChecked = folderCandidatesChecked;
    }

    public IReadOnlyList<ProgramLeftoverItem> Items { get; }

    public long TotalFolderBytes => Items.Sum(static i => i.SizeBytes ?? 0);

    public int InstalledProgramCount { get; }

    public int RegistryCandidatesChecked { get; }

    public int FolderCandidatesChecked { get; }
}
