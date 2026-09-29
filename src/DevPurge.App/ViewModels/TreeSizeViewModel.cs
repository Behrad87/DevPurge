using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevPurge.Core.Auditing;
using DevPurge.Core.Configuration;
using DevPurge.Core.Models;
using DevPurge.Core.Purging;
using DevPurge.Core.Scanning;
using DevPurge.Core.TreeSize;

namespace DevPurge.App.ViewModels;

public partial class TreeSizeViewModel : ObservableObject
{
    private readonly IUserSettingsManager _settingsManager;
    private readonly IAuditLogger _auditLogger;
    private readonly ITreeSizeScanner _scanner;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _targetPath = @"C:\repos";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _statusText = "Ready to analyze directory tree. Select any folder or drive to inspect space allocation.";

    [ObservableProperty]
    private string _currentScanningPath = string.Empty;

    [ObservableProperty]
    private string _searchFilterText = string.Empty;

    [ObservableProperty]
    private long _totalDiscoveredBytes;

    [ObservableProperty]
    private string _formattedTotalSize = "0 B";

    [ObservableProperty]
    private int _totalFolders;

    [ObservableProperty]
    private int _totalFiles;

    [ObservableProperty]
    private long _artifactsReclaimableBytes;

    [ObservableProperty]
    private string _formattedArtifactsSize = "0 B";

    [ObservableProperty]
    private int _artifactsCount;

    [ObservableProperty]
    private TreeSizeItemViewModel? _selectedNode;

    public ObservableCollection<TreeSizeItemViewModel> RootNodes { get; } = [];
    public ObservableCollection<string> RecentPaths { get; } = [];

    public bool HasNoItemsAndNotScanning => !IsScanning && !HasResults;

