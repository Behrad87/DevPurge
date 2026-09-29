using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.IO.Enumeration;
using DevPurge.Core.Models;

namespace DevPurge.Core.Scanning;

public record ScanProgress(
    string CurrentPath,
    int DiscoveredCount,
    long TotalBytesFound,
    bool IsCompleted
);

/// <summary>
/// High-speed asynchronous directory scanner targeting developer build and dependency artifacts.
/// </summary>
public class FastDirectoryScanner : IFastDirectoryScanner
{
    private static readonly EnumerationOptions SafeTraversalOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false
    };

    private readonly IReadOnlyList<PurgeRule> _rules;
    private readonly FrozenDictionary<string, PurgeRule> _folderNameToRuleMap;
    private readonly FrozenSet<string> _allowedFolderNames;

    public FastDirectoryScanner(IEnumerable<PurgeRule>? rules = null)
    {
        _rules = (rules ?? PurgeRule.GetDefaultRules()).Where(r => r.IsEnabled).ToList();
        var map = new Dictionary<string, PurgeRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in _rules)
        {
            foreach (var folder in rule.FolderNames)
            {
                map[folder] = rule;
            }
        }

        _folderNameToRuleMap = map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _allowedFolderNames = map.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scans root directories and returns discovered artifact folders.
    /// </summary>
    public async Task<List<DiscoveredFolder>> ScanAsync(
        IEnumerable<string> rootDirectories,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IEnumerable<string>? exclusions = null)
    {
        var discovered = new ConcurrentBag<DiscoveredFolder>();
        long totalBytesFound = 0;
        var exclusionSet = exclusions?.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        var validRoots = rootDirectories
            .Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validRoots.Count == 1)
        {
            var singleRoot = validRoots[0];
            string rootDirName = Path.GetFileName(singleRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            // If the specified root itself is a target artifact (e.g. user pointed directly to a node_modules folder)
            if (_folderNameToRuleMap.TryGetValue(rootDirName, out var rule))
            {
                var (isSafe, _) = SafetyValidator.ValidateSafeToDelete(singleRoot, _allowedFolderNames);
                if (isSafe)
                {
                    var (size, fileCount, lastModified) = CalculateDirectoryStats(singleRoot, cancellationToken);
                    discovered.Add(new DiscoveredFolder
                    {
                        Path = singleRoot,
                        FolderName = rootDirName,
                        ArtifactType = rule.ArtifactType,
                        CategoryName = rule.CategoryName,
                        SizeBytes = size,
                        FileCount = fileCount,
                        LastModifiedUtc = lastModified,
                        IsSelected = true
                    });
                    Interlocked.Add(ref totalBytesFound, size);
                    progress?.Report(new ScanProgress(singleRoot, discovered.Count, Interlocked.Read(ref totalBytesFound), true));
                    return discovered.OrderByDescending(d => d.SizeBytes).ToList();
                }
            }

            // Partition immediate child subdirectories for multi-core parallel scanning
            string[] subDirs = [];
            try
            {
                subDirs = Directory.GetDirectories(singleRoot, "*", SafeTraversalOptions);
            }
            catch { }

            var validSubDirs = subDirs
                .Where(s =>
                {
                    var n = Path.GetFileName(s);
                    return !string.Equals(n, ".git", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(n, ".svn", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(n, ".hg", StringComparison.OrdinalIgnoreCase) &&
                           (exclusionSet == null || (!exclusionSet.Contains(n) && !exclusionSet.Contains(s)));
                })
                .ToArray();

            if (validSubDirs.Length > 1)
            {
                int maxParallel = Math.Max(2, Math.Min(Environment.ProcessorCount, validSubDirs.Length));
                await Parallel.ForEachAsync(validSubDirs, new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxParallel,
                    CancellationToken = cancellationToken
                }, (subDir, ct) =>
                {
                    TraverseDirectory(subDir, discovered, ref totalBytesFound, progress, ct, exclusionSet);
                    return ValueTask.CompletedTask;
                });
            }
            else
            {
                await Task.Run(() =>
                {
                    TraverseDirectory(singleRoot, discovered, ref totalBytesFound, progress, cancellationToken, exclusionSet);
                }, cancellationToken);
            }
        }
        else if (validRoots.Count > 1)
        {
            await Parallel.ForEachAsync(validRoots, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, validRoots.Count)),
                CancellationToken = cancellationToken
            }, (root, ct) =>
            {
                TraverseDirectory(root, discovered, ref totalBytesFound, progress, ct, exclusionSet);
                return ValueTask.CompletedTask;
            });
        }

        progress?.Report(new ScanProgress(string.Empty, discovered.Count, Interlocked.Read(ref totalBytesFound), true));
        return discovered.OrderByDescending(d => d.SizeBytes).ToList();
    }

    private void TraverseDirectory(
        string currentDir,
        ConcurrentBag<DiscoveredFolder> results,
        ref long totalBytesRef,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken,
        FrozenSet<string>? exclusionSet = null)
    {
        var stack = new Stack<string>();
        stack.Push(currentDir);

        int counter = 0;

        while (stack.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var dir = stack.Pop();
            string dirName = Path.GetFileName(dir);

            // Skip version control internal directories and blacklisted system folders
            if (string.Equals(dirName, ".git", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dirName, ".svn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dirName, ".hg", StringComparison.OrdinalIgnoreCase) ||
                SafetyValidator.IsSystemBlacklisted(dirName))
            {
                continue;
            }

            // Skip user exclusions
            if (exclusionSet != null && (exclusionSet.Contains(dirName) || exclusionSet.Contains(dir)))
            {
                continue;
            }

            // Check if this directory is a matching target (e.g. node_modules, bin, obj, target, etc.)
            if (_folderNameToRuleMap.TryGetValue(dirName, out var rule))
            {
                var (isSafe, _) = SafetyValidator.ValidateSafeToDelete(dir, _allowedFolderNames);
                if (isSafe)
                {
                    // Calculate size of this directory
                    var (size, fileCount, lastModified) = CalculateDirectoryStats(dir, cancellationToken);

                    var item = new DiscoveredFolder
                    {
                        Path = dir,
                        FolderName = dirName,
                        ArtifactType = rule.ArtifactType,
                        CategoryName = rule.CategoryName,
                        SizeBytes = size,
                        FileCount = fileCount,
                        LastModifiedUtc = lastModified,
                        IsSelected = true
                    };

                    results.Add(item);
                    Interlocked.Add(ref totalBytesRef, size);

                    progress?.Report(new ScanProgress(dir, results.Count, Interlocked.Read(ref totalBytesRef), false));

                    // Prune traversal: DO NOT descend further into this matched directory!
                    continue;
                }
            }

            // Periodic progress update during traversal
            if (++counter % 20 == 0)
            {
                progress?.Report(new ScanProgress(dir, results.Count, Interlocked.Read(ref totalBytesRef), false));
            }

            // Enumerate subdirectories skipping reparse points (symlinks/junctions)
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir, "*", SafeTraversalOptions))
                {
                    var name = Path.GetFileName(sub);
                    if (!string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(name, ".svn", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(name, ".hg", StringComparison.OrdinalIgnoreCase) &&
                        !SafetyValidator.IsSystemBlacklisted(name) &&
                        (exclusionSet == null || (!exclusionSet.Contains(name) && !exclusionSet.Contains(sub))))
                    {
                        stack.Push(sub);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Ignored - permission denied
            }
            catch (DirectoryNotFoundException)
            {
                // Ignored - symlink or removed dir
            }
            catch (Exception)
            {
                // Ignore other IO transient issues
            }
        }
    }

    /// <summary>
    /// Computes recursive size, file count, and latest modified timestamp for an artifact folder.
    /// Skips reparse points (symlinks/junctions) to prevent counting external or circular directories.
    /// Uses single-pass filesystem enumeration with zero-allocation file entry reading for maximum throughput.
    /// </summary>
    public static (long TotalBytes, int FileCount, DateTime LastModifiedUtc) CalculateDirectoryStats(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        long totalBytes = 0;
        int fileCount = 0;
        var latestModified = DateTime.MinValue;

        try
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return (0, 0, DateTime.UtcNow);
            }

            var dirInfo = new DirectoryInfo(directoryPath);
            if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return (0, 0, DateTime.UtcNow);
            }

            latestModified = dirInfo.LastWriteTimeUtc;

            var queue = new Queue<string>();
            queue.Enqueue(directoryPath);

            while (queue.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var currentDir = queue.Dequeue();

                try
                {
                    var enumerable = new FileSystemEnumerable<(long Length, DateTime LastModifiedUtc, bool IsDirectory, string? SubDir)>(
                        currentDir,
                        (ref FileSystemEntry entry) =>
                        {
                            if (entry.IsDirectory)
                            {
                                return (0, entry.LastWriteTimeUtc.UtcDateTime, true, entry.ToSpecifiedFullPath());
                            }
                            else
                            {
                                return (entry.Length, entry.LastWriteTimeUtc.UtcDateTime, false, null);
                            }
                        },
                        SafeTraversalOptions);

                    foreach (var item in enumerable)
                    {
                        if (item.IsDirectory)
                        {
                            if (item.SubDir != null)
                            {
                                queue.Enqueue(item.SubDir);
                            }
                            if (item.LastModifiedUtc > latestModified)
                            {
                                latestModified = item.LastModifiedUtc;
                            }
                        }
                        else
                        {
                            totalBytes += item.Length;
                            fileCount++;
                            if (item.LastModifiedUtc > latestModified)
                            {
                                latestModified = item.LastModifiedUtc;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore inaccessible subdirectories/files
                }
            }
        }
        catch
        {
            // Ignore top-level read issues
        }

        if (latestModified == DateTime.MinValue)
        {
            latestModified = DateTime.UtcNow;
        }

        return (totalBytes, fileCount, latestModified);
    }
}
