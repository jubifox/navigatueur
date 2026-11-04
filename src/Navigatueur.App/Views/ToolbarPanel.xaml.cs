using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Navigatueur.App.Services;
using Navigatueur.App.Shell;
using Navigatueur.App.ViewModels;
using Navigatueur.Core;
using Navigatueur.Core.Bookmarks;
using Navigatueur.Core.Navigation;
using Navigatueur.Core.Settings;
using Navigatueur.Core.Shortcuts;

namespace Navigatueur.App.Views;

/// <summary>One row of the address bar's dropdown.</summary>
public sealed record AddressSuggestionItem(string Url, string Title, SuggestionKind Kind)
{
    public string DisplayUrl => UrlHelper.TryGetDisplayHost(Url) ?? Url;
}

/// <summary>
/// The top bar: navigation, address field, favorites, downloads, menu and
/// window buttons. It never decides when it's shown — ImmersiveShell does —
/// but it reports when it must stay (<see cref="IsHeld"/>): while typing an
/// address or while one of its menus is open, the mouse can be anywhere.
/// </summary>
public partial class ToolbarPanel : UserControl, IShellPanel
{
    private static readonly string[] CommonSites =
    {
        "youtube.com", "google.com", "github.com", "wikipedia.org", "reddit.com",
        "twitter.com", "amazon.com", "netflix.com", "gmail.com", "twitch.tv",
        "instagram.com", "linkedin.com", "spotify.com", "discord.com",
    };

    private readonly IShellCommands _shell;
    private readonly ObservableCollection<AddressSuggestionItem> _suggestions = new();
    private MainWindowViewModel? _viewModel;
    private BrowserTabViewModel? _observedTab;
    private bool _isApplyingText;
    private int _lastTypedLength;
    private (bool Floating, bool Effects, bool Dark)? _appliedChrome;
    private readonly Dictionary<Popup, DateTime> _popupClosedAt = new();

    public ToolbarPanel(IShellCommands shell)
    {
        InitializeComponent();
        _shell = shell;
        SuggestionsList.ItemsSource = _suggestions;
        DataContextChanged += OnDataContextChanged;
        BookmarksPopup.Closed += (_, _) => _popupClosedAt[BookmarksPopup] = DateTime.UtcNow;
        DownloadsPopup.Closed += (_, _) => _popupClosedAt[DownloadsPopup] = DateTime.UtcNow;
        ExtensionsPopup.Closed += (_, _) => _popupClosedAt[ExtensionsPopup] = DateTime.UtcNow;

        // The panel can outlive nothing but its window: subscribe to the app-wide
        // services only while it's on screen, so a closed window can be collected.
        Loaded += (_, _) =>
        {
            AppServices.Bookmarks.Changed += OnBookmarksChanged;
            UpdateBookmarksEmptyState();
        };
        Unloaded += (_, _) => AppServices.Bookmarks.Changed -= OnBookmarksChanged;
    }

    private void OnBookmarksChanged(object? sender, EventArgs e) => UpdateBookmarksEmptyState();

    /// <summary>
    /// Typing in the address bar holds the bar open wherever the mouse goes.
    /// Other focus only counts while the keyboard is driving (a button that
    /// merely took focus from a click mustn't pin the bar on screen).
    /// </summary>
    public bool IsHeld =>
        AddressBox.IsKeyboardFocusWithin
        || (IsKeyboardFocusWithin && InputManager.Current.MostRecentInputDevice is KeyboardDevice)
        || MainMenu.IsOpen
        || SuggestionsPopup.IsOpen
        || BookmarksPopup.IsOpen
        || DownloadsPopup.IsOpen
        || ExtensionsPopup.IsOpen;

    public void ApplyChrome(bool floating, bool effects, PanelSide side, double shadowSize)
    {
        var state = (floating, effects, AppServices.Theme.IsDark);
        if (_appliedChrome == state)
        {
            return;
        }

        _appliedChrome = state;
        if (floating && effects)
        {
            Body.CornerRadius = new CornerRadius(0, 0, 14, 14);
            Body.Effect = new DropShadowEffect
            {
                BlurRadius = shadowSize,
                ShadowDepth = 3,
                Direction = 270,
                Color = Colors.Black,
                Opacity = AppServices.Theme.IsDark ? 0.5 : 0.16,
            };
        }
        else
        {
            Body.CornerRadius = new CornerRadius(0);
            Body.Effect = null;
        }
    }

