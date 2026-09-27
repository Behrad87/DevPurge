using DevPurge.Core.Models;

namespace DevPurge.Core.Purging;

public record DeletionReport(
    int TotalRequested,
    int SuccessfulCount,
    int FailedCount,
    long ReclaimedBytes,
    List<(string Path, string Error)> Failures
)
{
    public string FormattedReclaimedSize => DiscoveredFolder.FormatByteSize(ReclaimedBytes);
    public bool IsSuccess => FailedCount == 0;
    public double SuccessPercentage => TotalRequested > 0 ? (double)SuccessfulCount / TotalRequested * 100.0 : 100.0;
}
