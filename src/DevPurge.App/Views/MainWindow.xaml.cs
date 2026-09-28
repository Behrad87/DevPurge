using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Wpf.Ui.Controls;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace DevPurge.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnHistoryButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && DataContext is ViewModels.MainViewModel vm)
        {
            var cm = new ContextMenu();
            if (vm.RecentPaths.Count == 0)
            {
                var emptyItem = new MenuItem { Header = "No recent workspaces yet", IsEnabled = false };
                cm.Items.Add(emptyItem);
            }
            else
            {
                foreach (var path in vm.RecentPaths)
                {
                    var item = new MenuItem
                    {
                        Header = $"📁  {path}",
                        Command = vm.SelectRecentPathCommand,
                        CommandParameter = path
                    };
                    cm.Items.Add(item);
                }

                cm.Items.Add(new Separator());
                var clearItem = new MenuItem
                {
                    Header = "Clear Recent History",
                    Command = vm.ClearRecentPathsCommand
                };
                cm.Items.Add(clearItem);
            }

            cm.PlacementTarget = fe;
            cm.Placement = PlacementMode.Bottom;
            cm.IsOpen = true;
        }
    }

    private void OnSupportButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu != null)
        {
            fe.ContextMenu.DataContext = fe.DataContext ?? DataContext;
            fe.ContextMenu.PlacementTarget = fe;
            fe.ContextMenu.Placement = PlacementMode.Bottom;
            fe.ContextMenu.IsOpen = true;
        }
    }

    private void StorageBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.UpdateStorageBarWidth(e.NewSize.Width);
        }
    }
}