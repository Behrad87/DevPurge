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
    [InlineData("/opt")]
    [InlineData("/root")]
    [InlineData("/lib")]
    [InlineData("/lib64")]
    [InlineData("/Volumes")]
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
    public void DirectoryWithVcxprojIsRejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "devpurge_test_vcxproj_" + Guid.NewGuid().ToString("N"), "bin");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "NativeApp.vcxproj"), "<Project DefaultTargets=\"Build\" />");

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

    [Theory]
    [InlineData("mix.exs")]
    [InlineData("pubspec.yaml")]
    [InlineData("build.zig")]
    [InlineData("package.swift")]
    [InlineData("gemfile")]
    [InlineData("deno.json")]
    [InlineData("deno.jsonc")]
    public void DirectoryWithModernProjectManifestIsRejected(string manifestFileName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "devpurge_test_manifest_" + Guid.NewGuid().ToString("N"), "bin");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, manifestFileName), "manifest content");

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

    [Theory]
    [InlineData("perflogs", true)]
    [InlineData("windows", true)]
    [InlineData("program files", true)]
    [InlineData("System Volume Information", true)]
    [InlineData("$recycle.bin", true)]
    [InlineData("node_modules", false)]
    [InlineData("bin", false)]
    [InlineData("my-project", false)]
    public void IsSystemBlacklisted_IdentifiesBlacklistedFolders(string folderName, bool expected)
    {
        Assert.Equal(expected, SafetyValidator.IsSystemBlacklisted(folderName));
    }

    [Theory]
    [InlineData("coverage", true)]
    [InlineData(".nyc_output", true)]
    [InlineData("tmp_build", true)]
    [InlineData(".turbo", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("windows", false)]
    [InlineData("system32", false)]
    [InlineData("program files", false)]
    [InlineData("appdata", false)]
    [InlineData(".git", false)]
    [InlineData(".svn", false)]
    [InlineData(".hg", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("sub/folder", false)]
    [InlineData("sub\\folder", false)]
    [InlineData("C:", false)]
    [InlineData("CON", false)]
    [InlineData("NUL", false)]
    [InlineData("users", false)]
    [InlineData(".nextcloud", false)]
    public void ValidateCustomRuleFolder_ValidatesCorrectly(string folderName, bool expectedValid)
    {
        var (isValid, error) = SafetyValidator.ValidateCustomRuleFolder(folderName);
        Assert.Equal(expectedValid, isValid);
        if (!expectedValid)
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Theory]
    [InlineData(@"\\wsl$\Ubuntu")]
    [InlineData(@"\\wsl.localhost\Debian")]
    [InlineData(@"//wsl$/Ubuntu")]
    [InlineData(@"//wsl.localhost/openSUSE")]
    public void CannotDeleteWslDistributionRoot(string wslRoot)
    {
        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(wslRoot, AllowedFolderNames);
        Assert.False(isSafe);
        Assert.NotNull(reason);
        Assert.Contains("WSL", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"\\wsl$\Ubuntu\bin")]
    [InlineData(@"\\wsl$\Ubuntu\sbin")]
    [InlineData(@"\\wsl$\Ubuntu\etc")]
    [InlineData(@"\\wsl$\Ubuntu\var")]
    [InlineData(@"\\wsl.localhost\Debian\usr\bin")]
    [InlineData(@"\\wsl.localhost\Ubuntu\boot")]
    public void CannotDeleteWslLinuxSystemDirectories(string wslSysDir)
    {
        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(wslSysDir, AllowedFolderNames);
        Assert.False(isSafe);
        Assert.NotNull(reason);
        Assert.Contains("WSL", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanDeleteWslUserWorkspaceArtifacts()
    {
        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(@"\\wsl$\Ubuntu\home\dev\myproject\node_modules", AllowedFolderNames);
        Assert.True(isSafe);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData(@"D:\repos\MyProject\.dropbox")]
    [InlineData(@"D:\repos\MyProject\.dropbox.cache")]
    [InlineData(@"D:\repos\MyProject\.onedrive")]
    [InlineData(@"D:\repos\MyProject\.nextcloud")]
    public void CannotDeleteCloudMetadataFolders(string cloudDir)
    {
        var (isSafe, reason) = SafetyValidator.ValidateSafeToDelete(cloudDir, AllowedFolderNames);
        Assert.False(isSafe);
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData("Dockerfile")]
    [InlineData("docker-compose.yml")]
    [InlineData("docker-compose.yaml")]
    [InlineData("pnpm-lock.yaml")]
    [InlineData("yarn.lock")]
    [InlineData("cargo.lock")]
    [InlineData("poetry.lock")]
    [InlineData("requirements.txt")]
    [InlineData("main.tf")]
    [InlineData("flake.nix")]
    [InlineData("go.work")]
    [InlineData("deno.lock")]
    [InlineData("uv.lock")]
    [InlineData("pdm.lock")]
    [InlineData("flake.lock")]
    [InlineData("pipfile.lock")]
    [InlineData("tsconfig.json")]
    [InlineData("nuget.config")]
    public void DirectoryWithExtendedManifests_IsRejected(string manifestName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "devpurge_test_ext_man_" + Guid.NewGuid().ToString("N"), "bin");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, manifestName), "sample content");

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
    public void IsReparsePoint_ReturnsFalseForNormalDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "devpurge_test_normal_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            Assert.False(SafetyValidator.IsReparsePoint(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir);
            }
        }
    }
}
