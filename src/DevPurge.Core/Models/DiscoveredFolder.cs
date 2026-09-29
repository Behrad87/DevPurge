namespace DevPurge.Core.Models;

/// <summary>
/// Represents a disposable folder discovered during directory scanning.
/// </summary>
public class DiscoveredFolder
{
    public required string Path { get; init; }
    public required string FolderName { get; init; }
    public required ArtifactType ArtifactType { get; init; }
    public required string CategoryName { get; init; }
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public DateTime LastModifiedUtc { get; init; }
    public bool IsSelected { get; set; } = true;
    public string? ErrorMessage { get; set; }

    public double AgeDays => Math.Max(0, (DateTime.UtcNow - LastModifiedUtc).TotalDays);
    public bool IsStale => AgeDays >= 30;

    private static readonly string[] Suffixes = ["B", "KB", "MB", "GB", "TB"];

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

    public string FormattedSize => FormatByteSize(SizeBytes);

    public static string FormatByteSize(long bytes)
    {
        if (bytes < 0) return "0 B";
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < Suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n2} {Suffixes[counter]}";
    }

    public override string ToString() => $"{FolderName} ({FormattedSize}) - {Path}";
}
