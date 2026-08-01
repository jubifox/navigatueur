using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;

namespace Navigatueur.App.Services;

/// <summary>
/// WebView2 actually supports loading real, unpacked Chromium extensions
/// (CoreWebView2Profile.AddBrowserExtensionAsync) — the same mechanism Edge
/// itself uses. Rather than reimplementing specific extensions (uBlock
/// Origin, BetterTTV, Coupert...) natively, which risks a fragile/incorrect
/// port of code we don't own, this exposes that real capability: point it at
/// an unpacked extension folder and it runs exactly as it would in Chrome/Edge.
/// </summary>
public partial class ExtensionService : ObservableObject
{
    public ObservableCollection<InstalledExtension> Extensions { get; } = new();

    [ObservableProperty]
    private string? lastError;

    private CoreWebView2Profile? _profile;

    /// <summary>Called once the first tab's CoreWebView2 exists — all tabs share the same profile, so this only needs to happen once.</summary>
    public async void AttachProfile(CoreWebView2Profile profile)
    {
        if (_profile is not null)
        {
            return;
        }

        _profile = profile;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_profile is null)
        {
            return;
        }

        try
        {
            var list = await _profile.GetBrowserExtensionsAsync();
            Extensions.Clear();
            foreach (var extension in list)
            {
                Extensions.Add(new InstalledExtension(extension, this));
            }

            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    public async Task<bool> AddExtensionAsync(string unpackedFolderPath)
    {
        if (_profile is null)
        {
            LastError = "Aucun onglet n'est encore chargé — ouvre un onglet puis réessaie.";
            return false;
        }

        try
        {
            await _profile.AddBrowserExtensionAsync(unpackedFolderPath);
            await RefreshAsync();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public async Task RemoveAsync(InstalledExtension extension)
    {
        try
        {
            await extension.Extension.RemoveAsync();
            Extensions.Remove(extension);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }
}

public partial class InstalledExtension : ObservableObject
{
    private readonly ExtensionService _owner;

    public InstalledExtension(CoreWebView2BrowserExtension extension, ExtensionService owner)
    {
        Extension = extension;
        _owner = owner;
        Id = extension.Id;
        Name = extension.Name;
        isEnabled = extension.IsEnabled;
    }

    internal CoreWebView2BrowserExtension Extension { get; }

    public string Id { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool isEnabled;

    partial void OnIsEnabledChanged(bool value) => _ = Extension.EnableAsync(value);

    [RelayCommand]
    private async Task Remove() => await _owner.RemoveAsync(this);
}
