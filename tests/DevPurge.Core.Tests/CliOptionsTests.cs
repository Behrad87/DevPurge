using DevPurge.Cli;

namespace DevPurge.Core.Tests;

public class CliOptionsTests
{
    [Fact]
    public void ParseCommandLine_DefaultValues_AreCorrect()
    {
        var options = Program.ParseCommandLine([]);

        Assert.False(options.Clean);
        Assert.False(options.DryRun);
        Assert.False(options.Permanent);
        Assert.False(options.Silent);
        Assert.False(options.Json);
        Assert.False(options.ShowHelp);
        Assert.False(options.ShowVersion);
        Assert.Equal(0, options.MinAgeDays);
        Assert.Empty(options.Paths);
    }

    [Fact]
    public void ParseCommandLine_ParsesAllFlagsCorrectly()
    {
        string[] args =
        [
            "--path", @"C:\repos",
            "--clean",
            "--min-age", "14",
            "--permanent",
            "--json",
            "--silent"
        ];

        var options = Program.ParseCommandLine(args);

        Assert.Contains(@"C:\repos", options.Paths);
        Assert.True(options.Clean);
        Assert.Equal(14, options.MinAgeDays);
        Assert.True(options.Permanent);
        Assert.True(options.Json);
        Assert.True(options.Silent);
    }

    [Fact]
    public void ParseCommandLine_ParsesMultiplePaths()
    {
        string[] args =
        [
            "--path", @"C:\repos",
            "--path", @"D:\work"
        ];

        var options = Program.ParseCommandLine(args);

        Assert.Equal(2, options.Paths.Count);
        Assert.Contains(@"C:\repos", options.Paths);
        Assert.Contains(@"D:\work", options.Paths);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void ParseCommandLine_ParsesHelpFlags(string helpFlag)
    {
        var options = Program.ParseCommandLine([helpFlag]);
        Assert.True(options.ShowHelp);
    }

    [Theory]
    [InlineData("-v")]
    [InlineData("--version")]
    public void ParseCommandLine_ParsesVersionFlags(string versionFlag)
    {
        var options = Program.ParseCommandLine([versionFlag]);
        Assert.True(options.ShowVersion);
    }

    [Fact]
    public void ParseCommandLine_DryRunFlag_OverridesClean()
    {
        var options = Program.ParseCommandLine(["--clean", "--dry-run"]);
        Assert.True(options.Clean);
        Assert.True(options.DryRun);
    }

    [Fact]
    public void ParseCommandLine_SupportsPositionalPaths()
    {
        var options = Program.ParseCommandLine([@"C:\repos", @"D:\work", "--clean"]);
        Assert.Equal(2, options.Paths.Count);
        Assert.Contains(@"C:\repos", options.Paths);
        Assert.Contains(@"D:\work", options.Paths);
        Assert.True(options.Clean);
    }

    [Fact]
    public void ParseCommandLine_SupportsShortAliases()
    {
        var options = Program.ParseCommandLine(["-p", @"C:\repos", "-c", "-m", "7", "-s", "-j"]);
        Assert.Contains(@"C:\repos", options.Paths);
        Assert.True(options.Clean);
        Assert.Equal(7, options.MinAgeDays);
        Assert.True(options.Silent);
        Assert.True(options.Json);
    }

    [Fact]
    public void ParseCommandLine_SupportsEqualsSyntax()
    {
        var options = Program.ParseCommandLine(["--path=C:\\repos", "--min-age=21"]);
        Assert.Contains(@"C:\repos", options.Paths);
        Assert.Equal(21, options.MinAgeDays);
    }

    [Fact]
    public void ParseCommandLine_FlagsFollowedByShortFlagsOrPaths_AreParsedCorrectly()
    {
        var options = Program.ParseCommandLine(["--clean", "-v"]);
        Assert.True(options.Clean);
        Assert.True(options.ShowVersion);

        var options2 = Program.ParseCommandLine(["--clean", @"C:\repos"]);
        Assert.True(options2.Clean);
        Assert.Contains(@"C:\repos", options2.Paths);
    }

    [Theory]
    [InlineData("--type", "node")]
    [InlineData("-t", "dotnet")]
    [InlineData("--category", "rust")]
    public void ParseCommandLine_ParsesFilterType(string flag, string expected)
    {
        var options = Program.ParseCommandLine([flag, expected]);
        Assert.Equal(expected, options.FilterType);
    }

    [Fact]
    public void ParseCommandLine_ParsesExclusions()
    {
        var options = Program.ParseCommandLine([
            "--exclude", "legacy-app",
            "-x", "important-dir",
            "--ignore=test-cache,scratch"
        ]);

        Assert.Equal(4, options.Exclusions.Count);
        Assert.Contains("legacy-app", options.Exclusions);
        Assert.Contains("important-dir", options.Exclusions);
        Assert.Contains("test-cache", options.Exclusions);
        Assert.Contains("scratch", options.Exclusions);
    }
}
