using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Navigatueur.App.Services;
using Navigatueur.App.Shell;
using Navigatueur.App.ViewModels;
using Navigatueur.Core;

namespace Navigatueur.App.Views;

/// <summary>
/// Hosts one tab's WebView2. The engine is created lazily and torn down when
/// the tab is suspended (TabManagerService decides), then recreated on resume.
/// </summary>
public partial class BrowserTabView : UserControl
{
    /// <summary>Virtual host mapped to <see cref="ThemeService.BackgroundsDirectory"/>, so a user-picked new-tab background image is reachable from the page as a real https URL.</summary>
    private const string UserBackgroundHostName = "navigatueur.userbg";

    /// <summary>Virtual host name for the phishing warning interstitial (Resources/Warning/index.html).</summary>
    private const string WarningHostName = "navigatueur.warning";

    /// <summary>Navigatueur's own pages (new tab, settings, history), served as https://navigatueur.home/… — see InternalPages.</summary>
    private static readonly string PagesFolder =
        Path.Combine(AppContext.BaseDirectory, "Resources", "Pages");

    private static readonly string WarningFolder =
        Path.Combine(AppContext.BaseDirectory, "Resources", "Warning");

    private WebView2? _webView;
    private BrowserTabViewModel? _subscribedViewModel;
    private readonly Queue<(CoreWebView2PermissionRequestedEventArgs Args, CoreWebView2Deferral Deferral)> _pendingPermissions = new();

