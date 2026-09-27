using DevPurge.Core.Models;
using DevPurge.Core.Purging;

namespace DevPurge.Core.Tests;

public class PurgeServiceTests : IDisposable
{
    private readonly string _testRoot;

    public PurgeServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "devpurge_purge_tests_" + Guid.NewGuid().ToString("N"));
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
        catch
        {
            // Ignore
        }
    }

    [Fact]
    public async Task PurgeAsync_DeletesFoldersPermanently()
    {
        var binPath = Path.Combine(_testRoot, "Project1", "bin");
        Directory.CreateDirectory(binPath);
        File.WriteAllText(Path.Combine(binPath, "file.dll"), "12345");

        var folder = new DiscoveredFolder
        {
            Path = binPath,
            FolderName = "bin",
            ArtifactType = ArtifactType.DotNetBuild,
            CategoryName = ".NET / C#",
            SizeBytes = 5,
            FileCount = 1,
            LastModifiedUtc = DateTime.UtcNow
        };

        var service = new PurgeService();
        var report = await service.PurgeAsync([folder], sendToRecycleBin: false);

        Assert.Equal(1, report.TotalRequested);
        Assert.Equal(1, report.SuccessfulCount);
        Assert.Equal(0, report.FailedCount);
        Assert.Equal(5, report.ReclaimedBytes);
        Assert.False(Directory.Exists(binPath));
    }

    [Fact]
    public void ForceDeleteDirectory_DeletesReadOnlyFilesAndFolders()
    {
        var targetDir = Path.Combine(_testRoot, "ReadOnlyTest");
        var subDir = Path.Combine(targetDir, "Sub");
        Directory.CreateDirectory(subDir);

        var filePath = Path.Combine(subDir, "locked.txt");
        File.WriteAllText(filePath, "read only content");

        // Mark file and directory read-only
        File.SetAttributes(filePath, FileAttributes.ReadOnly);
        var di = new DirectoryInfo(targetDir);
        di.Attributes |= FileAttributes.ReadOnly;

        PurgeService.ForceDeleteDirectory(targetDir);

        Assert.False(Directory.Exists(targetDir));
    }

    [Fact]
    public async Task PurgeAsync_RejectsUnsafeFoldersAndReportsFailures()
    {
        // Try to purge an disallowed folder name
        var unsafeDir = Path.Combine(_testRoot, "src");
        Directory.CreateDirectory(unsafeDir);

        var folder = new DiscoveredFolder
        {
            Path = unsafeDir,
            FolderName = "src",
            ArtifactType = ArtifactType.Custom,
            CategoryName = "Source",
            SizeBytes = 100,
            FileCount = 1,
            LastModifiedUtc = DateTime.UtcNow
        };

        var service = new PurgeService();
        var report = await service.PurgeAsync([folder], sendToRecycleBin: false);

        Assert.Equal(1, report.TotalRequested);
        Assert.Equal(0, report.SuccessfulCount);
        Assert.Equal(1, report.FailedCount);
        Assert.Single(report.Failures);
        Assert.True(Directory.Exists(unsafeDir)); // Safe: must NOT be deleted!
    }

    [Fact]
    public async Task PurgeAsync_ReportsProgressDuringDeletion()
    {
        var progressList = new List<(string Path, int Completed, int Total)>();
        var progress = new Progress<(string Path, int Completed, int Total)>(p => progressList.Add(p));

        var folderList = new List<DiscoveredFolder>();
        for (int i = 0; i < 3; i++)
        {
            var p = Path.Combine(_testRoot, $"Proj_{i}", "bin");
            Directory.CreateDirectory(p);
            File.WriteAllText(Path.Combine(p, "test.txt"), "data");

            folderList.Add(new DiscoveredFolder
            {
                Path = p,
                FolderName = "bin",
                ArtifactType = ArtifactType.DotNetBuild,
                CategoryName = ".NET / C#",
                SizeBytes = 4,
                FileCount = 1,
                LastModifiedUtc = DateTime.UtcNow
            });
        }

        var service = new PurgeService();
        var report = await service.PurgeAsync(folderList, sendToRecycleBin: false, progress: progress);

        Assert.Equal(3, report.SuccessfulCount);
        Assert.NotEmpty(progressList);
    }
}
