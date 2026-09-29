using System.Text.Json.Serialization;
using DevPurge.Core.Models;

namespace DevPurge.Core.TreeSize;

/// <summary>
/// Represents a hierarchical folder node in a TreeSize directory analysis.
/// </summary>
public class TreeSizeNode
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public int DirectoryCount { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public bool IsArtifact { get; set; }
    public string? ArtifactCategory { get; set; }
    public ArtifactType? ArtifactType { get; set; }
    public string? ErrorMessage { get; set; }

    [JsonIgnore]
    public TreeSizeNode? Parent { get; set; }

    public List<TreeSizeNode> Children { get; set; } = [];

    public double PercentOfParent { get; set; }
    public double PercentOfRoot { get; set; }

    public string FormattedSize => DiscoveredFolder.FormatByteSize(SizeBytes);
    public string FormattedPercent => $"{PercentOfParent:0.0}%";
    public string FormattedPercentOfRoot => $"{PercentOfRoot:0.0}%";

    public double AgeDays => Math.Max(0, (DateTime.UtcNow - LastModifiedUtc).TotalDays);

    public string FormattedAge => (int)Math.Floor(AgeDays) switch
    {
        0 => "Today",
        1 => "1 day ago",
        < 30 => $"{(int)Math.Floor(AgeDays)} days ago",
        var days => (days / 30) switch
        {
            1 => "1 month ago",
            var months => $"{months} months ago"
        }
    };

    public override string ToString() => $"{Name} ({FormattedSize}, {FormattedPercent}) - {Path}";
}
