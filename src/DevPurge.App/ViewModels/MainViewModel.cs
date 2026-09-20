using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevPurge.Core.Models;
using DevPurge.Core.Purging;
using DevPurge.Core.Scanning;

namespace DevPurge.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly FastDirectoryScanner _scanner = new();
    private readonly PurgeService _purgeService = new();
    private readonly List<FolderItemViewModel> _allItems = [];

    [ObservableProperty]
    private string _targetPath = @"D:\repos";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isPurging;

    [ObservableProperty]
    private string _statusText = "Ready to scan. Select your developer repository path and click Scan.";

    [ObservableProperty]
    private string _summaryText = "0 B selected";

    [ObservableProperty]
    private bool _sendToRecycleBin = true;

    [ObservableProperty]
    private int _minAgeFilterIndex = 0; // 0=All, 1=>7 days, 2=>14 days, 3=>30 days

    public ObservableCollection<FolderItemViewModel> DisplayedItems { get; } = [];

    public MainViewModel()
    {
        if (!Directory.Exists(_targetPath))
        {
            var userSource = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source");
            _targetPath = Directory.Exists(userSource) ? userSource : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    partial void OnMinAgeFilterIndexChanged(int value)
    {
        ApplyFilter();
    }

    public void UpdateSummary()
    {
        var selected = DisplayedItems.Where(i => i.IsSelected).ToList();
        long totalBytes = selected.Sum(i => i.SizeBytes);
        SummaryText = $"{DiscoveredFolder.FormatByteSize(totalBytes)} selected ({selected.Count} of {DisplayedItems.Count} folders)";
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning || IsPurging) return;

        if (string.IsNullOrWhiteSpace(TargetPath) || !Directory.Exists(TargetPath))
        {
            MessageBox.Show($"Target directory does not exist:\n{TargetPath}", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsScanning = true;
        StatusText = $"Scanning '{TargetPath}' for disposable build artifacts...";
        _allItems.Clear();
        DisplayedItems.Clear();
        SummaryText = "Scanning...";

        var progress = new Progress<ScanProgress>(p =>
        {
            if (!p.IsCompleted && !string.IsNullOrEmpty(p.CurrentPath))
            {
                StatusText = $"Found {p.DiscoveredCount} folders ({DiscoveredFolder.FormatByteSize(p.TotalBytesFound)}) - {System.IO.Path.GetFileName(p.CurrentPath)}";
            }
        });

        try
        {
            var results = await _scanner.ScanAsync([TargetPath], progress);

            foreach (var r in results)
            {
                var vm = new FolderItemViewModel(r, UpdateSummary);
                _allItems.Add(vm);
            }

            ApplyFilter();

            long totalBytes = results.Sum(r => r.SizeBytes);
            StatusText = $"Scan complete. Discovered {results.Count} disposable folders ({DiscoveredFolder.FormatByteSize(totalBytes)} reclaimable).";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan error: {ex.Message}";
            MessageBox.Show($"An error occurred during scanning:\n{ex.Message}", "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            UpdateSummary();
        }
    }

    [RelayCommand]
    private async Task PurgeAsync()
    {
        if (IsScanning || IsPurging) return;

        var selected = DisplayedItems.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Please select at least one folder to purge.", "No Folders Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        long totalBytes = selected.Sum(i => i.SizeBytes);
        var targetType = SendToRecycleBin ? "send to Windows Recycle Bin" : "PERMANENTLY delete";

        var confirm = MessageBox.Show(
            $"Are you sure you want to {targetType} {selected.Count} folder(s)?\n\nTotal space to reclaim: {DiscoveredFolder.FormatByteSize(totalBytes)}\n\nThis will remove dependencies/build artifacts (which can be recompiled or reinstalled).",
            "Confirm Purge",
            MessageBoxButton.YesNo,
            SendToRecycleBin ? MessageBoxImage.Question : MessageBoxImage.Warning
        );

        if (confirm != MessageBoxResult.Yes) return;

        IsPurging = true;
        StatusText = $"Purging {selected.Count} folder(s)...";

        var progress = new Progress<(string Path, int Completed, int Total)>(p =>
        {
            StatusText = $"Purging {p.Completed}/{p.Total}: {System.IO.Path.GetFileName(p.Path)}";
        });

        try
        {
            var report = await _purgeService.PurgeAsync(
                selected.Select(s => s.Model),
                sendToRecycleBin: SendToRecycleBin,
                progress: progress
            );

            // Remove successfully deleted folders from lists
            foreach (var item in selected)
            {
                if (!Directory.Exists(item.Path))
                {
                    _allItems.Remove(item);
                    DisplayedItems.Remove(item);
                }
            }

            UpdateSummary();
            StatusText = $"Purge complete! Reclaimed {report.FormattedReclaimedSize} across {report.SuccessfulCount} folders.";

            MessageBox.Show(
                $"Successfully purged {report.SuccessfulCount} folder(s)!\n\nReclaimed: {report.FormattedReclaimedSize}\n\n" +
                (report.FailedCount > 0 ? $"Note: {report.FailedCount} folders could not be deleted (may be locked by running processes)." : ""),
                "Purge Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            StatusText = $"Purge error: {ex.Message}";
            MessageBox.Show($"An error occurred while purging:\n{ex.Message}", "Purge Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsPurging = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in DisplayedItems)
        {
            item.IsSelected = true;
        }
        UpdateSummary();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var item in DisplayedItems)
        {
            item.IsSelected = false;
        }
        UpdateSummary();
    }

    [RelayCommand]
    private void BrowsePath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Development Directory to Scan",
            InitialDirectory = Directory.Exists(TargetPath) ? TargetPath : @"D:\repos"
        };

        if (dialog.ShowDialog() == true)
        {
            TargetPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void OpenGitHub() => OpenUrl("https://github.com/Behrad87/DevPurge");

    [RelayCommand]
    private void OpenSponsors() => OpenUrl("https://github.com/sponsors/Behrad87");

    [RelayCommand]
    private void OpenKofi() => OpenUrl("https://ko-fi.com/behrad87");

    [RelayCommand]
    private void OpenReymit() => OpenUrl("https://reymit.ir/behrad87");

    private void ApplyFilter()
    {
        DisplayedItems.Clear();
        int minDays = MinAgeFilterIndex switch
        {
            1 => 7,
            2 => 14,
            3 => 30,
            _ => 0
        };

        foreach (var item in _allItems)
        {
            if (minDays == 0 || item.AgeDays >= minDays)
            {
                DisplayedItems.Add(item);
            }
        }
        UpdateSummary();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore failure opening browser
        }
    }
}
