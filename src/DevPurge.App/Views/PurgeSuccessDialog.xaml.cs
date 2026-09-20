using System.Diagnostics;
using System.Windows;
using DevPurge.Core.Models;
using Wpf.Ui.Controls;

namespace DevPurge.App.Views;

public partial class PurgeSuccessDialog : FluentWindow
{
    public PurgeSuccessDialog(string reclaimedSize, int folderCount, long reclaimedBytes, bool isRecycleBin)
    {
        InitializeComponent();

        ReclaimedSizeText.Text = $"{reclaimedSize} Reclaimed!";
        var targetText = isRecycleBin ? "Moved to Windows Recycle Bin" : "Permanently Deleted";
        FoldersCountText.Text = $"Across {folderCount:N0} folder(s) • {targetText}";

        // Calculate approximate SSD hardware value ($0.15/GB or ~8,000 Toman/GB)
        double gigabytes = reclaimedBytes / (1024.0 * 1024.0 * 1024.0);
        if (gigabytes >= 0.5)
        {
            double estimatedUsd = Math.Max(1.0, gigabytes * 0.15);
            long estimatedToman = (long)(gigabytes * 8000);
            EstimatedValueText.Text = $"Estimated NVMe storage value: ~${estimatedUsd:F1} ({estimatedToman:N0} تومان)";
        }
        else
        {
            EstimatedValueText.Text = "Cleaned up obsolete development build artifacts";
        }
    }

    private void OnKofiClicked(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://ko-fi.com/behrad87");
    }

    private void OnReymitClicked(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://reymit.ir/behrad87");
    }

    private void OnSponsorClicked(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/sponsors/Behrad87");
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
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
            // Ignore
        }
    }
}
