using System.Reflection;
using System.Text.Json;
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
    public bool Permanent { get; init; }
    public bool Silent { get; init; }
    public bool Json { get; init; }
    public bool ShowHelp { get; init; }
    public bool ShowVersion { get; init; }
    public string? FilterType { get; init; }
    public List<string> Exclusions { get; init; } = [];
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

        return await RunScanOrPurgeAsync(options);
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"  ____             ____                                 ");
        Console.WriteLine(@" |  _ \  _____   _|  _ \ _   _ _ __ __ _  ___           ");
        Console.WriteLine(@" | | | |/ _ \ \ / / |_) | | | | '__/ _` |/ _ \          ");
        Console.WriteLine(@" | |_| |  __/\ V /|  __/| |_| | | | (_| |  __/          ");
        Console.WriteLine(@" |____/ \___| \_/ |_|    \__,_|_|  \__, |\___|          ");
        Console.WriteLine(@"                                   |___/                ");
        Console.ResetColor();
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
        Console.WriteLine("  -t, --type <name>    Filter by ecosystem or artifact type (e.g. node, dotnet, rust, python)");
        Console.WriteLine("  -x, --exclude <dir>  Exclude specific folders or pattern from scanning/purging");
        Console.WriteLine("  --permanent          Permanently delete (default: send to Recycle Bin)");
        Console.WriteLine("  -j, --json           Output scan results or deletion report as JSON");
        Console.WriteLine("  -s, --silent         Quiet output (no banners, only errors or JSON)");
        Console.WriteLine("  -v, --version        Show version information");
        Console.WriteLine("  -h, --help           Show this help information");
        Console.WriteLine();
        Console.WriteLine("EXAMPLES:");
        Console.WriteLine("  DevPurge.Cli D:\\repos");
        Console.WriteLine("  DevPurge.Cli -p D:\\repos -c -m 14");
        Console.WriteLine("  DevPurge.Cli -p D:\\repos -c -t node");
        Console.WriteLine("  DevPurge.Cli C:\\repos,D:\\repos --json");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Support this free open-source project:");
        Console.WriteLine("  GitHub Sponsors: https://github.com/sponsors/Behrad87");
        Console.WriteLine("  Ko-fi:           https://ko-fi.com/behrad87");
        Console.WriteLine("  Reymit (Iran):   https://reymit.ir/behrad87");
        Console.ResetColor();
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
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("No valid target directories to scan. Use --path <dir>.");
                Console.ResetColor();
            }
            return 1;
        }

        if (!silent)
        {
            Console.WriteLine($"Scanning target paths: {string.Join(", ", paths)}");
            Console.WriteLine($"Mode: {(isClean ? "PURGE" : "DRY-RUN (Safe Preview)")} | Min Age: {minAgeDays} days | Deletion: {(permanent ? "Permanent" : "Recycle Bin")}");
            Console.WriteLine("Scanning in progress...");
        }

        var scanner = new FastDirectoryScanner();
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

        long totalBytes = results.Sum(r => r.SizeBytes);

        if (!silent)
        {
            Console.WriteLine(new string('-', 60));
            Console.WriteLine($"Scan complete: Found {results.Count} artifact folders ({DiscoveredFolder.FormatByteSize(totalBytes)})");

            foreach (var item in results.Take(15))
            {
                Console.WriteLine($" • [{item.CategoryName}] {item.FormattedSize,-10} {item.FormattedAge,-12} {item.Path}");
            }

            if (results.Count > 15)
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

        if (!isClean)
        {
            if (options.Json)
            {
                var jsonResult = new
                {
                    mode = "dry-run",
                    targetPaths = paths,
                    folderCount = results.Count,
                    totalBytes = totalBytes,
                    formattedTotalSize = DiscoveredFolder.FormatByteSize(totalBytes),
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
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Dry-Run Complete] {DiscoveredFolder.FormatByteSize(totalBytes)} can be reclaimed.");
                Console.WriteLine("To purge these directories, re-run with --clean flag.");
                Console.ResetColor();
            }
            return 0;
        }

        // Perform Purge
        if (!silent)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"Purging {results.Count} folders (Total: {DiscoveredFolder.FormatByteSize(totalBytes)})...");
            Console.ResetColor();
        }

        var purgeService = new PurgeService();
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

        var report = await purgeService.PurgeAsync(results, sendToRecycleBin: !permanent, purgeProgress);

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
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Purge Complete] Reclaimed: {report.FormattedReclaimedSize} | Successfully removed: {report.SuccessfulCount}/{report.TotalRequested}");
            Console.ResetColor();

            if (report.FailedCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to remove {report.FailedCount} folders:");
                foreach (var (path, err) in report.Failures.Take(5))
                {
                    Console.WriteLine($"  - {path}: {err}");
                }
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.WriteLine("Enjoying DevPurge? Consider starring or supporting:");
            Console.WriteLine("  ⭐ GitHub:        https://github.com/Behrad87/DevPurge");
            Console.WriteLine("  ☕ Ko-fi:         https://ko-fi.com/behrad87");
            Console.WriteLine("  🇮🇷 Reymit (Iran): https://reymit.ir/behrad87");
        }

        return report.FailedCount == 0 ? 0 : 2;
    }

    private static List<string> ResolveTargetPaths(List<string> configuredPaths)
    {
        var result = new List<string>();

        if (configuredPaths.Count > 0)
        {
            foreach (var p in configuredPaths)
            {
                // Support comma-separated paths
                var split = p.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var s in split)
                {
                    if (Directory.Exists(s))
                    {
                        result.Add(Path.GetFullPath(s));
                    }
                }
            }
        }

        if (result.Count == 0)
        {
            // Auto-detect common workspace roots
            string[] defaultCandidates =
            [
                @"D:\repos",
                @"C:\repos",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "repos"),
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
            "type", "t", "category",
            "exclude", "x", "ignore"
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
                var split = val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                exclusions.AddRange(split);
            }
            else if (arg.StartsWith("--exclude=", StringComparison.OrdinalIgnoreCase) ||
                     arg.StartsWith("-x=", StringComparison.OrdinalIgnoreCase) ||
                     arg.StartsWith("--ignore=", StringComparison.OrdinalIgnoreCase))
            {
                var val = arg.Split('=', 2)[1];
                var split = val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

        return new CliOptions
        {
            Paths = paths,
            Clean = rawDict.ContainsKey("clean") || rawDict.ContainsKey("c"),
            DryRun = rawDict.ContainsKey("dry-run") || rawDict.ContainsKey("dryrun") || rawDict.ContainsKey("d"),
            MinAgeDays = Math.Max(0, minAge),
            Permanent = rawDict.ContainsKey("permanent"),
            Silent = rawDict.ContainsKey("silent") || rawDict.ContainsKey("s"),
            Json = rawDict.ContainsKey("json") || rawDict.ContainsKey("j"),
            ShowHelp = rawDict.ContainsKey("help") || rawDict.ContainsKey("h") || rawDict.ContainsKey("?"),
            ShowVersion = rawDict.ContainsKey("version") || rawDict.ContainsKey("v"),
            FilterType = filterType,
            Exclusions = exclusions
        };
    }

    public static Dictionary<string, string> ParseArguments(string[] args)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var booleanFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "clean", "c",
            "dry-run", "dryrun", "d",
            "permanent",
            "silent", "s",
            "json", "j",
            "help", "h", "?",
            "version", "v"
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
}
