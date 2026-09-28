using System.Windows;
using DevPurge.App.ViewModels;
using Wpf.Ui.Controls;

namespace DevPurge.App.Views;

/// <summary>
/// Interaction logic for RuleManagerDialog.xaml
/// </summary>
public partial class RuleManagerDialog : FluentWindow
{
    public RuleManagerDialog(RuleManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
