using CommunityToolkit.Mvvm.ComponentModel;
using DevPurge.Core.Models;

namespace DevPurge.App.ViewModels;

/// <summary>
/// Presentation wrapper for a built-in or user-defined purge rule.
/// </summary>
public partial class RuleItemViewModel : ObservableObject
{
    private readonly Action? _onChanged;

    public string Name { get; }
    public string CategoryName { get; }
    public string[] FolderNames { get; }
    public string FolderNamesDisplay => string.Join(", ", FolderNames);
    public string Description { get; }
    public ArtifactType ArtifactType { get; }
    public bool IsBuiltIn { get; }
    public bool CanDelete => !IsBuiltIn;

    [ObservableProperty]
    private bool _isEnabled;

    public string BadgeBackground => CategoryName switch
    {
        "JavaScript / Node.js" => "#1E293B",
        ".NET / C#" => "#2E1A47",
        "Rust" => "#451A03",
        "Java / Android" => "#064E3B",
        "Python" => "#1E3A8A",
        "Web Frameworks" => "#064E3B",
        "IDE Caches" => "#312E81",
        "Visual Studio" => "#2E1A47",
        "Dart / Flutter" => "#0E7490",
        "C++ / CMake" => "#155E75",
        "Zig" => "#78350F",
        "Swift / Apple" => "#831843",
        "Elixir" => "#4C1D95",
        _ => "#1E293B"
    };

    public string BadgeBorder => CategoryName switch
    {
        "JavaScript / Node.js" => "#F59E0B",
        ".NET / C#" => "#A855F7",
        "Rust" => "#F97316",
        "Java / Android" => "#10B981",
        "Python" => "#38BDF8",
        "Web Frameworks" => "#10B981",
        "IDE Caches" => "#818CF8",
        "Visual Studio" => "#C084FC",
        "Dart / Flutter" => "#06B6D4",
        "C++ / CMake" => "#22D3EE",
        "Zig" => "#F59E0B",
        "Swift / Apple" => "#F43F5E",
        "Elixir" => "#A855F7",
        _ => "#38BDF8"
    };

    public string BadgeForeground => BadgeBorder;

    public RuleItemViewModel(PurgeRule rule, bool isBuiltIn = false, Action? onChanged = null)
    {
        Name = rule.Name;
        CategoryName = rule.CategoryName;
        FolderNames = rule.FolderNames;
        Description = rule.Description;
        ArtifactType = rule.ArtifactType;
        _isEnabled = rule.IsEnabled;
        IsBuiltIn = isBuiltIn;
        _onChanged = onChanged;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _onChanged?.Invoke();
    }
}
