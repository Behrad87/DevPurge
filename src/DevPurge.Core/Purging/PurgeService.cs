using System.Collections.Concurrent;
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

    private readonly HashSet<string> _allowedFolderNames;

    public PurgeService(IEnumerable<PurgeRule>? rules = null)
    {
        var activeRules = rules ?? PurgeRule.GetDefaultRules();
        _allowedFolderNames = new HashSet<string>(
            activeRules.SelectMany(r => r.FolderNames),
            StringComparer.OrdinalIgnoreCase
        );
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
            // Batch Recycle Bin in chunks of 50 paths to avoid shell overhead
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
                        // Fallback: permanent parallel delete for any items that failed Recycle Bin
                        Parallel.ForEach(chunk, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, item =>
                        {
                            try
                            {
                                ForceDeleteDirectory(item.Path);
                                Interlocked.Increment(ref successful);
                                Interlocked.Add(ref reclaimedBytes, item.SizeBytes);
                            }
                            catch (Exception ex)
                            {
                                failures.Add((item.Path, ex.Message));
                                Interlocked.Increment(ref failed);
                            }
                        });
                    }

                    int currentCompleted = Interlocked.Add(ref completed, chunk.Length);
                    progress?.Report((chunk.Last().Path, currentCompleted, total));
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
    /// Fast-path tries direct deletion; fallback catches permission issues and clears read-only flags.
    /// </summary>
    public static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            // Fast path: direct recursive deletion (instant for 99% of folders)
            Directory.Delete(path, recursive: true);
        }
        catch (Exception)
        {
            // Fallback: strip read-only attributes and retry
            var di = new DirectoryInfo(path);
            ClearReadOnlyAttributes(di);
            Directory.Delete(path, recursive: true);
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

            foreach (var file in directory.EnumerateFiles())
            {
                if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    file.Attributes &= ~FileAttributes.ReadOnly;
                }
            }

            foreach (var sub in directory.EnumerateDirectories())
            {
                ClearReadOnlyAttributes(sub);
            }
        }
        catch
        {
            // Best effort attribute clearing
        }
    }
}
