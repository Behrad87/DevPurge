using CommunityToolkit.Mvvm.ComponentModel;
using DevPurge.Core.Models;

namespace DevPurge.App.ViewModels;

public partial class CategoryFilterItem : ObservableObject
{
    public required string Name { get; init; }
    public ArtifactType? ArtifactType { get; init; }
    public string AccentColor { get; init; } = "#38BDF8";
    public string DotColor { get; init; } = "#38BDF8";

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private bool _isSelected;

    public string FormattedSize => DiscoveredFolder.FormatByteSize(TotalBytes);

    public string DisplayText => TotalBytes > 0
        ? $"{Name} ({FormattedSize})"
        : Name;
}
