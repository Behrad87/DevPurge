using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevPurge.Core.Configuration;
using DevPurge.Core.Models;
using DevPurge.Core.Scanning;

namespace DevPurge.App.ViewModels;

/// <summary>
/// ViewModel driving the Purge Rules and Ecosystem Targets Manager dialog.
/// </summary>
public partial class RuleManagerViewModel : ObservableObject
{
    private readonly UserSettingsManager _settingsManager;

    public ObservableCollection<RuleItemViewModel> Rules { get; } = [];

    [ObservableProperty]
    private string _newRuleName = string.Empty;

    [ObservableProperty]
    private string _newCategoryName = "Custom";

    [ObservableProperty]
    private string _newFolderNames = string.Empty;

    [ObservableProperty]
    private string _newDescription = string.Empty;

    [ObservableProperty]
    private string _validationErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasValidationError;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    public int ActiveRulesCount => Rules.Count(r => r.IsEnabled);
    public int TotalRulesCount => Rules.Count;

    public RuleManagerViewModel(UserSettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
        LoadRules();
    }

    private void LoadRules()
    {
        Rules.Clear();

        var defaultRules = PurgeRule.GetDefaultRules();
        foreach (var rule in defaultRules)
        {
            bool isEnabled = !_settingsManager.Settings.DisabledRuleNames.Contains(rule.Name);
            var item = new RuleItemViewModel(rule with { IsEnabled = isEnabled }, isBuiltIn: true, OnRuleStateChanged);
            Rules.Add(item);
        }

        foreach (var custom in _settingsManager.Settings.CustomRules)
        {
            var rule = custom.ToPurgeRule();
            var item = new RuleItemViewModel(rule, isBuiltIn: false, OnRuleStateChanged);
            Rules.Add(item);
        }

        UpdateSummary();
    }

    private void OnRuleStateChanged()
    {
        foreach (var item in Rules)
        {
            _settingsManager.SetRuleEnabled(item.Name, item.IsEnabled);
        }
        _settingsManager.Save();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        SummaryText = $"{ActiveRulesCount} of {TotalRulesCount} rules active";
        OnPropertyChanged(nameof(ActiveRulesCount));
        OnPropertyChanged(nameof(TotalRulesCount));
    }

    [RelayCommand]
    private void AddCustomRule()
    {
        HasValidationError = false;
        ValidationErrorMessage = string.Empty;

        var name = NewRuleName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            HasValidationError = true;
            ValidationErrorMessage = "Please provide a rule name.";
            return;
        }

        if (Rules.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            HasValidationError = true;
            ValidationErrorMessage = $"A rule named '{name}' already exists.";
            return;
        }

        var folderNames = NewFolderNames
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (folderNames.Length == 0)
        {
            HasValidationError = true;
            ValidationErrorMessage = "Please specify at least one target folder name (e.g. 'coverage, .nyc_output').";
            return;
        }

        // Validate safety of each target folder
        foreach (var folder in folderNames)
        {
            var (isValid, error) = SafetyValidator.ValidateCustomRuleFolder(folder);
            if (!isValid)
            {
                HasValidationError = true;
                ValidationErrorMessage = $"Invalid target folder '{folder}': {error}";
                return;
            }
        }

        var category = string.IsNullOrWhiteSpace(NewCategoryName) ? "Custom" : NewCategoryName.Trim();
        var desc = string.IsNullOrWhiteSpace(NewDescription) ? $"Custom user rule for {string.Join(", ", folderNames)}" : NewDescription.Trim();

        var config = new PurgeRuleConfig(
            name,
            ArtifactType.Custom,
            category,
            folderNames,
            desc,
            IsEnabled: true,
            IsBuiltIn: false
        );

        _settingsManager.AddCustomRule(config);
        _settingsManager.Save();

        var vm = new RuleItemViewModel(config.ToPurgeRule(), isBuiltIn: false, OnRuleStateChanged);
        Rules.Add(vm);

        NewRuleName = string.Empty;
        NewFolderNames = string.Empty;
        NewDescription = string.Empty;
        NewCategoryName = "Custom";

        UpdateSummary();
    }

    [RelayCommand]
    private void DeleteRule(RuleItemViewModel? item)
    {
        if (item == null || item.IsBuiltIn) return;

        _settingsManager.RemoveCustomRule(item.Name);
        _settingsManager.Save();
        Rules.Remove(item);

        UpdateSummary();
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        _settingsManager.ResetRulesToDefault();
        _settingsManager.Save();
        LoadRules();
    }
}
