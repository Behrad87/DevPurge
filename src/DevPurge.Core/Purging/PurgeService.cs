using System.Runtime.InteropServices;
using DevPurge.Core.Models;
using DevPurge.Core.Scanning;

namespace DevPurge.Core.Purging;

/// <summary>
/// Handles safe deletion of target artifact directories.
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

    private static bool SendToRecycleBin(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        // SHFileOperation expects double null-terminated string
        var pathWithNull = path + "\0\0";
        var fileOp = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = pathWithNull,
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
    /// Purges a collection of discovered folders.
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
        var failures = new List<(string Path, string Error)>();

        await Task.Run(() =>
        {
            foreach (var folder in list)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    // 1. Safety validation re-check
                    var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(folder.Path, _allowedFolderNames);
                    if (!isSafe)
                    {
                        failures.Add((folder.Path, reason ?? "Safety validation failed."));
                        failed++;
                        continue;
                    }

                    if (!Directory.Exists(folder.Path))
                    {
                        // Folder already removed or no longer exists
                        completed++;
                        continue;
                    }

                    // 2. Perform deletion
                    bool deleted = false;
                    if (sendToRecycleBin)
                    {
                        deleted = SendToRecycleBin(folder.Path);
                    }

                    if (!deleted)
                    {
                        // Permanent deletion (or fallback if RecycleBin failed)
                        ForceDeleteDirectory(folder.Path);
                        deleted = true;
                    }

                    if (deleted)
                    {
                        successful++;
                        reclaimedBytes += folder.SizeBytes;
                    }
                }
                catch (Exception ex)
                {
                    failures.Add((folder.Path, ex.Message));
                    failed++;
                }
                finally
                {
                    completed++;
                    progress?.Report((folder.Path, completed, total));
                }
            }
        }, cancellationToken);

        return new DeletionReport(total, successful, failed, reclaimedBytes, failures);
    }

    /// <summary>
    /// Robust permanent directory deletion, clearing read-only attributes that cause UnauthorizedAccessException.
    /// </summary>
    public static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        var di = new DirectoryInfo(path);
        ClearReadOnlyAttributes(di);
        di.Delete(recursive: true);
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
