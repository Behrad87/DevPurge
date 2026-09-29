using DevPurge.Core.Auditing;
using DevPurge.Core.Configuration;
using DevPurge.Core.Purging;
using DevPurge.Core.Scanning;
using DevPurge.Core.TreeSize;
using Microsoft.Extensions.DependencyInjection;

namespace DevPurge.Core.DependencyInjection;

/// <summary>
/// Extension methods for setting up DevPurge core services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds DevPurge core services (scanner, purge service, audit logger, settings manager, tree analyzer) to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to register services into.</param>
    /// <param name="customSettingsPath">Optional custom path for user configuration settings.</param>
    /// <param name="customAuditLogPath">Optional custom path for persistent audit log.</param>
    /// <returns>The original <see cref="IServiceCollection"/> for method chaining.</returns>
    public static IServiceCollection AddDevPurgeCore(
        this IServiceCollection services,
        string? customSettingsPath = null,
        string? customAuditLogPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configuration and Settings
        services.AddSingleton<IUserSettingsManager>(_ => new UserSettingsManager(customSettingsPath));
        services.AddSingleton(sp => (UserSettingsManager)sp.GetRequiredService<IUserSettingsManager>());

        // Audit Logging
        services.AddSingleton<IAuditLogger>(_ => new AuditLogger(customAuditLogPath));
        services.AddSingleton(sp => (AuditLogger)sp.GetRequiredService<IAuditLogger>());

        // Directory Scanning
        services.AddTransient<IFastDirectoryScanner>(sp =>
        {
            var settings = sp.GetRequiredService<IUserSettingsManager>();
            return new FastDirectoryScanner(settings.GetEffectiveRules());
        });
        services.AddTransient<IScanner>(sp => sp.GetRequiredService<IFastDirectoryScanner>());
        services.AddTransient<FastDirectoryScanner>(sp => (FastDirectoryScanner)sp.GetRequiredService<IFastDirectoryScanner>());

        // Safe Purging
        services.AddTransient<IPurgeService>(sp =>
        {
            var settings = sp.GetRequiredService<IUserSettingsManager>();
            return new PurgeService(settings.GetEffectiveRules());
        });
        services.AddTransient<PurgeService>(sp => (PurgeService)sp.GetRequiredService<IPurgeService>());

        // TreeSize Deep Disk Analyzer
        services.AddTransient<ITreeSizeScanner, TreeSizeScanner>();
        services.AddTransient<TreeSizeScanner>();

        return services;
    }
}