    public TreeSizeViewModel(
        IUserSettingsManager? settingsManager = null,
        IAuditLogger? auditLogger = null,
        ITreeSizeScanner? scanner = null)
    {
        _settingsManager = settingsManager ?? new UserSettingsManager();
        _auditLogger = auditLogger ?? new AuditLogger();
        _scanner = scanner ?? new TreeSizeScanner(_settingsManager.GetEffectiveRules());

        foreach (var p in _settingsManager.Settings.RecentPaths)
        {
            RecentPaths.Add(p);
        }

        if (!string.IsNullOrWhiteSpace(_settingsManager.Settings.LastSelectedPath) &&
            Directory.Exists(_settingsManager.Settings.LastSelectedPath))
        {
            _targetPath = _settingsManager.Settings.LastSelectedPath;
        }
        else if (Directory.Exists(@"C:\repos"))
        {
            _targetPath = @"C:\repos";
        }
        else if (Directory.Exists(@"D:\repos"))
        {
            _targetPath = @"D:\repos";
        }
        else
        {
            _targetPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoItemsAndNotScanning));
    }

    partial void OnHasResultsChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoItemsAndNotScanning));
    }

    partial void OnSearchFilterTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        Task.Delay(150, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    foreach (var root in RootNodes)
                    {
                        root.Filter(value);
                    }
                });
            }
        }, TaskScheduler.Default);
    }

    private bool CanScan => !IsScanning;
    private bool CanCancelScan => IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    public async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath) || !Directory.Exists(TargetPath))
        {
            MessageBox.Show($"Target directory does not exist:\n{TargetPath}", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settingsManager.AddRecentPath(TargetPath);
        _settingsManager.Save();

        RecentPaths.Clear();
        foreach (var p in _settingsManager.Settings.RecentPaths)
        {
            RecentPaths.Add(p);
        }

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var cancellationToken = _scanCts.Token;

        IsScanning = true;
        HasResults = false;
        RootNodes.Clear();
        StatusText = $"Analyzing folder tree for '{TargetPath}'...";
        CurrentScanningPath = TargetPath;

        var progress = new Progress<TreeSizeProgress>(p =>
        {
            if (!p.IsCompleted)
            {
                CurrentScanningPath = p.CurrentPath;
                StatusText = $"Scanned {p.DirectoriesScanned:N0} folders, {p.FilesScanned:N0} files ({DiscoveredFolder.FormatByteSize(p.TotalBytesScanned)})...";
            }
        });

        try
        {
            var rootNode = await _scanner.ScanTreeAsync(TargetPath, maxDepth: 15, progress, cancellationToken);

            if (rootNode != null)
            {
                var rootVm = new TreeSizeItemViewModel(rootNode, depth: 0, onPurgeRequested: OnPurgeNodeRequested);
                RootNodes.Add(rootVm);

                TotalDiscoveredBytes = rootNode.SizeBytes;
                FormattedTotalSize = rootNode.FormattedSize;
                TotalFolders = rootNode.DirectoryCount;
                TotalFiles = rootNode.FileCount;

                // Calculate artifacts statistics across tree
                long artifactBytes = 0;
                int artifactCount = 0;
                CountArtifacts(rootNode, ref artifactBytes, ref artifactCount);

                ArtifactsReclaimableBytes = artifactBytes;
                ArtifactsCount = artifactCount;
                FormattedArtifactsSize = DiscoveredFolder.FormatByteSize(artifactBytes);

                HasResults = true;
                StatusText = $"Analysis complete for '{rootNode.Name}': {rootNode.FormattedSize} total across {rootNode.DirectoryCount:N0} folders and {rootNode.FileCount:N0} files.";
            }
            else
            {
                StatusText = "Could not analyze the target folder.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "TreeSize analysis cancelled by user.";
        }
        catch (Exception ex)
        {
            StatusText = $"TreeSize analysis error: {ex.Message}";
            MessageBox.Show($"An error occurred during TreeSize analysis:\n{ex.Message}", "Analysis Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            CurrentScanningPath = string.Empty;
        }
    }

    private static void CountArtifacts(TreeSizeNode node, ref long bytes, ref int count)
    {
        if (node.IsArtifact)
        {
            bytes += node.SizeBytes;
            count++;
        }
        foreach (var child in node.Children)
        {
            CountArtifacts(child, ref bytes, ref count);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    public void CancelScan()
    {
        _scanCts?.Cancel();
        StatusText = "Cancelling analysis...";
    }

    [RelayCommand]
    public void BrowsePath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Directory to Analyze with TreeSize",
            InitialDirectory = Directory.Exists(TargetPath) ? TargetPath : @"C:\repos"
        };

        if (dialog.ShowDialog() == true)
        {
            TargetPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    public void SelectQuickPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            TargetPath = path;
        }
    }

    [RelayCommand]
    public void ExpandAll()
    {
        foreach (var root in RootNodes)
        {
            root.ExpandAll();
        }
    }

    [RelayCommand]
    public void CollapseAll()
    {
        foreach (var root in RootNodes)
        {
            root.CollapseAll();
            root.IsExpanded = true; // Keep root expanded
        }
    }

    [RelayCommand]
    public async Task ExportTree()
    {
        if (RootNodes.Count == 0)
        {
            MessageBox.Show("No TreeSize data to export. Run an analysis first.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var root = RootNodes[0].Node;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export TreeSize Analysis",
            Filter = "CSV File (*.csv)|*.csv|JSON File (*.json)|*.json|Text Tree Outline (*.txt)|*.txt",
            FileName = $"DevPurge_TreeSize_{Path.GetFileName(root.Path)}_{DateTime.Now:yyyyMMdd_HHmmss}",
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                string content = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
                {
                    ".json" => TreeSizeExporter.ToJson(root),
                    ".txt" => TreeSizeExporter.ToTextTree(root),
                    _ => TreeSizeExporter.ToCsv(root)
                };

                await File.WriteAllTextAsync(dialog.FileName, content);
                MessageBox.Show($"TreeSize report successfully exported to:\n{dialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export TreeSize data:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnPurgeNodeRequested(TreeSizeItemViewModel item)
    {
        var targetPath = item.Path;
        if (!Directory.Exists(targetPath))
        {
            MessageBox.Show($"Directory no longer exists:\n{targetPath}", "Folder Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Safety verification
        var allowedNames = _settingsManager.GetEffectiveRules().SelectMany(r => r.FolderNames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var (isSafe, safetyReason) = SafetyValidator.ValidateSafeToDelete(targetPath, allowedNames);

        string promptMessage;
        MessageBoxImage icon;

        if (item.IsArtifact)
        {
            promptMessage = $"Are you sure you want to delete disposable artifact folder '{item.Name}' ({item.FormattedSize})?\n\nType: {item.ArtifactCategory ?? "Build Cache"}\nPath: {targetPath}\n\nThis will move the folder to the Windows Recycle Bin.";
            icon = MessageBoxImage.Question;
        }
        else
        {
            if (!isSafe)
            {
                MessageBox.Show($"Safety rail blocked deletion:\n{safetyReason}\n\nDevPurge protects critical system folders and git repositories from deletion.", "Safety Protection", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            promptMessage = $"CAUTION: '{item.Name}' is not a recognized build artifact.\n\nAre you sure you want to send this folder to the Windows Recycle Bin?\n\nSize: {item.FormattedSize}\nFiles: {item.FileCount:N0}\nPath: {targetPath}";
            icon = MessageBoxImage.Warning;
        }

        var result = MessageBox.Show(promptMessage, "Confirm Folder Deletion", MessageBoxButton.YesNo, icon);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                targetPath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin
            );

            // Audit
            var report = new DeletionReport(
                TotalRequested: 1,
                SuccessfulCount: 1,
                FailedCount: 0,
                ReclaimedBytes: item.SizeBytes,
                Failures: []
            );
            _ = _auditLogger.LogPurgeAsync(report, sendToRecycleBin: true, isDryRun: false, targetRoots: [TargetPath], purgedPaths: [targetPath]);

            MessageBox.Show($"Successfully moved to Recycle Bin:\n{item.Name} ({item.FormattedSize})", "Folder Purged", MessageBoxButton.OK, MessageBoxImage.Information);

            // Rescan or remove node from view
            if (item.Node.Parent != null)
            {
                // Refresh scan to update entire tree accurately
                _ = ScanAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete folder:\n{ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
