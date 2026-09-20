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

    public string CategoryBadgeBackground => _model.ArtifactType switch
    {
        ArtifactType.DotNetBuild => "#2E1A47",
        ArtifactType.NodeModules => "#064E3B",
        ArtifactType.RustTarget => "#7C2D12",
        ArtifactType.PythonVenv => "#1E3A8A",
        ArtifactType.GradleBuild => "#831843",
        ArtifactType.Vendor => "#134E4A",
        _ => "#1E293B"
    };

    public string CategoryBadgeBorder => _model.ArtifactType switch
    {
        ArtifactType.DotNetBuild => "#7C3AED",
        ArtifactType.NodeModules => "#10B981",
        ArtifactType.RustTarget => "#F97316",
        ArtifactType.PythonVenv => "#3B82F6",
        ArtifactType.GradleBuild => "#EC4899",
        ArtifactType.Vendor => "#14B8A6",
        _ => "#334155"
    };

    public string CategoryBadgeForeground => _model.ArtifactType switch
    {
        ArtifactType.DotNetBuild => "#DDD6FE",
        ArtifactType.NodeModules => "#A7F3D0",
        ArtifactType.RustTarget => "#FFEDD5",
        ArtifactType.PythonVenv => "#BFDBFE",
        ArtifactType.GradleBuild => "#FCE7F3",
        ArtifactType.Vendor => "#99F6E4",
        _ => "#94A3B8"
    };

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
