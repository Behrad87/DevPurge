using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevPurge.Core.Models;

namespace DevPurge.Core.Exporting;

/// <summary>
/// Metadata associated with a scan report export.
/// </summary>
public record ExportReportMetadata(
    string TargetPath,
    DateTime GeneratedAtUtc,
    string AppVersion = "1.1.0"
);

/// <summary>
/// High-speed, safe report exporter supporting CSV and JSON formats.
/// Implements RFC 4180 CSV compliance and CSV formula injection mitigations.
/// </summary>
public static class ScanReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Escapes a field for safe CSV output, preventing CSV formula injection in spreadsheet applications.
    /// </summary>
    public static string EscapeCsvValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var text = value;

        // Mitigate CSV Formula Injection (DDE attacks)
        // If cell starts with '=', '+', '-', '@', '\t', '\r', prefix with a single quote.
        if (text.Length > 0)
        {
            char firstChar = text[0];
            if (firstChar is '=' or '+' or '-' or '@' or '\t' or '\r')
            {
                text = "'" + text;
            }
        }

        // Escape double quotes by doubling them
        text = text.Replace("\"", "\"\"");

        return $"\"{text}\"";
    }

    /// <summary>
    /// Generates CSV report string for discovered folders with metadata header.
    /// </summary>
    public static string GenerateCsv(IEnumerable<DiscoveredFolder> folders, ExportReportMetadata? metadata = null)
    {
        var list = folders.ToList();
        var sb = new StringBuilder();

        if (metadata != null)
        {
            sb.AppendLine($"# DevPurge Scan Report v{metadata.AppVersion}");
            sb.AppendLine($"# Generated At (UTC): {metadata.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"# Target Path: {metadata.TargetPath}");
            sb.AppendLine($"# Total Folders: {list.Count}");
            sb.AppendLine($"# Total Reclaimable Space: {DiscoveredFolder.FormatByteSize(list.Sum(x => x.SizeBytes))}");
            sb.AppendLine();
        }

        sb.AppendLine("Category,Folder,Path,SizeBytes,FormattedSize,FileCount,AgeDays,FormattedAge,LastModifiedUtc,IsSelected");

        foreach (var item in list)
        {
            sb.Append(EscapeCsvValue(item.CategoryName)).Append(',');
            sb.Append(EscapeCsvValue(item.FolderName)).Append(',');
            sb.Append(EscapeCsvValue(item.Path)).Append(',');
            sb.Append(item.SizeBytes).Append(',');
            sb.Append(EscapeCsvValue(item.FormattedSize)).Append(',');
            sb.Append(item.FileCount).Append(',');
            sb.Append(item.AgeDays.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append(EscapeCsvValue(item.FormattedAge)).Append(',');
            sb.Append(EscapeCsvValue(item.LastModifiedUtc.ToString("o"))).Append(',');
            sb.Append(EscapeCsvValue(item.IsSelected ? "true" : "false"));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates structured JSON report string for discovered folders with metadata and analytics summary.
    /// </summary>
    public static string GenerateJson(IEnumerable<DiscoveredFolder> folders, ExportReportMetadata? metadata = null, bool writeIndented = true)
    {
        var list = folders.ToList();
        long totalBytes = list.Sum(f => f.SizeBytes);
        int totalFiles = list.Sum(f => f.FileCount);

        var report = new
        {
            generator = "DevPurge",
            version = metadata?.AppVersion ?? "1.1.0",
            generatedAtUtc = metadata?.GeneratedAtUtc ?? DateTime.UtcNow,
            targetPath = metadata?.TargetPath ?? string.Empty,
            summary = new
            {
                totalFolders = list.Count,
                totalFiles = totalFiles,
                totalSizeBytes = totalBytes,
                formattedTotalSize = DiscoveredFolder.FormatByteSize(totalBytes),
                selectedFolders = list.Count(f => f.IsSelected),
                selectedSizeBytes = list.Where(f => f.IsSelected).Sum(f => f.SizeBytes),
                formattedSelectedSize = DiscoveredFolder.FormatByteSize(list.Where(f => f.IsSelected).Sum(f => f.SizeBytes)),
                staleFolders = list.Count(f => f.IsStale),
                staleSizeBytes = list.Where(f => f.IsStale).Sum(f => f.SizeBytes),
                formattedStaleSize = DiscoveredFolder.FormatByteSize(list.Where(f => f.IsStale).Sum(f => f.SizeBytes))
            },
            breakdown = list
                .GroupBy(f => f.CategoryName)
                .OrderByDescending(g => g.Sum(x => x.SizeBytes))
                .Select(g => new
                {
                    category = g.Key,
                    folderCount = g.Count(),
                    totalSizeBytes = g.Sum(x => x.SizeBytes),
                    formattedSize = DiscoveredFolder.FormatByteSize(g.Sum(x => x.SizeBytes)),
                    percentage = totalBytes > 0 ? Math.Round((double)g.Sum(x => x.SizeBytes) / totalBytes * 100.0, 2) : 0
                }),
            folders = list.Select(f => new
            {
                path = f.Path,
                folderName = f.FolderName,
                category = f.CategoryName,
                artifactType = f.ArtifactType.ToString(),
                sizeBytes = f.SizeBytes,
                formattedSize = f.FormattedSize,
                fileCount = f.FileCount,
                ageDays = Math.Round(f.AgeDays, 1),
                formattedAge = f.FormattedAge,
                lastModifiedUtc = f.LastModifiedUtc,
                isSelected = f.IsSelected,
                isStale = f.IsStale
            })
        };

        var options = writeIndented ? JsonOptions : new JsonSerializerOptions();
        return JsonSerializer.Serialize(report, options);
    }

    /// <summary>
    /// Generates Markdown report string with tables and summary for discovered folders.
    /// </summary>
    public static string GenerateMarkdown(IEnumerable<DiscoveredFolder> folders, ExportReportMetadata? metadata = null)
    {
        var list = folders.ToList();
        var sb = new StringBuilder();

        sb.AppendLine($"# DevPurge Scan Report v{metadata?.AppVersion ?? "1.1.0"}");
        sb.AppendLine();
        if (metadata != null)
        {
            sb.AppendLine($"- **Target Path:** `{metadata.TargetPath}`");
            sb.AppendLine($"- **Generated At (UTC):** {metadata.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}");
        }
        sb.AppendLine($"- **Total Folders:** {list.Count:N0}");
        sb.AppendLine($"- **Total Reclaimable Space:** **{DiscoveredFolder.FormatByteSize(list.Sum(x => x.SizeBytes))}**");
        sb.AppendLine();

        sb.AppendLine("| Ecosystem | Folder | Path | Size | Files | Age | Last Modified (UTC) |");
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (var item in list)
        {
            sb.AppendLine($"| {item.CategoryName} | `{item.FolderName}` | `{item.Path}` | {item.FormattedSize} | {item.FileCount:N0} | {item.FormattedAge} | {item.LastModifiedUtc:yyyy-MM-dd HH:mm} |");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Exports discovered folders report asynchronously to a target file.
    /// Determines format by extension (.json for JSON, .md for Markdown, otherwise CSV).
    /// </summary>
    public static async Task ExportToFileAsync(
        string filePath,
        IEnumerable<DiscoveredFolder> folders,
        ExportReportMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string content;
        if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            content = GenerateJson(folders, metadata, writeIndented: true);
        }
        else if (filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            content = GenerateMarkdown(folders, metadata);
        }
        else
        {
            content = GenerateCsv(folders, metadata);
        }

        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
    }
}
