using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TimeRecordingAgent.App.History;

public sealed class ColumnConfig : INotifyPropertyChanged
{
    private bool _isVisible;
    private string _filterText = string.Empty;

    public ColumnConfig(string name, string bindingPath, bool isVisibleByDefault = true, double width = 120)
    {
        Name = name;
        BindingPath = bindingPath;
        _isVisible = isVisibleByDefault;
        Width = width;
    }

    public string Name { get; }
    public string BindingPath { get; }
    public double Width { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (_filterText == value) return;
            _filterText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
