using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Navigatueur.App.Services;
using Navigatueur.Core;

namespace Navigatueur.App.ViewModels;

public partial class BrowserTabViewModel : ObservableObject
{
    private readonly TabManagerService _tabManager;

    public TabManagerService TabManager => _tabManager;

    public Guid Id { get; } = Guid.NewGuid();

    public string InitialUrl { get; }

    public ObservableCollection<TabContextMenuEntry> ContextMenuItems { get; } = new();

    [ObservableProperty]
    private string title = "Nouvel onglet";

    [ObservableProperty]
    private string addressBarText;

    /// <summary>
    /// What the address bar actually shows: blank on the new-tab page and a
    /// short navigatueur:// address on the other internal pages, instead of
    /// the https://navigatueur.home/... URL they're really served from (see
    /// InternalPages). Writing it stores the raw text; <see cref="Navigate()"/>
    /// maps navigatueur:// addresses back.
    /// </summary>
    public string AddressBarDisplayText
    {
        get => InternalPages.ToDisplayUrl(AddressBarText);
        set => AddressBarText = value;
    }

    partial void OnAddressBarTextChanged(string value)
    {
        OnPropertyChanged(nameof(AddressBarDisplayText));
        OnPropertyChanged(nameof(IsSecure));
        OnPropertyChanged(nameof(IsInternalPage));
        OnPropertyChanged(nameof(CanForceDark));
        OnPropertyChanged(nameof(HostText));
        RefreshBookmarkState();
    }

    /// <summary>The page is served over HTTPS (lock icon). Internal pages count as secure too.</summary>
    public bool IsSecure => AddressBarText.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public bool IsInternalPage => InternalPages.IsInternal(AddressBarText);

    [ObservableProperty]
    private bool isBookmarked;

    /// <summary>Re-checks the star after a navigation or a change made from another tab/window.</summary>
    public void RefreshBookmarkState() => IsBookmarked = AppServices.Bookmarks.IsBookmarked(AddressBarText);

    /// <summary>The page itself requested fullscreen (a video's fullscreen button) — the window follows it.</summary>
    [ObservableProperty]
    private bool isPageFullScreen;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool isSuspended;

    [ObservableProperty]
    private DateTimeOffset lastActivatedAt = DateTimeOffset.Now;

    [ObservableProperty]
    private Guid? groupId;

    public bool IsGrouped => GroupId is not null;

    [ObservableProperty]
    private bool isPinned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool isMuted;

    /// <summary>Tracks the tab's actual audio output (not just the mute toggle) so the RAM-saving suspension logic can leave a tab currently playing sound alone.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool isPlayingAudio;

    /// <summary>The current page's declared &lt;meta name="theme-color"&gt; (hex or rgb()), or null if it doesn't have one. Drives the chrome background's top-to-gray gradient.</summary>
    [ObservableProperty]
    private string? siteAccentColorHex;

    [ObservableProperty]
    private ImageSource? favicon;

    /// <summary>Picture of the page as it last looked on screen, for the tab column's hover card. Memory only, never written to disk.</summary>
    [ObservableProperty]
    private ImageSource? preview;

    /// <summary>The engine's forced dark mode is on for the current page (Settings → Apparence, or per site from the "⋯" menu).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForceDarkMenuHeader))]
    private bool isForceDark;

    public string ForceDarkMenuHeader => IsForceDark ? "Désactiver le mode sombre sur ce site" : "Activer le mode sombre sur ce site";

    /// <summary>The current page can be darkened at all (not a Navigatueur page, not about:blank).</summary>
    public bool CanForceDark => ForceDarkService.Applies(AddressBarText);

    /// <summary>This tab's video sits in a picture-in-picture window that Navigatueur opened when the user switched away. Keeps the tab from being unloaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool isInAutoPictureInPicture;

    /// <summary>The site, for the hover card: its host, or the short navigatueur:// address of an internal page.</summary>
    public string HostText => IsInternalPage
        ? (InternalPages.ToDisplayUrl(AddressBarText) is { Length: > 0 } display ? display : "Page Navigatueur")
        : UrlHelper.TryGetDisplayHost(AddressBarText) ?? AddressBarText;

