using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Navigatueur.App.Animation;
using Navigatueur.App.Models;
using Navigatueur.App.Services;
using Navigatueur.App.ViewModels;
using Navigatueur.Core.Settings;

namespace Navigatueur.App;

public partial class MainWindow : Window, Views.IWebViewOverlayHost
{
    private readonly Views.MusicOverlayWindow _musicOverlay = new();
    private Views.TabSidebarWindow? _tabSidebarWindow;
    private Views.WebViewOverlayWindow? _webViewOverlay;
    private Views.ToolbarWindow? _toolbarWindow;

    public Canvas? WebViewTrailCanvas => _webViewOverlay?.TrailCanvas;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _forceClose;
    private readonly DispatcherTimer _autosaveTimer;

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

        var viewModel = new MainWindowViewModel(AppServices.TabManager);
        DataContext = viewModel;
        viewModel.PropertyChanged += OnMainViewModelPropertyChanged;

        if (AppServices.Settings.IsFirstRun)
        {
            new Views.WelcomeWindow().ShowDialog();
        }

        // WPF throws if you assign Owner to a window that doesn't have a real
        // Win32 handle yet — MainWindow's constructor is still running at this
        // point, so `this` has no handle until SourceInitialized fires. Creating
        // the sidebar window there instead (crashed every launch before this fix).
        SourceInitialized += (_, _) => InitializeTabSidebar(viewModel);
        LocationChanged += (_, _) => RepositionTabSidebar();
        SizeChanged += (_, _) => RepositionTabSidebar();
        StateChanged += (_, _) => RepositionTabSidebar();
        ContentRendered += (_, _) => RepositionTabSidebar();

        ApplyAddressBarPosition();
        AppServices.Theme.PropertyChanged += OnThemePropertyChanged;

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _autosaveTimer.Tick += (_, _) => SaveSessionState();
        _autosaveTimer.Start();

        // Only fires for genuinely new tabs (toolbar button, Ctrl+T) — not session
        // restore or reopening a saved group, which create tabs directly and
        // shouldn't steal focus on startup.
        AppServices.TabManager.TabOpened += _ => Dispatcher.BeginInvoke(
            new Action(FocusAddressBar), DispatcherPriority.Input);

        AppServices.RequestForceQuit = () =>
        {
            _forceClose = true;
            Close();
        };

