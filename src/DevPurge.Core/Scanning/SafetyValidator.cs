namespace DevPurge.Core.Scanning;

/// <summary>
/// Enforces critical safety constraints to prevent accidental data loss or system directory deletion.
/// </summary>
public static class SafetyValidator
{
    private static readonly HashSet<string> SystemFolderBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows",
        "system32",
        "syswow64",
        "program files",
        "program files (x86)",
        "programdata",
        "recovery",
        "system volume information",
        "$recycle.bin",
        "boot",
        "users",
        "documents and settings",
        "appdata"
    };

    private static readonly HashSet<string> ProtectedFileSignatures = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "package.json",
        "cargo.toml",
        "pom.xml",
        "build.gradle",
        "solution.sln",
        "devpurge.sln"
    };

    /// <summary>
    /// Checks if a folder path is strictly safe to be purged.
    /// </summary>
    public static (bool IsSafe, string? Reason) ValidateSafeToDelete(string folderPath, IEnumerable<string> allowedFolderNames)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return (false, "Path cannot be empty.");

        try
        {
            var fullPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // 1. Never delete root drive
            var root = Path.GetPathRoot(fullPath);
            if (string.Equals(fullPath, root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                return (false, "Cannot delete root drive directory.");

            // 2. Never delete user profile root directly
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile) && string.Equals(fullPath, userProfile, StringComparison.OrdinalIgnoreCase))
                return (false, "Cannot delete user profile folder.");

            // 2b. Unix / Linux system root protections
            var rawNormalized = folderPath.Trim().Replace('\\', '/');
            var normalized = fullPath.Replace('\\', '/');
            bool isLinuxSystem = rawNormalized == "/bin" || rawNormalized == "/sbin" || rawNormalized == "/usr/bin" ||
                rawNormalized.StartsWith("/etc", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/var", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/usr", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/sys", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/proc", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/dev", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/boot", StringComparison.OrdinalIgnoreCase) ||
                normalized == "/bin" || normalized == "/sbin" || normalized == "/usr/bin" ||
                normalized.StartsWith("/etc", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/var", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/usr", StringComparison.OrdinalIgnoreCase);

            if (isLinuxSystem)
            {
                return (false, "Cannot delete Linux root system directory.");
            }

            // 3. Check against system folder blacklist
            var segments = fullPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (SystemFolderBlacklist.Contains(segment))
                    return (false, $"Path contains protected system folder component: '{segment}'.");
            }

            // 4. Never delete .git folder
            var folderName = Path.GetFileName(fullPath);
            if (string.Equals(folderName, ".git", StringComparison.OrdinalIgnoreCase))
                return (false, "Cannot delete .git repository directory.");

            // 5. Must match one of the allowed target folder names
            var allowedSet = new HashSet<string>(allowedFolderNames, StringComparer.OrdinalIgnoreCase);
            if (!allowedSet.Contains(folderName))
                return (false, $"Folder name '{folderName}' is not in the allowed purge target list.");

            // 6. Safeguard: if this folder itself contains project manifest files, it might be a root project (e.g. project named 'build')
            // Don't delete if it contains source project manifests like package.json, Cargo.toml or .sln directly inside it
            if (Directory.Exists(fullPath))
            {
                foreach (var signature in ProtectedFileSignatures)
                {
                    if (signature.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Directory.EnumerateFiles(fullPath, "*.sln").Any())
                            return (false, "Directory contains a solution (.sln) file; aborting deletion for safety.");
                    }
                    else if (signature.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Directory.Exists(Path.Combine(fullPath, ".git")))
                            return (false, "Directory contains a .git repository root; aborting deletion for safety.");
                    }
                    else if (File.Exists(Path.Combine(fullPath, signature)))
                    {
                        // Exception: node_modules might occasionally have package.json inside some sub-package,
                        // but if a folder named 'bin' or 'target' has package.json or cargo.toml, it's a project root!
                        if (!string.Equals(folderName, "node_modules", StringComparison.OrdinalIgnoreCase))
                        {
                            return (false, $"Directory contains project manifest '{signature}'; aborting deletion for safety.");
                        }
                    }
                }
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Validation exception: {ex.Message}");
        }
    }
}
