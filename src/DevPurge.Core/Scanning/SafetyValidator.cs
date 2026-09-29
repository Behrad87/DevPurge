using System.Collections.Frozen;

namespace DevPurge.Core.Scanning;

/// <summary>
/// Enforces critical safety constraints to prevent accidental data loss, system directory deletion,
/// symlink/reparse hazards, or cloud-sync conflicts.
/// </summary>
public static class SafetyValidator
{
    private static readonly FrozenSet<string> SystemFolderBlacklist = new[]
    {
        "windows",
        "system32",
        "syswow64",
        "winsxs",
        "servicing",
        "program files",
        "program files (x86)",
        "programdata",
        "recovery",
        "system volume information",
        "$recycle.bin",
        "boot",
        "bootmgr",
        "$windows.~bt",
        "$windows.~ws",
        "$winre_backup_partition.marker",
        "msocache",
        "config.msi",
        "perflogs"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> LinuxSystemRoots = new[]
    {
        "bin", "sbin", "usr", "etc", "var", "lib", "lib64", "proc", "sys",
        "dev", "boot", "root", "opt", "srv", "mnt", "media", "run", "snap", "home"
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
        "global.json",
        "mix.exs",
        "pubspec.yaml",
        "build.zig",
        "package.swift",
        "gemfile",
        "deno.json",
        "deno.jsonc",
        "dockerfile",
        "docker-compose.yml",
        "docker-compose.yaml",
        "pnpm-lock.yaml",
        "yarn.lock",
        "package-lock.json",
        "bun.lockb",
        "bun.lock",
        "cargo.lock",
        "poetry.lock",
        "pipfile",
        "requirements.txt",
        "go.work",
        "deno.lock",
        "uv.lock",
        "pdm.lock",
        "flake.lock",
        "pipfile.lock",
        "tsconfig.json",
        "nuget.config",
        "directory.packages.props",
        ".env",
        ".env.local",
        ".env.production",
        "main.tf",
        "flake.nix"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ReservedDeviceNames = new[]
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks whether a directory name matches a protected system folder blacklist.
    /// </summary>
    public static bool IsSystemBlacklisted(string folderName) =>
        !string.IsNullOrEmpty(folderName) && SystemFolderBlacklist.Contains(folderName);

    /// <summary>
    /// Checks whether the specified directory is a reparse point (symbolic link, mount point, or directory junction).
    /// </summary>
    public static bool IsReparsePoint(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return false;

        try
        {
            var di = new DirectoryInfo(directoryPath);
            return di.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

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

        // Add cloud synchronization root folders
        string[] cloudEnvVars = ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];
        foreach (var envVar in cloudEnvVars)
        {
            try
            {
                var val = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrWhiteSpace(val) && Directory.Exists(val))
                {
                    folders.Add(Path.GetFullPath(val).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
            }
            catch { }
        }

        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                string[] cloudSubDirNames =
                [
                    "Dropbox",
                    "Dropbox (Personal)",
                    "Google Drive",
                    "GoogleDrive",
                    "iCloudDrive",
                    "OneDrive - Personal"
                ];

                foreach (var name in cloudSubDirNames)
                {
                    var full = Path.Combine(userProfile, name);
                    if (Directory.Exists(full))
                    {
                        folders.Add(Path.GetFullPath(full).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    }
                }
            }
        }
        catch { }

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

            // 1a. WSL (Windows Subsystem for Linux) and virtual mount protections
            var (isWslProtected, wslReason) = CheckWslAndMountProtections(folderPath, fullPath);
            if (isWslProtected)
            {
                return (false, wslReason);
            }

            // 1b. Never delete root drive
            var root = Path.GetPathRoot(fullPath);
            if (string.Equals(fullPath, root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                return (false, "Cannot delete root drive directory.");

            // 2. Never delete user profile root, personal library folders, or cloud sync roots directly
            if (ProtectedUserSpecialFolders.Contains(fullPath))
                return (false, "Cannot delete user profile, personal library, or cloud sync root folder.");

            // 2b. Unix / Linux / macOS system root protections
            var rawNormalized = folderPath.Trim().Replace('\\', '/');
            var normalized = fullPath.Replace('\\', '/');
            bool isLinuxOrUnixSystem = rawNormalized == "/bin" || rawNormalized == "/sbin" || rawNormalized == "/usr/bin" ||
                rawNormalized == "/lib" || rawNormalized == "/lib64" || rawNormalized == "/opt" || rawNormalized == "/root" ||
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
                rawNormalized.StartsWith("/Volumes", StringComparison.OrdinalIgnoreCase) ||
                rawNormalized.StartsWith("/private", StringComparison.OrdinalIgnoreCase) ||
                normalized == "/bin" || normalized == "/sbin" || normalized == "/usr/bin" ||
                normalized == "/lib" || normalized == "/lib64" || normalized == "/opt" || normalized == "/root" ||
                normalized.StartsWith("/etc", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/var", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/usr", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/System", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/Library", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("/Volumes", StringComparison.OrdinalIgnoreCase);

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

            // 4b. Cloud storage sync metadata check
            if (string.Equals(folderName, ".dropbox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(folderName, ".dropbox.cache", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(folderName, ".onedrive", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(folderName, ".nextcloud", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Cannot delete cloud storage metadata directory.");
            }

            // 4c. Developer credentials and security directories check
            if (string.Equals(folderName, ".ssh", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(folderName, ".gnupg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(folderName, ".aws", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(folderName, ".azure", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(folderName, ".kube", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Cannot delete developer credentials or security directory.");
            }

            // 5. Must match one of the allowed target folder names
            var allowedSet = allowedFolderNames is FrozenSet<string> fs
                ? fs
                : allowedFolderNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

            if (!allowedSet.Contains(folderName))
                return (false, $"Folder name '{folderName}' is not in the allowed purge target list.");

            // 6. Safeguards on physical disk directory if it exists
            if (Directory.Exists(fullPath))
            {
                var di = new DirectoryInfo(fullPath);

                // 6a. Reparse point (symbolic link / directory junction / mount point) check
                if (di.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return (false, "Directory is a symbolic link or junction (reparse point); deletion refused to prevent unintended target data loss.");
                }

                // 6b. Offline / cloud-only placeholder check
                if (di.Attributes.HasFlag(FileAttributes.Offline))
                {
                    return (false, "Directory is marked as cloud-only or offline storage placeholder; deletion refused to avoid synchronization conflicts.");
                }

                // 6c. Solution files check (.sln, .slnx)
                if (Directory.EnumerateFiles(fullPath, "*.sln").Any() || Directory.EnumerateFiles(fullPath, "*.slnx").Any())
                {
                    return (false, "Directory contains a solution (.sln/.slnx) file; aborting deletion for safety.");
                }

                // 6d. Project files check (*.csproj, *.fsproj, *.vbproj, *.vcxproj)
                if (Directory.EnumerateFiles(fullPath, "*.*proj").Any(f =>
                    f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, "Directory contains a project file (*.csproj/fsproj/vbproj/vcxproj); aborting deletion for safety.");
                }

                // 6e. VCS repository roots check (.git, .svn, .hg)
                if (Directory.Exists(Path.Combine(fullPath, ".git")) || File.Exists(Path.Combine(fullPath, ".git")) ||
                    Directory.Exists(Path.Combine(fullPath, ".svn")) || Directory.Exists(Path.Combine(fullPath, ".hg")))
                {
                    return (false, "Directory contains a repository root (.git/.svn/.hg); aborting deletion for safety.");
                }

                // 6f. Project manifest checks
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

    private static (bool IsProtected, string? Reason) CheckWslAndMountProtections(string rawPath, string fullPath)
    {
        var rawNorm = rawPath.Trim().Replace('\\', '/');
        var norm = fullPath.Replace('\\', '/').TrimEnd('/');

        string? wslPath = null;
        if (norm.StartsWith("//wsl$/", StringComparison.OrdinalIgnoreCase) ||
            norm.StartsWith("//wsl.localhost/", StringComparison.OrdinalIgnoreCase))
        {
            wslPath = norm;
        }
        else if (rawNorm.StartsWith("//wsl$/", StringComparison.OrdinalIgnoreCase) ||
                 rawNorm.StartsWith("//wsl.localhost/", StringComparison.OrdinalIgnoreCase))
        {
            wslPath = rawNorm;
        }

        if (wslPath != null)
        {
            var prefix = wslPath.StartsWith("//wsl$/", StringComparison.OrdinalIgnoreCase)
                ? "//wsl$/"
                : "//wsl.localhost/";

            var sub = wslPath[prefix.Length..].Trim('/');
            var parts = sub.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // parts[0] is Distro name (e.g. "Ubuntu")
            if (parts.Length <= 1)
            {
                return (true, "Cannot delete WSL distribution root directory.");
            }

            // parts[1] is the root directory in the WSL distro (e.g. "bin", "etc", "home")
            var rootDir = parts[1];
            if (LinuxSystemRoots.Contains(rootDir))
            {
                // If it directly targets /bin, /etc, /sbin, /usr or /home root
                if (parts.Length == 2)
                {
                    return (true, $"Cannot delete Linux root system directory '{rootDir}' inside WSL distribution.");
                }

                // If it targets critical system paths like /etc/..., /usr/..., /var/...
                if (!string.Equals(rootDir, "home", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(rootDir, "mnt", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(rootDir, "tmp", StringComparison.OrdinalIgnoreCase))
                {
                    return (true, "Cannot delete Linux root system directory inside WSL distribution.");
                }
            }
        }

        // Protection for WSL virtual disk images on Windows host
        if (norm.Contains("/packages/", StringComparison.OrdinalIgnoreCase) &&
            norm.EndsWith("/localstate/ext4.vhdx", StringComparison.OrdinalIgnoreCase))
        {
            return (true, "Cannot delete WSL virtual disk storage image.");
        }

        return (false, null);
    }

    /// <summary>
    /// Validates whether a folder name is safe and valid to be used in a custom purge rule.
    /// Prevents registering system directories, path traversal characters, or VCS repository names.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateCustomRuleFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return (false, "Folder name cannot be empty or whitespace.");
        }

        var trimmed = folderName.Trim();

        if (trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.Contains(':'))
        {
            return (false, "Folder name must be a single directory name, not a path with slashes or drive colons.");
        }

        if (trimmed == "." || trimmed == "..")
        {
            return (false, "Folder name cannot be relative path tokens ('.' or '..').");
        }

        if (string.Equals(trimmed, ".git", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, ".svn", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, ".hg", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Cannot target version control repository folders (.git, .svn, .hg).");
        }

        if (string.Equals(trimmed, ".dropbox", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, ".dropbox.cache", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, ".onedrive", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, ".nextcloud", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Cannot target cloud synchronization metadata folders.");
        }

        if (string.Equals(trimmed, ".ssh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, ".gnupg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, ".aws", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, ".azure", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, ".kube", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Cannot target developer credentials or security directory.");
        }

        if (SystemFolderBlacklist.Contains(trimmed) ||
            string.Equals(trimmed, "appdata", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "users", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "root", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "home", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"Folder name '{trimmed}' is a protected system directory and cannot be purged.");
        }

        // Check Windows device reserved names
        if (ReservedDeviceNames.Contains(trimmed))
        {
            return (false, $"'{trimmed}' is a reserved OS device name.");
        }

        return (true, null);
    }
}
