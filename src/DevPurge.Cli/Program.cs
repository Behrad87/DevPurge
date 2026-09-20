using DevPurge.Core.Models;
using DevPurge.Core.Purging;
using DevPurge.Core.Scanning;

namespace DevPurge.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var arguments = ParseArguments(args);

        if (arguments.ContainsKey("help") || arguments.ContainsKey("h"))
        {
            PrintHelp();
            return 0;
        }

        return await RunScanOrPurgeAsync(arguments);
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
        Console.WriteLine(" Developer Disk Reclaimer");
        Console.WriteLine(" GitHub: https://github.com/Behrad87/DevPurge");
        Console.WriteLine(new string('-', 60));
    }

    private static void PrintHelp()
    {
        PrintBanner();
        Console.WriteLine("USAGE:");
        Console.WriteLine("  DevPurge.Cli [options]");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("  --path <dir>         Root folder to scan (default: D:\\repos)");
        Console.WriteLine("  --clean              Perform actual purge (default is safe dry-run preview)");
        Console.WriteLine("  --min-age <days>     Only purge folders untouched for >= N days (default: 0)");
        Console.WriteLine("  --permanent          Permanently delete (default: send to Recycle Bin)");
        Console.WriteLine("  --silent             Quiet output");
        Console.WriteLine("  -h, --help           Show this help information");
        Console.WriteLine();
        Console.WriteLine("EXAMPLES:");
        Console.WriteLine("  DevPurge.Cli --path D:\\repos");
        Console.WriteLine("  DevPurge.Cli --path D:\\repos --clean --min-age 14");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Support this free open-source project:");
        Console.WriteLine("  GitHub Sponsors: https://github.com/sponsors/Behrad87");
        Console.WriteLine("  Ko-fi:           https://ko-fi.com/behrad87");
        Console.WriteLine("  Reymit (Iran):   https://reymit.ir/behrad87");
        Console.ResetColor();
    }

    private static async Task<int> RunScanOrPurgeAsync(Dictionary<string, string> args)
    {
        bool silent = args.ContainsKey("silent");
        bool isClean = args.ContainsKey("clean");
        bool permanent = args.ContainsKey("permanent");
        int minAgeDays = args.TryGetValue("min-age", out var ageStr) && int.TryParse(ageStr, out var age) ? age : 0;

        if (!silent)
        {
            PrintBanner();
        }

        var paths = new List<string>();
        if (args.TryGetValue("path", out var customPath))
        {
            paths.Add(customPath);
        }
        else
        {
            paths.Add(@"D:\repos");
        }

        paths = paths.Where(Directory.Exists).Distinct().ToList();
        if (paths.Count == 0)
        {
            if (!silent)
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
        var progress = silent ? null : new Progress<ScanProgress>(p =>
        {
            if (!p.IsCompleted && !string.IsNullOrEmpty(p.CurrentPath))
            {
                Console.Write($"\rDiscovered: {p.DiscoveredCount} folders ({DiscoveredFolder.FormatByteSize(p.TotalBytesFound)})   ");
            }
        });

        var results = await scanner.ScanAsync(paths, progress);

        if (minAgeDays > 0)
        {
            results = results.Where(r => r.AgeDays >= minAgeDays).ToList();
        }

        long totalBytes = results.Sum(r => r.SizeBytes);

        if (!silent)
        {
            Console.WriteLine();
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
            Console.WriteLine(new string('-', 60));
        }

        if (!isClean)
        {
            if (!silent)
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
        var purgeProgress = silent ? null : new Progress<(string Path, int Completed, int Total)>(p =>
        {
            Console.Write($"\rPurging: {p.Completed}/{p.Total} ({Path.GetFileName(p.Path)})        ");
        });

        var report = await purgeService.PurgeAsync(results, sendToRecycleBin: !permanent, purgeProgress);

        if (!silent)
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

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--"))
            {
                var key = arg[2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
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
                dict[key] = "true";
            }
        }
        return dict;
    }
}
