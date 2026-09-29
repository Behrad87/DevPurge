namespace DevPurge.Core.TreeSize;

/// <summary>
/// Real-time progress updates during TreeSize directory scanning.
/// </summary>
public record TreeSizeProgress(
    string CurrentPath,
    int DirectoriesScanned,
    int FilesScanned,
    long TotalBytesScanned,
    bool IsCompleted
);
