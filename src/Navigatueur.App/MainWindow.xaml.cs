using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Rectangle = System.Windows.Shapes.Rectangle;
using Shape = System.Windows.Shapes.Shape;
using System.Windows.Threading;
using Navigatueur.App.Services;
using Navigatueur.App.Shell;
using Navigatueur.App.ViewModels;
using Navigatueur.App.Views;
using Navigatueur.Core;
using Navigatueur.Core.Settings;
using Navigatueur.Core.Shortcuts;

namespace Navigatueur.App;

/// <summary>
/// A browser window. The page fills it; the toolbar and the tab column are
/// panels that <see cref="ImmersiveShell"/> slides over it on demand (or keeps
/// docked, in classic mode).
///
/// The same class serves three roles: the primary window (owns session
/// persistence, the tray icon and app exit), a secondary "Nouvelle fenêtre"
/// (own tabs, shared profile, persists nothing), and a private window (own
/// throwaway profile, deleted on close, violet palette).
/// </summary>
public partial class MainWindow : Window, IShellCommands
{
    private readonly TabManagerService _tabManager;
    private readonly MainWindowViewModel _viewModel;
    private readonly bool _isPrimary;
    private readonly bool _isPrivate;
    private readonly string? _privateProfileFolder;
    private readonly ToolbarPanel _toolbar;
    private readonly TabsPanel _tabs;
    private readonly TranslateTransform _loadingStreakOffset = new(-240, 0);
    private readonly WindowFrameController _frame;
    private readonly ImmersiveShell _shell;
    private readonly DispatcherTimer? _autosaveTimer;

    private Views.MusicOverlayWindow? _musicOverlay;
    private Views.ExtensionsWindow? _extensionsWindow;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private BrowserTabViewModel? _observedTab;
    private bool _forceClose;
    private bool _enteredFullScreenForPage;

    /// <param name="tabManager">Only for secondary/private windows: their own tab set. The primary window uses the app-wide one.</param>
    /// <param name="isPrimary">Only the primary window saves the session, owns the tray icon and quits the app on close.</param>
    /// <param name="isPrivate">Private browsing: violet palette, nothing recorded, <paramref name="privateProfileFolder"/> deleted on close.</param>
    public MainWindow(TabManagerService? tabManager = null, bool isPrimary = true, bool isPrivate = false, string? privateProfileFolder = null)
    {
        InitializeComponent();

        _isPrimary = isPrimary;
        _isPrivate = isPrivate;
        _privateProfileFolder = privateProfileFolder;
        _tabManager = tabManager ?? AppServices.TabManager;

        RestoreWindowBounds();

        _viewModel = new MainWindowViewModel(_tabManager, isPrivate);
        DataContext = _viewModel;

        _toolbar = new ToolbarPanel(this) { DataContext = _viewModel };
        _tabs = new TabsPanel(this) { DataContext = _viewModel };
        ToolbarHost.Child = _toolbar;
        SidebarHost.Child = _tabs;
        LoadingHost.Child = BuildLoadingStrip();

        var panelEffects = AppServices.Interface.Current.ArePanelEffectsEnabled;
        ToolbarHost.RequestTransparency = panelEffects;
        SidebarHost.RequestTransparency = panelEffects;
        LoadingHost.RequestTransparency = false;

        if (_isPrivate)
        {
            ApplyPrivatePalette();
        }

        _frame = new WindowFrameController(this, RootGrid);
        _shell = new ImmersiveShell(
            this, RootGrid, ContentHost, ToolbarHost, SidebarHost, LoadingHost,
            _toolbar, _tabs, _frame, AppServices.Interface, FocusPage);

        // Shortcuts are caught wherever focus is: in the window (and the page,
        // whose accelerator keys WebView2 re-raises here), or inside a panel —
        // each panel is its own input root, so it needs its own hook.
        PreviewKeyDown += OnShortcutKeyDown;
        _toolbar.PreviewKeyDown += OnShortcutKeyDown;
        _tabs.PreviewKeyDown += OnShortcutKeyDown;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ObserveActiveTab();

        StateChanged += (_, _) => SyncFrameState();
        _frame.FrameChanged += (_, _) => SyncFrameState();
        AppServices.Theme.AppearanceChanged += OnAppearanceChanged;

        // Ctrl+T / "+" open a blank tab: put the caret in the address bar, like every browser.
        _tabManager.TabOpened += OnTabOpened;

        if (_isPrimary)
        {
            _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            _autosaveTimer.Tick += (_, _) => SaveSessionState();
            _autosaveTimer.Start();

            AppServices.RequestForceQuit = () =>
            {
                _forceClose = true;
                Close();
            };

            ContentRendered += OnFirstContentRendered;
        }

        Closing += OnClosing;
        Closed += OnClosed;
    }

