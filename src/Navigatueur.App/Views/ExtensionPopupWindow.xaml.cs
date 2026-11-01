using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Navigatueur.App.Interop;
using Navigatueur.App.Services;

namespace Navigatueur.App.Views;

public partial class ExtensionPopupWindow : Window
{
    private readonly string _navigateUrl;
    private readonly Point? _anchor;
    private bool _isClosing;

    /// <param name="anchor">Screen point (device-independent pixels) where the window's top-right corner goes — just under the toolbar button it was opened from. Centered on the main window when null.</param>
    public ExtensionPopupWindow(string extensionName, string navigateUrl, Point? anchor = null)
    {
        InitializeComponent();
        Title = extensionName;
        _navigateUrl = navigateUrl;
        _anchor = anchor;

        if (Application.Current?.MainWindow is { IsVisible: true } owner && !ReferenceEquals(owner, this))
        {
            Owner = owner;
        }

        WindowStartupLocation = anchor is null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.Manual;
        SourceInitialized += (_, _) => ApplyRoundedCorners();
        Loaded += OnLoaded;

        // Like a browser popup: gone as soon as the user is back elsewhere.
        Deactivated += (_, _) => CloseOnce();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CloseOnce();
            }
        };
        Closing += (_, _) => _isClosing = true;
    }

    private void CloseOnce()
    {
        if (!_isClosing)
        {
            _isClosing = true;
            Dispatcher.BeginInvoke(new Action(Close));
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        PlaceUnderAnchor();

        // Same shared (non-private) environment every normal tab uses — the
        // extension is only loaded and running in that one profile.
        var environment = await AppServices.WebView2Environment.GetEnvironmentAsync();
        await PopupWebView.EnsureCoreWebView2Async(environment);
        PopupWebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        PopupWebView.CoreWebView2.WindowCloseRequested += (_, _) => CloseOnce();
        PopupWebView.CoreWebView2.Navigate(_navigateUrl);
        PopupWebView.Focus();
    }

    /// <summary>A link or "open dashboard" button in the popup opens a normal tab, not a bare window.</summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        AppServices.TabManager.OpenTab(e.Uri);
        Application.Current?.MainWindow?.Activate();
        CloseOnce();
    }

    /// <summary>Top-right corner just under the toolbar button, kept on screen (any monitor).</summary>
    private void PlaceUnderAnchor()
    {
        if (_anchor is not { } anchor)
        {
            return;
        }

        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;
        Left = Math.Max(left, Math.Min(anchor.X - ActualWidth, right - ActualWidth));
        Top = Math.Max(top, Math.Min(anchor.Y + 6, bottom - ActualHeight));
    }

    private void ApplyRoundedCorners()
    {
        // Windows 11 only; older versions reject the attribute and keep square corners.
        var preference = NativeMethods.DWMWCP_ROUND;
        try
        {
            NativeMethods.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }
}
