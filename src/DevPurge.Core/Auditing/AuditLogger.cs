using System.Text.Json;
using DevPurge.Core.Purging;

namespace DevPurge.Core.Auditing;

/// <summary>
/// Thread-safe audit logger that records all purge, dry-run, and reclamation operations to persistent disk storage.
/// </summary>
public class AuditLogger(string? customLogPath = null) : IAuditLogger
{
    private static readonly SemaphoreSlim FileLock = new(1, 1);
    private readonly string _defaultLogPath = customLogPath ?? GetDefaultLogPath();

    /// <summary>
    /// Gets the standard platform-specific log path for DevPurge audit records.
    /// </summary>
    public static string GetDefaultLogPath()
    {
        string baseDir;
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            baseDir = string.IsNullOrEmpty(localAppData)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".devpurge")
                : Path.Combine(localAppData, "DevPurge", "logs");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, ".config", "devpurge");
        }

        return Path.Combine(baseDir, "purge-audit.jsonl");
    }

    /// <summary>
    /// Records a purge operation to the audit log.
    /// </summary>
    public async Task<AuditEntry> LogPurgeAsync(
        DeletionReport report,
        bool sendToRecycleBin,
        bool isDryRun,
        IEnumerable<string> targetRoots,
        IEnumerable<string>? purgedPaths = null,
        string? customLogPath = null)
    {
        var entry = new AuditEntry
        {
            Action = isDryRun ? "DryRun" : "Purge",
            Mode = isDryRun ? "DryRun" : (sendToRecycleBin ? "RecycleBin" : "Permanent"),
            IsDryRun = isDryRun,
            TotalRequested = report.TotalRequested,
            SuccessfulCount = report.SuccessfulCount,
            FailedCount = report.FailedCount,
            ReclaimedBytes = report.ReclaimedBytes,
            FormattedReclaimedSize = report.FormattedReclaimedSize,
            TargetRoots = targetRoots.ToList(),
            PurgedPaths = purgedPaths?.ToList() ?? [],
            Failures = report.Failures.Select(f => new AuditFailure(f.Path, f.Error)).ToList()
        };

        var targetFile = customLogPath ?? _defaultLogPath;
        await AppendEntryAsync(entry, targetFile);
        return entry;
    }

    /// <summary>
    /// Records a dry-run verification result to the audit log.
    /// </summary>
    public async Task<AuditEntry> LogDryRunAsync(
        DryRunReport report,
        IEnumerable<string> targetRoots,
        string? customLogPath = null)
    {
        var entry = new AuditEntry
        {
            Action = "DryRun",
            Mode = "DryRun",
            IsDryRun = true,
            TotalRequested = report.TotalEvaluated,
            SuccessfulCount = report.EligibleCount,
            FailedCount = report.UnsafeCount + report.LockedCount,
            ReclaimedBytes = report.EligibleBytes,
            FormattedReclaimedSize = report.FormattedEligibleSize,
            TargetRoots = targetRoots.ToList(),
            PurgedPaths = report.Items.Where(i => i.Status == DryRunStatus.Eligible).Select(i => i.Path).ToList(),
            Failures = report.Items.Where(i => i.Status != DryRunStatus.Eligible)
                .Select(i => new AuditFailure(i.Path, i.Reason ?? i.Status.ToString())).ToList()
        };

        var targetFile = customLogPath ?? _defaultLogPath;
        await AppendEntryAsync(entry, targetFile);
        return entry;
    }

    private static async Task AppendEntryAsync(AuditEntry entry, string logFilePath)
    {
        await FileLock.WaitAsync();
        try
        {
            var dir = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(entry);
            await File.AppendAllLinesAsync(logFilePath, [json]);
        }
        finally
        {
            FileLock.Release();
        }
    }

    /// <summary>
    /// Retrieves recent audit log entries in reverse chronological order.
    /// </summary>
    public async Task<List<AuditEntry>> GetRecentEntriesAsync(int maxEntries = 50, string? customLogPath = null)
    {
        var targetFile = customLogPath ?? _defaultLogPath;
        if (!File.Exists(targetFile))
        {
            return [];
        }

        await FileLock.WaitAsync();
        try
        {
            var lines = await File.ReadAllLinesAsync(targetFile);
            var result = new List<AuditEntry>();

            for (int i = lines.Length - 1; i >= 0 && result.Count < maxEntries; i--)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var entry = JsonSerializer.Deserialize<AuditEntry>(line);
                    if (entry != null)
                    {
                        result.Add(entry);
                    }
                }
                catch
                {
                    // Ignore corrupted audit line
                }
            }

            return result;
        }
        finally
        {
            FileLock.Release();
        }
    }
}
