using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DevPurge.App.ViewModels;
using DevPurge.App.Views;
using DevPurge.Core.Models;

namespace DevPurge.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--screenshot"))
        {
            try
            {
                CaptureScreenshots();
            }
            catch (Exception ex)
            {
                File.WriteAllText(@"D:\repos\DevPurge\assets\error.log", ex.ToString());
            }
            Shutdown(0);
            return;
        }

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }

    private void CaptureScreenshots()
    {
        var assetsDir = @"D:\repos\DevPurge\assets";
        if (!Directory.Exists(assetsDir))
        {
            Directory.CreateDirectory(assetsDir);
        }

        // 1. Capture MainWindow content visual
        var vm = new MainViewModel
        {
            TargetPath = @"C:\repos",
            StatusText = "Scan complete. Discovered 1,721 disposable folders (93.72 GB reclaimable).",
            SummaryText = "93.72 GB selected (1,721 of 1,721 folders)",
            HasResults = true,
            IsScanning = false
        };

        var samples = new List<DiscoveredFolder>
        {
            new() { FolderName = "bin", Path = @"C:\repos\DiamondMicroServices\Dia.ImportEngineWebApi\bin", ArtifactType = ArtifactType.DotNetBuild, CategoryName = ".NET / C#", SizeBytes = 1073741824L, FileCount = 420, LastModifiedUtc = DateTime.UtcNow.AddDays(-27) },
            new() { FolderName = "obj", Path = @"C:\repos\DiamondMicroServices\Dia.ImportEngineWebApi\obj", ArtifactType = ArtifactType.DotNetBuild, CategoryName = ".NET / C#", SizeBytes = 287910000L, FileCount = 180, LastModifiedUtc = DateTime.UtcNow.AddDays(-27) },
            new() { FolderName = "node_modules", Path = @"C:\repos\WinUIApp\frontend\node_modules", ArtifactType = ArtifactType.NodeModules, CategoryName = "JavaScript / Node.js", SizeBytes = 724000000L, FileCount = 18400, LastModifiedUtc = DateTime.UtcNow.AddDays(-15) },
            new() { FolderName = "target", Path = @"C:\repos\TAM.TaxFlow.Engine\engine\target", ArtifactType = ArtifactType.RustTarget, CategoryName = "Rust", SizeBytes = 650000000L, FileCount = 2100, LastModifiedUtc = DateTime.UtcNow.AddDays(-34) },
            new() { FolderName = ".venv", Path = @"C:\repos\EventToJsonApp\.venv", ArtifactType = ArtifactType.PythonVenv, CategoryName = "Python", SizeBytes = 420000000L, FileCount = 3400, LastModifiedUtc = DateTime.UtcNow.AddDays(-8) },
            new() { FolderName = ".gradle", Path = @"C:\repos\BuildingExpenseManager\.gradle", ArtifactType = ArtifactType.GradleBuild, CategoryName = "Java / Android", SizeBytes = 380000000L, FileCount = 1200, LastModifiedUtc = DateTime.UtcNow.AddDays(-45) },
            new() { FolderName = "bin", Path = @"C:\repos\FavaSmsSolution\FavaSmsService\bin", ArtifactType = ArtifactType.DotNetBuild, CategoryName = ".NET / C#", SizeBytes = 320000000L, FileCount = 150, LastModifiedUtc = DateTime.UtcNow.AddDays(-12) },
            new() { FolderName = ".next", Path = @"C:\repos\Old.OnlineSales.Backend\client\.next", ArtifactType = ArtifactType.CacheAndTemp, CategoryName = "Web Frameworks", SizeBytes = 290000000L, FileCount = 890, LastModifiedUtc = DateTime.UtcNow.AddDays(-40) },
            new() { FolderName = "vendor", Path = @"C:\repos\PowershellScripts\tools\vendor", ArtifactType = ArtifactType.Vendor, CategoryName = "Composer / Go", SizeBytes = 180000000L, FileCount = 620, LastModifiedUtc = DateTime.UtcNow.AddDays(-19) }
        };

        foreach (var s in samples)
        {
            vm.DisplayedItems.Add(new FolderItemViewModel(s, vm.UpdateSummary));
        }

        var mainWindow = new MainWindow { DataContext = vm };
        if (mainWindow.Content is FrameworkElement mainElement)
        {
            mainElement.Width = 1080;
            mainElement.Height = 680;
            mainElement.Measure(new Size(1080, 680));
            mainElement.Arrange(new Rect(0, 0, 1080, 680));
            mainElement.UpdateLayout();
            SaveVisualToPng(mainElement, 1080, 680, Path.Combine(assetsDir, "screenshot.png"));
        }

        // 2. Capture PurgeSuccessDialog content visual
        var dialog = new PurgeSuccessDialog("93.72 GB", 1721, 93720000000L, true);
        if (dialog.Content is FrameworkElement dialogElement)
        {
            dialogElement.Width = 520;
            dialogElement.Height = 460;
            dialogElement.Measure(new Size(520, 460));
            dialogElement.Arrange(new Rect(0, 0, 520, 460));
            dialogElement.UpdateLayout();
            SaveVisualToPng(dialogElement, 520, 460, Path.Combine(assetsDir, "milestone_celebration.png"));
        }
    }

    private static void SaveVisualToPng(Visual visual, int width, int height, string filePath)
    {
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }
}
