using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;
using Navigatueur.App.Views;
using Navigatueur.Core.Extensions;

namespace Navigatueur.App.Services;

/// <summary>
/// Real Chromium extensions, loaded unpacked into the engine
/// (CoreWebView2Profile.AddBrowserExtensionAsync) — the same mechanism Edge
/// uses — rather than native re-implementations of code we don't own.
///
/// Six of them are part of Navigatueur's core (BuiltInExtensionCatalog):
/// uBlock Origin, Consent-O-Matic, SponsorBlock, Decentraleyes, Violentmonkey
/// and BetterTTV ship with the app, are installed automatically once the
/// engine runs (<see cref="EnsureBuiltInsAsync"/>) and can be switched off
/// but not removed. Any other unpacked extension can still be added.
/// </summary>
public partial class ExtensionService : ObservableObject
{
    /// <summary>The engine also reports Microsoft's own components (PDF viewer, clipboard) — they aren't user extensions.</summary>
    private static readonly HashSet<string> EngineComponentIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "mhjfbmdgcfjbbpaeojofohoefgiehjai",
        "dgiklkfkllikcanfonkcabmbdfmgleag",
    };

    /// <summary>
    /// Maps an installed extension's engine Id to the unpacked folder it was
    /// loaded from — CoreWebView2BrowserExtension doesn't expose that path, so
    /// it's recorded at install time to later read manifest.json (popup,
    /// options page, icon).
    /// </summary>
    private static readonly string ManifestPathsFile = Path.Combine(BuiltInExtensions.ExtensionsDirectory, "manifest-paths.json");

    /// <summary>Every visible extension, core ones first — what the toolbar's extensions menu lists.</summary>
    public ObservableCollection<InstalledExtension> Extensions { get; } = new();

    public ObservableCollection<InstalledExtension> BuiltIns { get; } = new();

    public ObservableCollection<InstalledExtension> Others { get; } = new();

    [ObservableProperty]
    private string? lastError;

    /// <summary>Progress or trouble installing the core extensions, shown in the Extensions window; null when all is well.</summary>
    [ObservableProperty]
    private string? builtInStatus;

    private CoreWebView2Profile? _profile;
    private readonly Dictionary<string, string> _manifestPaths;
    private Task? _builtInInstall;

    public ExtensionService()
    {
        _manifestPaths = LoadManifestPaths();
    }

    /// <summary>
    /// Called whenever a tab's CoreWebView2 loads (including resuming from suspension).
    /// The profile handle only stays valid while its originating WebView2 control is
    /// alive, so this keeps _profile pointed at a recently-attached control rather than
    /// caching the very first one forever (which goes stale as soon as that tab suspends).
    /// The first time, it also makes sure every core extension is installed.
    /// </summary>
    public async void AttachProfile(CoreWebView2Profile profile)
    {
        if (ReferenceEquals(_profile, profile))
        {
            return;
        }

        _profile = profile;
        await RefreshAsync();
        _builtInInstall ??= EnsureBuiltInsAsync();
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
            var catalog = BuiltInExtensions.Catalog;
            var items = new List<InstalledExtension>();
            foreach (var extension in await profile.GetBrowserExtensionsAsync())
            {
                if (EngineComponentIds.Contains(extension.Id))
                {
                    continue;
                }

                var builtIn = catalog.FindByEngineName(extension.Name);
                var folder = ResolveFolder(extension, builtIn);
                var manifest = folder is null ? ManifestInfo.None : ReadManifestInfo(folder, extension.Id);

                // folder is only ever null for a manually-added extension installed before
                // v0.17.0 (when Navigatueur started recording where extensions come from):
                // NeedsFolderLocation lets the window ask the user to point at it once.
                items.Add(new InstalledExtension(extension, this, builtIn, manifest, needsFolderLocation: folder is null));
            }

            var builtIns = items.Where(i => i.IsBuiltIn)
                .OrderBy(i => catalog.Extensions.IndexOf(i.BuiltIn!))
                .ToList();
            var others = items.Where(i => !i.IsBuiltIn)
                .OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            Replace(BuiltIns, builtIns);
            Replace(Others, others);
            Replace(Extensions, builtIns.Concat(others));
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private static void Replace(ObservableCollection<InstalledExtension> target, IEnumerable<InstalledExtension> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    /// <summary>
    /// Installs every core extension the engine doesn't have yet — first run,
    /// a new machine, or one that went missing. A copy the user already has
    /// (even one loaded from elsewhere, like a manually added Violentmonkey) is
    /// recognized by name and left alone, so nothing is ever installed twice.
    /// </summary>
    private async Task EnsureBuiltInsAsync()
    {
        var catalog = BuiltInExtensions.Catalog;
        if (catalog.IsEmpty || ResolveLiveProfile() is not { } profile)
        {
            return;
        }

        var failed = new List<string>();
        try
        {
            var installed = await profile.GetBrowserExtensionsAsync();
            foreach (var builtIn in catalog.Extensions)
            {
                if (installed.Any(e => builtIn.Recognizes(e.Name)))
                {
                    continue;
                }

                BuiltInStatus = $"Installation de {builtIn.Name}…";
                try
                {
                    var manifestDirectory = await BuiltInExtensions.InstallAsync(builtIn, AppServices.WebView2Environment.RuntimeVersion);
                    var added = await (ResolveLiveProfile() ?? profile).AddBrowserExtensionAsync(manifestDirectory);
                    _manifestPaths[added.Id] = manifestDirectory;
                    SaveManifestPaths();
                }
                catch (Exception ex)
                {
                    failed.Add(builtIn.Name);
                    CrashLog.Write($"Installation de l'extension intégrée « {builtIn.Name} »", ex);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // The tab whose profile this was got closed meanwhile; next start tries again.
            CrashLog.Write("Extensions intégrées", ex);
        }

        BuiltInStatus = failed.Count == 0
            ? null
            : $"Impossible d'installer {string.Join(", ", failed)} pour l'instant (connexion ?). Nouvel essai au prochain démarrage.";
        await RefreshAsync();
    }

    /// <summary>Where an extension's files are: the recorded install folder, or — for a core extension — its known folder.</summary>
    private string? ResolveFolder(CoreWebView2BrowserExtension extension, BuiltInExtension? builtIn)
    {
        if (_manifestPaths.TryGetValue(extension.Id, out var folder) && Directory.Exists(folder))
        {
            return folder;
        }

        if (builtIn is null || BuiltInExtensions.FindManifestDirectory(BuiltInExtensions.FolderOf(builtIn)) is not { } known)
        {
            return null;
        }

        _manifestPaths[extension.Id] = known;
        SaveManifestPaths();
        return known;
    }

    /// <summary>
    /// Records where an already-installed extension's unpacked folder lives,
    /// for the "Localiser…" button (see InstalledExtension.NeedsFolderLocation).
    /// Doesn't touch the extension itself, just supplies the lookup.
    /// </summary>
    public async Task<bool> SetManifestFolderAsync(string extensionId, string folderPath)
    {
        if (!File.Exists(Path.Combine(folderPath, "manifest.json")))
        {
            LastError = "Ce dossier ne contient pas de manifest.json.";
            return false;
        }

        _manifestPaths[extensionId] = folderPath;
        SaveManifestPaths();
        await RefreshAsync();
        return true;
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
        if (extension.IsBuiltIn)
        {
            return; // part of Navigatueur: can be switched off, not removed (it would come back at the next start anyway)
        }

        try
        {
            await extension.Extension.RemoveAsync();
            if (_manifestPaths.Remove(extension.Id))
            {
                SaveManifestPaths();
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    // ------------------------------------------------------------ manifest

    /// <summary>What Navigatueur's UI needs from an extension's manifest.json.</summary>
    public sealed record ManifestInfo(string? PopupUrl, string? OptionsUrl, string? IconPath)
    {
        public static ManifestInfo None { get; } = new(null, null, null);
    }

    /// <summary>
    /// Popup page (MV3 "action" / MV2 "browser_action"), options page, and the
    /// icon file closest to 32 px. WebView2 has no extension toolbar of its own,
    /// so these are what the toolbar's extensions menu shows and opens.
    /// </summary>
    private static ManifestInfo ReadManifestInfo(string folder, string extensionId)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;

            string? PageUrl(string? page) => string.IsNullOrWhiteSpace(page) ? null : $"chrome-extension://{extensionId}/{page.TrimStart('/')}";

            string? popup = null;
            foreach (var key in new[] { "action", "browser_action" })
            {
                if (root.TryGetProperty(key, out var action) && action.ValueKind == JsonValueKind.Object &&
                    action.TryGetProperty("default_popup", out var page) && page.ValueKind == JsonValueKind.String)
                {
                    popup = page.GetString();
                    break;
                }
            }

            string? options = null;
            if (root.TryGetProperty("options_ui", out var optionsUi) && optionsUi.ValueKind == JsonValueKind.Object &&
                optionsUi.TryGetProperty("page", out var optionsPage) && optionsPage.ValueKind == JsonValueKind.String)
            {
                options = optionsPage.GetString();
            }
            else if (root.TryGetProperty("options_page", out var legacyOptions) && legacyOptions.ValueKind == JsonValueKind.String)
            {
                options = legacyOptions.GetString();
            }

            string? icon = null;
            if (root.TryGetProperty("icons", out var icons) && icons.ValueKind == JsonValueKind.Object)
            {
                var best = icons.EnumerateObject()
                    .Where(p => int.TryParse(p.Name, out _) && p.Value.ValueKind == JsonValueKind.String)
                    .OrderBy(p => Math.Abs(int.Parse(p.Name) - 32) + (int.Parse(p.Name) < 32 ? 1000 : 0))
                    .Select(p => Path.GetFullPath(Path.Combine(folder, p.Value.GetString()!.TrimStart('/'))))
                    .FirstOrDefault(path => path.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) && File.Exists(path));
                icon = best;
            }

            return new ManifestInfo(PageUrl(popup), PageUrl(options), icon);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return ManifestInfo.None;
        }
    }

    private static Dictionary<string, string> LoadManifestPaths()
    {
        try
        {
            return File.Exists(ManifestPathsFile)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ManifestPathsFile)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    private void SaveManifestPaths()
    {
        try
        {
            Directory.CreateDirectory(BuiltInExtensions.ExtensionsDirectory);
            File.WriteAllText(ManifestPathsFile, JsonSerializer.Serialize(_manifestPaths));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal: worst case, the popup button just won't show up after a restart.
        }
    }
}

public partial class InstalledExtension : ObservableObject
{
    private readonly ExtensionService _owner;

    public InstalledExtension(
        CoreWebView2BrowserExtension extension,
        ExtensionService owner,
        BuiltInExtension? builtIn,
        ExtensionService.ManifestInfo manifest,
        bool needsFolderLocation)
    {
        Extension = extension;
        _owner = owner;
        BuiltIn = builtIn;
        Id = extension.Id;
        Name = extension.Name;
        isEnabled = extension.IsEnabled;
        PopupUrl = manifest.PopupUrl;
        OptionsUrl = manifest.OptionsUrl;
        IconPath = manifest.IconPath;
        NeedsFolderLocation = needsFolderLocation;
    }

    internal CoreWebView2BrowserExtension Extension { get; }

    /// <summary>Set for the extensions that are part of Navigatueur's core.</summary>
    public BuiltInExtension? BuiltIn { get; }

    public bool IsBuiltIn => BuiltIn is not null;

    public bool CanRemove => !IsBuiltIn;

    public string Id { get; }

    /// <summary>The name the engine reports (localized, sometimes long — "SponsorBlock pour YouTube - …").</summary>
    public string Name { get; }

    /// <summary>Short name for menus: the catalog's for core extensions, the engine's otherwise.</summary>
    public string DisplayName => BuiltIn?.Name ?? Name;

    public string? Description => BuiltIn?.Description;

    /// <summary>chrome-extension://{id}/{popup page}, or null when the manifest declares none.</summary>
    public string? PopupUrl { get; }

    public bool HasPopup => PopupUrl is not null;

    public string? OptionsUrl { get; }

    public bool HasOptions => OptionsUrl is not null;

    public string? IconPath { get; }

    /// <summary>What clicking it in the toolbar's menu does.</summary>
    public string ActionHint => HasPopup
        ? "Ouvrir son menu"
        : HasOptions
            ? "Ouvrir ses réglages"
            : IsBuiltIn && BuiltIn!.Id == "betterttv"
                ? "Réglages dans le chat Twitch"
                : "Fonctionne en arrière-plan";

    /// <summary>
    /// True when Navigatueur doesn't know which folder this extension was loaded
    /// from (a manually-added extension from before v0.17.0), so it can't read
    /// its manifest. Drives the "Localiser…" button.
    /// </summary>
    public bool NeedsFolderLocation { get; }

    [ObservableProperty]
    private bool isEnabled;

    partial void OnIsEnabledChanged(bool value) => _ = Extension.EnableAsync(value);

    [RelayCommand]
    private async Task Remove() => await _owner.RemoveAsync(this);

    /// <summary>
    /// Opens the extension's own popup (settings, per-site switches, stats —
    /// what clicking its icon shows in other browsers) in a small window,
    /// under <paramref name="anchor"/> (a screen point, in device-independent
    /// pixels: where the window's top-right corner goes) when given.
    /// </summary>
    public void OpenPopupAt(Point? anchor)
    {
        if (PopupUrl is not null)
        {
            new ExtensionPopupWindow(DisplayName, PopupUrl, anchor).Show();
        }
    }

    [RelayCommand]
    private void OpenPopup() => OpenPopupAt(null);

    /// <summary>Opens the extension's options page in a new tab of the primary window.</summary>
    [RelayCommand]
    private void OpenOptions()
    {
        if (OptionsUrl is not null)
        {
            AppServices.TabManager.OpenTab(OptionsUrl);
            Application.Current?.MainWindow?.Activate();
        }
    }
}