    public void PlaceWallpaper(Rect window)
    {
        // Relative to the layer, which sits inside Body (offset by any shadow margin).
        var origin = WallpaperLayer.IsLoaded ? WallpaperLayer.TranslatePoint(default, this) : default;
        Canvas.SetLeft(Wallpaper, window.X - origin.X);
        Canvas.SetTop(Wallpaper, window.Y - origin.Y);
        Wallpaper.Width = window.Width;
        Wallpaper.Height = window.Height;
    }

    /// <summary>Puts the caret in the address field with its text selected, ready to type over (Ctrl+L, new tab).</summary>
    public void FocusAddressBar()
    {
        // The panel may have only just been revealed: its window is shown
        // asynchronously, so focus once that has gone through.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            AddressBox.Focus();
            AddressBox.SelectAll();
        }), DispatcherPriority.Background);
    }

    /// <summary>
    /// Closes the "…" menu and the favorites/downloads popups; true if one was open.
    /// The toolbar's buttons don't take keyboard focus (clicking Back must leave
    /// it in the page), so Escape reaches the window rather than the menu: the
    /// window forwards it here.
    /// </summary>
    public bool CloseOpenMenus()
    {
        var wasOpen = MainMenu.IsOpen || BookmarksPopup.IsOpen || DownloadsPopup.IsOpen;
        MainMenu.IsOpen = false;
        BookmarksPopup.IsOpen = false;
        DownloadsPopup.IsOpen = false;
        return wasOpen;
    }

    public void ToggleDownloads()
    {
        // Ctrl+J with nothing downloaded yet: the downloads button is hidden, so anchor on the menu button.
        DownloadsPopup.PlacementTarget = DownloadsButton.IsVisible ? DownloadsButton : MenuButton;
        TogglePopup(DownloadsPopup);
    }

    /// <summary>
    /// A StaysOpen=False popup closes itself on the mouse-down of the very
    /// click that then reaches its button — toggling naively would reopen it
    /// at once. A popup that closed a moment ago stays closed.
    /// </summary>
    private void TogglePopup(Popup popup)
    {
        if (popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }

        if (_popupClosedAt.TryGetValue(popup, out var closedAt) && DateTime.UtcNow - closedAt < TimeSpan.FromMilliseconds(250))
        {
            return;
        }

        popup.IsOpen = true;
    }

    // --------------------------------------------------------- view model

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ObserveActiveTab();
    }

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

        _observedTab = _viewModel?.ActiveTab;
        if (_observedTab is not null)
        {
            _observedTab.PropertyChanged += OnActiveTabPropertyChanged;
        }

        // Switching tabs always shows the new tab's address, even mid-edit.
        ShowAddress(force: true);
        UpdateLoadingAnimation();
    }

    private void OnActiveTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BrowserTabViewModel.AddressBarDisplayText):
                ShowAddress(force: false);
                break;
            case nameof(BrowserTabViewModel.IsLoading):
                UpdateLoadingAnimation();
                break;
        }
    }

    /// <summary>
    /// The address box isn't data-bound: what the user types must not
    /// overwrite the tab's real URL until they press Enter, and a page
    /// redirecting in the background must not clobber what they're typing.
    /// </summary>
    private void ShowAddress(bool force)
    {
        if (!force && AddressBox.IsKeyboardFocused)
        {
            return;
        }

        SetAddressText(_observedTab?.AddressBarDisplayText ?? string.Empty);
    }

    private void SetAddressText(string text)
    {
        _isApplyingText = true;
        AddressBox.Text = text;
        _isApplyingText = false;
        _lastTypedLength = text.Length;
        AddressPlaceholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateLoadingAnimation()
    {
        if (_observedTab?.IsLoading == true)
        {
            var sweep = new DoubleAnimation(-220, Math.Max(ActualWidth, 800), TimeSpan.FromMilliseconds(1100))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            ProgressOffset.BeginAnimation(TranslateTransform.XProperty, sweep);
        }
        else
        {
            // Removing the animation stops its clock — nothing keeps ticking while idle.
            ProgressOffset.BeginAnimation(TranslateTransform.XProperty, null);
        }
    }

    // ----------------------------------------------------------- commands

    private void OnCommandClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string commandId })
        {
            BookmarksPopup.IsOpen = false;
            _shell.Execute(commandId);
        }
    }

    private void OnBodyMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _shell.Execute(ShellCommandIds.ToggleMaximize);
        }
        else
        {
            _shell.StartWindowDrag();
        }

        e.Handled = true;
    }

    private DateTime _mainMenuClosedAt;

    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        // Same close-then-reopen guard as TogglePopup: a click on "…" while the menu is open closes it.
        if (DateTime.UtcNow - _mainMenuClosedAt < TimeSpan.FromMilliseconds(250))
        {
            return;
        }

        MainMenu.PlacementTarget = MenuButton;
        MainMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        MainMenu.HorizontalOffset = -190;
        // The button spans the bar's height; the menu opens under its chip, as before.
        MainMenu.VerticalOffset = -MenuButton.Padding.Bottom;
        MainMenu.IsOpen = true;
    }

    private void OnMainMenuOpened(object sender, RoutedEventArgs e)
    {
        AppServices.MemoryMonitor.BeginWatching();
        ApplyShortcutHints(MainMenu.Items);
    }

    private void OnMainMenuClosed(object sender, RoutedEventArgs e)
    {
        _mainMenuClosedAt = DateTime.UtcNow;
        AppServices.MemoryMonitor.EndWatching();
    }

    /// <summary>Shows each command's current shortcut next to it — read live, since shortcuts can be remapped in Settings.</summary>
    private static void ApplyShortcutHints(ItemCollection items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            if (item.Tag is string commandId && ShortcutCatalog.Find(commandId) is not null)
            {
                item.InputGestureText = AppServices.Shortcuts.GetDisplayText(commandId);
            }

            ApplyShortcutHints(item.Items);
        }
    }

    private void OnBookmarksClick(object sender, RoutedEventArgs e) => TogglePopup(BookmarksPopup);

    private void OnExtensionsClick(object sender, RoutedEventArgs e) => TogglePopup(ExtensionsPopup);

    /// <summary>Opens the extension's own menu just under the toolbar button — or, without one, its settings in a new tab.</summary>
    private void OnExtensionItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledExtension { IsEnabled: true } extension)
        {
            return;
        }

        ExtensionsPopup.IsOpen = false;
        if (extension.HasPopup)
        {
            extension.OpenPopupAt(ScreenPointUnder(ExtensionsButton));
        }
        else if (extension.OptionsUrl is { } options && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.TabManager.OpenTab(options);
        }
    }

    /// <summary>The bottom-right corner of <paramref name="element"/> (of its visible chip, for a bar button) on screen, in device-independent pixels (what Window.Left/Top use).</summary>
    private static Point ScreenPointUnder(FrameworkElement element)
    {
        var inset = element is Control control ? control.Padding : default;
        var device = element.PointToScreen(new Point(element.ActualWidth - inset.Right, element.ActualHeight - inset.Bottom));
        return PresentationSource.FromVisual(element)?.CompositionTarget is { } target
            ? target.TransformFromDevice.Transform(device)
            : device;
    }

    private void OnDownloadsClick(object sender, RoutedEventArgs e) => ToggleDownloads();

    private void UpdateBookmarksEmptyState() =>
        BookmarksEmptyText.Visibility = AppServices.Bookmarks.Bookmarks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnBookmarkItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Bookmark bookmark)
        {
            var background = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            BookmarksPopup.IsOpen = false;
            _shell.OpenUrl(bookmark.Url, background);
        }
    }

    private void OnBookmarkItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && (sender as FrameworkElement)?.DataContext is Bookmark bookmark)
        {
            _shell.OpenUrl(bookmark.Url, inBackgroundTab: true);
            e.Handled = true;
        }
    }

    private void OnBookmarkRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Bookmark bookmark)
        {
            AppServices.Bookmarks.Remove(bookmark.Url);
        }

        e.Handled = true;
    }

    // -------------------------------------------------------- address box

    private void OnAddressGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AddressField.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        AddressField.SetResourceReference(Border.BackgroundProperty, "PanelBrush");

        // Deferred: a mouse click would otherwise place the caret right after we select.
        Dispatcher.BeginInvoke(new Action(AddressBox.SelectAll), DispatcherPriority.Input);
    }

    private void OnAddressLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AddressField.BorderBrush = Brushes.Transparent;
        AddressField.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        SuggestionsPopup.IsOpen = false;
        ShowAddress(force: true);
    }

    /// <summary>A click anywhere in the field (icon, padding) focuses the text — it must not start a window drag.</summary>
    private void OnAddressFieldMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        AddressBox.Focus();
        e.Handled = true;
    }

    /// <summary>WPF's TextBox selects a word on double-click but has no triple-click; select the whole address.</summary>
    private void OnAddressPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 3)
        {
            AddressBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnAddressPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit((Keyboard.Modifiers & ModifierKeys.Alt) != 0);
                e.Handled = true;
                break;

            case Key.Escape:
                if (SuggestionsPopup.IsOpen)
                {
                    SuggestionsPopup.IsOpen = false;
                }
                else
                {
                    ShowAddress(force: true);
                    _shell.FocusPage();
                    _shell.Execute(ShellCommandIds.HideToolbar);
                }

                e.Handled = true;
                break;

            case Key.Tab:
                // Tab accepts the inline completion (like the omnibox) and never leaves the field.
                if (AddressBox.SelectionLength > 0 && AddressBox.SelectionStart + AddressBox.SelectionLength == AddressBox.Text.Length)
                {
                    AddressBox.CaretIndex = AddressBox.Text.Length;
                }

                e.Handled = true;
                break;

            case Key.Down:
            case Key.Up:
                if (SuggestionsPopup.IsOpen && _suggestions.Count > 0)
                {
                    var step = e.Key == Key.Down ? 1 : -1;
                    var index = SuggestionsList.SelectedIndex + step;
                    if (index < -1)
                    {
                        index = _suggestions.Count - 1;
                    }
                    else if (index >= _suggestions.Count)
                    {
                        index = -1;
                    }

                    SuggestionsList.SelectedIndex = index;
                    if (index >= 0)
                    {
                        SetAddressText(_suggestions[index].Url);
                        AddressBox.CaretIndex = AddressBox.Text.Length;
                    }

                    e.Handled = true;
                }

                break;
        }
    }

    /// <summary>Enter: go to the selected suggestion or the typed text; Alt+Enter opens it in a new tab.</summary>
    private void Commit(bool inNewTab)
    {
        var target = SuggestionsPopup.IsOpen && SuggestionsList.SelectedItem is AddressSuggestionItem selected
            ? selected.Url
            : AddressBox.Text;

        SuggestionsPopup.IsOpen = false;
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        if (inNewTab)
        {
            _shell.Execute(ShortcutCommands.NewTab);
        }

        _shell.NavigateActiveTab(target);
        _shell.FocusPage();
        _shell.Execute(ShellCommandIds.HideToolbar);
    }

    private void OnSuggestionClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(SuggestionsList, source) is ListBoxItem { DataContext: AddressSuggestionItem item })
        {
            SuggestionsPopup.IsOpen = false;
            _shell.NavigateActiveTab(item.Url);
            _shell.FocusPage();
            _shell.Execute(ShellCommandIds.HideToolbar);
        }
    }

    private void OnAddressTextChanged(object sender, TextChangedEventArgs e)
    {
        AddressPlaceholder.Visibility = AddressBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_isApplyingText || !AddressBox.IsKeyboardFocused)
        {
            return;
        }

        var typed = AddressBox.Text;
        var isDeleting = typed.Length < _lastTypedLength;
        _lastTypedLength = typed.Length;

        UpdateSuggestions(typed);
        if (!isDeleting && AddressBox.SelectionStart == typed.Length)
        {
            ApplyInlineCompletion(typed);
        }
    }

    private void UpdateSuggestions(string typed)
    {
        _suggestions.Clear();
        if (!string.IsNullOrWhiteSpace(typed))
        {
            var sources = AppServices.Bookmarks.Bookmarks
                .Select(b => new SuggestionSource(b.Url, b.Title, SuggestionKind.Bookmark))
                .Concat(AppServices.History.Entries.Select(h => new SuggestionSource(h.Url, h.Title, SuggestionKind.History)));

            foreach (var suggestion in AddressSuggestions.Rank(typed, sources))
            {
                _suggestions.Add(new AddressSuggestionItem(suggestion.Url, suggestion.Title, suggestion.Kind));
            }
        }

        SuggestionsList.SelectedIndex = -1;
        SuggestionsPopup.IsOpen = _suggestions.Count > 0;
    }

    /// <summary>
    /// Omnibox-style inline completion: appends the best-matching host as
    /// *selected* text past the caret, so the next keystroke types over it
    /// and Enter accepts it.
    /// </summary>
    private void ApplyInlineCompletion(string typed)
    {
        if (typed.Length == 0 || typed.Contains(' ') || typed.Contains('/'))
        {
            return;
        }

        var completion = AppServices.Bookmarks.Bookmarks.Select(b => b.Url)
            .Concat(AppServices.History.Entries.Select(h => h.Url))
            .Select(UrlHelper.TryGetDisplayHost)
            .Where(host => host is not null)
            .Cast<string>()
            .Concat(CommonSites)
            .Where(host => host.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(host => host.Length)
            .FirstOrDefault();

        if (completion is null || completion.Length <= typed.Length)
        {
            return;
        }

        _isApplyingText = true;
        AddressBox.Text = typed + completion[typed.Length..];
        AddressBox.SelectionStart = typed.Length;
        AddressBox.SelectionLength = completion.Length - typed.Length;
        _isApplyingText = false;

        // Measured against the completed text, so deleting the selected tail
        // reads as a deletion and doesn't immediately re-complete.
        _lastTypedLength = completion.Length;
    }
}
