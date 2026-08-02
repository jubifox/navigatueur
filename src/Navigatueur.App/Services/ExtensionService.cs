using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;

namespace Navigatueur.App.Services;

/// <summary>Download source + a bit of context for a one-click curated extension install.</summary>
public sealed record RecommendedExtension(string Name, string Description, string DownloadUrl);

/// <summary>
/// WebView2 actually supports loading real, unpacked Chromium extensions
/// (CoreWebView2Profile.AddBrowserExtensionAsync) — the same mechanism Edge
/// itself uses. Rather than reimplementing specific extensions (uBlock
/// Origin, BetterTTV, Coupert...) natively, which risks a fragile/incorrect
/// port of code we don't own, this exposes that real capability: point it at
/// an unpacked extension folder and it runs exactly as it would in Chrome/Edge.
///
/// A handful of well-known open-source extensions are offered as one-click
/// installs too (verified to publish a ready-to-load unpacked build):
///  - uBlock Origin: the real thing, far ahead of our own reimplementation on
///    a fast-moving target like YouTube's ad system.
///  - Consent-O-Matic: auto-answers cookie consent banners.
///  - SponsorBlock: skips sponsored segments on YouTube (crowdsourced).
///  - Decentraleyes: local CDN emulation to dodge third-party JS tracking.
/// Privacy Badger isn't offered here — EFF doesn't publish a ready build,
/// only source that needs their npm build pipeline, which isn't safe to run
/// unattended as part of a one-click installer.
/// </summary>
public partial class ExtensionService : ObservableObject
{
    public static readonly RecommendedExtension[] Recommended =
    {
        new(
            "uBlock Origin",
            "Bloqueur de pubs/trackers complet — bien plus efficace que le nôtre sur YouTube.",
            "https://github.com/gorhill/uBlock/releases/download/1.72.2/uBlock0_1.72.2.chromium.zip"),
        new(
            "Consent-O-Matic",
            "Répond automatiquement aux bandeaux de consentement cookies.",
            "https://github.com/cavi-au/Consent-O-Matic/releases/download/v1.1.5/consent-o-matic-v1.1.5-unpacked-release-chromium.zip"),
        new(
            "SponsorBlock",
            "Passe automatiquement les segments sponsorisés sur YouTube.",
            "https://github.com/ajayyy/SponsorBlock/releases/download/6.1.7/ChromeExtension.zip"),
        new(
            "Decentraleyes",
            "Sert localement les bibliothèques JS courantes pour éviter le pistage via les CDN partagés.",
            "https://github.com/Synzvato/decentraleyes/archive/refs/heads/master.zip"),
    };

    private static readonly string ExtensionsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Navigatueur", "Extensions");

    public ObservableCollection<InstalledExtension> Extensions { get; } = new();

    public ObservableCollection<RecommendedExtensionViewModel> RecommendedItems { get; } = new(
        Recommended.Select(r => new RecommendedExtensionViewModel(r)));

    [ObservableProperty]
    private string? lastError;

    private CoreWebView2Profile? _profile;

    public ExtensionService()
    {
        Extensions.CollectionChanged += (_, _) => RefreshRecommendedInstalledState();
    }

    private void RefreshRecommendedInstalledState()
    {
        foreach (var item in RecommendedItems)
        {
            item.IsInstalled = Extensions.Any(e =>
                e.Name.Contains(item.Recommended.Name, StringComparison.OrdinalIgnoreCase));
        }
    }

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

    /// <summary>Downloads a recommended extension's zip, extracts it, locates its manifest.json (some zips wrap the extension in a subfolder), and installs it.</summary>
    public async Task<bool> InstallRecommendedAsync(RecommendedExtension recommended)
    {
        if (_profile is null)
        {
            LastError = "Aucun onglet n'est encore chargé — ouvre un onglet puis réessaie.";
            return false;
        }

        var safeName = string.Concat(recommended.Name.Split(Path.GetInvalidFileNameChars()));
        var extractDir = Path.Combine(ExtensionsDirectory, safeName);
        var zipPath = Path.Combine(Path.GetTempPath(), $"{safeName}.zip");

        try
        {
            using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            {
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Navigatueur");
                var bytes = await httpClient.GetByteArrayAsync(recommended.DownloadUrl);
                await File.WriteAllBytesAsync(zipPath, bytes);
            }

            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, recursive: true);
            }

            ZipFile.ExtractToDirectory(zipPath, extractDir);

            var manifestDir = FindManifestDirectory(extractDir);
            if (manifestDir is null)
            {
                LastError = $"« {recommended.Name} » : manifest.json introuvable dans l'archive téléchargée.";
                return false;
            }

            return await AddExtensionAsync(manifestDir);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        {
            LastError = $"« {recommended.Name} » : {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                File.Delete(zipPath);
            }
            catch (IOException)
            {
                // Non-fatal: a leftover temp file isn't worth failing the install over.
            }
        }
    }

    private static string? FindManifestDirectory(string root)
    {
        if (File.Exists(Path.Combine(root, "manifest.json")))
        {
            return root;
        }

        foreach (var dir in Directory.GetDirectories(root))
        {
            if (File.Exists(Path.Combine(dir, "manifest.json")))
            {
                return dir;
            }
        }

        return null;
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

/// <summary>Per-item install state (busy/installed) for a <see cref="RecommendedExtension"/> shown in the UI.</summary>
public partial class RecommendedExtensionViewModel : ObservableObject
{
    public RecommendedExtensionViewModel(RecommendedExtension recommended)
    {
        Recommended = recommended;
    }

    public RecommendedExtension Recommended { get; }

    public string Name => Recommended.Name;

    public string Description => Recommended.Description;

    [ObservableProperty]
    private bool isInstalling;

    [ObservableProperty]
    private bool isInstalled;

    [RelayCommand]
    private async Task Install()
    {
        if (IsInstalling || IsInstalled)
        {
            return;
        }

        IsInstalling = true;
        try
        {
            await AppServices.Extensions.InstallRecommendedAsync(Recommended);
        }
        finally
        {
            IsInstalling = false;
        }
    }
}
