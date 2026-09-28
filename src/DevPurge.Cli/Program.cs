using System.Reflection;
using System.Text.Json;
using DevPurge.Core.Auditing;
using DevPurge.Core.Models;
using DevPurge.Core.Purging;
using DevPurge.Core.Scanning;

namespace DevPurge.Cli;

public record CliOptions
{
    public List<string> Paths { get; init; } = [];
    public bool Clean { get; init; }
    public bool DryRun { get; init; }
    public int MinAgeDays { get; init; }
    public long MinSizeBytes { get; init; }
    public int TopCount { get; init; }
    public bool Yes { get; init; }
    public bool Permanent { get; init; }
    public bool Silent { get; init; }
    public bool Json { get; init; }
    public bool ShowHelp { get; init; }
    public bool ShowVersion { get; init; }
    public string? FilterType { get; init; }
    public List<string> Exclusions { get; init; } = [];
    public string? AuditLogPath { get; init; }
    public bool NoAudit { get; init; }
    public bool ShowAuditHistory { get; init; }
    public bool VerifyDryRun { get; init; }
    public string? ExportPath { get; init; }
    public bool NoConfig { get; init; }
}

public class Program
{
    private static readonly object ConsoleLock = new();

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0";

    public static async Task<int> Main(string[] args)
    {
        var options = ParseCommandLine(args);

        if (options.ShowVersion)
        {
            Console.WriteLine($"DevPurge v{Version}");
            return 0;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (options.ShowAuditHistory)
        {
            return await PrintAuditHistoryAsync(options);
        }

        return await RunScanOrPurgeAsync(options);
    }

    private static bool SupportsColor =>
        Environment.GetEnvironmentVariable("NO_COLOR") == null &&
        !Console.IsOutputRedirected;

    private static void SetColor(ConsoleColor color)
    {
        if (SupportsColor)
        {
            Console.ForegroundColor = color;
        }
    }

    private static void ResetColor()
    {
        if (SupportsColor)
        {
            Console.ResetColor();
        }
    }

    private static void PrintBanner()
    {
        SetColor(ConsoleColor.Cyan);
        Console.WriteLine(@"  ____             ____                                 ");
        Console.WriteLine(@" |  _ \  _____   _|  _ \ _   _ _ __ __ _  ___           ");
        Console.WriteLine(@" | | | |/ _ \ \ / / |_) | | | | '__/ _` |/ _ \          ");
        Console.WriteLine(@" | |_| |  __/\ V /|  __/| |_| | | | (_| |  __/          ");
        Console.WriteLine(@" |____/ \___| \_/ |_|    \__,_|_|  \__, |\___|          ");
        Console.WriteLine(@"                                   |___/                ");
        ResetColor();
        Console.WriteLine($" Developer Disk Reclaimer v{Version}");
        Console.WriteLine(" GitHub: https://github.com/Behrad87/DevPurge");
        Console.WriteLine(new string('-', 60));
    }

    private static void PrintHelp()
    {
        PrintBanner();
        Console.WriteLine("USAGE:");
        Console.WriteLine("  DevPurge.Cli [options] [paths...]");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("  -p, --path <dir>     Root folder(s) to scan (comma-separated, multiple, or positional)");
        Console.WriteLine("  -c, --clean          Perform actual purge (default is safe dry-run preview)");
        Console.WriteLine("  -d, --dry-run        Explicit dry-run preview without deleting anything");
        Console.WriteLine("  -m, --min-age <days> Only purge folders untouched for >= N days (default: 0)");
        Console.WriteLine("  -z, --min-size <sz>  Only purge folders with size >= minimum (e.g. 50MB, 1GB)");
        Console.WriteLine("  -t, --type <name>    Filter by ecosystem or artifact type (e.g. node, dotnet, rust, python)");
        Console.WriteLine("  -x, --exclude <dir>  Exclude specific folders or pattern from scanning/purging");
        Console.WriteLine("  --top <count>        Limit to top N largest discovered folders");
        Console.WriteLine("  -y, --yes            Automatic non-interactive confirmation");
        Console.WriteLine("  --verify             Simulate purge and verify file locks / safety without deleting");
        Console.WriteLine("  --audit-log <path>   Custom path for audit log records (default: %LOCALAPPDATA%/DevPurge/logs)");
        Console.WriteLine("  --no-audit           Disable recording purge actions to the audit log");
        Console.WriteLine("  --audit, --history   Display recent purge audit records");
        Console.WriteLine("  --export <file>      Export scan report to CSV, JSON, or Markdown file");
        Console.WriteLine("  --no-config          Ignore user custom rules and use built-in defaults");
        Console.WriteLine("  -j, --json           Output scan results or deletion report as JSON");
        Console.WriteLine("  -s, --silent         Quiet output (no banners, only errors or JSON)");
        Console.WriteLine("  -v, --version        Show version information");
        Console.WriteLine("  -h, --help           Show this help information");
        Console.WriteLine();
        Console.WriteLine("EXAMPLES:");
        Console.WriteLine("  DevPurge.Cli D:\\repos");
        Console.WriteLine("  DevPurge.Cli -p D:\\repos -c -m 14");
        Console.WriteLine("  DevPurge.Cli -p D:\\repos -c -z 100MB");
        Console.WriteLine("  DevPurge.Cli -p D:\\repos -c -t node");
        Console.WriteLine("  DevPurge.Cli C:\\repos,D:\\repos --json");
        Console.WriteLine();
        SetColor(ConsoleColor.Yellow);
        Console.WriteLine("Support this free open-source project:");
        Console.WriteLine("  GitHub Sponsors: https://github.com/sponsors/Behrad87");
        Console.WriteLine("  Ko-fi:           https://ko-fi.com/behrad87");
        Console.WriteLine("  Reymit (Iran):   https://reymit.ir/behrad87");
        ResetColor();
    }

    private static async Task<int> RunScanOrPurgeAsync(CliOptions options)
    {
        bool silent = options.Silent || options.Json;
        bool isClean = options.Clean && !options.DryRun;
        bool permanent = options.Permanent;
        int minAgeDays = options.MinAgeDays;

        if (!silent)
        {
            PrintBanner();
        }

        var paths = ResolveTargetPaths(options.Paths);
        if (paths.Count == 0)
        {
            if (options.Json)
            {
                var errorObj = new { error = "No valid target directories to scan. Specify --path <dir>." };
                Console.WriteLine(JsonSerializer.Serialize(errorObj, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (!silent)
            {
                SetColor(ConsoleColor.Red);
                Console.WriteLine("No valid target directories to scan. Use --path <dir>.");
                ResetColor();
            }
            return 1;
        }

        if (!silent)
        {
            Console.WriteLine($"Scanning target paths: {string.Join(", ", paths)}");
            var filtersDesc = new List<string> { $"Min Age: {minAgeDays}d" };
            if (options.MinSizeBytes > 0) filtersDesc.Add($"Min Size: {DiscoveredFolder.FormatByteSize(options.MinSizeBytes)}");
            if (options.TopCount > 0) filtersDesc.Add($"Top: {options.TopCount}");
            Console.WriteLine($"Mode: {(isClean ? "PURGE" : "DRY-RUN (Safe Preview)")} | {string.Join(" | ", filtersDesc)} | Deletion: {(permanent ? "Permanent" : "Recycle Bin")}");
            Console.WriteLine("Scanning in progress...");
        }

        var settingsManager = new DevPurge.Core.Configuration.UserSettingsManager();
        var effectiveRules = options.NoConfig ? PurgeRule.GetDefaultRules() : settingsManager.GetEffectiveRules();
        var scanner = new FastDirectoryScanner(effectiveRules);
        var progress = (silent || Console.IsOutputRedirected) ? null : new Progress<ScanProgress>(p =>
        {
            if (!p.IsCompleted && !string.IsNullOrEmpty(p.CurrentPath))
            {
                lock (ConsoleLock)
                {
                    try
                    {
                        var msg = $"Discovered: {p.DiscoveredCount} folders ({DiscoveredFolder.FormatByteSize(p.TotalBytesFound)})";
                        int width = Console.WindowWidth > 1 ? Console.WindowWidth - 1 : 70;
                        Console.Write($"\r{msg.PadRight(width)}");
                    }
                    catch
                    {
                        // Ignore console buffer query failure
                    }
                }
            }
        });

        var results = await scanner.ScanAsync(paths, progress, cancellationToken: default, exclusions: options.Exclusions);

        if (!silent && !Console.IsOutputRedirected)
        {
            lock (ConsoleLock)
            {
                Console.WriteLine();
            }
        }

        if (minAgeDays > 0)
        {
            results = results.Where(r => r.AgeDays >= minAgeDays).ToList();
        }

        if (options.MinSizeBytes > 0)
        {
            results = results.Where(r => r.SizeBytes >= options.MinSizeBytes).ToList();
        }

        if (!string.IsNullOrWhiteSpace(options.FilterType))
        {
            results = results.Where(r =>
                r.CategoryName.Contains(options.FilterType, StringComparison.OrdinalIgnoreCase) ||
                r.ArtifactType.ToString().Contains(options.FilterType, StringComparison.OrdinalIgnoreCase) ||
                r.FolderName.Equals(options.FilterType, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        if (options.Exclusions.Count > 0)
        {
            results = results.Where(r =>
                !options.Exclusions.Any(ex => r.Path.Contains(ex, StringComparison.OrdinalIgnoreCase) ||
                                              r.FolderName.Equals(ex, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }

        if (options.TopCount > 0 && results.Count > options.TopCount)
        {
            results = results.Take(options.TopCount).ToList();
        }

        long totalBytes = results.Sum(r => r.SizeBytes);
        int totalFiles = results.Sum(r => r.FileCount);

        if (!silent)
        {
            Console.WriteLine(new string('-', 60));
            Console.WriteLine($"Scan complete: Found {results.Count} artifact folders ({DiscoveredFolder.FormatByteSize(totalBytes)} across {totalFiles:N0} files)");

            int displayLimit = options.TopCount > 0 ? options.TopCount : 15;
            foreach (var item in results.Take(displayLimit))
            {
                Console.WriteLine($" • [{item.CategoryName}] {item.FormattedSize,-10} {item.FormattedAge,-12} {item.Path}");
            }

            if (options.TopCount == 0 && results.Count > 15)
            {
                Console.WriteLine($" ... and {results.Count - 15} more folders.");
            }

            var breakdown = results
                .GroupBy(r => r.CategoryName)
                .OrderByDescending(g => g.Sum(x => x.SizeBytes))
                .ToList();

            if (breakdown.Count > 1)
            {
                Console.WriteLine();
                Console.WriteLine("Breakdown by Ecosystem:");
                foreach (var group in breakdown)
                {
                    long groupBytes = group.Sum(x => x.SizeBytes);
                    Console.WriteLine($" • {group.Key,-24} {DiscoveredFolder.FormatByteSize(groupBytes),10} ({group.Count()} folders)");
                }
            }
            Console.WriteLine(new string('-', 60));
        }

        var purgeService = new PurgeService(effectiveRules);

        if (results.Count == 0)
        {
            if (options.Json)
            {
                var jsonEmpty = new
                {
                    mode = isClean ? "purge" : "dry-run",
                    targetPaths = paths,
                    folderCount = 0,
                    totalFiles = 0,
                    totalBytes = 0L,
                    formattedTotalSize = "0 B",
                    message = "No disposable artifact folders found."
                };
                Console.WriteLine(JsonSerializer.Serialize(jsonEmpty, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (!silent)
            {
                Console.WriteLine("No disposable artifact folders found.");
            }

            if (!string.IsNullOrWhiteSpace(options.ExportPath))
            {
                try
                {
                    var metadata = new DevPurge.Core.Exporting.ExportReportMetadata(string.Join(", ", paths), DateTime.UtcNow, Version);
                    await DevPurge.Core.Exporting.ScanReportExporter.ExportToFileAsync(options.ExportPath, results, metadata);
                }
                catch { }
            }

            return 0;
        }

        if (!isClean)
        {
            var dryRunReport = await purgeService.SimulatePurgeAsync(results);

            if (options.VerifyDryRun && !options.NoAudit)
            {
                try
                {
                    var auditLogger = new AuditLogger(options.AuditLogPath);
                    await auditLogger.LogDryRunAsync(dryRunReport, paths, options.AuditLogPath);
                }
                catch { }
            }

            if (options.Json)
            {
                var jsonResult = new
                {
                    mode = "dry-run",
                    targetPaths = paths,
                    folderCount = results.Count,
                    totalFiles = totalFiles,
                    totalBytes = totalBytes,
                    formattedTotalSize = DiscoveredFolder.FormatByteSize(totalBytes),
                    minSizeBytes = options.MinSizeBytes,
                    minAgeDays = options.MinAgeDays,
                    verification = new
                    {
                        allEligible = dryRunReport.AllEligible,
                        totalEvaluated = dryRunReport.TotalEvaluated,
                        eligibleCount = dryRunReport.EligibleCount,
                        unsafeCount = dryRunReport.UnsafeCount,
                        lockedCount = dryRunReport.LockedCount,
                        eligibleBytes = dryRunReport.EligibleBytes,
                        formattedEligibleSize = dryRunReport.FormattedEligibleSize,
                        issues = dryRunReport.Items
                            .Where(i => i.Status != DryRunStatus.Eligible)
                            .Select(i => new { path = i.Path, status = i.Status.ToString(), reason = i.Reason })
                    },
                    breakdown = results
                        .GroupBy(r => r.CategoryName)
                        .OrderByDescending(g => g.Sum(x => x.SizeBytes))
                        .Select(g => new
                        {
                            category = g.Key,
                            folderCount = g.Count(),
                            totalBytes = g.Sum(x => x.SizeBytes),
                            formattedSize = DiscoveredFolder.FormatByteSize(g.Sum(x => x.SizeBytes))
                        }),
                    folders = results.Select(r => new
                    {
                        path = r.Path,
                        folderName = r.FolderName,
                        category = r.CategoryName,
                        artifactType = r.ArtifactType.ToString(),
                        sizeBytes = r.SizeBytes,
                        formattedSize = r.FormattedSize,
                        fileCount = r.FileCount,
                        ageDays = Math.Round(r.AgeDays, 1),
                        formattedAge = r.FormattedAge
                    })
                };
                Console.WriteLine(JsonSerializer.Serialize(jsonResult, new JsonSerializerOptions { WriteIndented = true }));
            }
            else if (!silent)
            {
                SetColor(ConsoleColor.Yellow);
                if (dryRunReport.AllEligible)
                {
                    Console.WriteLine($"[Dry-Run Verified] All {results.Count} folders eligible for safe purge ({dryRunReport.FormattedEligibleSize}, {totalFiles:N0} files). 0 conflicts.");
                }
                else
                {
                    Console.WriteLine($"[Dry-Run Verified] {dryRunReport.EligibleCount}/{results.Count} folders eligible for safe purge ({dryRunReport.FormattedEligibleSize}).");
                    if (dryRunReport.UnsafeCount > 0 || dryRunReport.LockedCount > 0)
                    {
                        SetColor(ConsoleColor.Red);
                        Console.WriteLine($"⚠️ Safety & Lock Warnings ({dryRunReport.UnsafeCount} failed safety rails, {dryRunReport.LockedCount} locked by active processes):");
                        foreach (var issue in dryRunReport.Items.Where(i => i.Status != DryRunStatus.Eligible).Take(5))
                        {
                            Console.WriteLine($"  • [{issue.Status}] {issue.Path}: {issue.Reason}");
                        }
                        SetColor(ConsoleColor.Yellow);
                    }
                }
                Console.WriteLine("To purge these directories, re-run with --clean flag.");
                ResetColor();
            }

            if (!string.IsNullOrWhiteSpace(options.ExportPath))
            {
                try
                {
                    var metadata = new DevPurge.Core.Exporting.ExportReportMetadata(string.Join(", ", paths), DateTime.UtcNow, Version);
                    await DevPurge.Core.Exporting.ScanReportExporter.ExportToFileAsync(options.ExportPath, results, metadata);
                    if (!silent)
                    {
                        Console.WriteLine($"Report exported to: {options.ExportPath}");
                    }
                }
                catch (Exception ex)
                {
                    if (!silent)
                    {
                        SetColor(ConsoleColor.Red);
                        Console.WriteLine($"Failed to export report: {ex.Message}");
                        ResetColor();
                    }
                }
            }

            return 0;
        }

        // Interactive Confirmation before Purging
        if (!options.Yes && !silent && !Console.IsInputRedirected)
        {
            SetColor(ConsoleColor.Yellow);
            Console.Write($"Are you sure you want to {(permanent ? "PERMANENTLY delete" : "move to Recycle Bin")} {results.Count} folders ({DiscoveredFolder.FormatByteSize(totalBytes)})? [y/N]: ");
            ResetColor();
            var response = Console.ReadLine()?.Trim();
            if (!string.Equals(response, "y", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Purge aborted by user.");
                return 0;
            }
        }

        // Perform Purge
        if (!silent)
        {
            SetColor(ConsoleColor.Magenta);
            Console.WriteLine($"Purging {results.Count} folders (Total: {DiscoveredFolder.FormatByteSize(totalBytes)}, {totalFiles:N0} files)...");
            ResetColor();
        }

        var purgeProgress = (silent || Console.IsOutputRedirected) ? null : new Progress<(string Path, int Completed, int Total)>(p =>
        {
            lock (ConsoleLock)
            {
                var folderName = Path.GetFileName(p.Path);
                var msg = $"Purging: {p.Completed}/{p.Total} ({folderName})";
                try
                {
                    int width = Console.WindowWidth > 1 ? Console.WindowWidth - 1 : 70;
                    Console.Write($"\r{msg.PadRight(width)}");
                }
                catch
                {
                    // Ignore console buffer query failure
                }
            }
        });

        var purgeAuditLogger = options.NoAudit ? null : new AuditLogger(options.AuditLogPath);
        var report = await purgeService.PurgeAsync(
            results,
            sendToRecycleBin: !permanent,
            purgeProgress,
            cancellationToken: default,
            auditLogger: purgeAuditLogger,
            customAuditLogPath: options.AuditLogPath
        );

        if (!silent && !Console.IsOutputRedirected)
        {
            lock (ConsoleLock)
            {
                Console.WriteLine();
            }
        }

        if (options.Json)
        {
            var jsonReport = new
            {
                mode = "purge",
                totalRequested = report.TotalRequested,
                successfulCount = report.SuccessfulCount,
                failedCount = report.FailedCount,
                totalFiles = totalFiles,
                reclaimedBytes = report.ReclaimedBytes,
                formattedReclaimedSize = report.FormattedReclaimedSize,
                isSuccess = report.IsSuccess,
                failures = report.Failures.Select(f => new { path = f.Path, error = f.Error })
            };
            Console.WriteLine(JsonSerializer.Serialize(jsonReport, new JsonSerializerOptions { WriteIndented = true }));
        }
        else if (!silent)
        {
            Console.WriteLine();
            SetColor(ConsoleColor.Green);
            Console.WriteLine($"[Purge Complete] Reclaimed: {report.FormattedReclaimedSize} | Successfully removed: {report.SuccessfulCount}/{report.TotalRequested}");
            ResetColor();

            if (purgeAuditLogger != null)
            {
                Console.WriteLine($"Audit log recorded: {options.AuditLogPath ?? AuditLogger.GetDefaultLogPath()}");
            }

            if (report.FailedCount > 0)
            {
                SetColor(ConsoleColor.Red);
                Console.WriteLine($"Failed to remove {report.FailedCount} folders:");
                foreach (var (path, err) in report.Failures.Take(5))
                {
                    Console.WriteLine($"  - {path}: {err}");
                }
                ResetColor();
            }

            Console.WriteLine();
            Console.WriteLine("Enjoying DevPurge? Consider starring or supporting:");
            Console.WriteLine("  ⭐ GitHub:        https://github.com/Behrad87/DevPurge");
            Console.WriteLine("  ☕ Ko-fi:         https://ko-fi.com/behrad87");
            Console.WriteLine("  🇮🇷 Reymit (Iran): https://reymit.ir/behrad87");
        }

        if (!string.IsNullOrWhiteSpace(options.ExportPath))
        {
            try
            {
                var metadata = new DevPurge.Core.Exporting.ExportReportMetadata(string.Join(", ", paths), DateTime.UtcNow, Version);
                await DevPurge.Core.Exporting.ScanReportExporter.ExportToFileAsync(options.ExportPath, results, metadata);
                if (!silent)
                {
                    Console.WriteLine($"Report exported to: {options.ExportPath}");
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    SetColor(ConsoleColor.Red);
                    Console.WriteLine($"Failed to export report: {ex.Message}");
                    ResetColor();
                }
            }
        }

        return report.FailedCount == 0 ? 0 : 2;
    }

    public static List<string> ResolveTargetPaths(List<string> configuredPaths)
    {
        var result = new List<string>();

        if (configuredPaths.Count > 0)
        {
            foreach (var p in configuredPaths)
            {
                // Support comma- and semicolon-separated paths
                var split = p.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var s in split)
                {
                    if (Directory.Exists(s))
                    {
                        result.Add(Path.GetFullPath(s));
                    }
                }
            }
        }
        else
        {
            // Auto-detect common workspace roots only if no paths were explicitly specified
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] defaultCandidates =
            [
                @"D:\repos",
                @"C:\repos",
                Path.Combine(userProfile, "source", "repos"),
                Path.Combine(userProfile, "repos"),
                Path.Combine(userProfile, "projects"),
                Path.Combine(userProfile, "Development"),
                Path.Combine(userProfile, "workspace"),
                Directory.GetCurrentDirectory()
            ];

            foreach (var candidate in defaultCandidates)
            {
                if (Directory.Exists(candidate))
                {
                    result.Add(Path.GetFullPath(candidate));
                    break;
                }
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static CliOptions ParseCommandLine(string[] args)
    {
        var rawDict = ParseArguments(args);
        var paths = new List<string>();

        // Collect paths from --path / -p flags
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg.Equals("--path", StringComparison.OrdinalIgnoreCase) || arg.Equals("-p", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                paths.Add(args[++i]);
            }
            else if (arg.StartsWith("--path=", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("-p=", StringComparison.OrdinalIgnoreCase))
            {
                var val = arg.Split('=', 2)[1];
                if (!string.IsNullOrWhiteSpace(val)) paths.Add(val);
            }
        }

        // Collect positional arguments (any non-flag args not consumed by flags)
        var knownValueFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "path", "p",
            "min-age", "minage", "age", "m", "a",
            "min-size", "minsize", "size", "z",
            "top", "limit",
            "type", "t", "category",
            "exclude", "x", "ignore",
            "audit-log", "auditlog",
            "export", "output", "out", "e"
        };

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("-"))
            {
                var cleanName = arg.TrimStart('-');
                if (cleanName.Contains('='))
                {
                    continue;
                }
                if (knownValueFlags.Contains(cleanName) && i + 1 < args.Length)
                {
                    i++; // Skip the value of the flag
                }
            }
            else
            {
                paths.Add(arg);
            }
        }

        if (paths.Count == 0 && rawDict.TryGetValue("path", out var singlePath))
        {
            paths.Add(singlePath);
        }

        // Collect exclusions from --exclude / -x / --ignore
        var exclusions = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg.Equals("--exclude", StringComparison.OrdinalIgnoreCase) ||
                 arg.Equals("-x", StringComparison.OrdinalIgnoreCase) ||
                 arg.Equals("--ignore", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                var val = args[++i];
                var split = val.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                exclusions.AddRange(split);
            }
            else if (arg.StartsWith("--exclude=", StringComparison.OrdinalIgnoreCase) ||
                     arg.StartsWith("-x=", StringComparison.OrdinalIgnoreCase) ||
                     arg.StartsWith("--ignore=", StringComparison.OrdinalIgnoreCase))
            {
                var val = arg.Split('=', 2)[1];
                var split = val.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                exclusions.AddRange(split);
            }
        }

        string? filterType = null;
        if (rawDict.TryGetValue("type", out var typeVal) ||
            rawDict.TryGetValue("t", out typeVal) ||
            rawDict.TryGetValue("category", out typeVal))
        {
            filterType = typeVal;
        }

        int minAge = 0;
        if (rawDict.TryGetValue("min-age", out var ageStr) ||
            rawDict.TryGetValue("minage", out ageStr) ||
            rawDict.TryGetValue("age", out ageStr) ||
            rawDict.TryGetValue("m", out ageStr) ||
            rawDict.TryGetValue("a", out ageStr))
        {
            int.TryParse(ageStr, out minAge);
        }

        long minSizeBytes = 0;
        if (rawDict.TryGetValue("min-size", out var sizeStr) ||
            rawDict.TryGetValue("minsize", out sizeStr) ||
            rawDict.TryGetValue("size", out sizeStr) ||
            rawDict.TryGetValue("z", out sizeStr))
        {
            TryParseByteSize(sizeStr, out minSizeBytes);
        }

        int topCount = 0;
        if (rawDict.TryGetValue("top", out var topStr) ||
            rawDict.TryGetValue("limit", out topStr))
        {
            int.TryParse(topStr, out topCount);
        }

        bool yesFlag = rawDict.ContainsKey("yes") || rawDict.ContainsKey("y");

        string? auditLogPath = null;
        if (rawDict.TryGetValue("audit-log", out var logVal) ||
            rawDict.TryGetValue("auditlog", out logVal))
        {
            auditLogPath = logVal;
        }

        bool noAudit = rawDict.ContainsKey("no-audit") || rawDict.ContainsKey("noaudit");
        bool showAuditHistory = rawDict.ContainsKey("audit") ||
                                rawDict.ContainsKey("audit-history") ||
                                rawDict.ContainsKey("audithistory") ||
                                rawDict.ContainsKey("history");
        bool verifyDryRun = rawDict.ContainsKey("verify") || rawDict.ContainsKey("verify-dry-run");

        string? exportPath = null;
        if (rawDict.TryGetValue("export", out var expVal) ||
            rawDict.TryGetValue("output", out expVal) ||
            rawDict.TryGetValue("out", out expVal) ||
            rawDict.TryGetValue("e", out expVal))
        {
            exportPath = expVal;
        }

        bool noConfig = rawDict.ContainsKey("no-config") || rawDict.ContainsKey("noconfig");

        return new CliOptions
        {
            Paths = paths,
            Clean = rawDict.ContainsKey("clean") || rawDict.ContainsKey("c") || (yesFlag && !rawDict.ContainsKey("dry-run")),
            DryRun = rawDict.ContainsKey("dry-run") || rawDict.ContainsKey("dryrun") || rawDict.ContainsKey("d"),
            MinAgeDays = Math.Max(0, minAge),
            MinSizeBytes = Math.Max(0, minSizeBytes),
            TopCount = Math.Max(0, topCount),
            Yes = yesFlag,
            Permanent = rawDict.ContainsKey("permanent"),
            Silent = rawDict.ContainsKey("silent") || rawDict.ContainsKey("s"),
            Json = rawDict.ContainsKey("json") || rawDict.ContainsKey("j"),
            ShowHelp = rawDict.ContainsKey("help") || rawDict.ContainsKey("h") || rawDict.ContainsKey("?"),
            ShowVersion = rawDict.ContainsKey("version") || rawDict.ContainsKey("v"),
            FilterType = filterType,
            Exclusions = exclusions,
            AuditLogPath = auditLogPath,
            NoAudit = noAudit,
            ShowAuditHistory = showAuditHistory,
            VerifyDryRun = verifyDryRun,
            ExportPath = exportPath,
            NoConfig = noConfig
        };
    }

    /// <summary>
    /// Parses human-readable byte sizes (e.g. 100MB, 1.5GB, 500KB, 1048576) into raw bytes.
    /// </summary>
    public static bool TryParseByteSize(string? input, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        input = input.Trim();
        string clean = input.Replace(" ", "");

        long multiplier = 1;
        string numPart = clean;

        if (clean.EndsWith("tb", StringComparison.OrdinalIgnoreCase) || clean.EndsWith("t", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1024L * 1024 * 1024 * 1024;
            numPart = clean.EndsWith("tb", StringComparison.OrdinalIgnoreCase) ? clean[..^2] : clean[..^1];
        }
        else if (clean.EndsWith("gb", StringComparison.OrdinalIgnoreCase) || clean.EndsWith("g", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1024L * 1024 * 1024;
            numPart = clean.EndsWith("gb", StringComparison.OrdinalIgnoreCase) ? clean[..^2] : clean[..^1];
        }
        else if (clean.EndsWith("mb", StringComparison.OrdinalIgnoreCase) || clean.EndsWith("m", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1024L * 1024;
            numPart = clean.EndsWith("mb", StringComparison.OrdinalIgnoreCase) ? clean[..^2] : clean[..^1];
        }
        else if (clean.EndsWith("kb", StringComparison.OrdinalIgnoreCase) || clean.EndsWith("k", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1024L;
            numPart = clean.EndsWith("kb", StringComparison.OrdinalIgnoreCase) ? clean[..^2] : clean[..^1];
        }
        else if (clean.EndsWith("b", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1;
            numPart = clean[..^1];
        }

        if (double.TryParse(numPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsedVal))
        {
            if (parsedVal >= 0)
            {
                bytes = (long)(parsedVal * multiplier);
                return true;
            }
        }

        bytes = 0;
        return false;
    }

    public static Dictionary<string, string> ParseArguments(string[] args)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var booleanFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "clean", "c",
            "dry-run", "dryrun", "d",
            "yes", "y",
            "permanent",
            "silent", "s",
            "json", "j",
            "help", "h", "?",
            "version", "v",
            "no-audit", "noaudit",
            "audit", "audit-history", "audithistory", "history",
            "verify", "verify-dry-run",
            "no-config", "noconfig"
        };

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--"))
            {
                var key = arg[2..];
                if (key.Contains('='))
                {
                    var parts = key.Split('=', 2);
                    dict[parts[0]] = parts[1];
                }
                else if (booleanFlags.Contains(key))
                {
                    dict[key] = "true";
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    dict[key] = args[++i];
                }
                else
                {
                    dict[key] = "true";
                }
            }
            else if (arg.StartsWith("-"))
            {
                var key = arg[1..];
                if (key.Contains('='))
                {
                    var parts = key.Split('=', 2);
                    dict[parts[0]] = parts[1];
                }
                else if (booleanFlags.Contains(key))
                {
                    dict[key] = "true";
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    dict[key] = args[++i];
                }
                else
                {
                    dict[key] = "true";
                }
            }
        }
        return dict;
    }

    private static async Task<int> PrintAuditHistoryAsync(CliOptions options)
    {
        var logger = new AuditLogger(options.AuditLogPath);
        var entries = await logger.GetRecentEntriesAsync(30, options.AuditLogPath);

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        if (!options.Silent)
        {
            PrintBanner();
            Console.WriteLine("Recent Purge Audit Records:");
            Console.WriteLine(new string('-', 75));
        }

        if (entries.Count == 0)
        {
            Console.WriteLine("No audit history found.");
            return 0;
        }

        foreach (var entry in entries)
        {
            var dateStr = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var modeTag = entry.IsDryRun ? "[Dry-Run]" : $"[{entry.Mode}]";
            SetColor(entry.IsDryRun ? ConsoleColor.Yellow : ConsoleColor.Green);
            Console.Write($"{dateStr} {modeTag,-12} ");
            ResetColor();
            Console.WriteLine($"Reclaimed: {entry.FormattedReclaimedSize,-10} Success: {entry.SuccessfulCount}/{entry.TotalRequested} (User: {entry.User})");
            if (entry.PurgedPaths.Count > 0)
            {
                foreach (var p in entry.PurgedPaths.Take(3))
                {
                    Console.WriteLine($"   • {p}");
                }
                if (entry.PurgedPaths.Count > 3)
                {
                    Console.WriteLine($"   ... and {entry.PurgedPaths.Count - 3} more paths.");
                }
            }
        }
        Console.WriteLine(new string('-', 75));
        return 0;
    }
}
