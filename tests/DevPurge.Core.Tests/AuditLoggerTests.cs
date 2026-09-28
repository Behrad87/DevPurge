using DevPurge.Core.Auditing;
using DevPurge.Core.Purging;

namespace DevPurge.Core.Tests;

public class AuditLoggerTests : IDisposable
{
    private readonly string _testLogDir;
    private readonly string _testLogFile;

    public AuditLoggerTests()
    {
        _testLogDir = Path.Combine(Path.GetTempPath(), "devpurge_audit_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testLogDir);
        _testLogFile = Path.Combine(_testLogDir, "audit.jsonl");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testLogDir))
            {
                Directory.Delete(_testLogDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task LogPurgeAsync_WritesAndReadsAuditEntry()
    {
        var logger = new AuditLogger(_testLogFile);

        var report = new DeletionReport(
            TotalRequested: 3,
            SuccessfulCount: 2,
            FailedCount: 1,
            ReclaimedBytes: 1048576,
            Failures: [(@"C:\repos\locked", "File is locked")]
        );

        var entry = await logger.LogPurgeAsync(
            report,
            sendToRecycleBin: true,
            isDryRun: false,
            targetRoots: [@"C:\repos"],
            purgedPaths: [@"C:\repos\p1\bin", @"C:\repos\p2\obj"],
            customLogPath: _testLogFile
        );

        Assert.NotNull(entry);
        Assert.Equal("Purge", entry.Action);
        Assert.Equal("RecycleBin", entry.Mode);
        Assert.False(entry.IsDryRun);
        Assert.Equal(3, entry.TotalRequested);
        Assert.Equal(2, entry.SuccessfulCount);
        Assert.Equal(1, entry.FailedCount);
        Assert.Equal(1048576, entry.ReclaimedBytes);
        Assert.Single(entry.Failures);
        Assert.Equal(@"C:\repos\locked", entry.Failures[0].Path);

        // Read entries back
        var entries = await logger.GetRecentEntriesAsync(10, _testLogFile);
        Assert.Single(entries);
        Assert.Equal(entry.Id, entries[0].Id);
        Assert.Equal("Purge", entries[0].Action);
        Assert.Equal(2, entries[0].PurgedPaths.Count);
    }

    [Fact]
    public async Task LogDryRunAsync_WritesAndReadsDryRunEntry()
    {
        var logger = new AuditLogger(_testLogFile);

        var dryRunItems = new List<DryRunItem>
        {
            new(@"C:\repos\p1\bin", "bin", ".NET / C#", 2048, "2.00 KB", DryRunStatus.Eligible, null),
            new(@"C:\repos\unsafe", "src", "Custom", 1024, "1.00 KB", DryRunStatus.Unsafe, "Folder name not allowed")
        };

        var report = new DryRunReport(
            TotalEvaluated: 2,
            EligibleCount: 1,
            UnsafeCount: 1,
            LockedCount: 0,
            EligibleBytes: 2048,
            Items: dryRunItems
        );

        var entry = await logger.LogDryRunAsync(report, [@"C:\repos"], _testLogFile);

        Assert.True(entry.IsDryRun);
        Assert.Equal("DryRun", entry.Action);
        Assert.Equal(1, entry.SuccessfulCount);
        Assert.Equal(1, entry.FailedCount);
        Assert.Equal(2048, entry.ReclaimedBytes);

        var history = await logger.GetRecentEntriesAsync(10, _testLogFile);
        Assert.Single(history);
        Assert.True(history[0].IsDryRun);
    }

    [Fact]
    public async Task GetRecentEntriesAsync_ReturnsEmptyWhenFileDoesNotExist()
    {
        var nonExistentPath = Path.Combine(_testLogDir, "missing.jsonl");
        var logger = new AuditLogger(nonExistentPath);

        var entries = await logger.GetRecentEntriesAsync(10, nonExistentPath);
        Assert.Empty(entries);
    }

    [Fact]
    public void GetDefaultLogPath_ReturnsValidPath()
    {
        var defaultPath = AuditLogger.GetDefaultLogPath();
        Assert.False(string.IsNullOrWhiteSpace(defaultPath));
        Assert.EndsWith("purge-audit.jsonl", defaultPath);
    }
}
