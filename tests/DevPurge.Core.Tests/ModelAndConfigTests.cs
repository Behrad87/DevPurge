using DevPurge.Core.Models;

namespace DevPurge.Core.Tests;

public class ModelAndConfigTests
{
    [Theory]
    [InlineData(500, "500.00 B")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1024 * 1024 * 5, "5.00 MB")]
    [InlineData(1024L * 1024 * 1024 * 3, "3.00 GB")]
    [InlineData(0, "0.00 B")]
    [InlineData(-100, "0 B")]
    [InlineData(1024L * 1024 * 1024 * 1024 * 2, "2.00 TB")]
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
        Assert.Contains(rules, r => r.FolderNames.Contains(".dart_tool"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".idea"));
        Assert.Contains(rules, r => r.FolderNames.Contains("cmake-build-debug"));
        Assert.Contains(rules, r => r.FolderNames.Contains("zig-cache"));
        Assert.Contains(rules, r => r.FolderNames.Contains("DerivedData"));
        Assert.Contains(rules, r => r.FolderNames.Contains("_build"));
        Assert.Contains(rules, r => r.FolderNames.Contains("BenchmarkDotNet.Artifacts"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".ruff_cache"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".angular"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".vite"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".nx"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".docusaurus"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".kotlin"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".nox"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".hypothesis"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".fleet"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".uv_cache"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".uv"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".pixi"));
        Assert.Contains(rules, r => r.FolderNames.Contains("__pypackages__"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".rollup.cache"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".swc"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".rspack-cache"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".nyc_output"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".gocache"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".bloop"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".metals"));
        Assert.Contains(rules, r => r.FolderNames.Contains("cmake-build-relwithdebinfo"));
        Assert.Contains(rules, r => r.FolderNames.Contains("cmake-build-minsizerel"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".build"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".bundle"));
        Assert.Contains(rules, r => r.FolderNames.Contains("dist-newstyle"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".stack-work"));
        Assert.Contains(rules, r => r.FolderNames.Contains(".terraform"));
    }

    [Fact]
    public void CreateCustomRule_InitializesPropertiesCorrectly()
    {
        var custom = PurgeRule.CreateCustomRule("Unity Temp", ["Temp", "Library"], "Unity", ArtifactType.Custom, "Unity temp cache");

        Assert.Equal("Unity Temp", custom.Name);
        Assert.Equal(ArtifactType.Custom, custom.ArtifactType);
        Assert.Equal("Unity", custom.CategoryName);
        Assert.Contains("Temp", custom.FolderNames);
        Assert.Contains("Library", custom.FolderNames);
        Assert.True(custom.IsEnabled);
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

        var oneDayOld = new DiscoveredFolder
        {
            Path = @"C:\repos\test\obj",
            FolderName = "obj",
            ArtifactType = ArtifactType.DotNetBuild,
            CategoryName = ".NET / C#",
            LastModifiedUtc = DateTime.UtcNow.AddDays(-1.2),
            SizeBytes = 1024
        };
        Assert.Equal("1 day ago", oneDayOld.FormattedAge);

        var tenDaysOld = new DiscoveredFolder
        {
            Path = @"C:\repos\test\target",
            FolderName = "target",
            ArtifactType = ArtifactType.RustTarget,
            CategoryName = "Rust",
            LastModifiedUtc = DateTime.UtcNow.AddDays(-10),
            SizeBytes = 1024
        };
        Assert.Equal("10 days ago", tenDaysOld.FormattedAge);

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

        var multipleMonthsOld = new DiscoveredFolder
        {
            Path = @"C:\repos\test\.venv",
            FolderName = ".venv",
            ArtifactType = ArtifactType.PythonVenv,
            CategoryName = "Python",
            LastModifiedUtc = DateTime.UtcNow.AddDays(-95),
            SizeBytes = 1024 * 1024
        };
        Assert.Equal("3 months ago", multipleMonthsOld.FormattedAge);
    }

    [Fact]
    public void DeletionReport_CalculatesHelperProperties()
    {
        var successReport = new DevPurge.Core.Purging.DeletionReport(5, 5, 0, 1024 * 1024, []);
        Assert.True(successReport.IsSuccess);
        Assert.Equal(100.0, successReport.SuccessPercentage);
        Assert.Equal("1.00 MB", successReport.FormattedReclaimedSize);

        var partialReport = new DevPurge.Core.Purging.DeletionReport(10, 8, 2, 512, [(@"C:\locked", "In use")]);
        Assert.False(partialReport.IsSuccess);
        Assert.Equal(80.0, partialReport.SuccessPercentage);
    }

    [Fact]
    public void TryMatchFolder_FindsMatchingRule()
    {
        Assert.True(PurgeRule.TryMatchFolder("node_modules", out var nodeRule));
        Assert.NotNull(nodeRule);
        Assert.Equal(ArtifactType.NodeModules, nodeRule.ArtifactType);

        Assert.True(PurgeRule.TryMatchFolder("bin", out var binRule));
        Assert.NotNull(binRule);
        Assert.Equal(ArtifactType.DotNetBuild, binRule.ArtifactType);

        Assert.True(PurgeRule.TryMatchFolder(".vite", out var viteRule));
        Assert.NotNull(viteRule);
        Assert.Equal(ArtifactType.CacheAndTemp, viteRule.ArtifactType);

        Assert.True(PurgeRule.TryMatchFolder(".nx", out var nxRule));
        Assert.NotNull(nxRule);
        Assert.Equal(ArtifactType.CacheAndTemp, nxRule.ArtifactType);

        Assert.True(PurgeRule.TryMatchFolder(".kotlin", out var kotlinRule));
        Assert.NotNull(kotlinRule);
        Assert.Equal(ArtifactType.GradleBuild, kotlinRule.ArtifactType);

        Assert.False(PurgeRule.TryMatchFolder("src", out var srcRule));
        Assert.Null(srcRule);
    }

    [Fact]
    public void GetRulesByCategory_ReturnsExpectedRules()
    {
        var dotNetRules = PurgeRule.GetRulesByCategory(".NET / C#");
        Assert.NotEmpty(dotNetRules);
        Assert.Contains(dotNetRules, r => r.FolderNames.Contains("bin"));

        var unknownRules = PurgeRule.GetRulesByCategory("NonExistentCategory");
        Assert.Empty(unknownRules);
    }

    [Theory]
    [InlineData(".bundle", ArtifactType.RubyBundle)]
    [InlineData("dist-newstyle", ArtifactType.HaskellBuild)]
    [InlineData(".stack-work", ArtifactType.HaskellBuild)]
    [InlineData(".terraform", ArtifactType.TerraformCache)]
    public void TryMatchFolder_FindsModernEcosystemRules(string folderName, ArtifactType expectedType)
    {
        Assert.True(PurgeRule.TryMatchFolder(folderName, out var rule));
        Assert.NotNull(rule);
        Assert.Equal(expectedType, rule.ArtifactType);
    }

    [Fact]
    public void CoreServices_ImplementExpectedInterfaces()
    {
        DevPurge.Core.Scanning.IFastDirectoryScanner scanner = new DevPurge.Core.Scanning.FastDirectoryScanner();
        Assert.NotNull(scanner);

        DevPurge.Core.Purging.IPurgeService purgeService = new DevPurge.Core.Purging.PurgeService();
        Assert.NotNull(purgeService);

        DevPurge.Core.Auditing.IAuditLogger auditLogger = new DevPurge.Core.Auditing.AuditLogger();
        Assert.NotNull(auditLogger);

        var tempPath = Path.Combine(Path.GetTempPath(), $"settings_iface_{Guid.NewGuid():N}.json");
        try
        {
            DevPurge.Core.Configuration.IUserSettingsManager settingsManager = new DevPurge.Core.Configuration.UserSettingsManager(tempPath);
            Assert.NotNull(settingsManager);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