    // ================================================================ setup

    private void RestoreWindowBounds()
    {
        var settings = AppServices.CurrentSettings;

        // Never larger than the screen: a size saved on a big monitor (or the
        // 1280x800 default on a small laptop) would otherwise open with its
        // toolbar off-screen.
        var work = SystemParameters.WorkArea;
        Width = Math.Clamp(settings.WindowWidth, MinWidth, Math.Max(MinWidth, work.Width));
        Height = Math.Clamp(settings.WindowHeight, MinHeight, Math.Max(MinHeight, work.Height));

        if (settings.WindowLeft is { } left && settings.WindowTop is { } top && IsOnScreen(left, top, Width))
        {
            // Secondary windows cascade a little so they don't hide the primary one exactly.
            var offset = _isPrimary ? 0 : 32;
            Left = left + offset;
            Top = top + offset;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (_isPrimary && settings.WasMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>True when a window at this position would have its top edge (where its toolbar is) on one of the screens.</summary>
    private static bool IsOnScreen(double left, double top, double width) =>
        top >= SystemParameters.VirtualScreenTop
        && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 48
        && left + width > SystemParameters.VirtualScreenLeft + 100
        && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100;

    /// <summary>The 3px line shown at the top of the page while it loads and the toolbar (which has its own) is hidden.</summary>
    private FrameworkElement BuildLoadingStrip()
    {
        var streak = new Rectangle { Width = 240, Height = 3, RenderTransform = _loadingStreakOffset };
        streak.SetResourceReference(Shape.FillProperty, "AccentBrush");
        streak.OpacityMask = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Colors.Transparent, 0),
                new(Colors.Black, 0.5),
                new(Colors.Transparent, 1),
            },
            new Point(0, 0),
            new Point(1, 0));

        var track = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
        track.SetResourceReference(Panel.BackgroundProperty, "AccentSoftBrush");
        track.Children.Add(streak);
        return track;
    }

    private void ApplyPrivatePalette()
    {
        var overrides = AppServices.Theme.CreatePrivateOverrides();
        foreach (var target in new FrameworkElement[] { this, _toolbar, _tabs, (FrameworkElement)LoadingHost.Child! })
        {
            target.Resources.MergedDictionaries.Clear();
            target.Resources.MergedDictionaries.Add(overrides);
        }
    }

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        if (_isPrivate)
        {
            ApplyPrivatePalette();
        }

