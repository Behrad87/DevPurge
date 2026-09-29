using DevPurge.Core.Models;

namespace DevPurge.Core.Configuration;

/// <summary>
/// Abstraction for managing persistent user settings, path history, and purge rules.
/// </summary>
public interface IUserSettingsManager
{
    /// <summary>
    /// File path where user settings are stored.
    /// </summary>
    string SettingsFilePath { get; }

    /// <summary>
    /// The loaded user settings.
    /// </summary>
    UserSettings Settings { get; }

    /// <summary>
    /// Loads settings from disk if available, otherwise initializes defaults.
    /// </summary>
    void Load();

    /// <summary>
    /// Persists settings to disk.
    /// </summary>
    void Save();

    /// <summary>
    /// Adds a scanned path to the MRU list, moving it to top and capping at max count.
    /// </summary>
    void AddRecentPath(string path, int maxCount = 10);

    /// <summary>
    /// Removes a path from the MRU list.
    /// </summary>
    void RemoveRecentPath(string path);

    /// <summary>
    /// Clears all recent paths.
    /// </summary>
    void ClearRecentPaths();

    /// <summary>
    /// Resolves all effective purge rules (default built-in rules with disabled state applied, plus custom rules).
    /// </summary>
    IReadOnlyList<PurgeRule> GetEffectiveRules();

    /// <summary>
    /// Enables or disables a rule by name (handles both built-in and custom rules).
    /// </summary>
    void SetRuleEnabled(string ruleName, bool isEnabled);

    /// <summary>
    /// Adds or replaces a custom rule.
    /// </summary>
    void AddCustomRule(PurgeRuleConfig rule);

    /// <summary>
    /// Removes a custom rule by name.
    /// </summary>
    bool RemoveCustomRule(string ruleName);

    /// <summary>
    /// Resets all rules to factory defaults.
    /// </summary>
    void ResetRulesToDefault();
}
