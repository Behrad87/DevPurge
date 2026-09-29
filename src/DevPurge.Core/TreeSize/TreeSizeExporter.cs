using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevPurge.Core.TreeSize;

/// <summary>
/// Exporter for TreeSize analysis reports into CSV, JSON, and ASCII Text formats.
/// </summary>
public static class TreeSizeExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Exports the TreeSize hierarchy to a structured CSV format.
    /// </summary>
    public static string ToCsv(TreeSizeNode root)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Path,Name,SizeBytes,FormattedSize,PercentOfParent,PercentOfRoot,FileCount,DirectoryCount,LastModifiedUtc,IsArtifact,ArtifactCategory");

        void AppendNode(TreeSizeNode node)
        {
            var escPath = EscapeCsv(node.Path);
            var escName = EscapeCsv(node.Name);
            var escCat = EscapeCsv(node.ArtifactCategory ?? string.Empty);

            sb.AppendLine($"\"{escPath}\",\"{escName}\",{node.SizeBytes},\"{node.FormattedSize}\",{node.PercentOfParent:0.00},{node.PercentOfRoot:0.00},{node.FileCount},{node.DirectoryCount},\"{node.LastModifiedUtc:yyyy-MM-dd HH:mm:ss}\",{node.IsArtifact},\"{escCat}\"");

            foreach (var child in node.Children)
            {
                AppendNode(child);
            }
        }

        AppendNode(root);
        return sb.ToString();
    }

    /// <summary>
    /// Exports the TreeSize hierarchy to formatted JSON.
    /// </summary>
    public static string ToJson(TreeSizeNode root)
    {
        return JsonSerializer.Serialize(root, JsonOptions);
    }

    /// <summary>
    /// Exports the TreeSize hierarchy as an ASCII / Unicode outline tree.
    /// </summary>
    public static string ToTextTree(TreeSizeNode root, int maxDepth = 6)
    {
        var sb = new StringBuilder();

        var rootArtifact = root.IsArtifact ? $" [{root.ArtifactCategory ?? "Artifact"}]" : "";
        sb.AppendLine($"{root.Name}{rootArtifact} — {root.FormattedSize} (100%) — {root.FileCount:N0} files, {root.DirectoryCount:N0} folders");

        void AppendChildren(TreeSizeNode parent, string indent, int currentDepth)
        {
            if (currentDepth > maxDepth) return;

            for (int i = 0; i < parent.Children.Count; i++)
            {
                var child = parent.Children[i];
                bool isLast = (i == parent.Children.Count - 1);
                var branch = isLast ? "└── " : "├── ";
                var nextIndent = indent + (isLast ? "    " : "│   ");

                var artifactTag = child.IsArtifact ? $" [{child.ArtifactCategory ?? "Artifact"}]" : "";
                sb.AppendLine($"{indent}{branch}{child.Name}{artifactTag} — {child.FormattedSize} ({child.FormattedPercent}) — {child.FileCount:N0} files");

                AppendChildren(child, nextIndent, currentDepth + 1);
            }
        }

        AppendChildren(root, "", 1);
        return sb.ToString();
    }

    private static string EscapeCsv(string value) => value.Replace("\"", "\"\"");
}
