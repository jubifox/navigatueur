using System.Collections.Generic;

namespace Navigatueur.Core.Settings;

public sealed class AppSettings
{
    public string HomePageUrl { get; set; } = InternalPages.NewTabUrl;

    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 800;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public bool WasMaximized { get; set; }

    /// <summary>
    /// Tab groups from the previous session, restored before <see cref="Tabs"/>
    /// so tabs can be reassigned to them by <see cref="SessionTabState.GroupId"/>.
    /// </summary>
    public List<SessionGroupState> Groups { get; set; } = new();

    /// <summary>Open tabs from the previous session, reopened on next launch.</summary>
    public List<SessionTabState> Tabs { get; set; } = new();

    /// <summary>Index into <see cref="Tabs"/> of the tab that was active on shutdown.</summary>
    public int ActiveTabIndex { get; set; } = -1;

    /// <summary>Groups explicitly saved by the user, reopenable on demand — independent of the current session.</summary>
    public List<SavedTabGroup> SavedGroups { get; set; } = new();

    /// <summary>"Light" or "Dark".</summary>
    public string ThemeMode { get; set; } = "Dark";

    public string AccentColorHex { get; set; } = "#4C8DFF";

    /// <summary>Local copy under the app's own data folder — never the original user-picked path, which could move or be deleted.</summary>
    public string? ChromeBackgroundImagePath { get; set; }

    /// <summary>Local copy under the app's own data folder — never the original user-picked path, which could move or be deleted.</summary>
    public string? NewTabBackgroundImagePath { get; set; }

    /// <summary>Which search engine both the address bar and the new-tab page's search box use. One of the ids in SearchEngineService.Engines.</summary>
    public string SearchEngine { get; set; } = "Bing";

    /// <summary>Whether the accent-colored cursor trail effect is shown, both in the app's own panels and drawn inside web pages.</summary>
    public bool IsCursorTrailEnabled { get; set; } = true;

    /// <summary>Toolbar/tab-column display mode, edge-reveal timings and panel styling.</summary>
    public InterfaceSettings Interface { get; set; } = new();

    /// <summary>"RestoreSession" (reopen the previous session's tabs) or "HomePage" (start on a single home page tab).</summary>
    public string StartupBehavior { get; set; } = StartupBehaviors.RestoreSession;

    public bool IsAdBlockEnabled { get; set; } = true;

    public bool IsPhishingProtectionEnabled { get; set; } = true;

    /// <summary>Background tabs idle for this long are unloaded to free memory. 0 disables idle suspension.</summary>
    public int TabSuspendMinutes { get; set; } = 15;

    /// <summary>How many non-pinned tabs may keep a live web engine at once before the least recently used one is unloaded.</summary>
    public int MaxLiveTabs { get; set; } = 3;

    /// <summary>
    /// User-chosen keyboard shortcuts: command id (see Shortcuts.ShortcutCommands)
    /// to gesture text such as "Ctrl+Shift+T". An empty string disables that
    /// command's shortcut. Commands absent from this map keep their defaults.
    /// </summary>
    public Dictionary<string, string> ShortcutOverrides { get; set; } = new();
}

public static class StartupBehaviors
{
    public const string RestoreSession = "RestoreSession";
    public const string HomePage = "HomePage";
}
