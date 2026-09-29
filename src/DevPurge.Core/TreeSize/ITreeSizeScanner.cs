namespace DevPurge.Core.TreeSize;

/// <summary>
/// Contract for high-performance directory tree size analyzers.
/// </summary>
public interface ITreeSizeScanner
{
    /// <summary>
    /// Asynchronously scans a directory tree and returns the root node with hierarchical size metrics.
    /// </summary>
    /// <param name="rootPath">Directory or drive path to analyze.</param>
    /// <param name="maxDepth">Maximum directory recursion depth.</param>
    /// <param name="progress">Progress callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="exclusions">Optional folder names or paths to exclude.</param>
    /// <returns>Root TreeSizeNode, or null if root directory does not exist.</returns>
    Task<TreeSizeNode?> ScanTreeAsync(
        string rootPath,
        int maxDepth = 15,
        IProgress<TreeSizeProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IEnumerable<string>? exclusions = null);
}
