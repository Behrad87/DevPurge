using DevPurge.Core.Models;

namespace DevPurge.Core.Configuration;

/// <summary>
/// Serializable configuration representation of a purge rule.
/// </summary>
public record PurgeRuleConfig(
    string Name,
    ArtifactType ArtifactType,
    string CategoryName,
    string[] FolderNames,
    string Description,
    bool IsEnabled = true,
    bool IsBuiltIn = false
)
{
    /// <summary>
    /// Converts a <see cref="PurgeRule"/> domain model to a <see cref="PurgeRuleConfig"/>.
    /// </summary>
    public static PurgeRuleConfig FromPurgeRule(PurgeRule rule, bool isBuiltIn = false) =>
        new(
            rule.Name,
            rule.ArtifactType,
            rule.CategoryName,
            rule.FolderNames,
            rule.Description,
            rule.IsEnabled,
            isBuiltIn
        );

    /// <summary>
    /// Converts this config into a runtime <see cref="PurgeRule"/>.
    /// </summary>
    public PurgeRule ToPurgeRule() =>
        new(
            Name,
            ArtifactType,
            CategoryName,
            FolderNames,
            Description,
            IsEnabled
        );
}
