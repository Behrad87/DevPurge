using DevPurge.Core.Configuration;
using DevPurge.Core.Models;

namespace DevPurge.Core.Tests;

public class UserSettingsManagerTests
{
    [Fact]
    public void AddRecentPath_MaintainsMruOrderAndDeduplicates()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"devpurge_settings_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new UserSettingsManager(tempFile);

            manager.AddRecentPath(@"C:\repos\project1");
            manager.AddRecentPath(@"C:\repos\project2");
            manager.AddRecentPath(@"C:\repos\project3");

            Assert.Equal(3, manager.Settings.RecentPaths.Count);
            Assert.Equal(@"C:\repos\project3", manager.Settings.RecentPaths[0]);
            Assert.Equal(@"C:\repos\project2", manager.Settings.RecentPaths[1]);
            Assert.Equal(@"C:\repos\project1", manager.Settings.RecentPaths[2]);

            // Add duplicate of project1 - should move to index 0
            manager.AddRecentPath(@"C:\repos\project1");
            Assert.Equal(3, manager.Settings.RecentPaths.Count);
            Assert.Equal(@"C:\repos\project1", manager.Settings.RecentPaths[0]);
            Assert.Equal(@"C:\repos\project3", manager.Settings.RecentPaths[1]);
            Assert.Equal(@"C:\repos\project2", manager.Settings.RecentPaths[2]);

            // Check LastSelectedPath
            Assert.Equal(@"C:\repos\project1", manager.Settings.LastSelectedPath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void AddRecentPath_CapsAtMaxCount()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"devpurge_settings_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new UserSettingsManager(tempFile);
            for (int i = 1; i <= 15; i++)
            {
                manager.AddRecentPath($@"C:\repos\project{i}", maxCount: 10);
            }

            Assert.Equal(10, manager.Settings.RecentPaths.Count);
            Assert.Equal(@"C:\repos\project15", manager.Settings.RecentPaths[0]);
            Assert.Equal(@"C:\repos\project6", manager.Settings.RecentPaths[9]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void RemoveAndClearRecentPaths_WorksCorrectly()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"devpurge_settings_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new UserSettingsManager(tempFile);
            manager.AddRecentPath(@"C:\repos\a");
            manager.AddRecentPath(@"C:\repos\b");

            manager.RemoveRecentPath(@"C:\repos\b");
            Assert.Single(manager.Settings.RecentPaths);
            Assert.Equal(@"C:\repos\a", manager.Settings.RecentPaths[0]);
            Assert.Equal(@"C:\repos\a", manager.Settings.LastSelectedPath);

            manager.ClearRecentPaths();
            Assert.Empty(manager.Settings.RecentPaths);
            Assert.Null(manager.Settings.LastSelectedPath);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void CustomRules_AddRemoveAndToggleBuiltInRules()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"devpurge_settings_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new UserSettingsManager(tempFile);

            // 1. Disable built-in rule
            manager.SetRuleEnabled("Node.js Dependencies", false);
            var rules1 = manager.GetEffectiveRules();
            var nodeRule = rules1.First(r => r.Name == "Node.js Dependencies");
            Assert.False(nodeRule.IsEnabled);

            // 2. Add custom rule
            var customRule = new PurgeRuleConfig(
                "My Coverage",
                ArtifactType.Custom,
                "Testing",
                ["coverage", ".nyc_output"],
                "Code coverage output",
                IsEnabled: true,
                IsBuiltIn: false
            );
            manager.AddCustomRule(customRule);

            var rules2 = manager.GetEffectiveRules();
            var matchedCustom = rules2.FirstOrDefault(r => r.Name == "My Coverage");
            Assert.NotNull(matchedCustom);
            Assert.Contains("coverage", matchedCustom.FolderNames);
            Assert.Contains(".nyc_output", matchedCustom.FolderNames);

            // 3. Save and re-load from disk
            manager.Save();
            var manager2 = new UserSettingsManager(tempFile);
            var rulesLoaded = manager2.GetEffectiveRules();
            var nodeLoaded = rulesLoaded.First(r => r.Name == "Node.js Dependencies");
            Assert.False(nodeLoaded.IsEnabled);
            var customLoaded = rulesLoaded.FirstOrDefault(r => r.Name == "My Coverage");
            Assert.NotNull(customLoaded);

            // 4. Remove custom rule
            Assert.True(manager2.RemoveCustomRule("My Coverage"));
            Assert.False(manager2.RemoveCustomRule("NonExistent"));
            Assert.Null(manager2.GetEffectiveRules().FirstOrDefault(r => r.Name == "My Coverage"));

            // 5. Reset to defaults
            manager2.ResetRulesToDefault();
            var rulesReset = manager2.GetEffectiveRules();
            var nodeReset = rulesReset.First(r => r.Name == "Node.js Dependencies");
            Assert.True(nodeReset.IsEnabled);
            Assert.Empty(manager2.Settings.CustomRules);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void CorruptedSettingsFile_FallsBackGracefully()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"devpurge_corrupted_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempFile, "{ this is invalid json !!!");
            var manager = new UserSettingsManager(tempFile);

            Assert.NotNull(manager.Settings);
            Assert.Empty(manager.Settings.RecentPaths);
            Assert.Empty(manager.Settings.CustomRules);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
