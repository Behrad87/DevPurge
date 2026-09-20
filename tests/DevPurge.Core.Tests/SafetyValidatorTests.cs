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
}
