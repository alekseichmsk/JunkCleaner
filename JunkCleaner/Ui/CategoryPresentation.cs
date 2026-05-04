using System.ComponentModel;
using System.Runtime.CompilerServices;
using JunkCleaner.Contracts;

namespace JunkCleaner.Ui;

public sealed class CategoryPresentation : INotifyPropertyChanged
{
    private bool _isIncluded = true;
    private string _sizeText = "—";

    public CategoryPresentation(ICleanupCategory category) => Category = category;

    public ICleanupCategory Category { get; }

    public Models.ScanResult? LastScan { get; set; }

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (value == _isIncluded)
                return;
            _isIncluded = value;
            OnPropertyChanged();
        }
    }

    public string SizeText
    {
        get => _sizeText;
        set
        {
            if (value == _sizeText)
                return;
            _sizeText = value;
            OnPropertyChanged();
        }
    }

    public string Title => Category.DisplayName;

    public string DescriptionText => Category.Description;

    public bool RequiresAdmin => Category.RequiresAdmin;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