        // Shadow strength depends on light/dark.
        _shell.RefreshBehaviors();
    }

    private void OnFirstContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnFirstContentRendered;

        var interfaceSettings = AppServices.Interface.Current;
        if (AppServices.Settings.IsFirstRun || !interfaceSettings.HasSeenIntro)
        {
            new Views.WelcomeWindow(showEngineChoice: AppServices.Settings.IsFirstRun) { Owner = this }.ShowDialog();
            AppServices.Interface.Update(s => s.HasSeenIntro = true);
        }
    }

    // ========================================================= view model

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ActiveTab))
        {
            ObserveActiveTab();
        }
    }

    private void ObserveActiveTab()
    {
        if (_observedTab is not null)
        {
            _observedTab.PropertyChanged -= OnActiveTabPropertyChanged;
        }

        _observedTab = _viewModel.ActiveTab;
        if (_observedTab is not null)
        {
            _observedTab.PropertyChanged += OnActiveTabPropertyChanged;
        }

        SyncLoading();
        SyncPageFullScreen();

        // The previous tab's page is now hidden: if it had the keyboard, give
        // the keyboard to the new tab's page, or shortcuts and typing would go
        // to an invisible page (seen on the Windows CI run: Ctrl+H lost after
        // switching tabs). Not when the user is working in a panel.
        var newTab = _observedTab;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (newTab is not null && newTab == _tabManager.ActiveTab && IsActive &&
                !_toolbar.IsKeyboardFocusWithin && !_tabs.IsKeyboardFocusWithin)
            {
                newTab.FocusPage();
            }
        }), DispatcherPriority.Input);
    }

    private void OnActiveTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BrowserTabViewModel.IsLoading):
                SyncLoading();
                break;
            case nameof(BrowserTabViewModel.IsPageFullScreen):
                SyncPageFullScreen();
                break;
        }
    }

    private void SyncLoading()
    {
        var loading = _observedTab?.IsLoading == true;
        _shell.IsLoading = loading;

        if (loading)
        {
            var sweep = new DoubleAnimation(-240, Math.Max(ActualWidth, 800), TimeSpan.FromMilliseconds(1100))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            _loadingStreakOffset.BeginAnimation(TranslateTransform.XProperty, sweep);
        }
        else
        {
            _loadingStreakOffset.BeginAnimation(TranslateTransform.XProperty, null);
        }
    }

    /// <summary>A video (or any element) went fullscreen in the page: make the window fullscreen too, and get every panel out of the way.</summary>
    private void SyncPageFullScreen()
    {
        var pageFullScreen = _observedTab?.IsPageFullScreen == true;
        _shell.IsPageFullScreen = pageFullScreen;

        if (pageFullScreen && !_frame.IsFullScreen)
        {
            _enteredFullScreenForPage = true;
            _frame.EnterFullScreen();
        }
        else if (!pageFullScreen && _enteredFullScreenForPage)
        {
            _enteredFullScreenForPage = false;
            _frame.ExitFullScreen();
        }
    }

    private void SyncFrameState()
    {
        _viewModel.IsMaximized = WindowState == WindowState.Maximized;
        _viewModel.IsFullScreen = _frame.IsFullScreen;
    }

    private void OnTabOpened(BrowserTabViewModel tab)
    {
        if (tab.InitialUrl != InternalPages.NewTabUrl && tab.InitialUrl != AppServices.CurrentSettings.HomePageUrl)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            // The address bar, not the blank page, gets the keyboard on a new tab.
            tab.WantsFocus = false;
            _shell.RevealToolbar();
            _toolbar.FocusAddressBar();
        }), DispatcherPriority.Input);
    }

    // ======================================================== shortcuts

    private void OnShortcutKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && _toolbar.CloseOpenMenus())
        {
            e.Handled = true;
            return;
        }

        if (!AppServices.Shortcuts.TryResolve(e, out var commandId))
        {
            return;
        }

        e.Handled = true;
        _toolbar.CloseOpenMenus();
        Execute(commandId);
    }

    // ===================================================== IShellCommands

    public void Execute(string commandId)
    {
        var active = _tabManager.ActiveTab;

        if (commandId.StartsWith(ShortcutCommands.SelectTabPrefix, StringComparison.Ordinal))
        {
            if (commandId == ShortcutCommands.SelectLastTab)
            {
                _tabManager.ActivateLastTab();
            }
            else if (int.TryParse(commandId[ShortcutCommands.SelectTabPrefix.Length..], out var number))
            {
                _tabManager.ActivateTabAtIndex(number - 1);
            }

            return;
        }

        switch (commandId)
        {
            case ShortcutCommands.NewTab:
                _tabManager.OpenTab();
                break;
            case ShortcutCommands.CloseTab:
                if (active is not null)
                {
                    _tabManager.CloseTab(active);
                }

                break;
            case ShortcutCommands.NextTab:
                _tabManager.ActivateAdjacentTab(1);
                break;
            case ShortcutCommands.PreviousTab:
                _tabManager.ActivateAdjacentTab(-1);
                break;
            case ShortcutCommands.ReopenClosedTab:
                _tabManager.ReopenClosedTab();
                break;
            case ShortcutCommands.ShowTabs:
                _shell.RevealSidebar();
                _tabs.FocusActiveTab();
                break;
            case ShortcutCommands.FocusAddressBar:
                _shell.RevealToolbar();
                _toolbar.FocusAddressBar();
                break;
            case ShortcutCommands.Reload:
                active?.ReloadCommand.Execute(null);
                break;
            case ShellCommandIds.Stop:
                active?.StopCommand.Execute(null);
                break;
            case ShortcutCommands.Back:
                if (active?.GoBackCommand.CanExecute(null) == true)
                {
                    active.GoBackCommand.Execute(null);
                }

                break;
            case ShortcutCommands.Forward:
                if (active?.GoForwardCommand.CanExecute(null) == true)
                {
                    active.GoForwardCommand.Execute(null);
                }

                break;
            case ShortcutCommands.ToggleFullScreen:
                ToggleFullScreen();
                break;
            case ShortcutCommands.ToggleBookmark:
                ToggleBookmark();
                break;
            case ShortcutCommands.NewWindow:
                OpenNewWindow();
                break;
            case ShortcutCommands.NewPrivateWindow:
                OpenPrivateWindow();
                break;
            case ShortcutCommands.OpenHistory:
                OpenInternalPage(InternalPages.HistoryUrl);
                break;
            case ShortcutCommands.OpenDownloads:
                _shell.RevealToolbar();
                _toolbar.ToggleDownloads();
                break;
            case ShortcutCommands.OpenSettings:
                OpenInternalPage(InternalPages.SettingsUrl);
                break;
            case ShellCommandIds.SaveAs:
                SaveActivePageAs();
                break;
            case ShellCommandIds.Find:
                FindInPage();
                break;
            case ShellCommandIds.Translate:
                active?.TranslatePageCommand.Execute(null);
                break;
            case ShellCommandIds.PictureInPicture:
                active?.TogglePictureInPictureCommand.Execute(null);
                break;
            case ShellCommandIds.ZoomIn:
                active?.ZoomInCommand.Execute(null);
                break;
            case ShellCommandIds.ZoomOut:
                active?.ZoomOutCommand.Execute(null);
                break;
            case ShellCommandIds.ZoomReset:
                active?.ResetZoomCommand.Execute(null);
                break;
            case ShellCommandIds.ToggleMusic:
                ToggleMusicOverlay();
                break;
            case ShellCommandIds.OpenExtensions:
                OpenExtensionsWindow();
                break;
            case ShellCommandIds.InstallUpdate:
                InstallUpdate();
                break;
            case ShellCommandIds.Minimize:
                _frame.Minimize();
                break;
            case ShellCommandIds.ToggleMaximize:
                _frame.ToggleMaximize();
                break;
            case ShellCommandIds.CloseWindow:
                Close();
                break;
            case ShellCommandIds.ToggleSidebarDock:
                AppServices.Interface.ToggleSidebarDocked();
                break;
            case ShellCommandIds.HideToolbar:
                _shell.HideToolbar();
                break;
            case ShellCommandIds.HideTabs:
                _shell.HideSidebar();
                break;
        }
    }

    public void NavigateActiveTab(string input) => _tabManager.ActiveTab?.NavigateTo(input);

    public void OpenUrl(string url, bool inBackgroundTab)
    {
        if (inBackgroundTab || _tabManager.ActiveTab is null)
        {
            _tabManager.OpenBackgroundTab(url);
        }
        else
        {
            _tabManager.ActiveTab.NavigateTo(url);
            FocusPage();
        }
    }

    public void FocusPage() => _tabManager.ActiveTab?.FocusPage();

    public void StartWindowDrag() => _frame.StartDrag();

    // =========================================================== actions

    private void ToggleFullScreen()
    {
        if (_observedTab?.IsPageFullScreen == true)
        {
            // F11 while a video is fullscreen leaves the video's fullscreen, which in turn restores the window.
            _ = _observedTab.CoreWebView?.ExecuteScriptAsync("document.exitFullscreen && document.exitFullscreen()");
            return;
        }

        _frame.ToggleFullScreen();
    }

    private void ToggleBookmark()
    {
        var active = _tabManager.ActiveTab;
        if (active is null)
        {
            return;
        }

        AppServices.Bookmarks.Toggle(active.AddressBarText, active.Title);

        // The star is the only feedback: show it briefly if the toolbar is hidden.
        if (!_shell.IsToolbarShown)
        {
            _shell.RevealToolbar();
        }
    }

    /// <summary>Settings and history are pages: reuse a tab already showing one, else the current blank tab, else a new tab.</summary>
    private void OpenInternalPage(string url)
    {
        var existing = _tabManager.Tabs.FirstOrDefault(t =>
            string.Equals(StripFragment(t.AddressBarText), url, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _tabManager.ActivateTab(existing);
            return;
        }

        var active = _tabManager.ActiveTab;
        if (active is not null && string.Equals(StripFragment(active.AddressBarText), InternalPages.NewTabUrl, StringComparison.OrdinalIgnoreCase))
        {
            active.NavigateTo(url);
            return;
        }

        _tabManager.OpenTab(url);
    }

    private static string StripFragment(string url)
    {
        var index = url.IndexOfAny(new[] { '#', '?' });
        return index < 0 ? url : url[..index];
    }

    /// <summary>
    /// A genuinely separate browser window (own tab set, own panels) on the
    /// real profile. It never writes the session: only the primary window's
    /// tabs are restored on next launch.
    /// </summary>
    private static void OpenNewWindow()
    {
        var freshSettings = new AppSettings
        {
            HomePageUrl = AppServices.CurrentSettings.HomePageUrl,
            SavedGroups = AppServices.CurrentSettings.SavedGroups.ToList(),
        };
        var tabManager = new TabManagerService(freshSettings, AppServices.WebView2Environment, isPrivate: false, persistsToSettings: false);
        new MainWindow(tabManager, isPrimary: false).Show();
    }

    /// <summary>A private window: its own throwaway WebView2 profile under Temp, so cookies, cache and history never touch the normal one.</summary>
    private static void OpenPrivateWindow()
    {
        var profileFolder = Path.Combine(Path.GetTempPath(), "Navigatueur", "Private", Guid.NewGuid().ToString("N"));
        var environment = new WebView2EnvironmentService(profileFolder);
        var tabManager = new TabManagerService(new AppSettings(), environment, isPrivate: true, persistsToSettings: false);
        new MainWindow(tabManager, isPrimary: false, isPrivate: true, privateProfileFolder: profileFolder).Show();
    }

    private void OpenExtensionsWindow()
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

    private void ToggleMusicOverlay()
    {
        _musicOverlay ??= new Views.MusicOverlayWindow();
        if (_musicOverlay.IsVisible)
        {
            _musicOverlay.Hide();
        }
        else
        {
            _musicOverlay.Show();
        }
    }

    private async void InstallUpdate()
    {
        var update = AppServices.Update;
        if (!update.IsUpdateAvailable)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"La version {update.LatestVersion} est disponible. La télécharger et l'installer maintenant ? Navigatueur se fermera pendant l'installation.",
            "Mise à jour disponible",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await update.DownloadAndInstallAsync();
        }
    }

    private async void SaveActivePageAs()
    {
        var activeTab = _tabManager.ActiveTab;
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

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Page web (*.mhtml)|*.mhtml",
            FileName = SanitizeFileName(string.IsNullOrWhiteSpace(activeTab.Title) ? "page" : activeTab.Title),
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

    /// <summary>
    /// Chromium has its own find bar (Ctrl+F) once the page has focus; this
    /// menu entry focuses the page and sends it that keystroke, for people who
    /// look for the command in a menu rather than remember the shortcut.
    /// </summary>
    private void FindInPage()
    {
        var webView = _tabManager.ActiveTab?.WebViewControl;
        if (webView is null)
        {
            return;
        }

        webView.Focus();
        System.Windows.Forms.SendKeys.SendWait("^f");
    }

    // ===================================================== closing / tray

    /// <summary>
    /// Closing the primary window quits the app — unless the music mini-player
    /// is open, in which case the browser goes to the tray so the player keeps
    /// running. Secondary and private windows just close their own tabs.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_isPrimary)
        {
            SuspendAllTabs();
            return;
        }

        if (!_forceClose && _musicOverlay is { IsVisible: true })
        {
            e.Cancel = true;
            SaveSessionState();
            Hide();
            ShowTrayIcon();
            return;
        }

        _musicOverlay?.ShutDown();
        _trayIcon?.Dispose();
        _trayIcon = null;

        SaveSessionState();

        // Disposing every live WebView2 before exit is what reliably tears down
        // Chromium's child processes — otherwise an audio tab has been heard
        // playing on for a moment after the window disappeared.
        SuspendAllTabs();
        Environment.Exit(0);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        AppServices.Theme.AppearanceChanged -= OnAppearanceChanged;
        _tabManager.TabOpened -= OnTabOpened;
        _viewModel.Detach();

        if (_privateProfileFolder is not null)
        {
            _ = TryDeletePrivateProfileAsync(_privateProfileFolder);
        }
    }

    private void SuspendAllTabs()
    {
        foreach (var tab in _tabManager.Tabs)
        {
            tab.IsSuspended = true;
        }
    }

    /// <summary>Best effort: WebView2's processes can hold file locks for a moment after the window closes.</summary>
    private static async Task TryDeletePrivateProfileAsync(string folder)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(500);
            }
        }
    }

    private void SaveSessionState()
    {
        var settings = AppServices.CurrentSettings;
        var bounds = _frame.RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            settings.WindowWidth = bounds.Width;
            settings.WindowHeight = bounds.Height;
            settings.WindowLeft = bounds.Left;
            settings.WindowTop = bounds.Top;
        }

        settings.WasMaximized = WindowState == WindowState.Maximized || _frame.WasMaximizedBeforeFullScreen;

        settings.Groups = _tabManager.Groups.Select(group => new SessionGroupState
        {
            Id = group.Id,
            Name = group.Name,
            ColorHex = group.ColorHex,
            IsCollapsed = group.IsCollapsed,
            Icon = group.Icon,
        }).ToList();

        settings.Tabs = _tabManager.Tabs.Select(tab => new SessionTabState
        {
            Url = tab.AddressBarText,
            Title = tab.Title,
            GroupId = tab.GroupId,
            IsPinned = tab.IsPinned,
        }).ToList();

        settings.ActiveTabIndex = _tabManager.ActiveTab is null
            ? -1
            : _tabManager.Tabs.IndexOf(_tabManager.ActiveTab);

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
}
