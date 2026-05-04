namespace JunkCleaner.ProgramLeftovers;

public enum ProgramLeftoverKind
{
    RegistryKey,
    Folder,
}

public sealed class ProgramLeftoverItem
{
    public ProgramLeftoverItem(
        ProgramLeftoverKind kind,
        string name,
        string location,
        string reason,
        string confidence,
        long? sizeBytes = null)
    {
        Kind = kind;
        Name = name;
        Location = location;
        Reason = reason;
        Confidence = confidence;
        SizeBytes = sizeBytes;
    }

    public ProgramLeftoverKind Kind { get; }

    public string KindText => Kind == ProgramLeftoverKind.RegistryKey ? "Реестр" : "Папка";

    public string Name { get; }

    public string Location { get; }

    public string Reason { get; }

    public string Confidence { get; }

    public long? SizeBytes { get; }

    public string SizeText => SizeBytes is { } bytes
        ? Ui.ByteFormat.Format(bytes)
        : "—";

    public override string ToString() => Location;
}
