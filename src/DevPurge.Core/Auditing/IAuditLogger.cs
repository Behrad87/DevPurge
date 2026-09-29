using DevPurge.Core.Purging;

namespace DevPurge.Core.Auditing;

/// <summary>
/// Abstraction for recording and querying persistent audit entries.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Records a purge operation to the audit log.
    /// </summary>
    Task<AuditEntry> LogPurgeAsync(
        DeletionReport report,
        bool sendToRecycleBin,
        bool isDryRun,
        IEnumerable<string> targetRoots,
        IEnumerable<string>? purgedPaths = null,
        string? customLogPath = null);

    /// <summary>
    /// Records a dry-run verification result to the audit log.
    /// </summary>
    Task<AuditEntry> LogDryRunAsync(
        DryRunReport report,
        IEnumerable<string> targetRoots,
        string? customLogPath = null);

    /// <summary>
    /// Retrieves recent audit log entries in reverse chronological order.
    /// </summary>
    Task<List<AuditEntry>> GetRecentEntriesAsync(int maxEntries = 50, string? customLogPath = null);
}
