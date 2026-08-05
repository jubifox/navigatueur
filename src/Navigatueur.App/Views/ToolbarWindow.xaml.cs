using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Navigatueur.App.Services;
using Navigatueur.App.ViewModels;

namespace Navigatueur.App.Views;

/// <summary>
/// The toolbar (nav buttons, address bar, and every right-side icon button),
/// as a separate top-level window — see the XAML doc comment for why. Mostly
/// self-contained like TabSidebarWindow, except for anything that needs
/// MainWindow's own state (currently just the music overlay toggle), which
/// goes through the <see cref="_mainWindow"/> reference passed into the
/// constructor instead of duplicating that state here.
/// </summary>
public partial class ToolbarWindow : Window
{
    private const double CollapsedHeight = 10;
    private const double ExpandedHeight = 52;

    private readonly MainWindowViewModel _viewModel;
    private readonly MainWindow _mainWindow;
    private bool _isPointerInside;
    private bool _isForcedExpanded;

    private Views.SettingsWindow? _settingsWindow;
    private Views.ExtensionsWindow? _extensionsWindow;
    private Views.HistoryWindow? _historyWindow;
    private Views.PrivateBrowsingWindow? _privateWindow;

    public ToolbarWindow(MainWindowViewModel viewModel, MainWindow mainWindow)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _mainWindow = mainWindow;
        DataContext = viewModel;
        Height = CollapsedHeight;
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _isPointerInside = true;
        AnimateExpanded(true);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _isPointerInside = false;
        if (!_isForcedExpanded)
        {
            AnimateExpanded(false);
        }
    }

    /// <summary>
    /// Called by MainWindow while the address bar has keyboard focus, so
    /// typing a URL doesn't get cut off by the toolbar retracting out from
    /// under the cursor the moment it happens to leave the window bounds.
    /// </summary>
    public void SetForcedExpanded(bool expanded)
    {
        _isForcedExpanded = expanded;
        if (expanded || _isPointerInside)
        {
            AnimateExpanded(true);
        }
        else
        {
            AnimateExpanded(false);
        }
    }

    private void AnimateExpanded(bool expanded)
    {
        var toHeight = expanded ? ExpandedHeight : CollapsedHeight;
        var heightAnimation = new DoubleAnimation
        {
            To = toHeight,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop,
        };
        heightAnimation.Completed += (_, _) => Height = toHeight;
        BeginAnimation(HeightProperty, heightAnimation);

        var contentFade = new DoubleAnimation
        {
            To = expanded ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(expanded ? 180 : 100),
        };
        ToolbarContent.BeginAnimation(OpacityProperty, contentFade);

        var handleFade = new DoubleAnimation
        {
            To = expanded ? 0 : 0.6,
            Duration = TimeSpan.FromMilliseconds(150),
        };
        CollapsedHandle.BeginAnimation(OpacityProperty, handleFade);
    }

    private void OnToggleDownloadsClick(object sender, RoutedEventArgs e) =>
        DownloadsPopup.IsOpen = !DownloadsPopup.IsOpen;

    private void OnToggleMusicOverlayClick(object sender, RoutedEventArgs e) => _mainWindow.ToggleMusicOverlay();

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        var update = AppServices.Update;
        var result = MessageBox.Show(
            _mainWindow,
            $"Une nouvelle version ({update.LatestVersion}) est disponible. Télécharger et installer maintenant ? L'application va se fermer pendant l'installation.",
            "Mise à jour disponible",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await update.DownloadAndInstallAsync();
        }
    }

    private void OnOpenExtensionsClick(object sender, RoutedEventArgs e)
    {
        if (_extensionsWindow is null)
        {
            _extensionsWindow = new Views.ExtensionsWindow { Owner = _mainWindow };
            _extensionsWindow.Closed += (_, _) => _extensionsWindow = null;
            _extensionsWindow.Show();
        }
        else
        {
            _extensionsWindow.Activate();
        }
    }

    private void OnOpenHistoryClick(object sender, RoutedEventArgs e)
    {
        if (_historyWindow is null)
        {
            _historyWindow = new Views.HistoryWindow { Owner = _mainWindow };
            _historyWindow.Closed += (_, _) => _historyWindow = null;
            _historyWindow.Show();
        }
        else
        {
            _historyWindow.Activate();
        }
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new Views.SettingsWindow { Owner = _mainWindow };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    private void OnOpenPrivateWindowClick(object sender, RoutedEventArgs e) => OpenPrivateWindow();

    /// <summary>Also called directly by MainWindow's Ctrl+Shift+N handler.</summary>
    public void OpenPrivateWindow()
    {
        if (_privateWindow is null)
        {
            _privateWindow = new Views.PrivateBrowsingWindow();
            _privateWindow.Closed += (_, _) => _privateWindow = null;
            _privateWindow.Show();
        }
        else
        {
            _privateWindow.Activate();
        }
    }

    private void OnOpenSavedGroupsClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu })
        {
            menu.IsOpen = true;
        }
    }

    private void OnOpenMoreMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu })
        {
            menu.IsOpen = true;
        }
    }

    private async void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        var activeTab = _viewModel.ActiveTab;
        if (activeTab is null)
        {
            return;
        }

        var mhtml = await activeTab.CaptureMhtmlAsync();
        if (mhtml is null)
        {
            MessageBox.Show(_mainWindow, "Impossible d'enregistrer cette page.", "Enregistrer sous",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var suggestedName = SanitizeFileName(string.IsNullOrWhiteSpace(activeTab.Title) ? "page" : activeTab.Title);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Page web (*.mhtml)|*.mhtml",
            FileName = suggestedName,
        };

        if (dialog.ShowDialog(_mainWindow) == true)
        {
            await File.WriteAllTextAsync(dialog.FileName, mhtml);
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name;
    }

    private void OnTranslatePageClick(object sender, RoutedEventArgs e) =>
        _viewModel.ActiveTab?.TranslatePageCommand.Execute(null);

    /// <summary>
    /// Chromium already handles Ctrl+F natively (find-in-page toolbar) when
    /// the embedded WebView2 has keyboard focus — that shortcut is never
    /// routed through WPF at all, since WebView2 is a separate native HWND.
    /// This menu entry just focuses the page and forwards the same keystroke
    /// for people who'd rather click a menu item than remember the shortcut.
    /// </summary>
    private void OnFindInPageClick(object sender, RoutedEventArgs e)
    {
        var webView = _viewModel.ActiveTab?.WebViewControl;
        if (webView is null)
        {
            return;
        }

        webView.Focus();
        System.Windows.Forms.SendKeys.SendWait("^f");
    }
}