    /// <summary>One line on the hover card about what the tab is doing, or null when there's nothing to say.</summary>
    public string? StatusText =>
        IsInAutoPictureInPicture ? "Vidéo en image dans l'image"
        : IsSuspended ? "En veille : se recharge à l'ouverture"
        : IsMuted ? "Son coupé"
        : IsPlayingAudio ? "Lecture du son"
        : null;

    /// <summary>Per-tab escape hatch for the rare page a domain-list ad blocker still breaks, without touching global settings.</summary>
    [ObservableProperty]
    private bool isAdBlockDisabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool canGoBack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    private bool canGoForward;

    private const string WarningPageUrl = "https://navigatueur.warning/index.html";
    private const string WarningContinueUrl = "https://navigatueur.warning/continue";

    private readonly HashSet<string> _phishingBypassedUrls = new();

    private CoreWebView2? _coreWebView2;
    private WebView2? _zoomHost;

    private readonly EventHandler<CoreWebView2NavigationStartingEventArgs> _onNavigationStarting;
    private readonly EventHandler<CoreWebView2NavigationCompletedEventArgs> _onNavigationCompleted;
    private readonly EventHandler<CoreWebView2SourceChangedEventArgs> _onSourceChanged;
    private readonly EventHandler<object> _onDocumentTitleChanged;
    private readonly EventHandler<object> _onIsDocumentPlayingAudioChanged;
    private readonly EventHandler<object> _onFaviconChanged;
    private readonly EventHandler<object> _onContainsFullScreenElementChanged;

    public BrowserTabViewModel(string initialUrl, TabManagerService tabManager)
    {
        InitialUrl = initialUrl;
        addressBarText = initialUrl;

        // Until the page reports its own title: the site's name, so an unloaded
        // (restored, background) tab isn't just another "Nouvel onglet".
        if (!InternalPages.IsInternal(initialUrl) && UrlHelper.TryGetDisplayHost(initialUrl) is { } host)
        {
            title = host;
        }

        // Same idea for the icon: the site's last known favicon, so a tab that
        // has never been loaded in this session still shows it.
        _faviconSite = FaviconCache.KeyFor(initialUrl);
        favicon = FaviconCache.TryGet(initialUrl);

        _tabManager = tabManager;

        _onNavigationStarting = OnNavigationStarting;
        _onNavigationCompleted = (_, e) =>
        {
            IsLoading = false;
            CanGoBack = _coreWebView2?.CanGoBack ?? false;
            CanGoForward = _coreWebView2?.CanGoForward ?? false;

            if (e.IsSuccess && _coreWebView2 is not null)
            {
                if (!_tabManager.IsPrivate)
                {
                    AppServices.History.Record(_coreWebView2.Source, _coreWebView2.DocumentTitle);
                }

                _ = RefreshSiteAccentColorAsync();
                _ = CapturePreviewSoonAsync();
            }
            else
            {
                SiteAccentColorHex = null;
            }
        };
        _onSourceChanged = (_, _) =>
        {
            AddressBarText = _coreWebView2?.Source ?? AddressBarText;
            OnSiteMaybeChanged(AddressBarText);
        };
        _onDocumentTitleChanged = (_, _) =>
            Title = string.IsNullOrWhiteSpace(_coreWebView2?.DocumentTitle)
                ? AddressBarText
                : _coreWebView2!.DocumentTitle;
        _onIsDocumentPlayingAudioChanged = (_, _) => IsPlayingAudio = _coreWebView2?.IsDocumentPlayingAudio ?? false;
        _onFaviconChanged = (_, _) => _ = RefreshFaviconAsync();
        _onContainsFullScreenElementChanged = (_, _) => IsPageFullScreen = _coreWebView2?.ContainsFullScreenElement ?? false;

        _tabManager.Groups.CollectionChanged += OnGroupsCollectionChanged;
        RefreshContextMenuItems();
    }

    private void OnGroupsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshContextMenuItems();

    /// <summary>
    /// Cancels navigation to a known phishing domain and redirects to an
    /// internal warning page instead. The warning page's "Continuer quand
    /// même" link re-navigates through a special /continue marker URL that
    /// this same handler recognizes to let that one URL through once.
    /// </summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        IsLoading = true;
        ApplyForceDark(e.Uri);

