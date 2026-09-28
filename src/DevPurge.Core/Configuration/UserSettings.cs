namespace DevPurge.Core.Configuration;

/// <summary>
/// Persistent user preferences, path history, and customized rules.
/// </summary>
public class UserSettings
{
    /// <summary>
    /// Most Recently Used (MRU) workspace scan directories.
    /// </summary>
    public List<string> RecentPaths { get; set; } = [];

    /// <summary>
    /// Last actively scanned or selected target path.
    /// </summary>
    public string? LastSelectedPath { get; set; }

    /// <summary>
    /// User-created custom purge rules.
    /// </summary>
    public List<PurgeRuleConfig> CustomRules { get; set; } = [];

    /// <summary>
    /// Names of built-in purge rules that the user has explicitly disabled.
    /// </summary>
    public HashSet<string> DisabledRuleNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Deletion mode: whether to move to Recycle Bin / Trash (true) or delete permanently (false).
    /// </summary>
    public bool SendToRecycleBin { get; set; } = true;

    /// <summary>
    /// Default min-age filter index: 0=All, 1=>7d, 2=>14d, 3=>30d.
    /// </summary>
    public int MinAgeFilterIndex { get; set; } = 0;

    /// <summary>
    /// UI layout view: true for project cards, false for tabular grid.
    /// </summary>
    public bool IsCardView { get; set; } = false;
}
