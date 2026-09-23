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
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _targetPath = @"C:\repos";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(PurgeCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(PurgeCommand))]
    private bool _isPurging;

    [ObservableProperty]
    private string _statusText = "Ready to scan. Select your developer repository path and click Scan.";

    [ObservableProperty]
    private string _summaryText = "0 B selected (0 of 0 folders)";

    [ObservableProperty]
    private bool _sendToRecycleBin = true;

    [ObservableProperty]
    private int _minAgeFilterIndex = 0; // 0=All, 1=>7 days, 2=>14 days, 3=>30 days

    [ObservableProperty]
    private bool _hasResults = false;

    [ObservableProperty]
    private string _searchText = string.Empty;

    // --- KPI Analytics Metrics ---
    [ObservableProperty]
    private long _totalDiscoveredBytes;

    [ObservableProperty]
    private string _formattedTotalDiscoveredSize = "0 B";

    [ObservableProperty]
    private int _discoveredCount;

    [ObservableProperty]
    private long _selectedBytes;

    [ObservableProperty]
    private string _formattedSelectedSize = "0 B";

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private long _staleBytes;

    [ObservableProperty]
    private string _formattedStaleSize = "0 B";

    [ObservableProperty]
    private int _staleCount;

    [ObservableProperty]
    private string _topCategorySummary = "None";

    [ObservableProperty]
    private bool _hasSelectedItems;

    [ObservableProperty]
    private string _currentScanningPath = string.Empty;

    public bool HasNoItemsAndNotScanning => !IsScanning && !HasResults;

    public ObservableCollection<FolderItemViewModel> DisplayedItems { get; } = [];
    public ObservableCollection<CategoryFilterItem> CategoryFilters { get; } = [];

    [ObservableProperty]
    private CategoryFilterItem? _selectedCategoryFilter;

    private bool CanScan => !IsScanning && !IsPurging;
    private bool CanCancelScan => IsScanning;
    private bool CanPurge => !IsScanning && !IsPurging && HasSelectedItems;

    public MainViewModel()
    {
        if (Directory.Exists(@"C:\repos"))
        {
            _targetPath = @"C:\repos";
        }
        else if (Directory.Exists(@"D:\repos"))
        {
            _targetPath = @"D:\repos";
        }
        else
        {
            var userSource = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source");
            _targetPath = Directory.Exists(userSource) ? userSource : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        Task.Delay(120, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(ApplyFilter);
            }
        }, TaskScheduler.Default);
    }

    public bool IsAgeFilter0 => MinAgeFilterIndex == 0;
    public bool IsAgeFilter1 => MinAgeFilterIndex == 1;
    public bool IsAgeFilter2 => MinAgeFilterIndex == 2;
    public bool IsAgeFilter3 => MinAgeFilterIndex == 3;

    partial void OnMinAgeFilterIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAgeFilter0));
        OnPropertyChanged(nameof(IsAgeFilter1));
        OnPropertyChanged(nameof(IsAgeFilter2));
        OnPropertyChanged(nameof(IsAgeFilter3));
        ApplyFilter();
    }

    partial void OnSelectedCategoryFilterChanged(CategoryFilterItem? value)
    {
        foreach (var c in CategoryFilters)
        {
            c.IsSelected = (c == value);
        }
        ApplyFilter();
    }

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoItemsAndNotScanning));
        PurgeCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasResultsChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoItemsAndNotScanning));
    }

    public void UpdateSummary()
    {
        var selected = DisplayedItems.Where(i => i.IsSelected).ToList();
        SelectedCount = selected.Count;
        SelectedBytes = selected.Sum(i => i.SizeBytes);
        FormattedSelectedSize = DiscoveredFolder.FormatByteSize(SelectedBytes);
        HasSelectedItems = SelectedCount > 0;

        DiscoveredCount = _allItems.Count;
        TotalDiscoveredBytes = _allItems.Sum(i => i.SizeBytes);
        FormattedTotalDiscoveredSize = DiscoveredFolder.FormatByteSize(TotalDiscoveredBytes);

        var stale = _allItems.Where(i => i.IsStale).ToList();
        StaleCount = stale.Count;
        StaleBytes = stale.Sum(i => i.SizeBytes);
        FormattedStaleSize = DiscoveredFolder.FormatByteSize(StaleBytes);

        var topGroup = _allItems
            .GroupBy(i => i.CategoryName)
            .OrderByDescending(g => g.Sum(x => x.SizeBytes))
            .FirstOrDefault();

        TopCategorySummary = topGroup != null
            ? $"{topGroup.Key} ({DiscoveredFolder.FormatByteSize(topGroup.Sum(x => x.SizeBytes))})"
            : "None";

        SummaryText = $"{FormattedSelectedSize} selected ({SelectedCount} of {DisplayedItems.Count} folders)";
        PurgeCommand.NotifyCanExecuteChanged();
    }

    private void RebuildCategoryFilters()
    {
        CategoryFilters.Clear();

        if (_allItems.Count == 0) return;

        var allItem = new CategoryFilterItem
        {
            Name = "All",
            ArtifactType = null,
            Count = _allItems.Count,
            TotalBytes = _allItems.Sum(x => x.SizeBytes),
            IsSelected = SelectedCategoryFilter == null || SelectedCategoryFilter.ArtifactType == null,
            AccentColor = "#38BDF8",
            DotColor = "#38BDF8"
        };
        CategoryFilters.Add(allItem);

        var groups = _allItems
            .GroupBy(i => i.ArtifactType)
            .OrderByDescending(g => g.Sum(x => x.SizeBytes));

        foreach (var g in groups)
        {
            var first = g.First();
            var totalBytes = g.Sum(x => x.SizeBytes);
            var isSel = SelectedCategoryFilter?.ArtifactType == g.Key;

            var cat = new CategoryFilterItem
            {
                Name = first.CategoryName,
                ArtifactType = g.Key,
                Count = g.Count(),
                TotalBytes = totalBytes,
                IsSelected = isSel,
                AccentColor = first.CategoryBadgeBorder,
                DotColor = first.CategoryBadgeBorder
            };
            CategoryFilters.Add(cat);
        }

        if (SelectedCategoryFilter == null)
        {
            SelectedCategoryFilter = allItem;
        }
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath) || !Directory.Exists(TargetPath))
        {
            MessageBox.Show($"Target directory does not exist:\n{TargetPath}", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var cancellationToken = _scanCts.Token;

        IsScanning = true;
        HasResults = false;
        CurrentScanningPath = TargetPath;
        StatusText = $"Scanning '{TargetPath}' for disposable build artifacts...";
        _allItems.Clear();
        DisplayedItems.Clear();
        CategoryFilters.Clear();
        SummaryText = "Scanning...";

        var progress = new Progress<ScanProgress>(p =>
        {
            if (!p.IsCompleted && !string.IsNullOrEmpty(p.CurrentPath))
            {
                CurrentScanningPath = p.CurrentPath;
                StatusText = $"Discovered: {p.DiscoveredCount} folders ({DiscoveredFolder.FormatByteSize(p.TotalBytesFound)}) — {System.IO.Path.GetFileName(p.CurrentPath)}";
            }
        });

        try
        {
            var results = await _scanner.ScanAsync([TargetPath], progress, cancellationToken);

            foreach (var r in results)
            {
                var vm = new FolderItemViewModel(r, UpdateSummary);
                _allItems.Add(vm);
            }

            RebuildCategoryFilters();
            ApplyFilter();

            HasResults = results.Count > 0;
            long totalBytes = results.Sum(r => r.SizeBytes);
            StatusText = $"Scan complete. Discovered {results.Count} disposable folders ({DiscoveredFolder.FormatByteSize(totalBytes)} reclaimable).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled by user.";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan error: {ex.Message}";
            MessageBox.Show($"An error occurred during scanning:\n{ex.Message}", "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            CurrentScanningPath = string.Empty;
            UpdateSummary();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan()
    {
        _scanCts?.Cancel();
        StatusText = "Cancelling scan...";
    }

    [RelayCommand(CanExecute = nameof(CanPurge))]
    private async Task PurgeAsync()
    {
        var selected = DisplayedItems.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Please select at least one folder to purge.", "No Folders Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        long totalBytes = selected.Sum(i => i.SizeBytes);
        var targetType = SendToRecycleBin ? "send to Windows Recycle Bin (Safe Undo)" : "PERMANENTLY delete";

        var confirm = MessageBox.Show(
            $"Are you sure you want to {targetType} {selected.Count} folder(s)?\n\nTotal space to reclaim: {DiscoveredFolder.FormatByteSize(totalBytes)}\n\nThis will remove dependencies and compiled binaries (which can be safely restored or rebuilt).",
            "Confirm DevPurge Action",
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

            // Fast in-memory removal
            _allItems.RemoveAll(item => !Directory.Exists(item.Path));
            RebuildCategoryFilters();
            ApplyFilter();

            HasResults = DisplayedItems.Count > 0;
            UpdateSummary();
            StatusText = $"Purge complete! Reclaimed {report.FormattedReclaimedSize} across {report.SuccessfulCount} folders.";

            if (report.SuccessfulCount > 0)
            {
                var dialog = new Views.PurgeSuccessDialog(
                    report.FormattedReclaimedSize,
                    report.SuccessfulCount,
                    report.ReclaimedBytes,
                    SendToRecycleBin
                )
                {
                    Owner = Application.Current?.MainWindow
                };
                dialog.ShowDialog();
            }
            else
            {
                MessageBox.Show(
                    "No folders were removed. They may be locked by running IDEs or background processes.",
                    "Purge Incomplete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
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
    private void SelectStale()
    {
        foreach (var item in DisplayedItems)
        {
            item.IsSelected = item.IsStale;
        }
        UpdateSummary();
    }

    [RelayCommand]
    private void SelectCategory(CategoryFilterItem? category)
    {
        SelectedCategoryFilter = category;
    }

    [RelayCommand]
    private void SetMinAgeFilter(string indexStr)
    {
        if (int.TryParse(indexStr, out int index))
        {
            MinAgeFilterIndex = index;
        }
    }

    [RelayCommand]
    private void BrowsePath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Development Workspace to Scan",
            InitialDirectory = Directory.Exists(TargetPath) ? TargetPath : @"C:\repos"
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
        int minDays = MinAgeFilterIndex switch
        {
            1 => 7,
            2 => 14,
            3 => 30,
            _ => 0
        };

        var query = _allItems.AsEnumerable();

        if (SelectedCategoryFilter?.ArtifactType != null)
        {
            query = query.Where(item => item.ArtifactType == SelectedCategoryFilter.ArtifactType.Value);
        }

        if (minDays > 0)
        {
            query = query.Where(item => item.AgeDays >= minDays);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(item =>
                item.FolderName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                item.Path.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                item.CategoryName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                item.ParentDirectoryName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            );
        }

        var list = query.ToList();

        DisplayedItems.Clear();
        foreach (var item in list)
        {
            DisplayedItems.Add(item);
        }
        UpdateSummary();
    }

    [RelayCommand]
    private async Task SelectQuickPathAsync(string path)
    {
        TargetPath = path;
        await ScanAsync();
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
