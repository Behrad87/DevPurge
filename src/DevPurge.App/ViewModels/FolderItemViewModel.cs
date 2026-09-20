using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevPurge.Core.Models;

namespace DevPurge.App.ViewModels;

public partial class FolderItemViewModel : ObservableObject
{
    private readonly DiscoveredFolder _model;
    private readonly Action? _onSelectionChanged;

    public FolderItemViewModel(DiscoveredFolder model, Action? onSelectionChanged = null)
    {
        _model = model;
        _onSelectionChanged = onSelectionChanged;
        _isSelected = model.IsSelected;
    }

    public DiscoveredFolder Model => _model;

    public string Path => _model.Path;
    public string FolderName => _model.FolderName;
    public string CategoryName => _model.CategoryName;
    public long SizeBytes => _model.SizeBytes;
    public string FormattedSize => _model.FormattedSize;
    public string FormattedAge => _model.FormattedAge;
    public double AgeDays => _model.AgeDays;
    public int FileCount => _model.FileCount;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        _model.IsSelected = value;
        _onSelectionChanged?.Invoke();
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{Path}\"",
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // Ignore explorer launch issues
        }
    }
}
