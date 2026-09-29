using DevPurge.Core.Auditing;
using DevPurge.Core.Configuration;
using DevPurge.Core.DependencyInjection;
using DevPurge.Core.Purging;
using DevPurge.Core.Scanning;
using DevPurge.Core.TreeSize;
using Microsoft.Extensions.DependencyInjection;

namespace DevPurge.Core.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddDevPurgeCore_RegistersAllCoreServices()
    {
        var services = new ServiceCollection();
        services.AddDevPurgeCore();
        var provider = services.BuildServiceProvider();

        // 1. Settings Manager
        var settingsManager = provider.GetService<IUserSettingsManager>();
        Assert.NotNull(settingsManager);
        var concreteSettingsManager = provider.GetService<UserSettingsManager>();
        Assert.NotNull(concreteSettingsManager);
        Assert.Same(settingsManager, concreteSettingsManager);

        // 2. Audit Logger
        var auditLogger = provider.GetService<IAuditLogger>();
        Assert.NotNull(auditLogger);
        var concreteAuditLogger = provider.GetService<AuditLogger>();
        Assert.NotNull(concreteAuditLogger);
        Assert.Same(auditLogger, concreteAuditLogger);

        // 3. Fast Directory Scanner
        var fastScanner = provider.GetService<IFastDirectoryScanner>();
        Assert.NotNull(fastScanner);
        var genericScanner = provider.GetService<IScanner>();
        Assert.NotNull(genericScanner);
        var concreteScanner = provider.GetService<FastDirectoryScanner>();
        Assert.NotNull(concreteScanner);

        // 4. Purge Service
        var purgeService = provider.GetService<IPurgeService>();
        Assert.NotNull(purgeService);
        var concretePurgeService = provider.GetService<PurgeService>();
        Assert.NotNull(concretePurgeService);

        // 5. TreeSize Scanner
        var treeSizeScanner = provider.GetService<ITreeSizeScanner>();
        Assert.NotNull(treeSizeScanner);
        var concreteTreeSizeScanner = provider.GetService<TreeSizeScanner>();
        Assert.NotNull(concreteTreeSizeScanner);
    }

    [Fact]
    public void AddDevPurgeCore_SupportsCustomPaths()
    {
        var tempSettings = Path.Combine(Path.GetTempPath(), $"di_test_settings_{Guid.NewGuid():N}.json");
        var tempAudit = Path.Combine(Path.GetTempPath(), $"di_test_audit_{Guid.NewGuid():N}.jsonl");

        try
        {
            var services = new ServiceCollection();
            services.AddDevPurgeCore(customSettingsPath: tempSettings, customAuditLogPath: tempAudit);
            var provider = services.BuildServiceProvider();

            var settingsManager = provider.GetRequiredService<IUserSettingsManager>();
            Assert.Equal(tempSettings, settingsManager.SettingsFilePath);

            var auditLogger = provider.GetRequiredService<IAuditLogger>();
            Assert.NotNull(auditLogger);
        }
        finally
        {
            if (File.Exists(tempSettings)) File.Delete(tempSettings);
            if (File.Exists(tempAudit)) File.Delete(tempAudit);
        }
    }

    [Fact]
    public void AddDevPurgeCore_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection nullServices = null!;
        Assert.Throws<ArgumentNullException>(() => nullServices.AddDevPurgeCore());
    }

    [Fact]
    public void ScannerInterfaces_IScannerAndIFastDirectoryScanner_AreCompatible()
    {
        FastDirectoryScanner concrete = new();
        IFastDirectoryScanner ifast = concrete;
        IScanner iscanner = concrete;

        Assert.NotNull(ifast);
        Assert.NotNull(iscanner);
        Assert.IsAssignableFrom<IScanner>(ifast);
    }
}
