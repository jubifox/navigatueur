using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Navigatueur.App.Shell;
using Navigatueur.Core;
using Navigatueur.Core.Settings;
using Navigatueur.Core.Shortcuts;

namespace Navigatueur.App.Services;

/// <summary>
/// The message channel between Navigatueur's own pages (new tab, settings,
/// history — see InternalPages) and the app.
///
/// Every page in WebView2 can call chrome.webview.postMessage, websites
/// included, so the caller must only route messages here when the sending
/// document is one of ours (<see cref="InternalPages.IsInternal"/> on the
/// event's Source). Nothing in here trusts the payload beyond that: values
/// are type-checked, and settings go through the same validating setters as
/// everywhere else.
///
/// Protocol: the page sends { type: "nv:…", … }; the app answers with
/// { type: "nv:state", … } (everything the pages display) or a few
/// purpose-specific replies. Any change to the underlying settings raises
/// <see cref="StateInvalidated"/> so every open internal page refreshes.
/// </summary>
public static class InternalPageBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    static InternalPageBridge()
    {
        AppServices.Theme.AppearanceChanged += (_, _) => Invalidate();
        AppServices.Interface.Changed += (_, _) => Invalidate();
        AppServices.Bookmarks.Changed += (_, _) => Invalidate();
        AppServices.Shortcuts.Changed += (_, _) => Invalidate();
        AppServices.SearchEngine.PropertyChanged += (_, _) => Invalidate();
        AppServices.Update.PropertyChanged += (_, _) => Invalidate();
    }

    /// <summary>Something the pages display changed; open internal pages should ask <see cref="BuildState"/> again.</summary>
    public static event EventHandler? StateInvalidated;

    public static void Invalidate() => StateInvalidated?.Invoke(null, EventArgs.Empty);

    /// <summary>Handles one message from an internal page. <paramref name="shell"/> is the window hosting it (for dialogs and window-level commands).</summary>
    public static void Handle(CoreWebView2 core, string json, IShellCommands? shell, Window? owner, bool isPrivate)
    {
        JsonObject message;
        try
        {
            message = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return;
        }

        var type = GetString(message, "type");
        switch (type)
        {
            case "nv:hello":
                Post(core, BuildState(isPrivate));
                break;

            case "nv:set":
                ApplySetting(GetString(message, "key"), message["value"]);
                break;

            case "nv:action":
                RunAction(GetString(message, "name"), core, shell, owner, isPrivate);
                break;

            case "nv:open":
                if (GetString(message, "url") is { Length: > 0 } url && IsNavigable(url))
                {
                    shell?.OpenUrl(url, GetBool(message, "background") ?? false);
                }

                break;

            case "nv:navigate":
                // The new tab page's search box: same address-or-search rules as the address bar.
                if (GetString(message, "input") is { Length: > 0 } input &&
                    UrlHelper.Normalize(input, AppServices.SearchEngine.CurrentEngine.QueryUrlPrefix) is { Length: > 0 } target)
                {
                    core.Navigate(target);
                }

                break;

            case "nv:history.query":
                Post(core, BuildHistory(GetString(message, "query"), GetInt(message, "limit") ?? 300));
                break;

            case "nv:history.delete":
                if (GetString(message, "url") is { } historyUrl &&
                    DateTimeOffset.TryParse(GetString(message, "visitedAt"), out var visitedAt))
                {
                    AppServices.History.Remove(historyUrl, visitedAt);
                }

                break;

            case "nv:bookmark.remove":
                if (GetString(message, "url") is { } bookmarkUrl)
                {
                    AppServices.Bookmarks.Remove(bookmarkUrl);
                }

                break;

            case "nv:shortcut.capture":
                if (GetBool(message, "active") == true)
                {
                    AppServices.Shortcuts.BeginCapture();
                }
                else
                {
                    AppServices.Shortcuts.EndCapture();
                }

                break;

            case "nv:shortcut.set":
                var id = GetString(message, "id") ?? string.Empty;
                var error = AppServices.Shortcuts.TrySetOverride(id, GetString(message, "gesture") ?? string.Empty);
                Post(core, new { type = "nv:shortcut.result", id, ok = error is null, error });
                break;

            case "nv:shortcut.reset":
                AppServices.Shortcuts.ResetOverride(GetString(message, "id") ?? string.Empty);
                break;

            case "nv:shortcut.disable":
                AppServices.Shortcuts.DisableShortcut(GetString(message, "id") ?? string.Empty);
                break;
        }
    }

    public static void PostState(CoreWebView2 core, bool isPrivate) => Post(core, BuildState(isPrivate));

    // -------------------------------------------------------------- state

    public static object BuildState(bool isPrivate)
    {
        var theme = AppServices.Theme;
        var ui = AppServices.Interface.Current;
        var settings = AppServices.CurrentSettings;
        var shortcuts = AppServices.Shortcuts.Map;

        return new
        {
            type = "nv:state",
            isPrivate,
            theme = new
            {
                mode = theme.IsDark ? "Dark" : "Light",
                accent = isPrivate ? ThemeService.PrivateAccentHex : theme.AccentColorHex,
                newTabBackground = BackgroundUrl(theme.NewTabBackgroundImagePath),
                hasChromeBackground = theme.ChromeBackgroundImagePath is not null,
                cursorTrail = theme.IsCursorTrailEnabled,
            },
            @interface = new
            {
                mode = ui.ModeValue.ToString(),
                toolbarAutoHide = ui.ResolveToolbarBehavior() == PanelBehavior.AutoHide,
                sidebarAutoHide = ui.ResolveSidebarBehavior() == PanelBehavior.AutoHide,
                sidebarSide = ui.SidebarSideValue.ToString(),
                sidebarWidth = ui.SidebarWidth,
                edgeReveal = ui.IsEdgeRevealEnabled,
                revealDelay = ui.RevealDelayMilliseconds,
                hideDelay = ui.HideDelayMilliseconds,
                triggerSize = ui.EdgeTriggerSize,
                sidebarTriggerSize = ui.SidebarEdgeTriggerSize,
                animations = ui.AreAnimationsEnabled,
                panelEffects = ui.ArePanelEffectsEnabled,
            },
            navigation = new
            {
                startup = settings.StartupBehavior,
                homePage = settings.HomePageUrl == InternalPages.NewTabUrl ? string.Empty : settings.HomePageUrl,
                searchEngine = AppServices.SearchEngine.CurrentEngine.Id,
                searchAction = AppServices.SearchEngine.CurrentEngine.ActionUrl,
                engines = SearchEngineService.Engines.Select(e => new { id = e.Id, name = e.DisplayName }),
                adBlock = settings.IsAdBlockEnabled,
                phishing = settings.IsPhishingProtectionEnabled,
                suspendMinutes = settings.TabSuspendMinutes,
                maxLiveTabs = settings.MaxLiveTabs,
                rememberedPermissions = AppServices.PermissionStore.Count,
            },
            shortcuts = ShortcutCatalog.All
                .Where(d => d.IsCustomizable)
                .Select(d => new
                {
                    id = d.Id,
                    label = d.Label,
                    category = d.Category,
                    gestures = shortcuts.GetGestures(d.Id).Select(g => g.ToDisplayString()),
                    isDefault = !settings.ShortcutOverrides.ContainsKey(d.Id),
                }),
            fixedShortcuts = new[]
            {
                new { label = "Aller à l'onglet 1 à 8", gesture = "Ctrl + 1…8" },
                new { label = "Aller au dernier onglet", gesture = "Ctrl + 9" },
                new { label = "Rechercher dans la page", gesture = "Ctrl + F" },
                new { label = "Zoom", gesture = "Ctrl + molette, Ctrl + +/−/0" },
                new { label = "Quitter la barre d'adresse", gesture = "Échap" },
            },
            bookmarks = AppServices.Bookmarks.Bookmarks.Take(12).Select(b => new { url = b.Url, title = b.Title, host = UrlHelper.TryGetDisplayHost(b.Url) ?? b.Url }),
            about = new
            {
                version = AppServices.Update.CurrentVersion.ToString(3),
                updateAvailable = AppServices.Update.IsUpdateAvailable,
                latestVersion = AppServices.Update.LatestVersion,
                engineVersion = AppServices.WebView2Environment.RuntimeVersion,
                engineBundled = WebView2EnvironmentService.UsesBundledRuntime,
            },
        };
    }

    private static object BuildHistory(string? query, int limit) => new
    {
        type = "nv:history",
        query,
        entries = AppServices.History.Search(query, Math.Clamp(limit, 1, 2000)).Select(e => new
        {
            url = e.Url,
            title = e.Title,
            host = UrlHelper.TryGetDisplayHost(e.Url) ?? e.Url,
            visitedAt = e.VisitedAt.ToString("o"),
        }),
    };

    /// <summary>The user's new-tab image through its virtual host, with a version stamp so replacing the image shows the new one.</summary>
    private static string? BackgroundUrl(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        var stamp = File.GetLastWriteTimeUtc(path).Ticks;
        return $"https://navigatueur.userbg/{Uri.EscapeDataString(Path.GetFileName(path))}?v={stamp}";
    }

    // ----------------------------------------------------------- settings

    private static void ApplySetting(string? key, JsonNode? value)
    {
        var settings = AppServices.CurrentSettings;
        switch (key)
        {
            case "theme.mode" when AsString(value) is { } mode:
                AppServices.Theme.SetThemeMode(mode);
                break;
            case "theme.accent" when AsString(value) is { } accent:
                AppServices.Theme.SetAccentColor(accent);
                break;
            case "appearance.cursorTrail" when AsBool(value) is { } trail:
                AppServices.Theme.SetCursorTrailEnabled(trail);
                break;

            case "interface.mode" when AsString(value) is { } mode:
                AppServices.Interface.Update(s => s.Mode = mode);
                break;
            case "interface.toolbarAutoHide" when AsBool(value) is { } auto:
                AppServices.Interface.Update(s => s.ApplyBehaviors(auto ? PanelBehavior.AutoHide : PanelBehavior.Docked, s.ResolveSidebarBehavior()));
                break;
            case "interface.sidebarAutoHide" when AsBool(value) is { } auto:
                AppServices.Interface.Update(s => s.ApplyBehaviors(s.ResolveToolbarBehavior(), auto ? PanelBehavior.AutoHide : PanelBehavior.Docked));
                break;
            case "interface.sidebarSide" when AsString(value) is { } side:
                AppServices.Interface.Update(s => s.SidebarSide = side);
                break;
            case "interface.sidebarWidth" when AsDouble(value) is { } width:
                AppServices.Interface.Update(s => s.SidebarWidth = width);
                break;
            case "interface.edgeReveal" when AsBool(value) is { } enabled:
                AppServices.Interface.Update(s => s.IsEdgeRevealEnabled = enabled);
                break;
            case "interface.revealDelay" when AsDouble(value) is { } reveal:
                AppServices.Interface.Update(s => s.RevealDelayMilliseconds = (int)reveal);
                break;
            case "interface.hideDelay" when AsDouble(value) is { } hide:
                AppServices.Interface.Update(s => s.HideDelayMilliseconds = (int)hide);
                break;
            case "interface.triggerSize" when AsDouble(value) is { } size:
                AppServices.Interface.Update(s => s.EdgeTriggerSize = (int)size);
                break;
            case "interface.sidebarTriggerSize" when AsDouble(value) is { } sideSize:
                AppServices.Interface.Update(s => s.SidebarEdgeTriggerSize = (int)sideSize);
                break;
            case "interface.animations" when AsBool(value) is { } animations:
                AppServices.Interface.Update(s => s.AreAnimationsEnabled = animations);
                break;
            case "interface.panelEffects" when AsBool(value) is { } effects:
                AppServices.Interface.Update(s => s.ArePanelEffectsEnabled = effects);
                break;

            case "startup.behavior" when AsString(value) is StartupBehaviors.RestoreSession or StartupBehaviors.HomePage:
                settings.StartupBehavior = AsString(value)!;
                SaveAndInvalidate();
                break;
            case "startup.homePage" when AsString(value) is { } home:
                settings.HomePageUrl = string.IsNullOrWhiteSpace(home)
                    ? InternalPages.NewTabUrl
                    : UrlHelper.Normalize(home, AppServices.SearchEngine.CurrentEngine.QueryUrlPrefix);
                SaveAndInvalidate();
                break;
            case "search.engine" when AsString(value) is { } engine && SearchEngineService.Engines.Any(e => e.Id == engine):
                AppServices.SearchEngine.SetEngine(engine);
                break;
            case "privacy.adBlock" when AsBool(value) is { } adBlock:
                settings.IsAdBlockEnabled = adBlock;
                SaveAndInvalidate();
                break;
            case "privacy.phishing" when AsBool(value) is { } phishing:
                settings.IsPhishingProtectionEnabled = phishing;
                SaveAndInvalidate();
                break;
            case "perf.suspendMinutes" when AsDouble(value) is { } minutes:
                settings.TabSuspendMinutes = Math.Clamp((int)minutes, 0, 24 * 60);
                SaveAndInvalidate();
                break;
            case "perf.maxLiveTabs" when AsDouble(value) is { } tabs:
                settings.MaxLiveTabs = Math.Clamp((int)tabs, 1, 20);
                SaveAndInvalidate();
                break;
        }
    }

    private static void SaveAndInvalidate()
    {
        AppServices.Settings.Save(AppServices.CurrentSettings);
        Invalidate();
    }

    // ------------------------------------------------------------ actions

    private const string ImageFilter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp";

    private static async void RunAction(string? name, CoreWebView2 core, IShellCommands? shell, Window? owner, bool isPrivate)
    {
        switch (name)
        {
            case "background.newtab.choose":
                if (PickImage(owner) is { } newTabImage)
                {
                    AppServices.Theme.SetNewTabBackgroundImage(newTabImage);
                }

                break;
            case "background.newtab.reset":
                AppServices.Theme.ClearNewTabBackgroundImage();
                break;
            case "background.chrome.choose":
                if (PickImage(owner) is { } chromeImage)
                {
                    AppServices.Theme.SetChromeBackgroundImage(chromeImage);
                }

                break;
            case "background.chrome.reset":
                AppServices.Theme.ClearChromeBackgroundImage();
                break;
            case "update.check":
                await AppServices.Update.CheckForUpdateAsync();
                Post(core, new { type = "nv:update.checked", available = AppServices.Update.IsUpdateAvailable });
                break;
            case "update.install":
                shell?.Execute(ShellCommandIds.InstallUpdate);
                break;
            case "history.clear":
                AppServices.History.Clear();
                Post(core, BuildHistory(null, 300));
                break;
            case "permissions.clear":
                AppServices.PermissionStore.Clear();
                Invalidate();
                break;
            case "extensions.open":
                shell?.Execute(ShellCommandIds.OpenExtensions);
                break;
            case "codec.hevc.store":
                OpenHevcCodecInStore();
                break;
            case "shortcuts.resetAll":
                AppServices.Shortcuts.ResetAll();
                break;
            case "data.openFolder":
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Navigatueur");
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
                break;
        }
    }

    /// <summary>Microsoft's "HEVC Video Extensions" Store page: the Store app, or its web page when the app can't be opened.</summary>
    private static void OpenHevcCodecInStore()
    {
        const string productId = "9NMZLZ57R3T7";
        try
        {
            Process.Start(new ProcessStartInfo($"ms-windows-store://pdp/?ProductId={productId}") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppServices.TabManager.OpenTab($"https://apps.microsoft.com/detail/{productId}");
        }
    }

    private static string? PickImage(Window? owner)
    {
        var dialog = new OpenFileDialog { Filter = ImageFilter };
        return (owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true ? dialog.FileName : null;
    }

    // ------------------------------------------------------------ helpers

    /// <summary>Internal pages may only send the user to real web pages (or another internal page) — never javascript:, file: or data: URLs.</summary>
    private static bool IsNavigable(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static void Post(CoreWebView2 core, object payload)
    {
        try
        {
            core.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            // The tab was closed or suspended between the request and the reply.
        }
    }

    private static string? GetString(JsonObject message, string key) => AsString(message[key]);

    private static bool? GetBool(JsonObject message, string key) => AsBool(message[key]);

    private static int? GetInt(JsonObject message, string key) => AsDouble(message[key]) is { } d ? (int)d : null;

    private static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool? AsBool(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    private static double? AsDouble(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : null;
}
