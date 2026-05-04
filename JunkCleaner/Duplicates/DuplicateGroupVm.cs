using System.Collections.ObjectModel;
using JunkCleaner.Ui;

namespace JunkCleaner.Duplicates;

/// <summary>Группа дубликатов для отображения в TreeView.</summary>
public sealed class DuplicateGroupVm
{
    public DuplicateGroupVm(DuplicateScanGroup group)
    {
        Sha256Hex = group.Sha256Hex;
        FileSizeBytes = group.FileSizeBytes;

        var rows = group.Files.Select(static m => new DuplicateFileRowVm(m.FullPath, m.LastWriteTimeUtc)).ToList();
        Rows = new ObservableCollection<DuplicateFileRowVm>(rows);
        ApplyRetentionHeuristic();
    }

    public string Sha256Hex { get; }

    public long FileSizeBytes { get; }

    public ObservableCollection<DuplicateFileRowVm> Rows { get; }

    public long WastedBytesApprox => Rows.Count > 1 ? FileSizeBytes * (Rows.Count - 1) : 0;

    public string Summary =>
        $"{Sha256Hex[..Math.Min(8, Sha256Hex.Length)]}…  ·  {Rows.Count} файлов × {ByteFormat.Format(FileSizeBytes)}  ·  лишние ≈ {ByteFormat.Format(WastedBytesApprox)}";

    /// <summary>Оставляет самый «старый» по времени записи файл, остальные помечает к удалению.</summary>
    public void ApplyRetentionHeuristic()
    {
        if (Rows.Count == 0)
            return;

        var ordered = Rows.OrderBy(static r => r.LastWriteTimeUtc).ThenBy(static r => r.FullPath, StringComparer.OrdinalIgnoreCase).ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].MarkedForDeletion = i != 0;
        }
    }
}
