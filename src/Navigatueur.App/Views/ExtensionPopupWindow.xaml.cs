using System.Windows;
using Navigatueur.App.Services;

namespace Navigatueur.App.Views;

public partial class ExtensionPopupWindow : Window
{
    private readonly string _navigateUrl;

    public ExtensionPopupWindow(string extensionName, string navigateUrl)
    {
        InitializeComponent();
        Title = extensionName;
        _navigateUrl = navigateUrl;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Same shared (non-private) environment every normal tab uses — the
        // extension is only actually loaded/runnable in that one profile.
        var environment = await AppServices.WebView2Environment.GetEnvironmentAsync();
        await PopupWebView.EnsureCoreWebView2Async(environment);
        PopupWebView.CoreWebView2.Navigate(_navigateUrl);
    }
}
