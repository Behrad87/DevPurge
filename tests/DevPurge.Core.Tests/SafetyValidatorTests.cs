using DevPurge.Core.Models;
using DevPurge.Core.Scanning;

namespace DevPurge.Core.Tests;

public class SafetyValidatorTests
{
    private static readonly string[] AllowedFolderNames = ["node_modules", "bin", "obj", "target"];

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"C:")]
    [InlineData(@"/")]
    public void CannotDeleteRootDrive(string rootPath)
    {
        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(rootPath, AllowedFolderNames);
        Assert.False(isSafe);
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\node_modules")]
    [InlineData(@"C:\Program Files\bin")]
    [InlineData(@"C:\Program Files (x86)\obj")]
    [InlineData(@"C:\ProgramData\target")]
    public void CannotDeleteInsideProtectedSystemDirectories(string systemPath)
    {
        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(systemPath, AllowedFolderNames);
        Assert.False(isSafe);
        Assert.Contains("protected system folder", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/bin")]
    [InlineData("/sbin")]
    [InlineData("/usr/bin")]
    [InlineData("/etc/nginx")]
    [InlineData("/var/log/node_modules")]
    public void CannotDeleteLinuxSystemDirectories(string linuxPath)
    {
        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(linuxPath, AllowedFolderNames);
        Assert.False(isSafe);
        Assert.Contains("Linux root system directory", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CannotDeleteGitDirectory()
    {
        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(@"D:\repos\MyProject\.git", AllowedFolderNames);
        Assert.False(isSafe);
        Assert.Contains(".git", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisallowedFolderNameIsRejected()
    {
        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(@"D:\repos\MyProject\src", AllowedFolderNames);
        Assert.False(isSafe);
        Assert.Contains("not in the allowed purge target list", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidArtifactFolderIsAccepted()
    {
        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(@"D:\repos\MyProject\node_modules", AllowedFolderNames);
        Assert.True(isSafe);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyOrWhitespacePathIsRejected(string? path)
    {
        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(path!, AllowedFolderNames);
        Assert.False(isSafe);
        Assert.Contains("Path cannot be empty", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CannotDeleteUserProfileOrPersonalFolders()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrEmpty(desktop))
        {
            var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(desktop, AllowedFolderNames);
            Assert.False(isSafe);
            Assert.Contains("user profile", reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("/System/Library")]
    [InlineData("/Applications/Xcode.app")]
    [InlineData("/private/etc")]
    public void CannotDeleteMacOsSystemDirectories(string macPath)
    {
        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(macPath, AllowedFolderNames);
        Assert.False(isSafe);
        Assert.NotNull(reason);
    }

    [Fact]
    public void DirectoryWithSlnxIsRejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "devpurge_test_slnx_" + Guid.NewGuid().ToString("N"), "bin");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "solution.slnx"), "<Solution />");

            var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(tempDir, AllowedFolderNames);
            Assert.False(isSafe);
            Assert.Contains("solution", reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(Path.GetDirectoryName(tempDir)!, recursive: true);
            }
        }
    }

    [Fact]
    public void DirectoryWithGoModManifestIsRejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "devpurge_test_gomod_" + Guid.NewGuid().ToString("N"), "target");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "go.mod"), "module example.com/app\n\ngo 1.22");

            var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(tempDir, AllowedFolderNames);
            Assert.False(isSafe);
            Assert.Contains("project manifest", reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(Path.GetDirectoryName(tempDir)!, recursive: true);
            }
        }
    }

    [Fact]
    public void DirectoryWithGitFileWorktreeIsRejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "devpurge_test_gitworktree_" + Guid.NewGuid().ToString("N"), "bin");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, ".git"), "gitdir: ../../.git/worktrees/bin");

            var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(tempDir, AllowedFolderNames);
            Assert.False(isSafe);
            Assert.Contains(".git", reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(Path.GetDirectoryName(tempDir)!, recursive: true);
            }
        }
    }

    [Fact]
    public void DirectoryWithCsprojIsRejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "devpurge_test_csproj_" + Guid.NewGuid().ToString("N"), "bin");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "MyApp.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(tempDir, AllowedFolderNames);
            Assert.False(isSafe);
            Assert.Contains("project file", reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(Path.GetDirectoryName(tempDir)!, recursive: true);
            }
        }
    }

    [Fact]
    public void DirectoryWithFsprojIsRejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "devpurge_test_fsproj_" + Guid.NewGuid().ToString("N"), "bin");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "MyApp.fsproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(tempDir, AllowedFolderNames);
            Assert.False(isSafe);
            Assert.Contains("project file", reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(Path.GetDirectoryName(tempDir)!, recursive: true);
            }
        }
    }

    [Fact]
    public void DirectoryWithPyprojectTomlIsRejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "devpurge_test_pyproj_" + Guid.NewGuid().ToString("N"), "target");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "pyproject.toml"), "[project]\nname = \"myapp\"");

            var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(tempDir, AllowedFolderNames);
            Assert.False(isSafe);
            Assert.Contains("project manifest", reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(Path.GetDirectoryName(tempDir)!, recursive: true);
            }
        }
    }

    [Fact]
    public void DirectoryWithGitmodulesIsRejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "devpurge_test_gitmod_" + Guid.NewGuid().ToString("N"), "bin");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, ".gitmodules"), "[submodule \"lib\"]\n\tpath = lib");

            var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(tempDir, AllowedFolderNames);
            Assert.False(isSafe);
            Assert.Contains("project manifest", reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(Path.GetDirectoryName(tempDir)!, recursive: true);
            }
        }
    }

    [Fact]
    public void DirectoryWithGlobalJsonIsRejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "devpurge_test_globaljson_" + Guid.NewGuid().ToString("N"), "bin");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "global.json"), "{\"sdk\":{\"version\":\"10.0.100\"}}");

            var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(tempDir, AllowedFolderNames);
            Assert.False(isSafe);
            Assert.Contains("project manifest", reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(Path.GetDirectoryName(tempDir)!, recursive: true);
            }
        }
    }
}
