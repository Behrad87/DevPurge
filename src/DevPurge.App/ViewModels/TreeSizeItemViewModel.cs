using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevPurge.Core.Models;
using DevPurge.Core.TreeSize;

namespace DevPurge.App.ViewModels;

public partial class TreeSizeItemViewModel : ObservableObject
{
    private readonly TreeSizeNode _node;
    private readonly Action<TreeSizeItemViewModel>? _onPurgeRequested;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isVisible = true;

    public ObservableCollection<TreeSizeItemViewModel> Children { get; } = [];

    public TreeSizeNode Node => _node;
    public string Path => _node.Path;
    public string Name => _node.Name;
    public long SizeBytes => _node.SizeBytes;
    public string FormattedSize => _node.FormattedSize;
    public int FileCount => _node.FileCount;
    public int DirectoryCount => _node.DirectoryCount;
    public double PercentOfParent => _node.PercentOfParent;
    public double PercentOfRoot => _node.PercentOfRoot;
    public string FormattedPercent => _node.FormattedPercent;
    public string FormattedPercentOfRoot => _node.FormattedPercentOfRoot;
    public bool IsArtifact => _node.IsArtifact;
    public string? ArtifactCategory => _node.ArtifactCategory;
    public ArtifactType? ArtifactType => _node.ArtifactType;
    public string FormattedAge => _node.FormattedAge;

    public double RelativeBarWidth => Math.Max(2, Math.Min(80, (PercentOfParent / 100.0) * 80.0));

    public string BarBrush
    {
        get
        {
            if (IsArtifact) return "#10B981"; // Emerald green for reclaimable developer artifact
            if (PercentOfParent >= 50.0) return "#F59E0B"; // Amber for major space hogger
            if (PercentOfParent >= 25.0) return "#38BDF8"; // Sky blue
            return "#64748B"; // Slate
        }
    }

    public string FolderIconSymbol => IsArtifact ? "FolderZip24" : (Children.Count > 0 ? "Folder24" : "FolderOpen24");

    public string FolderIconBrush => IsArtifact ? "#10B981" : (SizeBytes >= 1024L * 1024 * 1024 ? "#38BDF8" : "#94A3B8");

    public TreeSizeItemViewModel(
        TreeSizeNode node,
        int depth = 0,
        Action<TreeSizeItemViewModel>? onPurgeRequested = null)
    {
        _node = node;
        _onPurgeRequested = onPurgeRequested;
        _isExpanded = depth <= 1; // Auto-expand first two levels

        foreach (var child in node.Children)
        {
            Children.Add(new TreeSizeItemViewModel(child, depth + 1, onPurgeRequested));
        }
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
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open File Explorer:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

    [RelayCommand]
    private void RequestPurge()
    {
        _onPurgeRequested?.Invoke(this);
    }

    public void ExpandAll()
    {
        IsExpanded = true;
        foreach (var child in Children)
        {
            child.ExpandAll();
        }
    }

    public void CollapseAll()
    {
        IsExpanded = false;
        foreach (var child in Children)
        {
            child.CollapseAll();
        }
    }

    public bool Filter(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            IsVisible = true;
            foreach (var child in Children)
            {
                child.Filter(search);
            }
            return true;
        }

        bool matchThis = Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                         (ArtifactCategory != null && ArtifactCategory.Contains(search, StringComparison.OrdinalIgnoreCase));

        bool anyChildMatch = false;
        foreach (var child in Children)
        {
            if (child.Filter(search))
            {
                anyChildMatch = true;
            }
        }

        IsVisible = matchThis || anyChildMatch;
        if (anyChildMatch)
        {
            IsExpanded = true;
        }

        return IsVisible;
    }
}
