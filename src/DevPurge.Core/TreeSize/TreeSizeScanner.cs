using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.IO.Enumeration;
using DevPurge.Core.Models;
using DevPurge.Core.Scanning;

namespace DevPurge.Core.TreeSize;

/// <summary>
/// Ultra-fast parallel directory tree scanner calculating hierarchical disk space consumption.
/// </summary>
public class TreeSizeScanner : ITreeSizeScanner
{
    private static readonly EnumerationOptions SafeEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false
    };

    private readonly FrozenDictionary<string, PurgeRule> _folderNameToRuleMap;

    public TreeSizeScanner(IEnumerable<PurgeRule>? rules = null)
    {
        var effectiveRules = (rules ?? PurgeRule.GetDefaultRules()).Where(r => r.IsEnabled).ToList();
        var map = new Dictionary<string, PurgeRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in effectiveRules)
        {
            foreach (var folder in rule.FolderNames)
            {
                map[folder] = rule;
            }
        }

        _folderNameToRuleMap = map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<TreeSizeNode?> ScanTreeAsync(
        string rootPath,
        int maxDepth = 15,
        IProgress<TreeSizeProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IEnumerable<string>? exclusions = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return null;
        }

        var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootName = Path.GetFileName(normalizedRoot);
        if (string.IsNullOrEmpty(rootName))
        {
            rootName = normalizedRoot; // Drive root like C: or D:
        }

        var exclusionSet = exclusions?.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        int totalDirs = 0;
        int totalFiles = 0;
        long totalBytes = 0;

        return await Task.Run(() =>
        {
            var rootNode = new TreeSizeNode
            {
                Path = normalizedRoot,
                Name = rootName,
                LastModifiedUtc = DateTime.UtcNow
            };

            // Check if root itself is a recognized artifact
            if (_folderNameToRuleMap.TryGetValue(rootName, out var rootRule))
            {
                rootNode.IsArtifact = true;
                rootNode.ArtifactCategory = rootRule.CategoryName;
                rootNode.ArtifactType = rootRule.ArtifactType;
            }

            // Calculate files directly in root directory
            var (directBytes, directFiles, rootModified) = CalculateDirectFiles(normalizedRoot, cancellationToken);
            Interlocked.Add(ref totalBytes, directBytes);
            Interlocked.Add(ref totalFiles, directFiles);
            Interlocked.Increment(ref totalDirs);

            rootNode.LastModifiedUtc = rootModified;

            // Enumerate top-level subdirectories
            string[] subDirs = [];
            try
            {
                subDirs = Directory.GetDirectories(normalizedRoot, "*", SafeEnumerationOptions);
            }
            catch (Exception ex)
            {
                rootNode.ErrorMessage = ex.Message;
            }

            var validSubDirs = subDirs
                .Where(s =>
                {
                    var name = Path.GetFileName(s);
                    return !SafetyValidator.IsSystemBlacklisted(name) &&
                           (exclusionSet == null || (!exclusionSet.Contains(name) && !exclusionSet.Contains(s)));
                })
                .ToArray();

            if (validSubDirs.Length > 0 && maxDepth > 0)
            {
                // Parallel scan across top-level subdirectories
                var childNodes = new ConcurrentBag<TreeSizeNode>();
                int maxParallel = Math.Max(2, Math.Min(Environment.ProcessorCount, validSubDirs.Length));

                Parallel.ForEach(validSubDirs, new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxParallel,
                    CancellationToken = cancellationToken
                }, subDir =>
                {
                    var subName = Path.GetFileName(subDir);
                    var child = BuildDirectoryNode(
                        subDir,
                        subName,
                        currentDepth: 1,
                        maxDepth: maxDepth,
                        ref totalDirs,
                        ref totalFiles,
                        ref totalBytes,
                        progress,
                        cancellationToken,
                        exclusionSet);

                    if (child != null)
                    {
                        childNodes.Add(child);
                    }
                });

                foreach (var child in childNodes)
                {
                    child.Parent = rootNode;
                    rootNode.Children.Add(child);
                }
            }

            // Aggregate metrics into rootNode
            long childrenBytes = 0;
            int childrenFiles = 0;
            int childrenDirs = rootNode.Children.Count;
            DateTime latestModified = rootNode.LastModifiedUtc;

            foreach (var child in rootNode.Children)
            {
                childrenBytes += child.SizeBytes;
                childrenFiles += child.FileCount;
                childrenDirs += child.DirectoryCount;
                if (child.LastModifiedUtc > latestModified)
                {
                    latestModified = child.LastModifiedUtc;
                }
            }

            rootNode.SizeBytes = directBytes + childrenBytes;
            rootNode.FileCount = directFiles + childrenFiles;
            rootNode.DirectoryCount = childrenDirs;
            rootNode.LastModifiedUtc = latestModified;

            // Sort children descending by size
            rootNode.Children.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));

            // Set percentages
            rootNode.PercentOfParent = 100.0;
            rootNode.PercentOfRoot = 100.0;

            foreach (var child in rootNode.Children)
            {
                child.PercentOfParent = rootNode.SizeBytes > 0 ? (double)child.SizeBytes / rootNode.SizeBytes * 100.0 : 0;
            }

            // Propagate PercentOfRoot throughout entire tree
            PropagatePercentOfRoot(rootNode, rootNode.SizeBytes);

            progress?.Report(new TreeSizeProgress(normalizedRoot, totalDirs, totalFiles, totalBytes, true));

            return rootNode;
        }, cancellationToken);
    }

    private TreeSizeNode? BuildDirectoryNode(
        string dirPath,
        string dirName,
        int currentDepth,
        int maxDepth,
        ref int totalDirs,
        ref int totalFiles,
        ref long totalBytes,
        IProgress<TreeSizeProgress>? progress,
        CancellationToken ct,
        FrozenSet<string>? exclusionSet)
    {
        if (ct.IsCancellationRequested) return null;

        var count = Interlocked.Increment(ref totalDirs);
        if (count % 25 == 0)
        {
            progress?.Report(new TreeSizeProgress(dirPath, totalDirs, totalFiles, Interlocked.Read(ref totalBytes), false));
        }

        var node = new TreeSizeNode
        {
            Path = dirPath,
            Name = dirName,
            LastModifiedUtc = DateTime.UtcNow
        };

        if (_folderNameToRuleMap.TryGetValue(dirName, out var rule))
        {
            node.IsArtifact = true;
            node.ArtifactCategory = rule.CategoryName;
            node.ArtifactType = rule.ArtifactType;
        }

        // Direct files in this directory
        var (directBytes, directFiles, dirModified) = CalculateDirectFiles(dirPath, ct);
        Interlocked.Add(ref totalBytes, directBytes);
        Interlocked.Add(ref totalFiles, directFiles);
        node.LastModifiedUtc = dirModified;

        if (currentDepth < maxDepth)
        {
            string[] subDirs = [];
            try
            {
                subDirs = Directory.GetDirectories(dirPath, "*", SafeEnumerationOptions);
            }
            catch (Exception ex)
            {
                node.ErrorMessage = ex.Message;
            }

            foreach (var sub in subDirs)
            {
                if (ct.IsCancellationRequested) break;

                var subName = Path.GetFileName(sub);
                if (SafetyValidator.IsSystemBlacklisted(subName) ||
                    (exclusionSet != null && (exclusionSet.Contains(subName) || exclusionSet.Contains(sub))))
                {
                    continue;
                }

                var child = BuildDirectoryNode(
                    sub,
                    subName,
                    currentDepth + 1,
                    maxDepth,
                    ref totalDirs,
                    ref totalFiles,
                    ref totalBytes,
                    progress,
                    ct,
                    exclusionSet);

                if (child != null)
                {
                    child.Parent = node;
                    node.Children.Add(child);
                }
            }
        }

        long childrenBytes = 0;
        int childrenFiles = 0;
        int childrenDirs = node.Children.Count;
        DateTime latestModified = node.LastModifiedUtc;

        foreach (var child in node.Children)
        {
            childrenBytes += child.SizeBytes;
            childrenFiles += child.FileCount;
            childrenDirs += child.DirectoryCount;
            if (child.LastModifiedUtc > latestModified)
            {
                latestModified = child.LastModifiedUtc;
            }
        }

        node.SizeBytes = directBytes + childrenBytes;
        node.FileCount = directFiles + childrenFiles;
        node.DirectoryCount = childrenDirs;
        node.LastModifiedUtc = latestModified;

        node.Children.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));

        foreach (var child in node.Children)
        {
            child.PercentOfParent = node.SizeBytes > 0 ? (double)child.SizeBytes / node.SizeBytes * 100.0 : 0;
        }

        return node;
    }

    private static (long Bytes, int Files, DateTime LastModified) CalculateDirectFiles(string dirPath, CancellationToken ct)
    {
        long bytes = 0;
        int files = 0;
        DateTime lastMod = DateTime.MinValue;

        try
        {
            var dirInfo = new DirectoryInfo(dirPath);
            lastMod = dirInfo.LastWriteTimeUtc;

            foreach (var file in dirInfo.EnumerateFiles("*", SafeEnumerationOptions))
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    bytes += file.Length;
                    files++;
                    if (file.LastWriteTimeUtc > lastMod)
                    {
                        lastMod = file.LastWriteTimeUtc;
                    }
                }
                catch { }
            }
        }
        catch { }

        return (bytes, files, lastMod == DateTime.MinValue ? DateTime.UtcNow : lastMod);
    }

    private static void PropagatePercentOfRoot(TreeSizeNode node, long rootSize)
    {
        if (rootSize > 0)
        {
            node.PercentOfRoot = (double)node.SizeBytes / rootSize * 100.0;
        }
        else
        {
            node.PercentOfRoot = 0;
        }

        foreach (var child in node.Children)
        {
            PropagatePercentOfRoot(child, rootSize);
        }
    }
}
