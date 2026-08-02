using System.IO;
using System.Windows;
using Navigatueur.App.Services;
using Navigatueur.App.ViewModels;
using Navigatueur.Core.Settings;

namespace Navigatueur.App.Views;

/// <summary>
/// A fully isolated browsing session: its own throwaway <see cref="AppSettings"/>
/// (never saved to disk) and its own WebView2 profile folder under Temp, so
/// cookies/history/cache never touch the normal profile. The profile folder
/// is deleted on close — best effort, since the WebView2 process can take a
/// moment to release its file locks.
/// </summary>
public partial class PrivateBrowsingWindow : Window
{
    private readonly string _profileFolder = Path.Combine(
        Path.GetTempPath(), "Navigatueur", "Private", Guid.NewGuid().ToString("N"));

    private readonly TabManagerService _tabManager;

    public PrivateBrowsingWindow()
    {
        InitializeComponent();

        var environment = new WebView2EnvironmentService(_profileFolder);
        _tabManager = new TabManagerService(new AppSettings(), environment, isPrivate: true);
        DataContext = new MainWindowViewModel(_tabManager);

        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>Same reasoning as MainWindow: explicitly dispose every live WebView2 before the window closes, so an audio-playing private tab doesn't keep its renderer (and sound) alive past the window disappearing.</summary>
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        foreach (var tab in _tabManager.Tabs)
        {
            tab.IsSuspended = true;
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        _ = TryDeleteProfileFolderAsync();
    }

    private async Task TryDeleteProfileFolderAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_profileFolder))
                {
                    Directory.Delete(_profileFolder, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                await Task.Delay(500);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(500);
            }
        }
    }
}
