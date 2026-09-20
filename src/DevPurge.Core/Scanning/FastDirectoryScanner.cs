using System.Collections.Concurrent;
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
public class FastDirectoryScanner
{
    private readonly List<PurgeRule> _rules;
    private readonly Dictionary<string, PurgeRule> _folderNameToRuleMap;

    public FastDirectoryScanner(IEnumerable<PurgeRule>? rules = null)
    {
        _rules = (rules ?? PurgeRule.GetDefaultRules()).Where(r => r.IsEnabled).ToList();
        _folderNameToRuleMap = new Dictionary<string, PurgeRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in _rules)
        {
            foreach (var folder in rule.FolderNames)
            {
                _folderNameToRuleMap[folder] = rule;
            }
        }
    }

    /// <summary>
    /// Scans root directories and returns discovered artifact folders.
    /// </summary>
    public async Task<List<DiscoveredFolder>> ScanAsync(
        IEnumerable<string> rootDirectories,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var discovered = new ConcurrentBag<DiscoveredFolder>();
        long totalBytesFound = 0;

        foreach (var root in rootDirectories)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            await Task.Run(() =>
            {
                TraverseDirectory(root, discovered, ref totalBytesFound, progress, cancellationToken);
            }, cancellationToken);
        }

        progress?.Report(new ScanProgress(string.Empty, discovered.Count, Interlocked.Read(ref totalBytesFound), true));
        return discovered.OrderByDescending(d => d.SizeBytes).ToList();
    }

    private void TraverseDirectory(
        string currentDir,
        ConcurrentBag<DiscoveredFolder> results,
        ref long totalBytesRef,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(currentDir);

        int counter = 0;

        while (stack.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var dir = stack.Pop();
            string dirName = Path.GetFileName(dir);

            // Skip .git directories
            if (string.Equals(dirName, ".git", StringComparison.OrdinalIgnoreCase))
                continue;

            // Check if this directory is a matching target (e.g. node_modules, bin, obj, target, etc.)
            if (_folderNameToRuleMap.TryGetValue(dirName, out var rule))
            {
                var (isSafe, _) = SafetyValidator.ValidateSafeToDelete(dir, _folderNameToRuleMap.Keys);
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

            // Enumerate subdirectories
            try
            {
                var subDirs = Directory.GetDirectories(dir);
                foreach (var sub in subDirs)
                {
                    var name = Path.GetFileName(sub);
                    // Skip hidden git folders
                    if (!string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
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
            var dirInfo = new DirectoryInfo(directoryPath);
            latestModified = dirInfo.LastWriteTimeUtc;

            var queue = new Queue<string>();
            queue.Enqueue(directoryPath);

            while (queue.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var current = queue.Dequeue();

                try
                {
                    var di = new DirectoryInfo(current);
                    foreach (var file in di.EnumerateFiles())
                    {
                        totalBytes += file.Length;
                        fileCount++;
                        if (file.LastWriteTimeUtc > latestModified)
                        {
                            latestModified = file.LastWriteTimeUtc;
                        }
                    }

                    foreach (var sub in di.EnumerateDirectories())
                    {
                        queue.Enqueue(sub.FullName);
                        if (sub.LastWriteTimeUtc > latestModified)
                        {
                            latestModified = sub.LastWriteTimeUtc;
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
