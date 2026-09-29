using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevPurge.Core.Models;

namespace DevPurge.App.ViewModels;

public partial class RepositoryGroupViewModel : ObservableObject
{
    private readonly Action? _onFolderSelectionChanged;

    public string RepositoryName { get; }
    public string RepositoryPath { get; }
    public string PrimaryEcosystem { get; }
    public string TechAbbreviation { get; }
    public string TechIconSymbol { get; }
    public string TechBadgeBackground { get; }
    public string TechBadgeBorder { get; }
    public string TechBadgeForeground { get; }

    public ObservableCollection<FolderItemViewModel> Folders { get; } = [];

    [ObservableProperty]
    private bool _isExpanded = true;

    public string ExpandIconSymbol => IsExpanded ? "ChevronUp24" : "ChevronDown24";

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpandIconSymbol));
    }

    private bool _isUpdatingSelection;

    public bool? IsSelected
    {
        get
        {
            if (Folders.Count == 0) return false;
            int selectedCount = Folders.Count(f => f.IsSelected);
            if (selectedCount == Folders.Count) return true;
            if (selectedCount == 0) return false;
            return null; // indeterminate
        }
        set
        {
            if (!value.HasValue) return;
            _isUpdatingSelection = true;
            foreach (var folder in Folders)
            {
                folder.IsSelected = value.Value;
            }
            _isUpdatingSelection = false;
            OnPropertyChanged(nameof(IsSelected));
            _onFolderSelectionChanged?.Invoke();
        }
    }

    public long TotalSizeBytes => Folders.Sum(f => f.SizeBytes);
    public string FormattedTotalSize => DiscoveredFolder.FormatByteSize(TotalSizeBytes);
    public int FolderCount => Folders.Count;
    public string FolderCountText => $"{FolderCount} disposable {(FolderCount == 1 ? "folder" : "folders")}";

    public RepositoryGroupViewModel(
        string repositoryName,
        string repositoryPath,
        IEnumerable<FolderItemViewModel> folders,
        Action? onFolderSelectionChanged = null)
    {
        RepositoryName = repositoryName;
        RepositoryPath = repositoryPath;
        _onFolderSelectionChanged = onFolderSelectionChanged;

        foreach (var folder in folders)
        {
            Folders.Add(folder);
        }

        // Determine dominant ecosystem
        var topType = Folders
            .GroupBy(f => f.ArtifactType)
            .OrderByDescending(g => g.Sum(x => x.SizeBytes))
            .Select(g => g.Key)
            .FirstOrDefault();

        PrimaryEcosystem = Folders.FirstOrDefault(f => f.ArtifactType == topType)?.CategoryName ?? "Build Artifacts";

        (TechAbbreviation, TechIconSymbol, TechBadgeBackground, TechBadgeBorder, TechBadgeForeground) = topType switch
        {
            ArtifactType.NodeModules => ("JS", "Code24", "#0D2E26", "#10B981", "#34D399"),
            ArtifactType.DotNetBuild => ("C#", "AppGeneric24", "#201736", "#8B5CF6", "#A78BFA"),
            ArtifactType.RustTarget => ("RS", "Cube24", "#3D1711", "#F97316", "#FB923C"),
            ArtifactType.PythonVenv => ("PY", "Branch24", "#0C294D", "#3B82F6", "#60A5FA"),
            ArtifactType.GradleBuild => ("JV", "DualScreenSpan24", "#3B1123", "#EC4899", "#F472B6"),
            ArtifactType.Vendor => ("VD", "Archive24", "#0D2B29", "#14B8A6", "#2DD4BF"),
            ArtifactType.DartFlutter => ("FL", "Branch24", "#08213B", "#0284C7", "#38BDF8"),
            ArtifactType.IdeCache => ("IDE", "Window24", "#260E3D", "#A855F7", "#C084FC"),
            ArtifactType.CppBuild => ("C++", "Wrench24", "#11224D", "#3B82F6", "#60A5FA"),
            ArtifactType.ZigBuild => ("ZG", "Flash24", "#33180C", "#EA580C", "#FB923C"),
            ArtifactType.SwiftBuild => ("SW", "Code24", "#331608", "#F97316", "#FB923C"),
            ArtifactType.ElixirBuild => ("EX", "Beaker24", "#240E4A", "#9333EA", "#C084FC"),
            ArtifactType.RubyBundle => ("RB", "Diamond24", "#3D111A", "#F43F5E", "#FB7185"),
            ArtifactType.HaskellBuild => ("HS", "Code24", "#231138", "#A855F7", "#C084FC"),
            ArtifactType.TerraformCache => ("TF", "Cloud24", "#201736", "#8B5CF6", "#A78BFA"),
            _ => ("DEV", "Folder24", "#1E293B", "#334155", "#94A3B8")
        };
    }

    public void NotifyChildSelectionChanged()
    {
        if (_isUpdatingSelection) return;
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(TotalSizeBytes));
        OnPropertyChanged(nameof(FormattedTotalSize));
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        try
        {
            if (Directory.Exists(RepositoryPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{RepositoryPath}\"",
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // Ignore explorer launch errors
        }
    }
}
