using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;
using Navigatueur.App.Views;

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

    /// <summary>
    /// Maps an installed extension's WebView2-assigned Id to the unpacked
    /// folder it was installed from — CoreWebView2BrowserExtension itself
    /// doesn't expose that path back, so it has to be recorded at install
    /// time (see AddExtensionAsync) to later read manifest.json and find the
    /// extension's popup (see TryReadPopupPath / RefreshAsync).
    /// </summary>
    private static readonly string ManifestPathsFile = Path.Combine(ExtensionsDirectory, "manifest-paths.json");

    public ObservableCollection<InstalledExtension> Extensions { get; } = new();

    public ObservableCollection<RecommendedExtensionViewModel> RecommendedItems { get; } = new(
        Recommended.Select(r => new RecommendedExtensionViewModel(r)));

    [ObservableProperty]
    private string? lastError;

    private CoreWebView2Profile? _profile;
    private readonly Dictionary<string, string> _manifestPaths;

    public ExtensionService()
    {
        Extensions.CollectionChanged += (_, _) => RefreshRecommendedInstalledState();
        _manifestPaths = LoadManifestPaths();
    }

    private static Dictionary<string, string> LoadManifestPaths()
    {
        try
        {
            return File.Exists(ManifestPathsFile)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ManifestPathsFile)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new();
        }
    }

    private void SaveManifestPaths()
    {
        try
        {
            Directory.CreateDirectory(ExtensionsDirectory);
            File.WriteAllText(ManifestPathsFile, JsonSerializer.Serialize(_manifestPaths));
        }
        catch (IOException)
        {
            // Non-fatal: worst case, the popup button just won't show up after a restart.
        }
    }

    /// <summary>Reads manifest.json's declared popup page (MV3 "action" or MV2 "browser_action"), if any — extensions without a toolbar popup (most ad-blockers' background-only logic) just won't offer the "Ouvrir" button.</summary>
    private static bool TryReadPopupPath(string folder, out string? popupPath)
    {
        popupPath = null;
        var manifestFile = Path.Combine(folder, "manifest.json");
        if (!File.Exists(manifestFile))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestFile));
            var root = doc.RootElement;

            if (root.TryGetProperty("action", out var action) && action.TryGetProperty("default_popup", out var mv3Popup))
            {
                popupPath = mv3Popup.GetString();
            }
            else if (root.TryGetProperty("browser_action", out var browserAction) && browserAction.TryGetProperty("default_popup", out var mv2Popup))
            {
                popupPath = mv2Popup.GetString();
            }

            return !string.IsNullOrEmpty(popupPath);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Falls back to the predictable install folder a *recommended* extension
    /// would have used (see InstallRecommendedAsync's safeName) when there's
    /// no recorded entry for it — the id-&gt;folder map was only added in
    /// v0.17.0, so anyone who installed uBlock Origin/Consent-O-Matic/etc.
    /// before that update would otherwise never get an "Ouvrir" button no
    /// matter how many times the list refreshes, since nothing ever wrote
    /// their entry. Backfills the map on a hit so this only has to run once
    /// per extension. A manually-added (non-recommended) extension installed
    /// before v0.17.0 has no predictable folder to guess, so it's still stuck
    /// without a popup button — there's no way to recover that path after the fact.
    /// </summary>
    private string? ResolveFolder(CoreWebView2BrowserExtension extension)
    {
        if (_manifestPaths.TryGetValue(extension.Id, out var folder))
        {
            return folder;
        }

        var match = Recommended.FirstOrDefault(r =>
            extension.Name.Contains(r.Name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return null;
        }

        var safeName = string.Concat(match.Name.Split(Path.GetInvalidFileNameChars()));
        var extractDir = Path.Combine(ExtensionsDirectory, safeName);
        // Some archives (e.g. Decentraleyes' GitHub "archive/refs/heads/master.zip")
        // nest manifest.json inside a "{repo}-{branch}" subfolder rather than at the
        // archive root — same lookup InstallRecommendedAsync itself uses.
        if (!Directory.Exists(extractDir) || FindManifestDirectory(extractDir) is not { } guessedFolder)
        {
            return null;
        }

        _manifestPaths[extension.Id] = guessedFolder;
        SaveManifestPaths();
        return guessedFolder;
    }

    private void RefreshRecommendedInstalledState()
    {
        foreach (var item in RecommendedItems)
        {
            item.IsInstalled = Extensions.Any(e =>
                e.Name.Contains(item.Recommended.Name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Called whenever a tab's CoreWebView2 loads (including resuming from suspension).
    /// The profile handle only stays valid while its originating WebView2 control is
    /// alive, so this keeps _profile pointed at a recently-attached control rather than
    /// caching the very first one forever (which goes stale as soon as that tab suspends).
    /// </summary>
    public async void AttachProfile(CoreWebView2Profile profile)
    {
        if (ReferenceEquals(_profile, profile))
        {
            return;
        }

        _profile = profile;
        await RefreshAsync();
    }

    /// <summary>Prefers the currently active tab's live profile handle, falling back to the last-attached one.</summary>
    private CoreWebView2Profile? ResolveLiveProfile()
    {
        var activeProfile = AppServices.TabManager.ActiveTab?.CurrentProfile;
        if (activeProfile is not null)
        {
            _profile = activeProfile;
        }

        return _profile;
    }

    public async Task RefreshAsync()
    {
        var profile = ResolveLiveProfile();
        if (profile is null)
        {
            return;
        }

        try
        {
            var list = await profile.GetBrowserExtensionsAsync();
            Extensions.Clear();
            foreach (var extension in list)
            {
                string? popupUrl = null;
                var folder = ResolveFolder(extension);
                if (folder is not null && TryReadPopupPath(folder, out var popupPath))
                {
                    popupUrl = $"chrome-extension://{extension.Id}/{popupPath!.TrimStart('/')}";
                }

                Extensions.Add(new InstalledExtension(extension, this, popupUrl));
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
        var profile = ResolveLiveProfile();
        if (profile is null)
        {
            LastError = "Aucun onglet n'est encore chargé — ouvre un onglet puis réessaie.";
            return false;
        }

        try
        {
            var extension = await profile.AddBrowserExtensionAsync(unpackedFolderPath);
            _manifestPaths[extension.Id] = unpackedFolderPath;
            SaveManifestPaths();
            await RefreshAsync();
            return true;
        }
        catch (Exception ex)
        {
            LastError = "Impossible d'installer l'extension — ouvre/active un onglet non suspendu puis réessaie. (" + ex.Message + ")";
            return false;
        }
    }

    public async Task RemoveAsync(InstalledExtension extension)
    {
        try
        {
            await extension.Extension.RemoveAsync();
            Extensions.Remove(extension);

            if (_manifestPaths.Remove(extension.Id))
            {
                SaveManifestPaths();
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    /// <summary>Downloads a recommended extension's zip, extracts it, locates its manifest.json (some zips wrap the extension in a subfolder), and installs it.</summary>
    public async Task<bool> InstallRecommendedAsync(RecommendedExtension recommended)
    {
        if (ResolveLiveProfile() is null)
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

    public InstalledExtension(CoreWebView2BrowserExtension extension, ExtensionService owner, string? popupUrl)
    {
        Extension = extension;
        _owner = owner;
        Id = extension.Id;
        Name = extension.Name;
        isEnabled = extension.IsEnabled;
        PopupUrl = popupUrl;
    }

    internal CoreWebView2BrowserExtension Extension { get; }

    public string Id { get; }

    public string Name { get; }

    /// <summary>chrome-extension://{id}/{popup page}, or null for an extension whose manifest declares no toolbar popup (or one Navigatueur doesn't have the unpacked folder path for — see ExtensionService's manifest-paths.json). Drives the "Ouvrir" button's visibility.</summary>
    public string? PopupUrl { get; }

    public bool HasPopup => PopupUrl is not null;

    [ObservableProperty]
    private bool isEnabled;

    partial void OnIsEnabledChanged(bool value) => _ = Extension.EnableAsync(value);

    [RelayCommand]
    private async Task Remove() => await _owner.RemoveAsync(this);

    /// <summary>
    /// Opens the extension's own popup UI (settings, per-site toggles, stats —
    /// whatever it normally shows when you click its toolbar icon in a real
    /// browser) in a small standalone window. WebView2 has no extension
    /// toolbar of its own to click, so this is the only way to reach that UI
    /// at all for extensions that need it (uBlock Origin's dashboard/per-site
    /// controls, Consent-O-Matic's settings, etc.).
    /// </summary>
    [RelayCommand]
    private void OpenPopup()
    {
        if (PopupUrl is null)
        {
            return;
        }

        new ExtensionPopupWindow(Name, PopupUrl).Show();
    }
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
