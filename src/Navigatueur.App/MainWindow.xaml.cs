using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Navigatueur.App.Animation;
using Navigatueur.App.Models;
using Navigatueur.App.Services;
using Navigatueur.App.ViewModels;
using Navigatueur.Core.Settings;

namespace Navigatueur.App;

public partial class MainWindow : Window
{
    private readonly Views.MusicOverlayWindow _musicOverlay = new();
    private Views.SettingsWindow? _settingsWindow;
    private Views.ExtensionsWindow? _extensionsWindow;
    private Views.HistoryWindow? _historyWindow;
    private Views.PrivateBrowsingWindow? _privateWindow;
    private Point? _tabDragStart;
    private BrowserTabViewModel? _tabDragSource;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();

        var settings = AppServices.CurrentSettings;
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        if (settings.WindowLeft.HasValue)
        {
            Left = settings.WindowLeft.Value;
        }

        if (settings.WindowTop.HasValue)
        {
            Top = settings.WindowTop.Value;
        }

        DataContext = new MainWindowViewModel(AppServices.TabManager);

        if (AppServices.Settings.IsFirstRun)
        {
            new Views.WelcomeWindow().ShowDialog();
        }

        ApplyAddressBarPosition();
        AppServices.Theme.PropertyChanged += OnThemePropertyChanged;

