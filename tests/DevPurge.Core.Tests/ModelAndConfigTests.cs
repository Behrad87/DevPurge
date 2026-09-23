using DevPurge.Core.Models;

namespace DevPurge.Core.Tests;

public class ModelAndConfigTests
{
    [Theory]
    [InlineData(500, "500.00 B")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1024 * 1024 * 5, "5.00 MB")]
    [InlineData(1024L * 1024 * 1024 * 3, "3.00 GB")]
    public void ByteFormatting_CalculatesCorrectSuffixes(long bytes, string expected)
    {
        var formatted = DiscoveredFolder.FormatByteSize(bytes);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void DefaultRules_CoverStandardEcosystems()
    {
        var rules = PurgeRule.GetDefaultRules();

        Assert.Contains(rules, r => r.FolderNames.Contains("node_modules"));
        Assert.Contains(rules, r => r.FolderNames.Contains("bin"));
        Assert.Contains(rules, r => r.FolderNames.Contains("target"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".gradle"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".venv"));
    }

    [Fact]
    public void DiscoveredFolder_FormattedAge_FormatsCorrectly()
    {
        var todayFolder = new DiscoveredFolder
        {
            Path = @"C:\repos\test\bin",
            FolderName = "bin",
            ArtifactType = ArtifactType.DotNetBuild,
            CategoryName = ".NET / C#",
            LastModifiedUtc = DateTime.UtcNow,
            SizeBytes = 1024
        };
        Assert.Equal("Today", todayFolder.FormattedAge);

        var monthOldFolder = new DiscoveredFolder
        {
            Path = @"C:\repos\test\node_modules",
            FolderName = "node_modules",
            ArtifactType = ArtifactType.NodeModules,
            CategoryName = "JavaScript / Node.js",
            LastModifiedUtc = DateTime.UtcNow.AddDays(-35),
            SizeBytes = 1024 * 1024
        };
        Assert.Equal("1 month ago", monthOldFolder.FormattedAge);
    }
}
