using DevPurge.Core.Auditing;
using DevPurge.Core.Models;

namespace DevPurge.Core.Purging;

/// <summary>
/// Abstraction for safe directory purge and dry-run simulation operations.
/// </summary>
public interface IPurgeService
{
    /// <summary>
    /// Purges target folders using parallel deletion or batched shell operations.
    /// </summary>
    Task<DeletionReport> PurgeAsync(
        IEnumerable<DiscoveredFolder> folders,
        bool sendToRecycleBin = true,
        IProgress<(string Path, int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default,
        IAuditLogger? auditLogger = null,
        string? customAuditLogPath = null);

    /// <summary>
    /// Performs non-destructive dry-run simulation and lock verification on target folders.
    /// </summary>
    Task<DryRunReport> SimulatePurgeAsync(
        IEnumerable<DiscoveredFolder> folders,
        CancellationToken cancellationToken = default);
}
