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
    private readonly DispatcherTimer _autosaveTimer;
    private readonly double _preferredExpandedWidth = SidebarExpandedWidth;

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
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        if (AppServices.Settings.IsFirstRun)
        {
            new Views.WelcomeWindow().ShowDialog();
        }

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsSidebarPinned))
        {
            return;
        }

        var vm = (MainWindowViewModel)DataContext;
        AnimateTabColumnWidth(vm.IsSidebarPinned ? _preferredExpandedWidth : SidebarCollapsedWidth);
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

    private CursorTrailTracker? _cursorTrailTracker;

    /// <summary>
    /// Single Window-level PreviewMouseMove handler doing two unrelated jobs:
    /// the cursor trail, and driving the sidebar hover expand/collapse via a
    /// direct geometric position check against TabColumnHost's live bounds —
    /// far more reliable than that element's own MouseEnter/Leave, which
    /// depends on precise hit-test boundaries that proved flaky in practice.
    /// Both only ever see moves over the app's own WPF chrome; WebView2 hosts
    /// page content in a separate native child window that never routes
    /// mouse input through WPF at all.
    /// </summary>
    private void OnWindowMouseMoveForTrail(object sender, MouseEventArgs e)
    {
        UpdateSidebarHoverState(e);

        if (!AppServices.Theme.IsCursorTrailEnabled)
        {
            return;
        }

        _cursorTrailTracker ??= new CursorTrailTracker(CursorTrailCanvas);
        _cursorTrailTracker.OnMove(e.GetPosition(CursorTrailCanvas));
    }

    private const double SidebarCollapsedWidth = 52;
    private const double SidebarExpandedWidth = 220;

    private void UpdateSidebarHoverState(MouseEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext;
        if (vm.IsSidebarPinned)
        {
            return;
        }

        var position = e.GetPosition(TabColumnHost);
        var isOver = position.X >= 0 && position.X <= TabColumnHost.ActualWidth
            && position.Y >= 0 && position.Y <= TabColumnHost.ActualHeight;

        if (isOver == vm.IsSidebarExpanded)
        {
            return;
        }

        vm.IsSidebarExpanded = isOver;
        AnimateTabColumnWidth(isOver ? _preferredExpandedWidth : SidebarCollapsedWidth);
    }

    /// <summary>
    /// Animates TabColumnHost's own Width — a plain double (FrameworkElement.Width),
    /// not a Grid column — so this is a completely ordinary DoubleAnimation. The
    /// content area's real layout size never changes, since the tab strip now
    /// floats over it (see the XAML comment above TabColumnHost) instead of
    /// resizing the Grid column it used to live in.
    /// </summary>
    private void AnimateTabColumnWidth(double toPixels)
    {
        // EaseOut front-loads most of the size change into the first frames, which
        // read as an instant "pop" to the eye rather than a glide — EaseInOut spreads
        // the motion evenly across the whole duration instead.
        var animation = new DoubleAnimation
        {
            To = toPixels,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) => TabColumnHost.Width = toPixels;
        TabColumnHost.BeginAnimation(FrameworkElement.WidthProperty, animation);
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
                OnOpenPrivateWindowClick(this, new RoutedEventArgs());
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
