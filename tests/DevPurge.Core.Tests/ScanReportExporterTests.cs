using System.Text.Json;
using DevPurge.Core.Exporting;
using DevPurge.Core.Models;

namespace DevPurge.Core.Tests;

public class ScanReportExporterTests
{
    private static List<DiscoveredFolder> CreateSampleFolders()
    {
        return
        [
            new DiscoveredFolder
            {
                Path = @"C:\repos\my-app\node_modules",
                FolderName = "node_modules",
                CategoryName = "JavaScript / Node.js",
                ArtifactType = ArtifactType.NodeModules,
                SizeBytes = 1024 * 1024 * 150, // 150 MB
                FileCount = 12000,
                LastModifiedUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
                IsSelected = true
            },
            new DiscoveredFolder
            {
                Path = @"C:\repos\my-api\bin",
                FolderName = "bin",
                CategoryName = ".NET / C#",
                ArtifactType = ArtifactType.DotNetBuild,
                SizeBytes = 1024 * 1024 * 25, // 25 MB
                FileCount = 350,
                LastModifiedUtc = new DateTime(2026, 8, 15, 10, 30, 0, DateTimeKind.Utc),
                IsSelected = false
            }
        ];
    }

    [Fact]
    public void GenerateCsv_ProducesValidHeaderAndRows()
    {
        var folders = CreateSampleFolders();
        var metadata = new ExportReportMetadata(@"C:\repos", DateTime.UtcNow, "1.1.0");

        var csv = ScanReportExporter.GenerateCsv(folders, metadata);

        Assert.Contains("# DevPurge Scan Report v1.1.0", csv);
        Assert.Contains("Category,Folder,Path,SizeBytes,FormattedSize,FileCount,AgeDays,FormattedAge,LastModifiedUtc,IsSelected", csv);
        Assert.Contains("\"JavaScript / Node.js\",\"node_modules\",\"C:\\repos\\my-app\\node_modules\"", csv);
        Assert.Contains("\"150.00 MB\"", csv);
        Assert.Contains("\"true\"", csv);
        Assert.Contains("\"false\"", csv);
    }

    [Theory]
    [InlineData("=cmd|' /C calc'!A0", "\"'=cmd|' /C calc'!A0\"")]
    [InlineData("+12345", "\"'+12345\"")]
    [InlineData("-SUM(A1:A10)", "\"'-SUM(A1:A10)\"")]
    [InlineData("@special", "\"'@special\"")]
    [InlineData("normal_folder", "\"normal_folder\"")]
    [InlineData("quote \"inside\"", "\"quote \"\"inside\"\"\"")]
    public void EscapeCsvValue_MitigatesFormulaInjectionAndEscapesQuotes(string input, string expected)
    {
        var escaped = ScanReportExporter.EscapeCsvValue(input);
        Assert.Equal(expected, escaped);
    }

    [Fact]
    public void GenerateJson_ProducesValidJsonWithSummaryAndBreakdown()
    {
        var folders = CreateSampleFolders();
        var metadata = new ExportReportMetadata(@"C:\repos", DateTime.UtcNow, "1.1.0");

        var json = ScanReportExporter.GenerateJson(folders, metadata);

        Assert.NotEmpty(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("DevPurge", root.GetProperty("generator").GetString());
        Assert.Equal("1.1.0", root.GetProperty("version").GetString());
        Assert.Equal(@"C:\repos", root.GetProperty("targetPath").GetString());

        var summary = root.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("totalFolders").GetInt32());
        Assert.Equal(12350, summary.GetProperty("totalFiles").GetInt32());
        Assert.Equal(1024 * 1024 * 175, summary.GetProperty("totalSizeBytes").GetInt64());
        Assert.Equal(1, summary.GetProperty("selectedFolders").GetInt32());

        var breakdown = root.GetProperty("breakdown");
        Assert.Equal(2, breakdown.GetArrayLength());

        var foldersArray = root.GetProperty("folders");
        Assert.Equal(2, foldersArray.GetArrayLength());
    }

    [Fact]
    public void GenerateMarkdown_ProducesFormattedTableAndSummary()
    {
        var folders = CreateSampleFolders();
        var metadata = new ExportReportMetadata(@"C:\repos", DateTime.UtcNow, "1.1.0");

        var md = ScanReportExporter.GenerateMarkdown(folders, metadata);

        Assert.Contains("# DevPurge Scan Report v1.1.0", md);
        Assert.Contains("- **Target Path:** `C:\\repos`", md);
        Assert.Contains("| Ecosystem | Folder | Path | Size | Files | Age | Last Modified (UTC) |", md);
        Assert.Contains("| JavaScript / Node.js | `node_modules` | `C:\\repos\\my-app\\node_modules` | 150.00 MB | 12,000 |", md);
        Assert.Contains("| .NET / C# | `bin` | `C:\\repos\\my-api\\bin` | 25.00 MB | 350 |", md);
    }

    [Fact]
    public async Task ExportToFileAsync_WritesFileCorrectly()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"devpurge_export_test_{Guid.NewGuid():N}.json");
        try
        {
            var folders = CreateSampleFolders();
            await ScanReportExporter.ExportToFileAsync(tempFile, folders);

            Assert.True(File.Exists(tempFile));
            var content = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("DevPurge", content);
            Assert.Contains("node_modules", content);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task ExportToFileAsync_WritesMarkdownFileCorrectly()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"devpurge_export_test_{Guid.NewGuid():N}.md");
        try
        {
            var folders = CreateSampleFolders();
            var metadata = new ExportReportMetadata(@"C:\repos", DateTime.UtcNow, "1.1.0");
            await ScanReportExporter.ExportToFileAsync(tempFile, folders, metadata);

            Assert.True(File.Exists(tempFile));
            var content = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("# DevPurge Scan Report v1.1.0", content);
            Assert.Contains("| JavaScript / Node.js | `node_modules` |", content);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