    public BrowserTabView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private BrowserTabViewModel? ViewModel => DataContext as BrowserTabViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = ViewModel;

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BrowserTabViewModel.IsSuspended) || ViewModel is null)
        {
            return;
        }

        if (ViewModel.IsSuspended)
        {
            TearDown();
        }
        else if (IsLoaded)
        {
            await EnsureLoadedAsync();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || ViewModel.IsSuspended)
        {
            return;
        }

        await EnsureLoadedAsync();
    }

    private async void OnRetryEngineClick(object sender, RoutedEventArgs e)
    {
        EngineErrorPanel.Visibility = Visibility.Collapsed;
        await EnsureLoadedAsync();
    }

    private async Task EnsureLoadedAsync()
    {
        if (_webView is not null || ViewModel is null)
        {
            return;
        }

        var webView = new WebView2();
        ContentHost.Children.Add(webView);
        _webView = webView;

        try
        {
            var environment = await ViewModel.TabManager.Environment.GetEnvironmentAsync();
            await webView.EnsureCoreWebView2Async(environment);
        }
        catch (ObjectDisposedException)
        {
            // The tab was suspended again while WebView2 was still initializing.
            return;
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or FileNotFoundException or ArgumentException)
        {
            // No usable engine: say so in the tab instead of crashing the whole app.
            ContentHost.Children.Remove(webView);
            webView.Dispose();
            _webView = null;
            EngineErrorText.Text = ex is WebView2RuntimeNotFoundException
                ? "Le moteur WebView2 est introuvable. Réinstallez Navigatueur (il embarque son moteur), ou installez « Microsoft Edge WebView2 Runtime » depuis le site de Microsoft."
                : $"Détail : {ex.Message}";
            EngineErrorPanel.Visibility = Visibility.Visible;
            return;
        }

        if (_webView != webView)
        {
            // Suspended again mid-initialization: the tear-down already ran, nothing more to do.
            return;
        }

        var core = webView.CoreWebView2;
        if (!ViewModel.TabManager.IsPrivate)
        {
            AppServices.Extensions.AttachProfile(core.Profile);
        }

        core.SetVirtualHostNameToFolderMapping(InternalPages.HostName, PagesFolder, CoreWebView2HostResourceAccessKind.Allow);

        Directory.CreateDirectory(ThemeService.BackgroundsDirectory);
        core.SetVirtualHostNameToFolderMapping(UserBackgroundHostName, ThemeService.BackgroundsDirectory, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping(WarningHostName, WarningFolder, CoreWebView2HostResourceAccessKind.Allow);

        if (AppServices.CurrentSettings.IsAdBlockEnabled && !ViewModel.IsAdBlockDisabled &&
            AppServices.AdBlock.GetCosmeticInjectionScript() is { Length: > 0 } cosmeticScript)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(cosmeticScript);
        }

        await core.AddScriptToExecuteOnDocumentCreatedAsync(BuildPageEnhancementScript());

        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.NewWindowRequested += OnNewWindowRequested;
        core.PermissionRequested += OnPermissionRequested;
        core.DownloadStarting += OnDownloadStarting;
        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationStarting += OnNavigationStarting;

        InternalPageBridge.StateInvalidated += OnInternalStateInvalidated;
        AppServices.Theme.PropertyChanged += OnThemePropertyChanged;
        AppServices.ForceDark.Changed += OnForceDarkChanged;

        ViewModel.AttachCoreWebView2(core);
        ViewModel.AttachZoomHost(webView);
        core.Navigate(ViewModel.AddressBarText);

        // Focus was asked for while this tab was still unloaded (tab switch):
        // hand it over now — unless the user has since moved on to a panel.
        if (ViewModel.WantsFocus)
        {
            ViewModel.WantsFocus = false;
            if (ViewModel.IsActive && !IsKeyboardFocusInAnotherWindowPart())
            {
                webView.Focus();
            }
        }
    }

    /// <summary>True when keyboard focus is in a panel (toolbar, tab column) — those live in their own child windows, i.e. a different PresentationSource.</summary>
    private bool IsKeyboardFocusInAnotherWindowPart() =>
        Keyboard.FocusedElement is DependencyObject focused
        && PresentationSource.FromDependencyObject(focused) is { } source
        && source != PresentationSource.FromVisual(this);

    // ---------------------------------------------------------- page script

    /// <summary>
    /// Injected into every page: the accent-colored cursor trail, drawn on a click-through canvas *inside* the page. The
    /// trail used to be drawn by a separate transparent window over the page,
    /// fed by one cross-process message per mouse move; drawing it in the page
    /// itself needs neither, and the canvas only exists once the trail is on
    /// and the mouse has moved.
    /// </summary>
    private static string BuildPageEnhancementScript()
    {
        var enabled = AppServices.Theme.IsCursorTrailEnabled ? "true" : "false";
        var accent = AppServices.Theme.AccentColorHex;

        return $$"""
            (function () {
              if (window.__navigatueurEnhanced) return;
              window.__navigatueurEnhanced = true;

              var enabled = {{enabled}};
              var accent = {{System.Text.Json.JsonSerializer.Serialize(accent)}};
              var LIFE = 260, GAP = 120;
              var canvas = null, ctx = null, points = [], frame = 0;

              function resize() {
                var ratio = window.devicePixelRatio || 1;
                canvas.width = innerWidth * ratio;
                canvas.height = innerHeight * ratio;
                ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
              }

              function ensureCanvas() {
                if (canvas) return;
                canvas = document.createElement('canvas');
                canvas.setAttribute('aria-hidden', 'true');
                canvas.style.cssText = 'position:fixed;left:0;top:0;width:100vw;height:100vh;pointer-events:none;z-index:2147483647';
                document.documentElement.appendChild(canvas);
                ctx = canvas.getContext('2d');
                resize();
                addEventListener('resize', resize, { passive: true });
              }

              function draw() {
                var now = performance.now();
                points = points.filter(function (p) { return now - p.t < LIFE; });
                ctx.clearRect(0, 0, innerWidth, innerHeight);
                ctx.lineCap = 'round';
                ctx.lineWidth = 3.5;
                ctx.strokeStyle = accent;
                for (var i = 1; i < points.length; i++) {
                  var a = points[i - 1], b = points[i];
                  if (b.t - a.t > GAP) continue;
                  var life = 1 - (now - b.t) / LIFE;
                  ctx.globalAlpha = 0.7 * life * life;
                  ctx.beginPath();
                  ctx.moveTo(a.x, a.y);
                  ctx.lineTo(b.x, b.y);
                  ctx.stroke();
                }
                frame = points.length ? requestAnimationFrame(draw) : 0;
              }

              addEventListener('mousemove', function (e) {
                if (!enabled) return;
                ensureCanvas();
                points.push({ x: e.clientX, y: e.clientY, t: performance.now() });
                if (!frame) frame = requestAnimationFrame(draw);
              }, { capture: true, passive: true });

              window.__navigatueurTrail = function (on, color) {
                enabled = !!on;
                if (color) accent = color;
                if (!enabled && ctx) { points = []; ctx.clearRect(0, 0, innerWidth, innerHeight); }
              };
            })();
            """;
    }

    private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ThemeService.IsCursorTrailEnabled) or nameof(ThemeService.AccentColorHex)) ||
            _webView?.CoreWebView2 is not { } core)
        {
            return;
        }

        var theme = AppServices.Theme;
        var script = $"window.__navigatueurTrail && window.__navigatueurTrail({(theme.IsCursorTrailEnabled ? "true" : "false")}, {System.Text.Json.JsonSerializer.Serialize(theme.AccentColorHex)})";
        _ = core.ExecuteScriptAsync(script);
    }

    /// <summary>The forced dark mode was switched (globally or for a site): this page follows straight away.</summary>
    private void OnForceDarkChanged(object? sender, EventArgs e) => ViewModel?.ApplyForceDark();

    // ------------------------------------------------------ internal pages

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Any site can postMessage to the host; only Navigatueur's own pages are listened to.
        if (!InternalPages.IsInternal(e.Source) || _webView?.CoreWebView2 is not { } core)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        InternalPageBridge.Handle(core, e.WebMessageAsJson, owner as IShellCommands, owner, ViewModel?.TabManager.IsPrivate ?? false);
    }

    private void OnInternalStateInvalidated(object? sender, EventArgs e)
    {
        if (_webView?.CoreWebView2 is { } core && InternalPages.IsInternal(core.Source))
        {
            InternalPageBridge.PostState(core, ViewModel?.TabManager.IsPrivate ?? false);
        }
    }

    /// <summary>Leaving the Settings page in the middle of recording a shortcut must not leave shortcuts disabled.</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (sender is CoreWebView2 core && InternalPages.IsInternal(core.Source))
        {
            AppServices.Shortcuts.EndCapture();
        }
    }

    // ------------------------------------------------------------ network

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e) =>
        AppServices.Downloads.TrackDownload(e.DownloadOperation);

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        // Never block a Document request (top-level navigation, or an iframe's own document): the
        // ad-block list has no exception rules, so blindly blocking it can take down an entire page
        // instead of just the ad it's meant to catch. Subresources (scripts, images, XHR...) are the
        // ones actually worth blocking, and failing quietly there doesn't break navigation.
        if (e.ResourceContext == CoreWebView2WebResourceContext.Document ||
            !AppServices.CurrentSettings.IsAdBlockEnabled ||
            (ViewModel?.IsAdBlockDisabled ?? false))
        {
            return;
        }

        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) || !AppServices.AdBlock.IsBlocked(uri.Host))
        {
            return;
        }

        e.Response = _webView!.CoreWebView2.Environment.CreateWebResourceResponse(
            null, 403, "Blocked", string.Empty);
    }

    /// <summary>
    /// Links opening a new browsing context (target="_blank", window.open, ctrl+click...)
    /// default to a bare, chrome-less popup window outside the app's tab strip.
    /// Redirect them into a proper Navigatueur tab instead.
    /// </summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        ViewModel?.TabManager.OpenTab(e.Uri);
    }

    // -------------------------------------------------------- permissions

    /// <summary>
    /// Site permission requests (camera/mic/location/notifications...) are
    /// resolved instantly from a remembered per-origin decision if one
    /// exists; otherwise a deferral is taken and the prompt bar asks the
    /// user, queuing further requests if more than one arrives at once.
    /// </summary>
    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (AppServices.PermissionStore.TryGetDecision(e.Uri, e.PermissionKind, out var allowed))
        {
            e.State = allowed ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            return;
        }

        var deferral = e.GetDeferral();
        _pendingPermissions.Enqueue((e, deferral));
        if (_pendingPermissions.Count == 1)
        {
            ShowNextPermissionPrompt();
        }
    }

    private void ShowNextPermissionPrompt()
    {
        if (_pendingPermissions.Count == 0)
        {
            PermissionBar.Visibility = Visibility.Collapsed;
            return;
        }

        var (args, _) = _pendingPermissions.Peek();
        var host = Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ? uri.Host : args.Uri;
        PermissionText.Text = $"{host} demande à {DescribePermission(args.PermissionKind)}";
        PermissionRememberCheckBox.IsChecked = true;
        PermissionBar.Visibility = Visibility.Visible;
    }

    private void OnPermissionAllowClick(object sender, RoutedEventArgs e) => ResolvePendingPermission(true);

    private void OnPermissionBlockClick(object sender, RoutedEventArgs e) => ResolvePendingPermission(false);

    private void ResolvePendingPermission(bool allow)
    {
        if (_pendingPermissions.Count == 0)
        {
            return;
        }

        var (args, deferral) = _pendingPermissions.Dequeue();
        args.State = allow ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;

        if (PermissionRememberCheckBox.IsChecked == true)
        {
            AppServices.PermissionStore.SetDecision(args.Uri, args.PermissionKind, allow);
        }

        deferral.Complete();
        ShowNextPermissionPrompt();
    }

    private static string DescribePermission(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Microphone => "utiliser le microphone",
        CoreWebView2PermissionKind.Camera => "utiliser la caméra",
        CoreWebView2PermissionKind.Geolocation => "connaître votre position",
        CoreWebView2PermissionKind.Notifications => "envoyer des notifications",
        CoreWebView2PermissionKind.ClipboardRead => "lire le presse-papiers",
        CoreWebView2PermissionKind.MultipleAutomaticDownloads => "lancer plusieurs téléchargements",
        _ => $"obtenir une autorisation ({kind})",
    };

    // ------------------------------------------------------------ teardown

    private void TearDown()
    {
        if (_webView is null)
        {
            return;
        }

        var webView = _webView;
        _webView = null;

        InternalPageBridge.StateInvalidated -= OnInternalStateInvalidated;
        AppServices.Theme.PropertyChanged -= OnThemePropertyChanged;
        AppServices.ForceDark.Changed -= OnForceDarkChanged;

        // A suspended tab can't answer pending permission prompts: deny them rather than leave the page waiting.
        while (_pendingPermissions.Count > 0)
        {
            var (args, deferral) = _pendingPermissions.Dequeue();
            args.State = CoreWebView2PermissionState.Deny;
            deferral.Complete();
        }

        PermissionBar.Visibility = Visibility.Collapsed;

        ViewModel?.DetachCoreWebView2();
        ViewModel?.DetachZoomHost();
        ContentHost.Children.Remove(webView);
        webView.Dispose();
    }
}
