using System.Diagnostics;
using System.IO;
using System.Windows;
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

        var parent = System.IO.Path.GetDirectoryName(model.Path);
        ParentDirectoryName = !string.IsNullOrEmpty(parent) ? System.IO.Path.GetFileName(parent) : string.Empty;
        ParentPath = parent ?? string.Empty;
    }

    public DiscoveredFolder Model => _model;

    public string Path => _model.Path;
    public string FolderName => _model.FolderName;
    public string CategoryName => _model.CategoryName;
    public ArtifactType ArtifactType => _model.ArtifactType;
    public long SizeBytes => _model.SizeBytes;
    public string FormattedSize => _model.FormattedSize;
    public string FormattedAge => _model.FormattedAge;
    public double AgeDays => _model.AgeDays;
    public int FileCount => _model.FileCount;
    public string ParentDirectoryName { get; }
    public string ParentPath { get; }

    public bool IsStale => AgeDays >= 30;
    public bool IsVeryLarge => SizeBytes >= 1024L * 1024 * 1024; // >= 1 GB

    public string SizeForeground => IsVeryLarge ? "#38BDF8" : "#E2E8F0";

    public string AgeBadgeBackground => IsStale ? "#451A03" : "#1E293B";
    public string AgeBadgeBorder => IsStale ? "#B45309" : "#334155";
    public string AgeBadgeForeground => IsStale ? "#FDE68A" : "#94A3B8";

    public string CategoryBadgeBackground => _model.ArtifactType switch
    {
        ArtifactType.DotNetBuild => "#2E1A47",
        ArtifactType.NodeModules => "#064E3B",
        ArtifactType.RustTarget => "#7C2D12",
        ArtifactType.PythonVenv => "#1E3A8A",
        ArtifactType.GradleBuild => "#831843",
        ArtifactType.Vendor => "#134E4A",
        ArtifactType.DartFlutter => "#0B2545",
        ArtifactType.IdeCache => "#3B0764",
        ArtifactType.CppBuild => "#172554",
        ArtifactType.ZigBuild => "#431407",
        ArtifactType.SwiftBuild => "#451A03",
        ArtifactType.ElixirBuild => "#2E1065",
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
        ArtifactType.DartFlutter => "#0284C7",
        ArtifactType.IdeCache => "#A855F7",
        ArtifactType.CppBuild => "#3B82F6",
        ArtifactType.ZigBuild => "#EA580C",
        ArtifactType.SwiftBuild => "#F97316",
        ArtifactType.ElixirBuild => "#9333EA",
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
        ArtifactType.DartFlutter => "#BAE6FD",
        ArtifactType.IdeCache => "#F3E8FF",
        ArtifactType.CppBuild => "#DBEAFE",
        ArtifactType.ZigBuild => "#FFEDD5",
        ArtifactType.SwiftBuild => "#FFEDD5",
        ArtifactType.ElixirBuild => "#F3E8FF",
        _ => "#94A3B8"
    };

    public string CategoryIconSymbol => _model.ArtifactType switch
    {
        ArtifactType.NodeModules => "Code24",
        ArtifactType.DotNetBuild => "AppGeneric24",
        ArtifactType.RustTarget => "Cube24",
        ArtifactType.PythonVenv => "Branch24",
        ArtifactType.GradleBuild => "DualScreenSpan24",
        ArtifactType.Vendor => "Archive24",
        ArtifactType.DartFlutter => "Branch24",
        ArtifactType.IdeCache => "Window24",
        ArtifactType.CppBuild => "Wrench24",
        ArtifactType.ZigBuild => "Flash24",
        ArtifactType.SwiftBuild => "Code24",
        ArtifactType.ElixirBuild => "Beaker24",
        _ => "Folder24"
    };

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        _model.IsSelected = value;
        _onSelectionChanged?.Invoke();
    }

    [ObservableProperty]
    private double _relativeSizePercent;

    public double RelativeSizeWidth => Math.Clamp((RelativeSizePercent / 100.0) * 80.0, 3.0, 80.0);

    partial void OnRelativeSizePercentChanged(double value)
    {
        OnPropertyChanged(nameof(RelativeSizeWidth));
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

    [RelayCommand]
    private void CopyPath()
    {
        try
        {
            Clipboard.SetDataObject(Path, true);
        }
        catch
        {
            // Ignore clipboard errors
        }
    }
}
