using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
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
    /// What the address bar actually shows: blank on the internal new-tab
    /// page (navigatueur.home/...) instead of exposing that implementation
    /// detail, matching how real browsers show an empty omnibox on their own
    /// new-tab page. Reading/writing this just proxies to <see cref="AddressBarText"/>.
    /// </summary>
    public string AddressBarDisplayText
    {
        get => AddressBarText.StartsWith("https://navigatueur.home/", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : AddressBarText;
        set => AddressBarText = value;
    }

    partial void OnAddressBarTextChanged(string value) => OnPropertyChanged(nameof(AddressBarDisplayText));

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private bool isSuspended;

    [ObservableProperty]
    private DateTimeOffset lastActivatedAt = DateTimeOffset.Now;

    [ObservableProperty]
    private Guid? groupId;

    public bool IsGrouped => GroupId is not null;

    [ObservableProperty]
    private bool isPinned;

    [ObservableProperty]
    private bool isMuted;

    /// <summary>Tracks the tab's actual audio output (not just the mute toggle) so the RAM-saving suspension logic can leave a tab currently playing sound alone.</summary>
    [ObservableProperty]
    private bool isPlayingAudio;

    /// <summary>The current page's declared &lt;meta name="theme-color"&gt; (hex or rgb()), or null if it doesn't have one. Drives the chrome background's top-to-gray gradient.</summary>
    [ObservableProperty]
    private string? siteAccentColorHex;

    [ObservableProperty]
    private ImageSource? favicon;

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

    public BrowserTabViewModel(string initialUrl, TabManagerService tabManager)
    {
        InitialUrl = initialUrl;
        addressBarText = initialUrl;
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
            }
            else
            {
                SiteAccentColorHex = null;
            }
        };
        _onSourceChanged = (_, _) => AddressBarText = _coreWebView2?.Source ?? AddressBarText;
        _onDocumentTitleChanged = (_, _) =>
            Title = string.IsNullOrWhiteSpace(_coreWebView2?.DocumentTitle)
                ? AddressBarText
                : _coreWebView2!.DocumentTitle;
        _onIsDocumentPlayingAudioChanged = (_, _) => IsPlayingAudio = _coreWebView2?.IsDocumentPlayingAudio ?? false;
        _onFaviconChanged = (_, _) => _ = RefreshFaviconAsync();

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

        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && AppServices.PhishingProtection.IsPhishing(uri.Host))
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

        _coreWebView2.NavigationStarting += _onNavigationStarting;
        _coreWebView2.NavigationCompleted += _onNavigationCompleted;
        _coreWebView2.SourceChanged += _onSourceChanged;
        _coreWebView2.DocumentTitleChanged += _onDocumentTitleChanged;
        _coreWebView2.IsDocumentPlayingAudioChanged += _onIsDocumentPlayingAudioChanged;
        IsPlayingAudio = _coreWebView2.IsDocumentPlayingAudio;
        _coreWebView2.FaviconChanged += _onFaviconChanged;
        _ = RefreshFaviconAsync();
    }

    /// <summary>Exposed so MainWindow can focus the actual embedded browser HWND before sending it Ctrl+F (native Chromium find-in-page).</summary>
    public WebView2? WebViewControl => _zoomHost;

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
        _coreWebView2 = null;
        IsLoading = false;
        IsPlayingAudio = false;
        SiteAccentColorHex = null;
        Favicon = null;
    }

    private async Task RefreshFaviconAsync()
    {
        if (_coreWebView2 is null)
        {
            return;
        }

        try
        {
            using var stream = await _coreWebView2.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (stream.Length == 0)
            {
                Favicon = null;
                return;
            }

            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            memory.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            Favicon = bitmap;
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            // Missing/unsupported favicon format for this page — not worth surfacing.
            Favicon = null;
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

    [RelayCommand]
    private void Navigate()
    {
        if (_coreWebView2 is null || string.IsNullOrWhiteSpace(AddressBarText))
        {
            return;
        }

        _coreWebView2.Navigate(UrlHelper.Normalize(AddressBarText));
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
