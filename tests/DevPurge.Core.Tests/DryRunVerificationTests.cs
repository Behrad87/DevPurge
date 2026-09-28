using DevPurge.Core.Models;
using DevPurge.Core.Purging;

namespace DevPurge.Core.Tests;

public class DryRunVerificationTests : IDisposable
{
    private readonly string _testRoot;

    public DryRunVerificationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "devpurge_dryrun_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                PurgeService.ForceDeleteDirectory(_testRoot);
            }
        }
        catch { }
    }

    [Fact]
    public async Task SimulatePurgeAsync_ClassifiesEligibleFolderCorrectly()
    {
        var binPath = Path.Combine(_testRoot, "ProjectA", "bin");
        Directory.CreateDirectory(binPath);
        File.WriteAllText(Path.Combine(binPath, "output.dll"), "dll data");

        var folder = new DiscoveredFolder
        {
            Path = binPath,
            FolderName = "bin",
            ArtifactType = ArtifactType.DotNetBuild,
            CategoryName = ".NET / C#",
            SizeBytes = 8,
            FileCount = 1,
            LastModifiedUtc = DateTime.UtcNow
        };

        var service = new PurgeService();
        var report = await service.SimulatePurgeAsync([folder]);

        Assert.Equal(1, report.TotalEvaluated);
        Assert.Equal(1, report.EligibleCount);
        Assert.Equal(0, report.UnsafeCount);
        Assert.Equal(0, report.LockedCount);
        Assert.Equal(8, report.EligibleBytes);
        Assert.True(report.AllEligible);
        Assert.Equal(DryRunStatus.Eligible, report.Items[0].Status);
        Assert.Null(report.Items[0].Reason);

        // Dry-run MUST NOT delete the directory
        Assert.True(Directory.Exists(binPath));
    }

    [Fact]
    public async Task SimulatePurgeAsync_FlagsUnsafeFolder()
    {
        var srcPath = Path.Combine(_testRoot, "ProjectA", "src");
        Directory.CreateDirectory(srcPath);

        var folder = new DiscoveredFolder
        {
            Path = srcPath,
            FolderName = "src",
            ArtifactType = ArtifactType.Custom,
            CategoryName = "Source",
            SizeBytes = 100,
            FileCount = 2,
            LastModifiedUtc = DateTime.UtcNow
        };

        var service = new PurgeService();
        var report = await service.SimulatePurgeAsync([folder]);

        Assert.Equal(1, report.TotalEvaluated);
        Assert.Equal(0, report.EligibleCount);
        Assert.Equal(1, report.UnsafeCount);
        Assert.False(report.AllEligible);
        Assert.Equal(DryRunStatus.Unsafe, report.Items[0].Status);
        Assert.NotNull(report.Items[0].Reason);
    }

    [Fact]
    public async Task SimulatePurgeAsync_FlagsMissingFolderAsNotFound()
    {
        var missingPath = Path.Combine(_testRoot, "NonExistent", "bin");

        var folder = new DiscoveredFolder
        {
            Path = missingPath,
            FolderName = "bin",
            ArtifactType = ArtifactType.DotNetBuild,
            CategoryName = ".NET / C#",
            SizeBytes = 0,
            FileCount = 0,
            LastModifiedUtc = DateTime.UtcNow
        };

        var service = new PurgeService();
        var report = await service.SimulatePurgeAsync([folder]);

        Assert.Equal(1, report.TotalEvaluated);
        Assert.Equal(0, report.EligibleCount);
        Assert.Equal(DryRunStatus.NotFound, report.Items[0].Status);
    }

    [Fact]
    public async Task SimulatePurgeAsync_FlagsLockedFolder()
    {
        var binPath = Path.Combine(_testRoot, "ProjectLocked", "bin");
        Directory.CreateDirectory(binPath);
        var lockedFile = Path.Combine(binPath, "app.dll");
        File.WriteAllText(lockedFile, "binary content");

        // Hold exclusive lock on the file
        using var stream = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var folder = new DiscoveredFolder
        {
            Path = binPath,
            FolderName = "bin",
            ArtifactType = ArtifactType.DotNetBuild,
            CategoryName = ".NET / C#",
            SizeBytes = 14,
            FileCount = 1,
            LastModifiedUtc = DateTime.UtcNow
        };

        var service = new PurgeService();
        var report = await service.SimulatePurgeAsync([folder]);

        Assert.Equal(1, report.TotalEvaluated);
        Assert.Equal(0, report.EligibleCount);
        Assert.Equal(1, report.LockedCount);
        Assert.Equal(DryRunStatus.LockedOrInaccessible, report.Items[0].Status);
        Assert.NotNull(report.Items[0].Reason);
        Assert.Contains("locked", report.Items[0].Reason, StringComparison.OrdinalIgnoreCase);
    }
}
