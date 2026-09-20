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

    public string FormattedAge
    {
        get
        {
            var days = (int)Math.Floor(AgeDays);
            if (days == 0) return "Today";
            if (days == 1) return "1 day ago";
            if (days < 30) return $"{days} days ago";
            var months = days / 30;
            return months == 1 ? "1 month ago" : $"{months} months ago";
        }
    }

    public string FormattedSize => FormatByteSize(SizeBytes);

    public static string FormatByteSize(long bytes)
    {
        if (bytes < 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n2} {suffixes[counter]}";
    }

    public override string ToString() => $"{FolderName} ({FormattedSize}) - {Path}";
}
