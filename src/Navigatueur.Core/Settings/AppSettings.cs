using System.Collections.Generic;

namespace Navigatueur.Core.Settings;

public sealed class AppSettings
{
    public string HomePageUrl { get; set; } = "https://navigatueur.home/index.html";

    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 800;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

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

    /// <summary>Which search engine the new-tab page's search box uses. One of the ids in SearchEngineService.Engines.</summary>
    public string SearchEngine { get; set; } = "Bing";
}
