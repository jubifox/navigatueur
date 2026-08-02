using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Navigatueur.App.Services;

namespace Navigatueur.App.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
        DataContext = AppServices.History;
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => AppServices.History.Clear();

    private void OnEntryClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url })
        {
            AppServices.TabManager.OpenTab(url);
        }
    }
}
