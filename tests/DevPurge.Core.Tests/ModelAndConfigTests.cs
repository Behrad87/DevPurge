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
}
