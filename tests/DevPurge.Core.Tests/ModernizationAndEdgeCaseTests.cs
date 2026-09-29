using System.Text.Json;
using DevPurge.Cli;
using DevPurge.Core.Auditing;
using DevPurge.Core.Configuration;
using DevPurge.Core.Exporting;
using DevPurge.Core.Models;
using DevPurge.Core.Purging;
using DevPurge.Core.Scanning;
using DevPurge.Core.TreeSize;

namespace DevPurge.Core.Tests;

/// <summary>
/// Comprehensive edge-case tests validating modernized C# 12/13 behavior,
/// safety constraints, formatting, CLI parsing, reporting, and settings management.
/// </summary>
public class ModernizationAndEdgeCaseTests : IDisposable
{
    private readonly string _tempRoot;

    public ModernizationAndEdgeCaseTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"devpurge_modern_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region SafetyValidator Edge Cases

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("prn")]
    [InlineData("AUX")]
    [InlineData("aux")]
    [InlineData("NUL")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("com5")]
    [InlineData("LPT1")]
    [InlineData("lpt9")]
    public void ValidateCustomRuleFolder_RejectsAllReservedDeviceNamesCaseInsensitively(string deviceName)
    {
        var (isValid, error) = SafetyValidator.ValidateCustomRuleFolder(deviceName);
        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Contains("reserved OS device name", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData(null)]
    public void ValidateCustomRuleFolder_RejectsEmptyOrWhitespace(string? input)
    {
        var (isValid, error) = SafetyValidator.ValidateCustomRuleFolder(input!);
        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Contains("cannot be empty", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("folder/subfolder")]
    [InlineData(@"folder\subfolder")]
    [InlineData("C:")]
    [InlineData("D:/folder")]
    public void ValidateCustomRuleFolder_RejectsPathTraversalAndSlashes(string folderName)
    {
        var (isValid, error) = SafetyValidator.ValidateCustomRuleFolder(folderName);
        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(".git")]
    [InlineData(".svn")]
    [InlineData(".hg")]
    public void ValidateCustomRuleFolder_RejectsVcsFolders(string vcsFolder)
    {
        var (isValid, error) = SafetyValidator.ValidateCustomRuleFolder(vcsFolder);
        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Contains("version control", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("System32")]
    [InlineData("Program Files")]
    [InlineData("appdata")]
    [InlineData("users")]
    [InlineData("root")]
    [InlineData("home")]
    public void ValidateCustomRuleFolder_RejectsSystemDirectories(string systemDir)
    {
        var (isValid, error) = SafetyValidator.ValidateCustomRuleFolder(systemDir);
        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Contains("protected system directory", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("my-custom-cache")]
    [InlineData(".turbo-cache")]
    [InlineData("artifacts_temp")]
    [InlineData("build-output")]
    public void ValidateCustomRuleFolder_AcceptsValidFolderNames(string folderName)
    {
        var (isValid, error) = SafetyValidator.ValidateCustomRuleFolder(folderName);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateSafeToDelete_RejectsProjectFileInDirectory()
    {
        string[] allowed = ["bin"];
        var targetDir = Path.Combine(_tempRoot, "ProjectWithCsproj", "bin");
        Directory.CreateDirectory(targetDir);

        var csprojFile = Path.Combine(targetDir, "MyProject.csproj");
        File.WriteAllText(csprojFile, "<Project />");

        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(targetDir, allowed);
        Assert.False(isSafe);
        Assert.Contains("project file", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSafeToDelete_RejectsSolutionFileInDirectory()
    {
        string[] allowed = ["bin"];
        var targetDir = Path.Combine(_tempRoot, "ProjectWithSln", "bin");
        Directory.CreateDirectory(targetDir);

        var slnFile = Path.Combine(targetDir, "App.slnx");
        File.WriteAllText(slnFile, "<Solution />");

        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(targetDir, allowed);
        Assert.False(isSafe);
        Assert.Contains("solution (.sln/.slnx)", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSafeToDelete_AllowsNodeModulesWithInternalPackageJson()
    {
        string[] allowed = ["node_modules"];
        var targetDir = Path.Combine(_tempRoot, "WebProject", "node_modules");
        Directory.CreateDirectory(targetDir);

        // Sub-packages inside node_modules often have package.json - this must NOT block purging node_modules!
        var packageJson = Path.Combine(targetDir, "package.json");
        File.WriteAllText(packageJson, "{}");

        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(targetDir, allowed);
        Assert.True(isSafe);
        Assert.Null(reason);
    }

    [Fact]
    public void ValidateSafeToDelete_RejectsBinWithProjectManifest()
    {
        string[] allowed = ["bin"];
        var targetDir = Path.Combine(_tempRoot, "MisconfiguredProject", "bin");
        Directory.CreateDirectory(targetDir);

        // If bin contains cargo.toml or package.json, it's actually a project root!
        var manifest = Path.Combine(targetDir, "Cargo.toml");
        File.WriteAllText(manifest, "[package]");

        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(targetDir, allowed);
        Assert.False(isSafe);
        Assert.Contains("project manifest 'Cargo.toml'", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSafeToDelete_RejectsSystemUsersRoot()
    {
        string[] allowed = ["Users"];
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (!string.IsNullOrEmpty(root))
        {
            var usersPath = Path.Combine(root, "Users");
            var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(usersPath, allowed);
            Assert.False(isSafe);
            Assert.Contains("system Users root directory", reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void IsSystemBlacklisted_ReturnsTrueForBlacklistedAndFalseForSafe()
    {
        Assert.True(SafetyValidator.IsSystemBlacklisted("windows"));
        Assert.True(SafetyValidator.IsSystemBlacklisted("SYSTEM32"));
        Assert.True(SafetyValidator.IsSystemBlacklisted("winsxs"));
        Assert.True(SafetyValidator.IsSystemBlacklisted("Program Files"));
        Assert.True(SafetyValidator.IsSystemBlacklisted("perflogs"));

        Assert.False(SafetyValidator.IsSystemBlacklisted("node_modules"));
        Assert.False(SafetyValidator.IsSystemBlacklisted("bin"));
        Assert.False(SafetyValidator.IsSystemBlacklisted("target"));
        Assert.False(SafetyValidator.IsSystemBlacklisted(""));
    }

    #endregion

    #region DiscoveredFolder & TreeSizeNode Formatting Edge Cases

    [Theory]
    [InlineData(0, "0.00 B")]
    [InlineData(-1, "0 B")]
    [InlineData(-99999, "0 B")]
    [InlineData(1, "1.00 B")]
    [InlineData(500, "500.00 B")]
    [InlineData(1023, "1.00 KB")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1024 * 1024 - 1, "1.00 MB")]
    [InlineData(1024 * 1024, "1.00 MB")]
    [InlineData(1024L * 1024 * 1024, "1.00 GB")]
    [InlineData(1024L * 1024 * 1024 * 1024, "1.00 TB")]
    public void DiscoveredFolder_FormatByteSize_HandlesAllBoundaryValues(long bytes, string expected)
    {
        var result = DiscoveredFolder.FormatByteSize(bytes);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DiscoveredFolder_AgeDaysAndFormattedAge_HandleBoundaryCases()
    {
        // Future date clamped to 0
        var futureFolder = new DiscoveredFolder
        {
            Path = @"C:\test\bin",
            FolderName = "bin",
            ArtifactType = ArtifactType.DotNetBuild,
            CategoryName = ".NET",
            LastModifiedUtc = DateTime.UtcNow.AddDays(5),
            SizeBytes = 100
        };
        Assert.Equal(0, futureFolder.AgeDays);
        Assert.Equal("Today", futureFolder.FormattedAge);
        Assert.False(futureFolder.IsStale);

        // Exactly 2 days ago
        var twoDaysOld = new DiscoveredFolder
        {
            Path = @"C:\test\bin",
            FolderName = "bin",
            ArtifactType = ArtifactType.DotNetBuild,
            CategoryName = ".NET",
            LastModifiedUtc = DateTime.UtcNow.AddDays(-2.1),
            SizeBytes = 100
        };
        Assert.Equal("2 days ago", twoDaysOld.FormattedAge);
        Assert.False(twoDaysOld.IsStale);

        // Exactly 29 days ago (not stale)
        var twentyNineDaysOld = new DiscoveredFolder
        {
            Path = @"C:\test\bin",
            FolderName = "bin",
            ArtifactType = ArtifactType.DotNetBuild,
            CategoryName = ".NET",
            LastModifiedUtc = DateTime.UtcNow.AddDays(-29.5),
            SizeBytes = 100
        };
        Assert.Equal("29 days ago", twentyNineDaysOld.FormattedAge);
        Assert.False(twentyNineDaysOld.IsStale);

        // Exactly 30 days ago (stale)
        var thirtyDaysOld = new DiscoveredFolder
        {
            Path = @"C:\test\bin",
            FolderName = "bin",
            ArtifactType = ArtifactType.DotNetBuild,
            CategoryName = ".NET",
            LastModifiedUtc = DateTime.UtcNow.AddDays(-30.1),
            SizeBytes = 100
        };
        Assert.Equal("1 month ago", thirtyDaysOld.FormattedAge);
        Assert.True(thirtyDaysOld.IsStale);

        // 60 days ago
        var sixtyDaysOld = new DiscoveredFolder
        {
            Path = @"C:\test\bin",
            FolderName = "bin",
            ArtifactType = ArtifactType.DotNetBuild,
            CategoryName = ".NET",
            LastModifiedUtc = DateTime.UtcNow.AddDays(-61),
            SizeBytes = 100
        };
        Assert.Equal("2 months ago", sixtyDaysOld.FormattedAge);
        Assert.True(sixtyDaysOld.IsStale);
    }

    [Fact]
    public void TreeSizeNode_FormattedAge_MatchesDiscoveredFolderLogic()
    {
        var node = new TreeSizeNode
        {
            Path = @"C:\test\node_modules",
            Name = "node_modules",
            SizeBytes = 1048576,
            FileCount = 50,
            LastModifiedUtc = DateTime.UtcNow.AddDays(-1.5)
        };

        Assert.Equal("1 day ago", node.FormattedAge);
        Assert.Equal("1.00 MB", node.FormattedSize);
    }

    #endregion

    #region PurgeRule Edge Cases

    [Theory]
    [InlineData("NODE_MODULES", ArtifactType.NodeModules)]
    [InlineData("Bin", ArtifactType.DotNetBuild)]
    [InlineData("OBJ", ArtifactType.DotNetBuild)]
    [InlineData("Target", ArtifactType.RustTarget)]
    [InlineData(".Gradle", ArtifactType.GradleBuild)]
    [InlineData(".VENV", ArtifactType.PythonVenv)]
    [InlineData(".DART_TOOL", ArtifactType.DartFlutter)]
    [InlineData("CMake-Build-Debug", ArtifactType.CppBuild)]
    [InlineData("ZIG-CACHE", ArtifactType.ZigBuild)]
    [InlineData("DERIVEDDATA", ArtifactType.SwiftBuild)]
    [InlineData("_BUILD", ArtifactType.ElixirBuild)]
    [InlineData(".BUNDLE", ArtifactType.RubyBundle)]
    [InlineData("DIST-NEWSTYLE", ArtifactType.HaskellBuild)]
    [InlineData(".TERRAFORM", ArtifactType.TerraformCache)]
    public void PurgeRule_TryMatchFolder_IsCaseInsensitive(string folderName, ArtifactType expectedType)
    {
        var matched = PurgeRule.TryMatchFolder(folderName, out var rule);
        Assert.True(matched);
        Assert.NotNull(rule);
        Assert.Equal(expectedType, rule.ArtifactType);
    }

    [Theory]
    [InlineData("src")]
    [InlineData("docs")]
    [InlineData("lib")]
    [InlineData("test")]
    [InlineData("images")]
    [InlineData("public")]
    public void PurgeRule_TryMatchFolder_ReturnsFalseForNonArtifacts(string folderName)
    {
        var matched = PurgeRule.TryMatchFolder(folderName, out var rule);
        Assert.False(matched);
        Assert.Null(rule);
    }

    [Fact]
    public void PurgeRule_GetRulesByCategory_IsCaseInsensitive()
    {
        var rulesLower = PurgeRule.GetRulesByCategory("rust");
        var rulesUpper = PurgeRule.GetRulesByCategory("RUST");
        Assert.NotEmpty(rulesLower);
        Assert.Equal(rulesLower.Count, rulesUpper.Count);
    }

    #endregion

    #region CLI Options & Byte Parsing Edge Cases

    [Theory]
    [InlineData("100MB", 100L * 1024 * 1024)]
    [InlineData("100mb", 100L * 1024 * 1024)]
    [InlineData("100M", 100L * 1024 * 1024)]
    [InlineData("1.5GB", (long)(1.5 * 1024 * 1024 * 1024))]
    [InlineData("1.5 gb", (long)(1.5 * 1024 * 1024 * 1024))]
    [InlineData("500KB", 500L * 1024)]
    [InlineData("500k", 500L * 1024)]
    [InlineData("2TB", 2L * 1024 * 1024 * 1024 * 1024)]
    [InlineData("1024B", 1024L)]
    [InlineData("1024b", 1024L)]
    [InlineData("0MB", 0L)]
    [InlineData("0B", 0L)]
    public void TryParseByteSize_ParsesValidInputsCorrectly(string input, long expectedBytes)
    {
        var ok = Program.TryParseByteSize(input, out var bytes);
        Assert.True(ok);
        Assert.Equal(expectedBytes, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("-50MB")]
    [InlineData("MB")]
    [InlineData("invalid")]
    [InlineData("100XYZ")]
    public void TryParseByteSize_RejectsInvalidInputs(string? input)
    {
        var ok = Program.TryParseByteSize(input, out var bytes);
        Assert.False(ok);
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void ParseCommandLine_ParsesComplexCombinations()
    {
        string[] args =
        [
            "--path=C:\\repos;D:\\work",
            "--clean",
            "--min-size=250MB",
            "--min-age=14",
            "--top=20",
            "--type=node",
            "--exclude=legacy,deprecated",
            "--no-config",
            "--json"
        ];

        var options = Program.ParseCommandLine(args);

        Assert.Single(options.Paths);
        Assert.True(options.Clean);
        Assert.Equal(250L * 1024 * 1024, options.MinSizeBytes);
        Assert.Equal(14, options.MinAgeDays);
        Assert.Equal(20, options.TopCount);
        Assert.Equal("node", options.FilterType);
        Assert.Contains("legacy", options.Exclusions);
        Assert.Contains("deprecated", options.Exclusions);
        Assert.True(options.NoConfig);
        Assert.True(options.Json);
    }

    [Fact]
    public void ParseCommandLine_HandlesNegativeMinAgeGracefully()
    {
        var options = Program.ParseCommandLine(["--min-age", "-5"]);
        Assert.Equal(0, options.MinAgeDays);
    }

    #endregion

    #region ScanReportExporter Edge Cases

    [Theory]
    [InlineData("\tcmd", "\"'\tcmd\"")]
    [InlineData("\rcmd", "\"'\rcmd\"")]
    [InlineData("=1+1", "\"\'=1+1\"")]
    [InlineData("+calc", "\"\'+calc\"")]
    [InlineData("-SUM", "\"\'-SUM\"")]
    [InlineData("@leak", "\"\'@leak\"")]
    public void EscapeCsvValue_NeutralizesDdeAttackPrefixes(string input, string expected)
    {
        var result = ScanReportExporter.EscapeCsvValue(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GenerateCsv_WithEmptyList_ProducesHeaderOnly()
    {
        var csv = ScanReportExporter.GenerateCsv([]);
        Assert.StartsWith("Category,Folder,Path,SizeBytes,FormattedSize,FileCount,AgeDays,FormattedAge,LastModifiedUtc,IsSelected", csv.Trim());
    }

    [Fact]
    public void GenerateJson_WithEmptyList_ProducesZeroSummary()
    {
        var json = ScanReportExporter.GenerateJson([]);
        using var doc = JsonDocument.Parse(json);
        var summary = doc.RootElement.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("totalFolders").GetInt32());
        Assert.Equal(0, summary.GetProperty("totalSizeBytes").GetInt64());
    }

    [Fact]
    public void GenerateMarkdown_WithEmptyList_ProducesValidMarkdown()
    {
        var md = ScanReportExporter.GenerateMarkdown([]);
        Assert.Contains("# DevPurge Scan Report", md);
        Assert.Contains("- **Total Folders:** 0", md);
        Assert.Contains("| Ecosystem | Folder | Path | Size | Files | Age | Last Modified (UTC) |", md);
    }

    [Fact]
    public async Task ExportToFileAsync_SupportsAllExtensionsAndAutoCreatesDirectory()
    {
        var exportDir = Path.Combine(_tempRoot, "exports", "sub");
        var csvPath = Path.Combine(exportDir, "report.csv");
        var jsonPath = Path.Combine(exportDir, "report.json");
        var mdPath = Path.Combine(exportDir, "report.md");

        var folders = new List<DiscoveredFolder>
        {
            new()
            {
                Path = @"C:\repos\myproj\bin",
                FolderName = "bin",
                CategoryName = ".NET",
                ArtifactType = ArtifactType.DotNetBuild,
                SizeBytes = 2048,
                FileCount = 5,
                LastModifiedUtc = DateTime.UtcNow
            }
        };

        await ScanReportExporter.ExportToFileAsync(csvPath, folders);
        await ScanReportExporter.ExportToFileAsync(jsonPath, folders);
        await ScanReportExporter.ExportToFileAsync(mdPath, folders);

        Assert.True(File.Exists(csvPath));
        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(mdPath));

        Assert.Contains("Category,Folder,Path", await File.ReadAllTextAsync(csvPath));
        Assert.Contains("totalFolders", await File.ReadAllTextAsync(jsonPath));
        Assert.Contains("# DevPurge Scan Report", await File.ReadAllTextAsync(mdPath));
    }

    #endregion

    #region UserSettingsManager Edge Cases

    [Fact]
    public void UserSettingsManager_HandlesCorruptedFileGracefully()
    {
        var settingsFile = Path.Combine(_tempRoot, "corrupted_settings.json");
        File.WriteAllText(settingsFile, "{ this is not valid json! }");

        var manager = new UserSettingsManager(settingsFile);
        Assert.NotNull(manager.Settings);
        Assert.Empty(manager.Settings.RecentPaths);
        Assert.Empty(manager.Settings.CustomRules);
    }

    [Fact]
    public void UserSettingsManager_DeduplicatesTrailingSlashesInRecentPaths()
    {
        var settingsFile = Path.Combine(_tempRoot, "mru_test.json");
        var manager = new UserSettingsManager(settingsFile);

        manager.AddRecentPath(@"C:\repos\myproject\");
        manager.AddRecentPath(@"C:\repos\myproject");

        Assert.Single(manager.Settings.RecentPaths);
        Assert.Equal(@"C:\repos\myproject", manager.Settings.RecentPaths[0]);
    }

    [Fact]
    public void UserSettingsManager_ResetRulesToDefault_ClearsCustomAndReenablesAll()
    {
        var settingsFile = Path.Combine(_tempRoot, "reset_rules.json");
        var manager = new UserSettingsManager(settingsFile);

        manager.SetRuleEnabled("Rust Cargo Target", false);
        manager.AddCustomRule(new PurgeRuleConfig("My Custom", ArtifactType.Custom, "Custom", ["custom_dir"], "Desc"));

        var beforeReset = manager.GetEffectiveRules();
        Assert.Contains(beforeReset, r => r.Name == "My Custom");
        Assert.False(beforeReset.First(r => r.Name == "Rust Cargo Target").IsEnabled);

        manager.ResetRulesToDefault();

        var afterReset = manager.GetEffectiveRules();
        Assert.DoesNotContain(afterReset, r => r.Name == "My Custom");
        Assert.True(afterReset.First(r => r.Name == "Rust Cargo Target").IsEnabled);
    }

    #endregion

    #region AuditLogger Edge Cases

    [Fact]
    public async Task AuditLogger_GetRecentEntries_HandlesEmptyOrMissingFile()
    {
        var missingLog = Path.Combine(_tempRoot, "non_existent.jsonl");
        var logger = new AuditLogger(missingLog);

        var entries = await logger.GetRecentEntriesAsync(10, missingLog);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task AuditLogger_GetRecentEntries_IgnoresCorruptedLines()
    {
        var logFile = Path.Combine(_tempRoot, "partially_corrupt.jsonl");
        var validEntry = new AuditEntry
        {
            Action = "Purge",
            ReclaimedBytes = 1000,
            SuccessfulCount = 1
        };

        await File.WriteAllLinesAsync(logFile,
        [
            JsonSerializer.Serialize(validEntry),
            "{ NOT VALID JSON }",
            ""
        ]);

        var logger = new AuditLogger(logFile);
        var entries = await logger.GetRecentEntriesAsync(10, logFile);

        Assert.Single(entries);
        Assert.Equal(1000, entries[0].ReclaimedBytes);
    }

    #endregion
}
