using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JunkCleaner.Duplicates;

public sealed class DuplicateFileRowVm : INotifyPropertyChanged
{
    private bool _markedForDeletion;

    public DuplicateFileRowVm(string fullPath, DateTime lastWriteTimeUtc)
    {
        FullPath = fullPath;
        LastWriteTimeUtc = lastWriteTimeUtc;
    }

    public string FullPath { get; }

    public DateTime LastWriteTimeUtc { get; }

    public bool MarkedForDeletion
    {
        get => _markedForDeletion;
        set
        {
            if (value == _markedForDeletion)
                return;

            _markedForDeletion = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(KeeperLabelVisibility));
        }
    }

    /// <summary>Отображение «держим», если файл не помечен к удалению.</summary>
    public string KeeperLabelVisibility => MarkedForDeletion ? string.Empty : "оставить";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
