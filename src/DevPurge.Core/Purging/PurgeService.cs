using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DevPurge.Core.Models;
using DevPurge.Core.Scanning;

namespace DevPurge.Core.Purging;

/// <summary>
/// High-speed safe deletion service for target artifact directories.
/// Supports high-throughput batch Recycle Bin operations and parallel permanent purging.
/// </summary>
public class PurgeService
{
    #region Win32 Shell API for Recycle Bin

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.U4)]
        public int wFunc;
        public string pFrom;
        public string? pTo;
        public short fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    private const int FO_DELETE = 0x0003;
    private const short FOF_ALLOWUNDO = 0x0040;
    private const short FOF_NOCONFIRMATION = 0x0010;
    private const short FOF_SILENT = 0x0004;
    private const short FOF_NOERRORUI = 0x0400;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    /// <summary>
    /// Deletes a batch of folders to the Windows Recycle Bin in a single native shell operation.
    /// Dramatically faster than invoking the shell for each folder individually.
    /// </summary>
    private static bool SendToRecycleBinBatch(IEnumerable<string> paths)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        var validPaths = paths.Where(Directory.Exists).ToList();
        if (validPaths.Count == 0) return true;

        // SHFileOperation expects null-delimited paths ending with a double null: "path1\0path2\0path3\0\0"
        var buffer = string.Join("\0", validPaths) + "\0\0";
        var fileOp = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = buffer,
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
        };

        var result = SHFileOperation(ref fileOp);
        return result == 0 && !fileOp.fAnyOperationsAborted;
    }

    #endregion

    private readonly FrozenSet<string> _allowedFolderNames;

    public PurgeService(IEnumerable<PurgeRule>? rules = null)
    {
        var activeRules = rules ?? PurgeRule.GetDefaultRules();
        _allowedFolderNames = activeRules
            .SelectMany(r => r.FolderNames)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Purges target folders using parallel deletion and batched shell operations for maximum throughput.
    /// </summary>
    public async Task<DeletionReport> PurgeAsync(
        IEnumerable<DiscoveredFolder> folders,
        bool sendToRecycleBin = true,
        IProgress<(string Path, int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var list = folders.ToList();
        int total = list.Count;
        int completed = 0;
        int successful = 0;
        int failed = 0;
        long reclaimedBytes = 0;
        var failures = new ConcurrentBag<(string Path, string Error)>();

        // Pre-validate safety
        var safeFolders = new List<DiscoveredFolder>();
        foreach (var folder in list)
        {
            var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(folder.Path, _allowedFolderNames);
            if (!isSafe)
            {
                failures.Add((folder.Path, reason ?? "Safety validation failed."));
                Interlocked.Increment(ref failed);
                Interlocked.Increment(ref completed);
            }
            else
            {
                safeFolders.Add(folder);
            }
        }

        if (sendToRecycleBin && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Batch Recycle Bin in chunks of 50 paths to minimize shell overhead
            const int batchSize = 50;
            var chunks = safeFolders.Chunk(batchSize).ToList();

            await Task.Run(() =>
            {
                foreach (var chunk in chunks)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var paths = chunk.Select(c => c.Path).ToList();
                    bool batchOk = SendToRecycleBinBatch(paths);

                    if (batchOk)
                    {
                        foreach (var item in chunk)
                        {
                            Interlocked.Increment(ref successful);
                            Interlocked.Add(ref reclaimedBytes, item.SizeBytes);
                        }
                    }
                    else
                    {
                        // Fallback: try individual folders in the chunk
                        foreach (var item in chunk)
                        {
                            if (cancellationToken.IsCancellationRequested) break;

                            if (SendToRecycleBinBatch([item.Path]))
                            {
                                Interlocked.Increment(ref successful);
                                Interlocked.Add(ref reclaimedBytes, item.SizeBytes);
                            }
                            else
                            {
                                failures.Add((item.Path, "Unable to move folder to Windows Recycle Bin. Skipping permanent deletion for safety."));
                                Interlocked.Increment(ref failed);
                            }
                        }
                    }

                    int currentCompleted = Interlocked.Add(ref completed, chunk.Length);
                    progress?.Report((chunk.Last().Path, currentCompleted, total));
                }
            }, cancellationToken);
        }
        else if (sendToRecycleBin && (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)))
        {
            await Task.Run(() =>
            {
                foreach (var item in safeFolders)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    if (TryMoveToTrashUnix(item.Path))
                    {
                        Interlocked.Increment(ref successful);
                        Interlocked.Add(ref reclaimedBytes, item.SizeBytes);
                    }
                    else
                    {
                        failures.Add((item.Path, "Unable to move folder to system Trash. Skipping permanent deletion for safety."));
                        Interlocked.Increment(ref failed);
                    }

                    int current = Interlocked.Increment(ref completed);
                    if (current % 10 == 0 || current == total)
                    {
                        progress?.Report((item.Path, current, total));
                    }
                }
            }, cancellationToken);
        }
        else
        {
            // High-speed parallel permanent deletion across all CPU cores
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount * 2),
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(safeFolders, parallelOptions, (folder, ct) =>
            {
                try
                {
                    ForceDeleteDirectory(folder.Path);
                    Interlocked.Increment(ref successful);
                    Interlocked.Add(ref reclaimedBytes, folder.SizeBytes);
                }
                catch (Exception ex)
                {
                    failures.Add((folder.Path, ex.Message));
                    Interlocked.Increment(ref failed);
                }
                finally
                {
                    int current = Interlocked.Increment(ref completed);
                    // Throttle progress updates to avoid UI thread saturation
                    if (current % 10 == 0 || current == total)
                    {
                        progress?.Report((folder.Path, current, total));
                    }
                }

                return ValueTask.CompletedTask;
            });
        }

        progress?.Report((string.Empty, total, total));
        return new DeletionReport(total, successful, failed, reclaimedBytes, failures.ToList());
    }

    /// <summary>
    /// Robust, high-speed directory deletion.
    /// Fast-path tries direct deletion; fallback catches permission issues, clears read-only flags, and retries.
    /// </summary>
    public static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Fast path: direct recursive deletion
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception)
            {
                // Fallback: strip read-only attributes and retry
                try
                {
                    var di = new DirectoryInfo(path);
                    ClearReadOnlyAttributes(di);
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch when (attempt < maxRetries)
                {
                    // Brief delay to allow antivirus or search indexing file handles to close
                    Thread.Sleep(50 * attempt);
                }
            }
        }
    }

    private static void ClearReadOnlyAttributes(DirectoryInfo directory)
    {
        try
        {
            if (directory.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                directory.Attributes &= ~FileAttributes.ReadOnly;
            }

            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (var file in directory.EnumerateFiles("*", enumOptions))
            {
                try
                {
                    if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
                    {
                        file.Attributes &= ~FileAttributes.ReadOnly;
                    }
                }
                catch
                {
                    // Continue with remaining files
                }
            }

            foreach (var sub in directory.EnumerateDirectories("*", enumOptions))
            {
                try
                {
                    if (sub.Attributes.HasFlag(FileAttributes.ReadOnly))
                    {
                        sub.Attributes &= ~FileAttributes.ReadOnly;
                    }
                }
                catch
                {
                    // Continue with remaining directories
                }
            }
        }
        catch
        {
            // Best effort attribute clearing
        }
    }

    private static bool TryMoveToTrashUnix(string path)
    {
        if (!Directory.Exists(path)) return true;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "gio",
                    Arguments = $"trash \"{path}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(5000);
                    if (p.ExitCode == 0) return true;
                }

                psi.FileName = "trash-put";
                psi.Arguments = $"\"{path}\"";
                using var p2 = Process.Start(psi);
                if (p2 != null)
                {
                    p2.WaitForExit(5000);
                    if (p2.ExitCode == 0) return true;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var script = $"tell application \"Finder\" to delete POSIX file \"{path}\"";
                var psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"-e '{script}'",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(5000);
                    if (p.ExitCode == 0) return true;
                }
            }
        }
        catch
        {
            // Best-effort invocation of trash command
        }

        return false;
    }
}
