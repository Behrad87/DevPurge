using DevPurge.Core.Models;

namespace DevPurge.Core.Scanning;

/// <summary>
/// Abstraction for scanning developer artifact directories.
/// </summary>
public interface IScanner
{
    /// <summary>
    /// Scans root directories and returns discovered artifact folders.
    /// </summary>
    Task<List<DiscoveredFolder>> ScanAsync(
        IEnumerable<string> rootDirectories,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IEnumerable<string>? exclusions = null);
}