        Closing += OnClosing;
    }

    private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeService.AddressBarPosition))
        {
            ApplyAddressBarPosition();
        }
    }

    /// <summary>
    /// The address bar is a single TextBox instance moved between three empty
    /// slot Borders (top toolbar, bottom of the window, top of the tab
    /// column) rather than duplicated, so there's exactly one source of truth
    /// for its focus/selection state regardless of where it's docked.
    /// </summary>
    private void ApplyAddressBarPosition()
    {
        AddressBarTopSlot.Child = null;
        AddressBarBottomSlot.Child = null;
        AddressBarSidebarSlot.Child = null;

        var target = AppServices.Theme.AddressBarPosition switch
        {
            "Bottom" => AddressBarBottomSlot,
            "Sidebar" => AddressBarSidebarSlot,
            _ => AddressBarTopSlot,
        };
        target.Child = AddressBarTextBox;
    }

    private DateTime _lastTrailSpawn = DateTime.MinValue;
    private static readonly TimeSpan TrailSpawnInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>osu!-style fading dot trail. Only ever sees moves over the app's own WPF chrome (see the XAML comment above CursorTrailCanvas) — that's an inherent WebView2 limitation, not a bug.</summary>
    private void OnWindowMouseMoveForTrail(object sender, MouseEventArgs e)
    {
        var now = DateTime.UtcNow;
        if (now - _lastTrailSpawn < TrailSpawnInterval)
        {
            return;
        }

        _lastTrailSpawn = now;

        var position = e.GetPosition(CursorTrailCanvas);
        var accentColor = Application.Current.Resources["AccentBrush"] is SolidColorBrush accentBrush
            ? accentBrush.Color
            : Colors.White;

        const double size = 8;
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(accentColor),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(dot, position.X - size / 2);
        Canvas.SetTop(dot, position.Y - size / 2);

        var scale = new ScaleTransform(1, 1, size / 2, size / 2);
        dot.RenderTransform = scale;
        CursorTrailCanvas.Children.Add(dot);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var shrink = new DoubleAnimation(1, 0.2, TimeSpan.FromMilliseconds(450));
        fade.Completed += (_, _) => CursorTrailCanvas.Children.Remove(dot);

        dot.BeginAnimation(UIElement.OpacityProperty, fade);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    private const double SidebarCollapsedWidth = 52;
    private const double SidebarExpandedWidth = 220;

    private void OnTabColumnMouseEnter(object sender, MouseEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext;
        if (vm.IsSidebarPinned)
        {
            return;
        }

        vm.IsSidebarExpanded = true;
        AnimateTabColumnWidth(SidebarExpandedWidth);
    }

    private void OnTabColumnMouseLeave(object sender, MouseEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext;
        if (vm.IsSidebarPinned)
        {
            return;
        }

        vm.IsSidebarExpanded = false;
        AnimateTabColumnWidth(SidebarCollapsedWidth);
    }

    private void AnimateTabColumnWidth(double toPixels)
    {
        var animation = new GridLengthAnimation
        {
            From = TabColumnDefinition.Width,
            To = new GridLength(toPixels),
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };

        // FillBehavior.Stop releases the animation's hold on the property as soon
        // as it completes; without this, WPF keeps Width "animated" forever and a
        // later GridSplitter drag (which sets Width directly) would be ignored.
        animation.Completed += (_, _) => TabColumnDefinition.Width = new GridLength(toPixels);
        TabColumnDefinition.BeginAnimation(ColumnDefinition.WidthProperty, animation);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnToggleMusicOverlayClick(object sender, RoutedEventArgs e)
    {
        if (_musicOverlay.IsVisible)
        {
            _musicOverlay.Hide();
        }
        else
        {
            _musicOverlay.Show();
        }
    }

    /// <summary>
    /// Closing the main window normally quits the app. But if the floating
    /// music overlay is currently up, closing instead backgrounds the browser
    /// (hide + tray icon) so the mini-player keeps running — matching how
    /// Spotify/Discord-style mini-players survive their host app closing.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_forceClose && _musicOverlay.IsVisible)
        {
            e.Cancel = true;
            // Persist now, not only on a real quit — otherwise a session that
            // never explicitly hits "Quitter" from the tray (closed via Task
            // Manager, or just left running for days) loses every tab
            // opened/closed since the last real exit, and the *previous*
            // (stale) session gets restored next launch instead.
            SaveSessionState();
            Hide();
            ShowTrayIcon();
            return;
        }

        _musicOverlay.ShutDown();
        _trayIcon?.Dispose();
        _trayIcon = null;

        SaveSessionState();

        // WebView2's renderer/GPU/network child processes are only reliably
        // torn down if every live CoreWebView2 is explicitly disposed before
        // exit — otherwise background timers (idle-suspend, update checks,
        // memory monitor) can keep this process alive a moment longer than
        // expected, and an audio-playing tab's renderer has been observed to
        // keep producing sound during that gap. Suspending every tab tears
        // down its WebView2 synchronously via the existing suspend/resume
        // pipeline, then a hard process exit guarantees nothing lingers.
        foreach (var tab in AppServices.TabManager.Tabs)
        {
            tab.IsSuspended = true;
        }

        Environment.Exit(0);
    }

    private void SaveSessionState()
    {
        var settings = AppServices.CurrentSettings;
        settings.WindowWidth = Width;
        settings.WindowHeight = Height;
        settings.WindowLeft = Left;
        settings.WindowTop = Top;

        var tabManager = AppServices.TabManager;

        settings.Groups = tabManager.Groups.Select(group => new SessionGroupState
        {
            Id = group.Id,
            Name = group.Name,
            ColorHex = group.ColorHex,
            IsCollapsed = group.IsCollapsed,
        }).ToList();

        settings.Tabs = tabManager.Tabs.Select(tab => new SessionTabState
        {
            Url = tab.AddressBarText,
            GroupId = tab.GroupId,
            IsPinned = tab.IsPinned,
        }).ToList();

        settings.ActiveTabIndex = tabManager.ActiveTab is null
            ? -1
            : tabManager.Tabs.IndexOf(tabManager.ActiveTab);

        // SavedGroups is already kept in sync with disk by TabManagerService
        // whenever it changes, so it's deliberately not re-serialized here.
        AppServices.Settings.Save(settings);
    }

    private void ShowTrayIcon()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = true;
            return;
        }

        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        var icon = exePath is not null
            ? System.Drawing.Icon.ExtractAssociatedIcon(exePath)
            : System.Drawing.SystemIcons.Application;

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir Navigatueur", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Quitter", null, (_, _) =>
        {
            _forceClose = true;
            Close();
        });

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "Navigatueur — lecture en cours",
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
        }
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        var update = AppServices.Update;
        var result = MessageBox.Show(
            this,
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
            _extensionsWindow = new Views.ExtensionsWindow { Owner = this };
            _extensionsWindow.Closed += (_, _) => _extensionsWindow = null;
            _extensionsWindow.Show();
        }
        else
        {
            _extensionsWindow.Activate();
        }
    }

    private async void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        var activeTab = (DataContext as MainWindowViewModel)?.ActiveTab;
        if (activeTab is null)
        {
            return;
        }

        var mhtml = await activeTab.CaptureMhtmlAsync();
        if (mhtml is null)
        {
            MessageBox.Show(this, "Impossible d'enregistrer cette page.", "Enregistrer sous",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var suggestedName = SanitizeFileName(string.IsNullOrWhiteSpace(activeTab.Title) ? "page" : activeTab.Title);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Page web (*.mhtml)|*.mhtml",
            FileName = suggestedName,
        };

        if (dialog.ShowDialog(this) == true)
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
        ((DataContext as MainWindowViewModel)?.ActiveTab)?.TranslatePageCommand.Execute(null);

    /// <summary>
    /// Chromium already handles Ctrl+F natively (find-in-page toolbar) when
    /// the embedded WebView2 has keyboard focus — that shortcut is never
    /// routed through WPF at all, since WebView2 is a separate native HWND.
    /// This menu entry just focuses the page and forwards the same keystroke
    /// for people who'd rather click a menu item than remember the shortcut.
    /// </summary>
    private void OnFindInPageClick(object sender, RoutedEventArgs e)
    {
        var webView = (DataContext as MainWindowViewModel)?.ActiveTab?.WebViewControl;
        if (webView is null)
        {
            return;
        }

        webView.Focus();
        System.Windows.Forms.SendKeys.SendWait("^f");
    }

    private void OnOpenHistoryClick(object sender, RoutedEventArgs e)
    {
        if (_historyWindow is null)
        {
            _historyWindow = new Views.HistoryWindow { Owner = this };
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
            _settingsWindow = new Views.SettingsWindow { Owner = this };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    private void OnOpenPrivateWindowClick(object sender, RoutedEventArgs e)
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

    private void OnToggleDownloadsClick(object sender, RoutedEventArgs e) =>
        DownloadsPopup.IsOpen = !DownloadsPopup.IsOpen;

    /// <summary>WPF's TextBox only handles double-click (select word) natively — triple-click (select the whole address) needs an explicit hook.</summary>
    private void OnAddressBarPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 3 && sender is TextBox textBox)
        {
            textBox.SelectAll();
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

    private void OnTabPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _tabDragStart = e.GetPosition(null);
        _tabDragSource = (sender as FrameworkElement)?.DataContext as BrowserTabViewModel;
    }

    private void OnTabPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _tabDragStart is not { } start || _tabDragSource is null)
        {
            return;
        }

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, _tabDragSource, DragDropEffects.Move);
        _tabDragStart = null;
        _tabDragSource = null;
    }

    private void OnTabDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(BrowserTabViewModel)))
        {
            return;
        }

        var source = (BrowserTabViewModel)e.Data.GetData(typeof(BrowserTabViewModel))!;
        if (sender is not FrameworkElement { DataContext: BrowserTabViewModel target } targetElement || source == target)
        {
            return;
        }

        // Dropping on the middle band of the target row groups the two tabs (Chrome/Edge-style);
        // dropping near the top/bottom edge just reorders, as before.
        var dropY = e.GetPosition(targetElement).Y;
        var isCenterDrop = targetElement.ActualHeight > 0
            && dropY > targetElement.ActualHeight * 0.25
            && dropY < targetElement.ActualHeight * 0.75;

        if (isCenterDrop)
        {
            AppServices.TabManager.GroupTabs(source, target);
        }
        else
        {
            AppServices.TabManager.ReorderTab(source, target);
        }
    }

    private void OnGroupNameEditPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Stops the click from bubbling into the group header Button, which would otherwise toggle collapse instead of placing the caret.
        e.Handled = true;
        ((TextBox)sender).Focus();
    }

    private void OnGroupNameEditKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (sender as FrameworkElement)?.DataContext is TabGroup group)
        {
            group.IsEditingName = false;
        }
    }

    private void OnGroupNameEditLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TabGroup group)
        {
            group.IsEditingName = false;
        }
    }

    private void OnGroupNameEditIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox { IsVisible: true } textBox)
        {
            return;
        }

        // The context menu that triggered "Renommer..." restores focus to its
        // placement target (the group's Button) as it closes — if that lands
        // after a synchronous Focus() call here, it steals focus right back
        // and LostFocus immediately reverts the rename. Deferring past that
        // restoration (ApplicationIdle) makes ours win instead.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
