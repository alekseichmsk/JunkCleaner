using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JunkCleaner.LargeFiles;

/// <summary>Выбор фиксированного тома для сканирования.</summary>
public sealed class DriveChoiceVm : INotifyPropertyChanged
{
    private bool _isIncluded = true;

    public DriveChoiceVm(string rootPath, string title)
    {
        RootPath = rootPath;
        Title = title;
    }

    public string RootPath { get; }

    public string Title { get; }

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (_isIncluded == value)
                return;

            _isIncluded = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
