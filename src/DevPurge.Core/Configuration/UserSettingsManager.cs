using System.Text.Json;
using DevPurge.Core.Models;

namespace DevPurge.Core.Configuration;

/// <summary>
/// Manages loading, saving, and querying persistent user configuration and workspace path history.
/// </summary>
public class UserSettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string SettingsFilePath { get; }
    public UserSettings Settings { get; private set; }

    public UserSettingsManager(string? customSettingsPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customSettingsPath))
        {
            SettingsFilePath = customSettingsPath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var devPurgeDir = Path.Combine(appData, "DevPurge");
            SettingsFilePath = Path.Combine(devPurgeDir, "settings.json");
        }

        Settings = new UserSettings();
        Load();
    }

    /// <summary>
    /// Loads settings from disk if available, otherwise initializes defaults.
    /// </summary>
    public void Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    Settings = loaded;
                    Settings.DisabledRuleNames = new HashSet<string>(Settings.DisabledRuleNames ?? [], StringComparer.OrdinalIgnoreCase);
                    Settings.RecentPaths ??= [];
                    Settings.CustomRules ??= [];
                    return;
                }
            }
        }
        catch
        {
            // Fall back to clean defaults on corruption or IO error
        }

        Settings = new UserSettings();
    }

    /// <summary>
    /// Persists settings to disk.
    /// </summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Best-effort save
        }
    }

    /// <summary>
    /// Adds a scanned path to the MRU list, moving it to top and capping at max count.
    /// </summary>
    public void AddRecentPath(string path, int maxCount = 10)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var clean = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Settings.RecentPaths.RemoveAll(p => string.Equals(p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), clean, StringComparison.OrdinalIgnoreCase));
        Settings.RecentPaths.Insert(0, clean);

        if (Settings.RecentPaths.Count > maxCount)
        {
            Settings.RecentPaths = Settings.RecentPaths.Take(maxCount).ToList();
        }

        Settings.LastSelectedPath = clean;
    }

    /// <summary>
    /// Removes a path from the MRU list.
    /// </summary>
    public void RemoveRecentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var clean = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Settings.RecentPaths.RemoveAll(p => string.Equals(p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), clean, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(Settings.LastSelectedPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), clean, StringComparison.OrdinalIgnoreCase))
        {
            Settings.LastSelectedPath = Settings.RecentPaths.FirstOrDefault();
        }
    }

    /// <summary>
    /// Clears all recent paths.
    /// </summary>
    public void ClearRecentPaths()
    {
        Settings.RecentPaths.Clear();
        Settings.LastSelectedPath = null;
    }

    /// <summary>
    /// Resolves all effective purge rules (default built-in rules with disabled state applied, plus custom rules).
    /// </summary>
    public IReadOnlyList<PurgeRule> GetEffectiveRules()
    {
        var result = new List<PurgeRule>();
        var defaultRules = PurgeRule.GetDefaultRules();

        foreach (var rule in defaultRules)
        {
            bool isEnabled = !Settings.DisabledRuleNames.Contains(rule.Name);
            result.Add(rule with { IsEnabled = isEnabled });
        }

        foreach (var custom in Settings.CustomRules)
        {
            result.Add(custom.ToPurgeRule());
        }

        return result;
    }

    /// <summary>
    /// Enables or disables a rule by name (handles both built-in and custom rules).
    /// </summary>
    public void SetRuleEnabled(string ruleName, bool isEnabled)
    {
        var custom = Settings.CustomRules.FirstOrDefault(c => string.Equals(c.Name, ruleName, StringComparison.OrdinalIgnoreCase));
        if (custom != null)
        {
            int index = Settings.CustomRules.IndexOf(custom);
            Settings.CustomRules[index] = custom with { IsEnabled = isEnabled };
            return;
        }

        if (isEnabled)
        {
            Settings.DisabledRuleNames.Remove(ruleName);
        }
        else
        {
            Settings.DisabledRuleNames.Add(ruleName);
        }
    }

    /// <summary>
    /// Adds or replaces a custom rule.
    /// </summary>
    public void AddCustomRule(PurgeRuleConfig rule)
    {
        Settings.CustomRules.RemoveAll(c => string.Equals(c.Name, rule.Name, StringComparison.OrdinalIgnoreCase));
        Settings.CustomRules.Add(rule);
    }

    /// <summary>
    /// Removes a custom rule by name.
    /// </summary>
    public bool RemoveCustomRule(string ruleName)
    {
        return Settings.CustomRules.RemoveAll(c => string.Equals(c.Name, ruleName, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    /// <summary>
    /// Resets all rules to factory defaults (clearing disabled names and custom rules).
    /// </summary>
    public void ResetRulesToDefault()
    {
        Settings.DisabledRuleNames.Clear();
        Settings.CustomRules.Clear();
    }
}
