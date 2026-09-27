using System.Collections.Frozen;

namespace DevPurge.Core.Scanning;

/// <summary>
/// Enforces critical safety constraints to prevent accidental data loss or system directory deletion.
/// </summary>
public static class SafetyValidator
{
    private static readonly FrozenSet<string> SystemFolderBlacklist = new[]
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
        "$windows.~bt",
        "$windows.~ws",
        "msocache",
        "config.msi"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ProtectedFileSignatures = new[]
    {
        ".git",
        ".gitmodules",
        "package.json",
        "cargo.toml",
        "pom.xml",
        "build.gradle",
        "build.gradle.kts",
        "solution.sln",
        "devpurge.sln",
        "go.mod",
        "pyproject.toml",
        "composer.json",
        "cmakelists.txt",
        "directory.build.props",
        "directory.build.targets",
        "global.json"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ProtectedUserSpecialFolders = GetProtectedUserFolders();

    private static FrozenSet<string> GetProtectedUserFolders()
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Environment.SpecialFolder[] targetFolders =
        [
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyVideos
        ];

        foreach (var folder in targetFolders)
        {
            try
            {
                var path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(path))
                {
                    folders.Add(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
            }
            catch
            {
                // Ignore environment resolution issues on non-standard platforms
            }
        }

        return folders.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

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

            // 2. Never delete user profile root or personal library folders directly
            if (ProtectedUserSpecialFolders.Contains(fullPath))
                return (false, "Cannot delete user profile or personal library folder.");

            // 2b. Unix / Linux system root protections
            var rawNormalized = folderPath.Trim().Replace('\\', '/');
            var normalized = fullPath.Replace('\\', '/');
            bool isLinuxOrUnixSystem = rawNormalized == "/bin" || rawNormalized == "/sbin" || rawNormalized == "/usr/bin" ||
                rawNormalized.StartsWith("/etc", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/var", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/usr", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/sys", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/proc", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/dev", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/boot", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/System", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/Library", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/Applications", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/private", StringComparison.OrdinalIgnoreCase) ||
                normalized == "/bin" || normalized == "/sbin" || normalized == "/usr/bin" ||
                normalized.StartsWith("/etc", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/var", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/usr", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/System", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/Library", StringComparison.OrdinalIgnoreCase);

            if (isLinuxOrUnixSystem)
            {
                return (false, "Cannot delete Linux root system directory.");
            }

            // 3. Check against system folder blacklist
            var tempPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool isInTemp = !string.IsNullOrEmpty(tempPath) && fullPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase);

            var segments = fullPath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (SystemFolderBlacklist.Contains(segment))
                    return (false, $"Path contains protected system folder component: '{segment}'.");

                if (string.Equals(segment, "appdata", StringComparison.OrdinalIgnoreCase) && !isInTemp)
                    return (false, $"Path contains protected system folder component: '{segment}'.");
            }

            // 3b. Prevent deleting root Users / Documents and Settings directory directly
            if (root != null)
            {
                var usersDir = Path.Combine(root, "Users").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var docAndSettings = Path.Combine(root, "Documents and Settings").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(fullPath, usersDir, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fullPath, docAndSettings, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, "Cannot delete system Users root directory.");
                }
            }

            // 4. Never delete .git folder
            var folderName = Path.GetFileName(fullPath);
            if (string.Equals(folderName, ".git", StringComparison.OrdinalIgnoreCase))
                return (false, "Cannot delete .git repository directory.");

            // 5. Must match one of the allowed target folder names
            var allowedSet = allowedFolderNames is FrozenSet<string> fs
                ? fs
                : allowedFolderNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

            if (!allowedSet.Contains(folderName))
                return (false, $"Folder name '{folderName}' is not in the allowed purge target list.");

            // 6. Safeguard: if this folder itself contains project manifest files, it might be a root project (e.g. project named 'build')
            // Don't delete if it contains source project manifests like package.json, Cargo.toml or .sln directly inside it
            if (Directory.Exists(fullPath))
            {
                // Check if directory contains a solution file (.sln or modern .slnx)
                if (Directory.EnumerateFiles(fullPath, "*.sln").Any() || Directory.EnumerateFiles(fullPath, "*.slnx").Any())
                {
                    return (false, "Directory contains a solution (.sln/.slnx) file; aborting deletion for safety.");
                }

                // Check if directory contains a .NET project file (*.csproj, *.fsproj, *.vbproj)
                if (Directory.EnumerateFiles(fullPath, "*.*proj").Any(f =>
                    f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, "Directory contains a .NET project file (*.csproj/fsproj/vbproj); aborting deletion for safety.");
                }

                // Check if directory contains a VCS repository root (.git, .svn, .hg)
                if (Directory.Exists(Path.Combine(fullPath, ".git")) || File.Exists(Path.Combine(fullPath, ".git")) ||
                    Directory.Exists(Path.Combine(fullPath, ".svn")) || Directory.Exists(Path.Combine(fullPath, ".hg")))
                {
                    return (false, "Directory contains a repository root (.git/.svn/.hg); aborting deletion for safety.");
                }

                foreach (var signature in ProtectedFileSignatures)
                {
                    if (signature.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || signature.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (File.Exists(Path.Combine(fullPath, signature)))
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
