using DevPurge.Core.Models;
using DevPurge.Core.Scanning;

namespace DevPurge.Core.Tests;

public class FastDirectoryScannerTests : IDisposable
{
    private readonly string _testRoot;

    public FastDirectoryScannerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "devpurge_scanner_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task ScanAsync_DiscoversTargetArtifacts()
    {
        // Setup folder structure
        var projectDir = Path.Combine(_testRoot, "MyProject");
        var nodeModules = Path.Combine(projectDir, "node_modules");
        var binDir = Path.Combine(projectDir, "bin");
        var srcDir = Path.Combine(projectDir, "src");

        Directory.CreateDirectory(nodeModules);
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(srcDir);

        File.WriteAllText(Path.Combine(nodeModules, "package.txt"), "dummy dependency data");
        File.WriteAllText(Path.Combine(binDir, "app.dll"), "binary content");
        File.WriteAllText(Path.Combine(srcDir, "Program.cs"), "Console.WriteLine();");

        var scanner = new FastDirectoryScanner();
        var results = await scanner.ScanAsync([_testRoot]);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.FolderName == "node_modules" && r.ArtifactType == ArtifactType.NodeModules);
        Assert.Contains(results, r => r.FolderName == "bin" && r.ArtifactType == ArtifactType.DotNetBuild);
        Assert.DoesNotContain(results, r => r.FolderName == "src");
    }

    [Fact]
    public async Task ScanAsync_PrunesSubdirectoriesInsideDiscoveredArtifact()
    {
        // When node_modules is found, inner subdirectories (like node_modules/package/node_modules) should NOT be returned as separate items
        var rootNodeModules = Path.Combine(_testRoot, "Project", "node_modules");
        var nestedNodeModules = Path.Combine(rootNodeModules, "subpackage", "node_modules");

        Directory.CreateDirectory(nestedNodeModules);
        File.WriteAllText(Path.Combine(rootNodeModules, "a.js"), "content");
        File.WriteAllText(Path.Combine(nestedNodeModules, "b.js"), "content");

        var scanner = new FastDirectoryScanner();
        var results = await scanner.ScanAsync([_testRoot]);

        Assert.Single(results);
        Assert.Equal(Path.GetFullPath(rootNodeModules), Path.GetFullPath(results[0].Path));
    }

    [Fact]
    public async Task ScanAsync_IgnoresGitDirectories()
    {
        var gitDir = Path.Combine(_testRoot, "Repo", ".git");
        var gitHooks = Path.Combine(gitDir, "hooks");
        var binInsideGit = Path.Combine(gitDir, "bin"); // Should never be scanned!

        Directory.CreateDirectory(gitHooks);
        Directory.CreateDirectory(binInsideGit);

        var scanner = new FastDirectoryScanner();
        var results = await scanner.ScanAsync([_testRoot]);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanAsync_ReportsProgress()
    {
        var project = Path.Combine(_testRoot, "App", "target");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "output.bin"), "12345678");

        var progressUpdates = new List<ScanProgress>();
        var progress = new Progress<ScanProgress>(p => progressUpdates.Add(p));

        var scanner = new FastDirectoryScanner();
        var results = await scanner.ScanAsync([_testRoot], progress);

        Assert.Single(results);
        await Task.Delay(50);
        Assert.NotEmpty(progressUpdates);
        Assert.Contains(progressUpdates, p => p.IsCompleted);
    }

    [Fact]
    public async Task ScanAsync_RespectsCancellationToken()
    {
        for (int i = 0; i < 50; i++)
        {
            var dir = Path.Combine(_testRoot, $"Project_{i}", "bin");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "out.dll"), "test");
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var scanner = new FastDirectoryScanner();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scanner.ScanAsync([_testRoot], cancellationToken: cts.Token));
    }

    [Fact]
    public void CalculateDirectoryStats_ComputesAccurateSizeAndCount()
    {
        var targetDir = Path.Combine(_testRoot, "StatsTest");
        var subDir = Path.Combine(targetDir, "Sub");
        Directory.CreateDirectory(subDir);

        var file1 = Path.Combine(targetDir, "f1.dat");
        var file2 = Path.Combine(subDir, "f2.dat");

        File.WriteAllBytes(file1, new byte[1000]);
        File.WriteAllBytes(file2, new byte[2500]);

        var (totalBytes, fileCount, lastModified) = FastDirectoryScanner.CalculateDirectoryStats(targetDir);

        Assert.Equal(3500, totalBytes);
        Assert.Equal(2, fileCount);
        Assert.True(lastModified > DateTime.MinValue);
    }

    [Fact]
    public async Task ScanAsync_SingleRootWithMultipleProjects_DiscoversAllConcurrently()
    {
        for (int i = 0; i < 5; i++)
        {
            var proj = Path.Combine(_testRoot, $"MultiProject_{i}");
            var bin = Path.Combine(proj, "bin");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, "app.dll"), "12345");
        }

        var scanner = new FastDirectoryScanner();
        var results = await scanner.ScanAsync([_testRoot]);

        Assert.Equal(5, results.Count);
        Assert.All(results, r => Assert.Equal("bin", r.FolderName));
    }

    [Fact]
    public async Task ScanAsync_RespectsExclusions()
    {
        var projA = Path.Combine(_testRoot, "ProjectA", "node_modules");
        var projB = Path.Combine(_testRoot, "ProjectB", "node_modules");
        var projC = Path.Combine(_testRoot, "ProjectC", "bin");

        Directory.CreateDirectory(projA);
        Directory.CreateDirectory(projB);
        Directory.CreateDirectory(projC);

        File.WriteAllText(Path.Combine(projA, "pkg.json"), "{}");
        File.WriteAllText(Path.Combine(projB, "pkg.json"), "{}");
        File.WriteAllText(Path.Combine(projC, "app.dll"), "dll");

        var scanner = new FastDirectoryScanner();
        var results = await scanner.ScanAsync([_testRoot], exclusions: ["ProjectB"]);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Path.Contains("ProjectA"));
        Assert.Contains(results, r => r.Path.Contains("ProjectC"));
        Assert.DoesNotContain(results, r => r.Path.Contains("ProjectB"));
    }

    [Fact]
    public void CalculateDirectoryStats_NonExistentDirectory_ReturnsZero()
    {
        var nonExistent = Path.Combine(_testRoot, "does_not_exist");
        var (totalBytes, fileCount, _) = FastDirectoryScanner.CalculateDirectoryStats(nonExistent);

        Assert.Equal(0, totalBytes);
        Assert.Equal(0, fileCount);
    }
}
