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

    [Theory]
    [InlineData("--min-size", "500MB", 500L * 1024 * 1024)]
    [InlineData("-z", "1.5GB", (long)(1.5 * 1024 * 1024 * 1024))]
    [InlineData("--minsize", "100KB", 100L * 1024)]
    [InlineData("--size", "1048576", 1048576L)]
    public void ParseCommandLine_ParsesMinSizeFlag(string flag, string value, long expectedBytes)
    {
        var options = Program.ParseCommandLine([flag, value]);
        Assert.Equal(expectedBytes, options.MinSizeBytes);
    }

    [Fact]
    public void ParseCommandLine_SupportsMinSizeEqualsSyntax()
    {
        var options = Program.ParseCommandLine(["--min-size=250MB"]);
        Assert.Equal(250L * 1024 * 1024, options.MinSizeBytes);
    }

    [Theory]
    [InlineData("--top", "10", 10)]
    [InlineData("--limit", "25", 25)]
    public void ParseCommandLine_ParsesTopCountFlag(string flag, string value, int expectedCount)
    {
        var options = Program.ParseCommandLine([flag, value]);
        Assert.Equal(expectedCount, options.TopCount);
    }

    [Fact]
    public void ParseCommandLine_SupportsTopEqualsSyntax()
    {
        var options = Program.ParseCommandLine(["--top=15"]);
        Assert.Equal(15, options.TopCount);
    }

    [Theory]
    [InlineData("-y")]
    [InlineData("--yes")]
    public void ParseCommandLine_ParsesYesFlag(string flag)
    {
        var options = Program.ParseCommandLine([flag]);
        Assert.True(options.Yes);
        Assert.True(options.Clean);
    }

    [Theory]
    [InlineData("500B", 500L)]
    [InlineData("500 B", 500L)]
    [InlineData("10KB", 10L * 1024)]
    [InlineData("10K", 10L * 1024)]
    [InlineData("10 KB", 10L * 1024)]
    [InlineData("500MB", 500L * 1024 * 1024)]
    [InlineData("500M", 500L * 1024 * 1024)]
    [InlineData("1.5GB", (long)(1.5 * 1024 * 1024 * 1024))]
    [InlineData("2G", 2L * 1024 * 1024 * 1024)]
    [InlineData("1TB", 1024L * 1024 * 1024 * 1024)]
    [InlineData("1T", 1024L * 1024 * 1024 * 1024)]
    [InlineData("1048576", 1048576L)]
    public void TryParseByteSize_ValidInputs_ReturnsExpectedBytes(string input, long expectedBytes)
    {
        var success = Program.TryParseByteSize(input, out var bytes);
        Assert.True(success);
        Assert.Equal(expectedBytes, bytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("-100MB")]
    public void TryParseByteSize_InvalidInputs_ReturnsFalse(string? input)
    {
        var success = Program.TryParseByteSize(input, out var bytes);
        Assert.False(success);
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void ParseCommandLine_ParsesAuditLogPath()
    {
        var options = Program.ParseCommandLine(["--audit-log", @"C:\temp\custom-audit.jsonl"]);
        Assert.Equal(@"C:\temp\custom-audit.jsonl", options.AuditLogPath);
    }

    [Theory]
    [InlineData("--no-audit")]
    [InlineData("--noaudit")]
    public void ParseCommandLine_ParsesNoAuditFlag(string flag)
    {
        var options = Program.ParseCommandLine([flag]);
        Assert.True(options.NoAudit);
    }

    [Theory]
    [InlineData("--audit")]
    [InlineData("--audit-history")]
    [InlineData("--history")]
    public void ParseCommandLine_ParsesAuditHistoryFlags(string flag)
    {
        var options = Program.ParseCommandLine([flag]);
        Assert.True(options.ShowAuditHistory);
    }

    [Theory]
    [InlineData("--verify")]
    [InlineData("--verify-dry-run")]
    public void ParseCommandLine_ParsesVerifyFlag(string flag)
    {
        var options = Program.ParseCommandLine([flag]);
        Assert.True(options.VerifyDryRun);
    }

    [Theory]
    [InlineData("--export", @"C:\temp\report.md")]
    [InlineData("-e", @"C:\temp\report.csv")]
    [InlineData("--output", @"C:\temp\report.json")]
    [InlineData("--out", "summary.md")]
    public void ParseCommandLine_ParsesExportFlags(string flag, string path)
    {
        var options = Program.ParseCommandLine([flag, path]);
        Assert.Equal(path, options.ExportPath);
    }

    [Theory]
    [InlineData("--no-config")]
    [InlineData("--noconfig")]
    public void ParseCommandLine_ParsesNoConfigFlag(string flag)
    {
        var options = Program.ParseCommandLine([flag]);
        Assert.True(options.NoConfig);
    }

    [Fact]
    public void ParseCommandLine_ParsesSemicolonDelimitedExclusions()
    {
        var options = Program.ParseCommandLine(["--exclude", "dir1;dir2,dir3"]);
        Assert.Equal(3, options.Exclusions.Count);
        Assert.Contains("dir1", options.Exclusions);
        Assert.Contains("dir2", options.Exclusions);
        Assert.Contains("dir3", options.Exclusions);
    }

    [Fact]
    public void ResolveTargetPaths_SupportsCommaAndSemicolonSeparators()
    {
        var temp1 = Path.Combine(Path.GetTempPath(), "devpurge_resolve_1_" + Guid.NewGuid().ToString("N"));
        var temp2 = Path.Combine(Path.GetTempPath(), "devpurge_resolve_2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp1);
        Directory.CreateDirectory(temp2);

        try
        {
            var resolved = Program.ResolveTargetPaths([$"{temp1};{temp2}"]);
            Assert.Equal(2, resolved.Count);
            Assert.Contains(Path.GetFullPath(temp1), resolved);
            Assert.Contains(Path.GetFullPath(temp2), resolved);
        }
        finally
        {
            if (Directory.Exists(temp1)) Directory.Delete(temp1);
            if (Directory.Exists(temp2)) Directory.Delete(temp2);
        }
    }

    [Fact]
    public void ResolveTargetPaths_FiltersNonExistentPaths_WithoutFallback()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "devpurge_nonexistent_" + Guid.NewGuid().ToString("N"));
        var resolved = Program.ResolveTargetPaths([nonExistent]);
        Assert.Empty(resolved);
    }

    [Fact]
    public void ResolveTargetPaths_WhenEmpty_AutoDetectsCandidates()
    {
        var resolved = Program.ResolveTargetPaths([]);
        Assert.NotEmpty(resolved);
        Assert.All(resolved, p => Assert.True(Directory.Exists(p)));
    }
}