        Closing += OnClosing;
    }

    private void InitializeTabSidebar(MainWindowViewModel viewModel)
    {
        _tabSidebarWindow = new Views.TabSidebarWindow(viewModel) { Owner = this };
        _tabSidebarWindow.Show();

        // Re-run now that AddressBarSidebarSlot actually exists, in case the
        // saved AddressBarPosition is "Sidebar" — the earlier call in the
        // constructor fell back to the top toolbar slot since the sidebar
        // window didn't exist yet at that point.
        ApplyAddressBarPosition();

        _webViewOverlay = new Views.WebViewOverlayWindow { Owner = this };
        _webViewOverlay.Show();

        _toolbarWindow = new Views.ToolbarWindow(viewModel, this) { Owner = this };
        _toolbarWindow.Show();

        // Re-run again now that the toolbar (and its AddressBarTopSlot) exists.
        ApplyAddressBarPosition();

        RepositionTabSidebar();
    }

    /// <summary>
    /// Keeps the separate TabSidebarWindow glued to MainWindow's left edge,
    /// spanning from just below the toolbar down to the bottom address bar
    /// slot (if that's where it's currently docked). Hidden while minimized,
    /// since an owned window doesn't automatically follow its owner's
    /// minimize/restore state.
    /// </summary>
    private void RepositionTabSidebar()
    {
        if (_tabSidebarWindow is null)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            _tabSidebarWindow.Hide();
            _webViewOverlay?.Hide();
            _toolbarWindow?.Hide();
            return;
        }

        // Just the title bar now — the toolbar no longer occupies a fixed Grid
        // row of its own (see ToolbarWindow), so content starts right below it.
        var topOffset = TitleBarBorder.ActualHeight;
        var bottomOffset = AddressBarBottomSlot.Child is not null ? AddressBarBottomSlot.ActualHeight : 0;

        _tabSidebarWindow.Left = Left;
        _tabSidebarWindow.Top = Top + topOffset;
        _tabSidebarWindow.Height = Math.Max(0, ActualHeight - topOffset - bottomOffset);
        _tabSidebarWindow.SyncBackgroundGeometry(ActualWidth, ActualHeight, topOffset);

        if (!_tabSidebarWindow.IsVisible)
        {
            _tabSidebarWindow.Show();
        }

        RepositionWebViewOverlay(topOffset, bottomOffset);
        RepositionToolbar(topOffset);
    }

    /// <summary>
    /// Docks ToolbarWindow to the top of the content area (same left inset as
    /// WebViewOverlayWindow, so it starts flush with the page, not over the
    /// sidebar) — it floats there rather than reserving space, expanding
    /// downward over the page on hover instead of pushing content down.
    /// </summary>
    private void RepositionToolbar(double topOffset)
    {
        if (_toolbarWindow is null)
        {
            return;
        }

        var leftInset = ContentHost.Margin.Left;
        _toolbarWindow.Left = Left + leftInset;
        _toolbarWindow.Top = Top + topOffset;
        _toolbarWindow.Width = Math.Max(0, ActualWidth - leftInset);

        if (!_toolbarWindow.IsVisible)
        {
            _toolbarWindow.Show();
        }
    }

    /// <summary>
    /// Tracks ContentHost's own on-screen rect (not the sidebar's) — the
    /// area WebView2 actually renders into. ContentHost.Margin.Left is the
    /// docked sidebar inset (see AnimateContentInset), so this stays correct
    /// whether the sidebar is collapsed or pinned open, and doesn't move for
    /// a temporary hover-expand (which floats over content instead).
    /// </summary>
    private void RepositionWebViewOverlay(double topOffset, double bottomOffset)
    {
        if (_webViewOverlay is null)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            _webViewOverlay.Hide();
            return;
        }

        var leftInset = ContentHost.Margin.Left;
        _webViewOverlay.SyncBounds(
            Left + leftInset,
            Top + topOffset,
            ActualWidth - leftInset,
            Math.Max(0, ActualHeight - topOffset - bottomOffset));

        if (!_webViewOverlay.IsVisible)
        {
            _webViewOverlay.Show();
        }
    }

    private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeService.AddressBarPosition))
        {
            ApplyAddressBarPosition();
        }
    }

    /// <summary>
    /// Only reacts to IsSidebarPinned, not IsSidebarExpanded — a temporary
    /// hover-expand should float the sidebar over the page (v0.12 behavior),
    /// not shove the page's content aside. Only pinning the sidebar open
    /// changes how much width it permanently reserves.
    /// </summary>
    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsSidebarPinned) && sender is MainWindowViewModel vm)
        {
            AnimateContentInset(vm.IsSidebarPinned ? Views.TabSidebarWindow.ExpandedWidth : Views.TabSidebarWindow.CollapsedWidth);
        }
    }

    private void AnimateContentInset(double toPixels)
    {
        var animation = new ThicknessAnimation
        {
            To = new Thickness(toPixels, 0, 0, 0),
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) =>
        {
            ContentHost.Margin = new Thickness(toPixels, 0, 0, 0);
            RepositionTabSidebar();
        };
        ContentHost.BeginAnimation(FrameworkElement.MarginProperty, animation);
    }

    /// <summary>
    /// The address bar is a single TextBox instance moved between empty slot
    /// Borders (top toolbar — now in the separate ToolbarWindow, bottom of
    /// the window, top of the tab sidebar — in TabSidebarWindow) rather than
    /// duplicated, so there's exactly one source of truth for its
    /// focus/selection state regardless of where it's docked. Falls back to
    /// AddressBarInitialHost (an always-Collapsed placeholder declared in
    /// MainWindow.xaml) before ToolbarWindow/TabSidebarWindow exist yet.
    /// </summary>
    private void ApplyAddressBarPosition()
    {
        AddressBarInitialHost.Child = null;
        AddressBarBottomSlot.Child = null;
        if (_toolbarWindow is not null)
        {
            _toolbarWindow.AddressBarTopSlot.Child = null;
        }

        if (_tabSidebarWindow is not null)
        {
            _tabSidebarWindow.AddressBarSidebarSlot.Child = null;
        }

        var target = AppServices.Theme.AddressBarPosition switch
        {
            "Bottom" => AddressBarBottomSlot,
            "Sidebar" => _tabSidebarWindow?.AddressBarSidebarSlot ?? _toolbarWindow?.AddressBarTopSlot ?? AddressBarInitialHost,
            _ => _toolbarWindow?.AddressBarTopSlot ?? AddressBarInitialHost,
        };
        target.Child = AddressBarTextBox;
        RepositionTabSidebar();
    }

    private CursorTrailTracker? _cursorTrailTracker;

    /// <summary>
    /// Cursor trail over the app's own WPF chrome (toolbar, title bar) — the
    /// sidebar has its own separate trail since it's a separate window now.
    /// Never sees moves over actual page content; WebView2 hosts that in a
    /// separate native child window that never routes mouse input through WPF.
    /// </summary>
    private void OnWindowMouseMoveForTrail(object sender, MouseEventArgs e)
    {
        if (!AppServices.Theme.IsCursorTrailEnabled)
        {
            return;
        }

        _cursorTrailTracker ??= new CursorTrailTracker(CursorTrailCanvas);
        _cursorTrailTracker.OnMove(e.GetPosition(CursorTrailCanvas));
    }

    /// <summary>
    /// Standard browser shortcuts. Only ever fires while a WPF element has
    /// keyboard focus (address bar, tab strip, toolbar) — WebView2 hosts page
    /// content in a separate native child window, so these don't fire while
    /// focus is inside a page itself, same limitation as Ctrl+F noted elsewhere.
    /// </summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext;

        if (e.Key == Key.F5)
        {
            vm.ActiveTab?.ReloadCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        switch (e.Key)
        {
            case Key.T:
                AppServices.TabManager.OpenTab();
                e.Handled = true;
                break;
            case Key.W:
                if (vm.ActiveTab is { } activeTab)
                {
                    AppServices.TabManager.CloseTab(activeTab);
                }

                e.Handled = true;
                break;
            case Key.Tab:
                AppServices.TabManager.ActivateAdjacentTab(shift ? -1 : 1);
                e.Handled = true;
                break;
            case Key.L:
                FocusAddressBar();
                e.Handled = true;
                break;
            case Key.R:
                vm.ActiveTab?.ReloadCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.N when shift:
                _toolbarWindow?.OpenPrivateWindow();
                e.Handled = true;
                break;
            case >= Key.D1 and <= Key.D8:
                AppServices.TabManager.ActivateTabAtIndex(e.Key - Key.D1);
                e.Handled = true;
                break;
            case Key.D9:
                AppServices.TabManager.ActivateLastTab();
                e.Handled = true;
                break;
        }
    }

    private void FocusAddressBar()
    {
        AddressBarTextBox.Focus();
        AddressBarTextBox.SelectAll();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>Called from ToolbarWindow's music button — the overlay's own lifecycle (and the tray-background logic in OnClosing) stays here in MainWindow.</summary>
    public void ToggleMusicOverlay()
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
            _tabSidebarWindow?.Hide();
            _toolbarWindow?.Hide();
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
        RepositionTabSidebar();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
        }
    }

    /// <summary>WPF's TextBox only handles double-click (select word) natively — triple-click (select the whole address) needs an explicit hook.</summary>
    private void OnAddressBarPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 3 && sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private static readonly string[] CommonSiteSuggestions =
    {
        "youtube.com", "google.com", "github.com", "wikipedia.org", "reddit.com",
        "twitter.com", "amazon.com", "netflix.com", "gmail.com", "twitch.tv",
        "instagram.com", "linkedin.com", "spotify.com", "discord.com",
    };

    private bool _isApplyingAddressBarSuggestion;
    private int _lastAddressBarTypedLength;

    /// <summary>
    /// Classic omnibox-style inline completion: appends the best-matching
    /// hostname as *selected* text past the caret, so the next keystroke
    /// (which types over a selection) naturally overwrites it, and Enter
    /// navigates to the completed URL if left untouched.
    /// </summary>
    private void OnAddressBarTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingAddressBarSuggestion)
        {
            return;
        }

        var textBox = (TextBox)sender;
        var typed = textBox.Text;
        var isDeleting = typed.Length < _lastAddressBarTypedLength;
        _lastAddressBarTypedLength = typed.Length;

        if (isDeleting || string.IsNullOrEmpty(typed) || textBox.SelectionStart != typed.Length)
        {
            return;
        }

        var suggestion = FindAddressBarSuggestion(typed);
        if (suggestion is null || suggestion.Length <= typed.Length)
        {
            return;
        }

        _isApplyingAddressBarSuggestion = true;
        textBox.Text = suggestion;
        textBox.SelectionStart = typed.Length;
        textBox.SelectionLength = suggestion.Length - typed.Length;
        _isApplyingAddressBarSuggestion = false;

        // Without this, the next keystroke (e.g. pressing Delete to remove the
        // selected suggestion) compared its shrunk length against the length
        // from *before* the suggestion was appended — since that never changed
        // while the guard above was skipping the length update, a Delete that
        // correctly removed the whole suggestion looked like "no change in
        // length" and immediately re-triggered the same suggestion right back.
        _lastAddressBarTypedLength = suggestion.Length;
    }

    /// <summary>Best-matching hostname (history first, then a handful of common sites for a cold history), or null if nothing starts with what's typed.</summary>
    private static string? FindAddressBarSuggestion(string typed)
    {
        if (typed.Contains(' ') || typed.Contains('/'))
        {
            return null; // looks like a search query or a path, not a bare domain being typed
        }

        var historyHosts = AppServices.History.Entries
            .Select(entry => TryGetHost(entry.Url))
            .Where(host => host is not null)
            .Cast<string>();

        return historyHosts
            .Concat(CommonSiteSuggestions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(host => host.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(host => host.Length)
            .FirstOrDefault();
    }

    private static string? TryGetHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
    }

    /// <summary>Keeps ToolbarWindow expanded for the whole time the user is actively typing a URL, instead of retracting mid-type if the mouse happens to drift off it.</summary>
    private void OnAddressBarGotKeyboardFocus(object sender, RoutedEventArgs e) => _toolbarWindow?.SetForcedExpanded(true);

    private void OnAddressBarLostKeyboardFocus(object sender, RoutedEventArgs e) => _toolbarWindow?.SetForcedExpanded(false);
}
