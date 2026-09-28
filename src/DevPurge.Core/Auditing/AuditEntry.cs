namespace DevPurge.Core.Auditing;

/// <summary>
/// Represents a failure recorded during a purge operation.
/// </summary>
public record AuditFailure(string Path, string Error);

/// <summary>
/// A persistent audit log entry recording a purge, dry-run, or reclamation action.
/// </summary>
public record AuditEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string Action { get; init; } = "Purge";
    public string Mode { get; init; } = "RecycleBin";
    public string User { get; init; } = Environment.UserName;
    public string MachineName { get; init; } = Environment.MachineName;
    public bool IsDryRun { get; init; }
    public int TotalRequested { get; init; }
    public int SuccessfulCount { get; init; }
    public int FailedCount { get; init; }
    public long ReclaimedBytes { get; init; }
    public string FormattedReclaimedSize { get; init; } = "0 B";
    public List<string> TargetRoots { get; init; } = [];
    public List<string> PurgedPaths { get; init; } = [];
    public List<AuditFailure> Failures { get; init; } = [];
}