        if (e.Uri.StartsWith(WarningContinueUrl, StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            var target = ExtractQueryParam(e.Uri, "url");
            if (!string.IsNullOrEmpty(target))
            {
                _phishingBypassedUrls.Add(target);
                _coreWebView2?.Navigate(target);
            }

            return;
        }

        if (_phishingBypassedUrls.Remove(e.Uri))
        {
            return;
        }

        if (AppServices.CurrentSettings.IsPhishingProtectionEnabled &&
            Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && AppServices.PhishingProtection.IsPhishing(uri.Host))
        {
            e.Cancel = true;
            IsLoading = false;
            _coreWebView2?.Navigate($"{WarningPageUrl}?url={Uri.EscapeDataString(e.Uri)}");
        }
    }

    private static string? ExtractQueryParam(string url, string key)
    {
        var queryIndex = url.IndexOf('?');
        if (queryIndex < 0)
        {
            return null;
        }

        foreach (var pair in url[(queryIndex + 1)..].Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == key)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    partial void OnGroupIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(IsGrouped));
        RefreshContextMenuItems();
    }

    private void RefreshContextMenuItems()
    {
        ContextMenuItems.Clear();

        ContextMenuItems.Add(new TabContextMenuEntry(
            IsPinned ? "Détacher" : "Épingler",
            new RelayCommand(() => IsPinned = !IsPinned)));

        ContextMenuItems.Add(new TabContextMenuEntry(
            IsMuted ? "Réactiver le son" : "Couper le son",
            ToggleMuteCommand));

        ContextMenuItems.Add(new TabContextMenuEntry(
            IsAdBlockDisabled ? "Réactiver le bloqueur sur ce site" : "Désactiver le bloqueur sur ce site",
            new RelayCommand(() => IsAdBlockDisabled = !IsAdBlockDisabled)));

        ContextMenuItems.Add(new TabContextMenuEntry(
            "Nouveau groupe...",
            new RelayCommand(() => _tabManager.CreateGroupForTab(this))));

        if (GroupId is not null)
        {
            ContextMenuItems.Add(new TabContextMenuEntry(
                "Retirer du groupe",
                new RelayCommand(() => _tabManager.RemoveFromGroup(this))));
        }

        foreach (var group in _tabManager.Groups)
        {
            if (group.Id == GroupId)
            {
                continue;
            }

            ContextMenuItems.Add(new TabContextMenuEntry(
                $"Ajouter à « {group.Name} »",
                new RelayCommand(() => _tabManager.AssignToGroup(this, group))));
        }

        ContextMenuItems.Add(new TabContextMenuEntry(
            "Fermer",
            new RelayCommand(() => _tabManager.CloseTab(this))));
    }

    partial void OnIsPinnedChanged(bool value)
    {
        if (value)
        {
            IsSuspended = false;
        }

        RefreshContextMenuItems();
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (_coreWebView2 is not null)
        {
            _coreWebView2.IsMuted = value;
        }

        RefreshContextMenuItems();
    }

    partial void OnIsAdBlockDisabledChanged(bool value)
    {
        RefreshContextMenuItems();
        _coreWebView2?.Reload();
    }

    /// <summary>Live profile handle, only valid while this tab's WebView2 is loaded (not suspended/disposed).</summary>
    public CoreWebView2Profile? CurrentProfile => _coreWebView2?.Profile;

    public void AttachCoreWebView2(CoreWebView2 coreWebView2)
    {
        _coreWebView2 = coreWebView2;
        _coreWebView2.IsMuted = IsMuted;
        _forceDarkApplied = false;
        ApplyForceDark(AddressBarText);

        _coreWebView2.NavigationStarting += _onNavigationStarting;
        _coreWebView2.NavigationCompleted += _onNavigationCompleted;
        _coreWebView2.SourceChanged += _onSourceChanged;
        _coreWebView2.DocumentTitleChanged += _onDocumentTitleChanged;
        _coreWebView2.IsDocumentPlayingAudioChanged += _onIsDocumentPlayingAudioChanged;
        IsPlayingAudio = _coreWebView2.IsDocumentPlayingAudio;
        _coreWebView2.FaviconChanged += _onFaviconChanged;
        _coreWebView2.ContainsFullScreenElementChanged += _onContainsFullScreenElementChanged;
        _ = RefreshFaviconAsync();
    }

    /// <summary>Exposed so MainWindow can focus the actual embedded browser HWND before sending it Ctrl+F (native Chromium find-in-page).</summary>
    public WebView2? WebViewControl => _zoomHost;

    /// <summary>Live engine for this tab, or null while suspended — used by the internal-pages bridge to reply to the page.</summary>
    public CoreWebView2? CoreWebView => _coreWebView2;

    /// <summary>Set when the page should take the keyboard as soon as its engine exists (focus requested while the tab was still unloaded).</summary>
    public bool WantsFocus { get; set; }

    /// <summary>Gives keyboard focus to the page (after the address bar, a menu, a tab switch…), now or once it has loaded.</summary>
    public void FocusPage()
    {
        if (_zoomHost is null)
        {
            WantsFocus = true;
            return;
        }

        WantsFocus = false;
        _zoomHost.Focus();
    }

    /// <summary>The WPF-level zoom control lives on the hosting WebView2 control, not CoreWebView2 — kept separate so this ViewModel only needs the Core surface for everything else.</summary>
    public void AttachZoomHost(WebView2 webView) => _zoomHost = webView;

    public void DetachZoomHost() => _zoomHost = null;

    /// <summary>
    /// Unhooks from the CoreWebView2 before it gets disposed, so the tab can
    /// be resumed later with a fresh WebView2 instance without leaking handlers.
    /// </summary>
    public void DetachCoreWebView2()
    {
        if (_coreWebView2 is null)
        {
            return;
        }

        _coreWebView2.NavigationStarting -= _onNavigationStarting;
        _coreWebView2.NavigationCompleted -= _onNavigationCompleted;
        _coreWebView2.SourceChanged -= _onSourceChanged;
        _coreWebView2.DocumentTitleChanged -= _onDocumentTitleChanged;
        _coreWebView2.IsDocumentPlayingAudioChanged -= _onIsDocumentPlayingAudioChanged;
        _coreWebView2.FaviconChanged -= _onFaviconChanged;
        _coreWebView2.ContainsFullScreenElementChanged -= _onContainsFullScreenElementChanged;
        _coreWebView2 = null;
        _forceDarkApplied = false;
        IsInAutoPictureInPicture = false;
        IsPageFullScreen = false;
        IsLoading = false;
        IsPlayingAudio = false;
        SiteAccentColorHex = null;

        // The favicon deliberately stays: an unloaded tab keeps showing its site's icon.
    }

    /// <summary>Host the current <see cref="Favicon"/> belongs to — when the tab moves to another site, the old icon must go.</summary>
    private string? _faviconSite;

    private void OnSiteMaybeChanged(string url)
    {
        var site = FaviconCache.KeyFor(url);
        if (string.Equals(site, _faviconSite, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _faviconSite = site;
        Favicon = FaviconCache.TryGet(url);

        // A background tab that moved to another site: its picture shows the old one.
        if (!IsActive)
        {
            Preview = null;
        }
    }

    private async Task RefreshFaviconAsync()
    {
        if (_coreWebView2 is not { } core)
        {
            return;
        }

        try
        {
            var source = core.Source;
            using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var png = memory.ToArray();

            // Empty while a page is still starting up (e.g. right after an unloaded
            // tab comes back): keep the icon already shown rather than flashing a globe.
            if (png.Length == 0 || _coreWebView2 != core)
            {
                return;
            }

            _faviconSite = FaviconCache.KeyFor(source);
            Favicon = _tabManager.IsPrivate ? FaviconCache.Decode(png) : FaviconCache.Store(source, png);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            // Missing/unsupported favicon format for this page — keep whatever is shown.
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => _coreWebView2?.GoBack();

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => _coreWebView2?.GoForward();

    [RelayCommand]
    private void Reload() => _coreWebView2?.Reload();

    [RelayCommand]
    private void Stop() => _coreWebView2?.Stop();

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    private const string TogglePictureInPictureScript = """
        (function() {
          window.__navigatueurAutoPip = null;
          if (document.pictureInPictureElement) {
            document.exitPictureInPicture();
            return;
          }
          var video = document.querySelector('video');
          if (video && video.requestPictureInPicture) {
            video.requestPictureInPicture().catch(function() {});
          }
        })();
        """;

    [RelayCommand]
    private void TogglePictureInPicture() => _ = _coreWebView2?.ExecuteScriptAsync(TogglePictureInPictureScript);

    // ------------------------------------------------- automatic picture-in-picture

    /// <summary>
    /// Picks the video worth following — playing, audible, big enough to be
    /// the page's main video — and moves it to picture-in-picture. Run with a
    /// simulated user gesture (the engine only opens picture-in-picture in
    /// answer to one). Answers "ok", or why nothing happened.
    /// </summary>
    private const string EnterAutoPictureInPictureScript = """
        (async () => {
          if (!document.pictureInPictureEnabled) return 'unsupported';
          if (document.pictureInPictureElement) return 'already';
          let best = null, bestArea = 0;
          for (const v of document.querySelectorAll('video')) {
            if (v.paused || v.ended || v.readyState < 2 || v.muted || v.volume === 0 || v.disablePictureInPicture) continue;
            const r = v.getBoundingClientRect();
            const area = r.width * r.height;
            if (r.width < 200 || r.height < 110 || area <= bestArea) continue;
            best = v; bestArea = area;
          }
          if (!best) return 'none';
          await best.requestPictureInPicture();
          window.__navigatueurAutoPip = best;
          return 'ok';
        })()
        """;

    /// <summary>
    /// Settles when that window closes, however it closes: back in the page,
    /// closed by the user, or replaced by another tab's (there is only ever
    /// one picture-in-picture window), which is why nothing else needs to
    /// keep track of which tab owns it.
    /// </summary>
    private const string WatchAutoPictureInPictureScript = """
        new Promise(resolve => {
          const v = window.__navigatueurAutoPip;
          if (!v || document.pictureInPictureElement !== v) return resolve('gone');
          v.addEventListener('leavepictureinpicture', () => resolve('left'), { once: true });
        })
        """;

    /// <summary>Puts the video back in the page — only if it's still the one Navigatueur moved out.</summary>
    private const string ExitAutoPictureInPictureScript = """
        (() => {
          const v = window.__navigatueurAutoPip;
          window.__navigatueurAutoPip = null;
          if (v && document.pictureInPictureElement === v) document.exitPictureInPicture().catch(() => {});
        })()
        """;

    /// <summary>The user switched from this tab to another one in the same window.</summary>
    public void OnLeftForAnotherTab()
    {
        if (!AppServices.CurrentSettings.IsAutoPictureInPictureEnabled || !IsPlayingAudio || _coreWebView2 is not { } core)
        {
            return;
        }

        _ = EnterAutoPictureInPictureAsync(core);
    }

    /// <summary>The user came back to this tab: its video returns to the page.</summary>
    public void OnReturnedTo()
    {
        if (IsInAutoPictureInPicture)
        {
            ExitAutoPictureInPicture();
        }
    }

    private void ExitAutoPictureInPicture()
    {
        IsInAutoPictureInPicture = false;
        if (_coreWebView2 is { } core)
        {
            _ = RunQuietlyAsync(() => core.ExecuteScriptAsync(ExitAutoPictureInPictureScript));
        }
    }

    private async Task EnterAutoPictureInPictureAsync(CoreWebView2 core)
    {
        var answer = await EvaluateAsync(core, EnterAutoPictureInPictureScript, userGesture: true);
        if (answer != "ok" || _coreWebView2 != core)
        {
            return;
        }

        if (IsActive)
        {
            // Came straight back while the window was opening.
            ExitAutoPictureInPicture();
            return;
        }

        IsInAutoPictureInPicture = true;

        await EvaluateAsync(core, WatchAutoPictureInPictureScript, userGesture: false);
        if (_coreWebView2 == core)
        {
            IsInAutoPictureInPicture = false;
        }
    }

    /// <summary>Runs <paramref name="script"/> through the DevTools protocol, awaiting its promise; the string it returns, or null if it failed (page gone, navigated, script threw).</summary>
    private static async Task<string?> EvaluateAsync(CoreWebView2 core, string script, bool userGesture)
    {
        try
        {
            var parameters = JsonSerializer.Serialize(new { expression = script, awaitPromise = true, userGesture, returnByValue = true });
            using var result = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parameters));
            return result.RootElement.TryGetProperty("result", out var value) && value.TryGetProperty("value", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null;
        }
        catch (Exception ex) when (IsEngineGone(ex) || ex is JsonException)
        {
            return null;
        }
    }

    private static async Task RunQuietlyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (IsEngineGone(ex))
        {
            // The tab was unloaded or closed meanwhile.
        }
    }

    /// <summary>What the engine throws once its page or the whole engine has gone away (navigation, tab unloaded).</summary>
    private static bool IsEngineGone(Exception ex) =>
        ex is ObjectDisposedException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException;

    // ------------------------------------------------------------ forced dark mode

    /// <summary>What was last asked of the engine — it keeps the setting across navigations, so it's only sent again when it changes.</summary>
    private bool _forceDarkApplied;

    /// <summary>Turns the engine's auto dark mode on or off for <paramref name="url"/> (default: the current page).</summary>
    public void ApplyForceDark(string? url = null)
    {
        url ??= _coreWebView2?.Source ?? AddressBarText;
        var wanted = AppServices.ForceDark.IsEnabledFor(url, _tabManager.IsPrivate);
        IsForceDark = wanted;

        if (_coreWebView2 is not { } core || _forceDarkApplied == wanted)
        {
            return;
        }

        _forceDarkApplied = wanted;
        _ = RunQuietlyAsync(() => core.CallDevToolsProtocolMethodAsync(
            "Emulation.setAutoDarkModeOverride", wanted ? "{\"enabled\":true}" : "{\"enabled\":false}"));
    }

    [RelayCommand]
    private void ToggleForceDarkForSite() =>
        AppServices.ForceDark.SetForSite(_coreWebView2?.Source ?? AddressBarText, !IsForceDark, _tabManager.IsPrivate);

    // ------------------------------------------------------------- hover preview

    /// <summary>Width the picture is decoded at: the card shows it about 260 px wide, this leaves room for a 150 % display.</summary>
    private const int PreviewPixelWidth = 420;

    private DateTime _previewTakenAt;
    private int _previewRequest;

    /// <summary>A moment after a page finished loading, once it has had time to draw itself.</summary>
    private async Task CapturePreviewSoonAsync()
    {
        await Task.Delay(1200);
        await CapturePreviewAsync(force: true);
    }

    /// <summary>
    /// Takes a picture of the page for the hover card. Only works while the
    /// page is on screen — the engine doesn't draw a hidden one — so this is
    /// called while the tab is the visible one: after a load, when the mouse
    /// heads for the tab column, and every so often.
    /// </summary>
    public async Task CapturePreviewAsync(bool force = false)
    {
        if (_coreWebView2 is not { } core || !IsActive || _zoomHost is not { IsVisible: true } host ||
            Window.GetWindow(host) is { WindowState: WindowState.Minimized } ||
            (!force && DateTime.UtcNow - _previewTakenAt < TimeSpan.FromSeconds(2)))
        {
            return;
        }

        _previewTakenAt = DateTime.UtcNow;
        var request = ++_previewRequest;
        var stream = new MemoryStream();

        try
        {
            var capture = core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, stream);
            if (await Task.WhenAny(capture, Task.Delay(2000)) != capture)
            {
                // Hidden in the meantime: the engine only answers once the page shows again. Nobody waits for it.
                _ = capture.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                return;
            }

            await capture;
            var jpeg = stream.ToArray();
            if (jpeg.Length == 0 || request != _previewRequest || _coreWebView2 != core)
            {
                return;
            }

            Preview = await Task.Run(() => DecodePreview(jpeg));
        }
        catch (Exception ex) when (IsEngineGone(ex) || ex is NotSupportedException or IOException or FileFormatException)
        {
            // Keep the previous picture.
        }
    }

    private static ImageSource DecodePreview(byte[] jpeg)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = PreviewPixelWidth;
        image.StreamSource = new MemoryStream(jpeg);
        image.EndInit();
        image.Freeze();
        return image;
    }

    [RelayCommand]
    private void Navigate() => NavigateTo(AddressBarText);

    /// <summary>
    /// Navigates to whatever was typed: a URL, a navigatueur:// address, or
    /// a search with the engine chosen in Settings. A suspended tab (no live
    /// engine yet) just remembers the target and loads it when it resumes.
    /// </summary>
    public void NavigateTo(string input)
    {
        var url = UrlHelper.Normalize(input, AppServices.SearchEngine.CurrentEngine.QueryUrlPrefix);
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        AddressBarText = url;
        if (_coreWebView2 is not null)
        {
            _coreWebView2.Navigate(url);
        }
        else
        {
            IsSuspended = false;
        }
    }

    private const string ThemeColorScript =
        "(function(){var m=document.querySelector('meta[name=\"theme-color\"]');return m?m.content:'';})();";

    private async Task RefreshSiteAccentColorAsync()
    {
        if (_coreWebView2 is null)
        {
            return;
        }

        try
        {
            var json = await _coreWebView2.ExecuteScriptAsync(ThemeColorScript);
            var value = JsonSerializer.Deserialize<string>(json);
            SiteAccentColorHex = string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // The tab was suspended/torn down while the script was in flight — not worth surfacing for a cosmetic feature.
            SiteAccentColorHex = null;
        }
    }

    private const double ZoomStep = 0.1;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 5.0;

    [RelayCommand]
    private void ZoomIn() => SetZoom((_zoomHost?.ZoomFactor ?? 1.0) + ZoomStep);

    [RelayCommand]
    private void ZoomOut() => SetZoom((_zoomHost?.ZoomFactor ?? 1.0) - ZoomStep);

    [RelayCommand]
    private void ResetZoom() => SetZoom(1.0);

    private void SetZoom(double factor)
    {
        if (_zoomHost is null)
        {
            return;
        }

        _zoomHost.ZoomFactor = Math.Clamp(factor, MinZoom, MaxZoom);
    }

    /// <summary>
    /// Opens the current page through Yandex's page-translation proxy
    /// (translated.turbopages.org) into French — the same mechanism Yandex
    /// Browser's own "translate this page" button uses. Passing only the
    /// target language (no source) lets Yandex auto-detect the source
    /// language, which is the reliable form of this URL; passing an explicit
    /// "auto" source is not reliably honored.
    /// </summary>
    [RelayCommand]
    private void TranslatePage()
    {
        if (_coreWebView2 is null || string.IsNullOrWhiteSpace(_coreWebView2.Source))
        {
            return;
        }

        var encoded = Uri.EscapeDataString(_coreWebView2.Source);
        _coreWebView2.Navigate($"https://translate.yandex.com/translate?url={encoded}&lang=fr");
    }

    /// <summary>
    /// Captures the current page as MHTML via the DevTools protocol for
    /// "Enregistrer sous". The returned JSON's "data" field is decoded
    /// defensively: some CDP builds return raw MHTML text, others base64 —
    /// this checks for the MHTML signature first and only falls back to
    /// base64-decoding if that signature isn't present.
    /// </summary>
    public async Task<string?> CaptureMhtmlAsync()
    {
        if (_coreWebView2 is null)
        {
            return null;
        }

        var resultJson = await _coreWebView2.CallDevToolsProtocolMethodAsync("Page.captureSnapshot", "{\"format\":\"mhtml\"}");
        using var doc = JsonDocument.Parse(resultJson);
        if (!doc.RootElement.TryGetProperty("data", out var dataProp))
        {
            return null;
        }

        var data = dataProp.GetString();
        if (string.IsNullOrEmpty(data))
        {
            return null;
        }

        if (data.Contains("MIME-Version:", StringComparison.OrdinalIgnoreCase))
        {
            return data;
        }

        try
        {
            var bytes = Convert.FromBase64String(data);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return data;
        }
    }
}
