using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Navigatueur.App.Services;
using Navigatueur.App.ViewModels;

namespace Navigatueur.App.Views;

public partial class BrowserTabView : UserControl
{
    /// <summary>
    /// Virtual host name mapped to <see cref="NewTabFolder"/>, so the custom
    /// "new tab" home page (Resources/NewTab/index.html) is navigable as a
    /// real https URL instead of loading Microsoft's Bing homepage directly.
    /// </summary>
    private const string NewTabHostName = "navigatueur.home";

    /// <summary>Virtual host mapped to <see cref="ThemeService.BackgroundsDirectory"/>, so a user-picked new-tab background image is reachable from the page as a real https URL.</summary>
    private const string UserBackgroundHostName = "navigatueur.userbg";

    /// <summary>Virtual host name for the phishing warning interstitial (Resources/Warning/index.html).</summary>
    private const string WarningHostName = "navigatueur.warning";

    private static readonly string NewTabFolder =
        Path.Combine(AppContext.BaseDirectory, "Resources", "NewTab");

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

        if (_webView != webView)
        {
            // Suspended again mid-initialization: the tear-down already ran, nothing more to do.
            return;
        }

        if (!ViewModel.TabManager.IsPrivate)
        {
            AppServices.Extensions.AttachProfile(webView.CoreWebView2.Profile);
        }

        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            NewTabHostName, NewTabFolder, CoreWebView2HostResourceAccessKind.Allow);

        Directory.CreateDirectory(ThemeService.BackgroundsDirectory);
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            UserBackgroundHostName, ThemeService.BackgroundsDirectory, CoreWebView2HostResourceAccessKind.Allow);

        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            WarningHostName, WarningFolder, CoreWebView2HostResourceAccessKind.Allow);

        var theme = AppServices.Theme;
        var backgroundFile = theme.NewTabBackgroundImagePath is { } backgroundPath ? Path.GetFileName(backgroundPath) : null;
        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            BuildNewTabThemeScript(backgroundFile, theme.AccentColorHex, AppServices.SearchEngine.CurrentEngine.ActionUrl));

        var cosmeticScript = AppServices.AdBlock.GetCosmeticInjectionScript();
        if (!string.IsNullOrEmpty(cosmeticScript) && !ViewModel.IsAdBlockDisabled)
        {
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(cosmeticScript);
        }

        webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        webView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
        webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        webView.CoreWebView2.PermissionRequested += OnPermissionRequested;
        webView.CoreWebView2.DownloadStarting += OnDownloadStarting;

        ViewModel.AttachCoreWebView2(webView.CoreWebView2);
        webView.CoreWebView2.Navigate(ViewModel.AddressBarText);
    }

    /// <summary>
    /// Applies the current new-tab background image and accent color to the
    /// home page. Runs at document-created time (before the body exists), so
    /// it defers its own DOM work to DOMContentLoaded.
    /// </summary>
    private static string BuildNewTabThemeScript(string? backgroundFile, string accentHex, string searchActionUrl)
    {
        var backgroundJs = backgroundFile is null ? "null" : $"'{Uri.EscapeDataString(backgroundFile)}'";
        return $$"""
        (function() {
          if (location.hostname !== 'navigatueur.home') return;
          function apply() {
            var bgFile = {{backgroundJs}};
            var layer = document.getElementById('bgLayer');
            if (layer && bgFile) {
              layer.style.backgroundImage = "url('https://navigatueur.userbg/" + bgFile + "')";
              layer.classList.add('has-image');
            }
            document.documentElement.style.setProperty('--nv-accent', '{{accentHex}}');
            var form = document.getElementById('searchForm');
            if (form) form.action = '{{searchActionUrl}}';
          }
          if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', apply);
          } else {
            apply();
          }
        })();
        """;
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e) =>
        AppServices.Downloads.TrackDownload(e.DownloadOperation);

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        // Never block a Document request (top-level navigation, or an iframe's own document): the
        // ad-block list has no exception rules, so blindly blocking it can take down an entire page
        // instead of just the ad it's meant to catch. Subresources (scripts, images, XHR...) are the
        // ones actually worth blocking, and failing quietly there doesn't break navigation.
        if (e.ResourceContext == CoreWebView2WebResourceContext.Document ||
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

    /// <summary>
    /// Site permission requests (camera/mic/location/notifications...) are
    /// resolved instantly from a remembered per-origin decision if one
    /// exists; otherwise a deferral is taken and the in-page prompt bar asks
    /// the user, queuing further requests if more than one arrives at once.
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
        PermissionText.Text = $"{host} demande : {DescribePermission(args.PermissionKind)}";
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
        CoreWebView2PermissionKind.MultipleAutomaticDownloads => "lancer plusieurs téléchargements automatiques",
        _ => kind.ToString(),
    };

    private void TearDown()
    {
        if (_webView is null)
        {
            return;
        }

        var webView = _webView;
        _webView = null;

        ViewModel?.DetachCoreWebView2();
        ContentHost.Children.Remove(webView);
        webView.Dispose();
    }
}
