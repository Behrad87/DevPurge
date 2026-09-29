using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DevPurge.Core.Auditing;
using DevPurge.Core.Models;
using DevPurge.Core.Scanning;

namespace DevPurge.Core.Purging;

/// <summary>
/// High-speed safe deletion service for target artifact directories.
/// Supports high-throughput batch Recycle Bin operations and parallel permanent purging.
/// </summary>
public class PurgeService(IEnumerable<PurgeRule>? rules = null) : IPurgeService
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

    private readonly FrozenSet<string> _allowedFolderNames = (rules ?? PurgeRule.GetDefaultRules())
        .SelectMany(r => r.FolderNames)
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Purges target folders using parallel deletion and batched shell operations for maximum throughput.
    /// </summary>
    public async Task<DeletionReport> PurgeAsync(
        IEnumerable<DiscoveredFolder> folders,
        bool sendToRecycleBin = true,
        IProgress<(string Path, int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default,
        IAuditLogger? auditLogger = null,
        string? customAuditLogPath = null)
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
                            if (!Directory.Exists(item.Path))
                            {
                                Interlocked.Increment(ref successful);
                                Interlocked.Add(ref reclaimedBytes, item.SizeBytes);
                            }
                            else
                            {
                                failures.Add((item.Path, "Folder still exists after attempting to move to Windows Recycle Bin."));
                                Interlocked.Increment(ref failed);
                            }
                        }
                    }
                    else
                    {
                        // Fallback: try individual folders in the chunk
                        foreach (var item in chunk)
                        {
                            if (cancellationToken.IsCancellationRequested) break;

                            if (SendToRecycleBinBatch([item.Path]) && !Directory.Exists(item.Path))
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

                    if (TryMoveToTrashUnix(item.Path) && !Directory.Exists(item.Path))
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
        var report = new DeletionReport(total, successful, failed, reclaimedBytes, failures.ToList());

        if (auditLogger != null)
        {
            try
            {
                var targetRoots = list.Select(f => Path.GetDirectoryName(f.Path) ?? f.Path).Distinct().ToList();
                var purgedPaths = safeFolders.Select(f => f.Path).ToList();
                await auditLogger.LogPurgeAsync(report, sendToRecycleBin, isDryRun: false, targetRoots, purgedPaths, customAuditLogPath);
            }
            catch
            {
                // Never let audit logging failure fail the purge operation itself
            }
        }

        return report;
    }

    /// <summary>
    /// Performs non-destructive dry-run verification on target folders.
    /// Validates safety rules, existence, and checks for active file handle locks without deleting or altering files.
    /// </summary>
    public async Task<DryRunReport> SimulatePurgeAsync(
        IEnumerable<DiscoveredFolder> folders,
        CancellationToken cancellationToken = default)
    {
        var list = folders.ToList();
        var items = new List<DryRunItem>();
        int eligible = 0;
        int @unsafe = 0;
        int locked = 0;
        long eligibleBytes = 0;

        await Task.Run(() =>
        {
            foreach (var folder in list)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // 1. Validate safety
                var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(folder.Path, _allowedFolderNames);
                if (!isSafe)
                {
                    items.Add(new DryRunItem(folder.Path, folder.FolderName, folder.CategoryName, folder.SizeBytes, folder.FormattedSize, DryRunStatus.Unsafe, reason));
                    @unsafe++;
                    continue;
                }

                // 2. Validate existence
                if (!Directory.Exists(folder.Path))
                {
                    items.Add(new DryRunItem(folder.Path, folder.FolderName, folder.CategoryName, folder.SizeBytes, folder.FormattedSize, DryRunStatus.NotFound, "Directory not found on disk."));
                    continue;
                }

                // 3. Inspect accessibility and file locks (non-destructive check)
                var (isLocked, lockReason) = CheckDirectoryLock(folder.Path);
                if (isLocked)
                {
                    items.Add(new DryRunItem(folder.Path, folder.FolderName, folder.CategoryName, folder.SizeBytes, folder.FormattedSize, DryRunStatus.LockedOrInaccessible, lockReason));
                    locked++;
                    continue;
                }

                items.Add(new DryRunItem(folder.Path, folder.FolderName, folder.CategoryName, folder.SizeBytes, folder.FormattedSize, DryRunStatus.Eligible, null));
                eligible++;
                eligibleBytes += folder.SizeBytes;
            }
        }, cancellationToken);

        return new DryRunReport(list.Count, eligible, @unsafe, locked, eligibleBytes, items);
    }

    private static (bool IsLocked, string? Reason) CheckDirectoryLock(string path)
    {
        try
        {
            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = false,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            int checkedCount = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", enumOptions))
            {
                if (++checkedCount > 10) break;

                try
                {
                    var fi = new FileInfo(file);
                    var access = fi.Attributes.HasFlag(FileAttributes.ReadOnly) ? FileAccess.Read : FileAccess.ReadWrite;
                    using var stream = new FileStream(file, FileMode.Open, access, FileShare.None);
                }
                catch (UnauthorizedAccessException)
                {
                    var fi = new FileInfo(file);
                    if (!fi.Attributes.HasFlag(FileAttributes.ReadOnly))
                    {
                        return (true, $"File access denied: {Path.GetFileName(file)}");
                    }
                }
                catch (IOException ex) when (ex.HResult != 0)
                {
                    return (true, $"File locked by running process: {Path.GetFileName(file)}");
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            return (true, "Directory access denied (insufficient permissions).");
        }
        catch (Exception ex)
        {
            return (true, $"Lock check warning: {ex.Message}");
        }

        return (false, null);
    }

    /// <summary>
    /// Robust, high-speed directory deletion.
    /// Fast-path tries direct deletion; fallback catches permission issues, clears read-only flags, and retries.
    /// </summary>
    public static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        const int maxRetries = 3;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Fast path: direct recursive deletion
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                // Fallback: strip read-only attributes and retry
                try
                {
                    var di = new DirectoryInfo(path);
                    ClearReadOnlyAttributes(di);
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch (Exception retryEx)
                {
                    lastException = retryEx;
                    if (attempt < maxRetries)
                    {
                        // Brief delay to allow antivirus or search indexing file handles to close
                        Thread.Sleep(50 * attempt);
                    }
                }
            }
        }

        if (Directory.Exists(path))
        {
            throw new IOException($"Directory '{path}' could not be deleted after {maxRetries} attempts.", lastException);
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
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("trash");
                psi.ArgumentList.Add(path);
                using var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(5000);
                    if (p.ExitCode == 0) return true;
                }

                var psi2 = new ProcessStartInfo
                {
                    FileName = "trash-put",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi2.ArgumentList.Add(path);
                using var p2 = Process.Start(psi2);
                if (p2 != null)
                {
                    p2.WaitForExit(5000);
                    if (p2.ExitCode == 0) return true;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var script = $"tell application \"Finder\" to delete POSIX file \"{path.Replace("\"", "\\\"")}\"";
                var psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(script);
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
