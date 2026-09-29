using DevPurge.Core.TreeSize;

namespace DevPurge.Core.Tests;

public class TreeSizeScannerTests : IDisposable
{
    private readonly string _testRoot;

    public TreeSizeScannerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "DevPurge_TreeSizeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task ScanTreeAsync_NonExistentPath_ReturnsNull()
    {
        var scanner = new TreeSizeScanner();
        var result = await scanner.ScanTreeAsync(Path.Combine(_testRoot, "does_not_exist"));
        Assert.Null(result);
    }

    [Fact]
    public async Task ScanTreeAsync_EmptyDirectory_ReturnsRootWithZeroSize()
    {
        var scanner = new TreeSizeScanner();
        var result = await scanner.ScanTreeAsync(_testRoot);

        Assert.NotNull(result);
        Assert.Equal(0, result.SizeBytes);
        Assert.Equal(0, result.FileCount);
        Assert.Equal(0, result.DirectoryCount);
        Assert.Empty(result.Children);
        Assert.Equal(100.0, result.PercentOfParent);
    }

    [Fact]
    public async Task ScanTreeAsync_ComputesHierarchicalSizesAndPercentages()
    {
        // Setup folder hierarchy:
        // root/
        //   file_root.txt (1000 bytes)
        //   SubA/
        //     file_a.txt (3000 bytes)
        //     node_modules/
        //       mod.js (6000 bytes)
        //   SubB/
        //     file_b.txt (2000 bytes)

        File.WriteAllBytes(Path.Combine(_testRoot, "file_root.txt"), new byte[1000]);

        var subA = Path.Combine(_testRoot, "SubA");
        Directory.CreateDirectory(subA);
        File.WriteAllBytes(Path.Combine(subA, "file_a.txt"), new byte[3000]);

        var nodeModules = Path.Combine(subA, "node_modules");
        Directory.CreateDirectory(nodeModules);
        File.WriteAllBytes(Path.Combine(nodeModules, "mod.js"), new byte[6000]);

        var subB = Path.Combine(_testRoot, "SubB");
        Directory.CreateDirectory(subB);
        File.WriteAllBytes(Path.Combine(subB, "file_b.txt"), new byte[2000]);

        var scanner = new TreeSizeScanner();
        var root = await scanner.ScanTreeAsync(_testRoot);

        Assert.NotNull(root);
        // Total bytes = 1000 + 3000 + 6000 + 2000 = 12000
        Assert.Equal(12000, root.SizeBytes);
        Assert.Equal(4, root.FileCount);
        Assert.Equal(3, root.DirectoryCount);
        Assert.Equal(2, root.Children.Count);

        // SubA is 9000 bytes, SubB is 2000 bytes -> SubA should be first child
        var childSubA = root.Children[0];
        var childSubB = root.Children[1];

        Assert.Equal("SubA", childSubA.Name);
        Assert.Equal(9000, childSubA.SizeBytes);
        Assert.Equal(2, childSubA.FileCount);
        Assert.Equal(1, childSubA.DirectoryCount);
        Assert.Equal(75.0, childSubA.PercentOfParent); // 9000 / 12000 = 75%
        Assert.Equal(75.0, childSubA.PercentOfRoot);

        Assert.Equal("SubB", childSubB.Name);
        Assert.Equal(2000, childSubB.SizeBytes);
        Assert.Equal(1, childSubB.FileCount);
        Assert.Equal(0, childSubB.DirectoryCount);
        Assert.True(Math.Abs(childSubB.PercentOfParent - (2000.0 / 12000.0 * 100.0)) < 0.1);

        // SubA children should contain node_modules and mark it as artifact
        Assert.Single(childSubA.Children);
        var modNode = childSubA.Children[0];
        Assert.Equal("node_modules", modNode.Name);
        Assert.Equal(6000, modNode.SizeBytes);
        Assert.True(modNode.IsArtifact);
        Assert.NotNull(modNode.ArtifactCategory);
        Assert.True(Math.Abs(modNode.PercentOfParent - (6000.0 / 9000.0 * 100.0)) < 0.1);
        Assert.Equal(50.0, modNode.PercentOfRoot); // 6000 / 12000 = 50%
    }

    [Fact]
    public async Task ScanTreeAsync_RespectsCancellation()
    {
        for (int i = 0; i < 20; i++)
        {
            var dir = Path.Combine(_testRoot, $"dir_{i}");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "data.bin"), new byte[500]);
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var scanner = new TreeSizeScanner();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await scanner.ScanTreeAsync(_testRoot, cancellationToken: cts.Token);
        });
    }

    [Fact]
    public async Task TreeSizeExporter_ExportsCsvJsonAndTextTree()
    {
        var subDir = Path.Combine(_testRoot, "test_sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllBytes(Path.Combine(subDir, "file.bin"), new byte[2048]);

        var scanner = new TreeSizeScanner();
        var root = await scanner.ScanTreeAsync(_testRoot);
        Assert.NotNull(root);

        var csv = TreeSizeExporter.ToCsv(root);
        Assert.Contains("Path,Name,SizeBytes", csv);
        Assert.Contains("test_sub", csv);

        var json = TreeSizeExporter.ToJson(root);
        Assert.Contains("\"SizeBytes\": 2048", json);
        Assert.Contains("test_sub", json);

        var textTree = TreeSizeExporter.ToTextTree(root);
        Assert.Contains("test_sub", textTree);
        Assert.Contains("2.00 KB", textTree);
    }
}
