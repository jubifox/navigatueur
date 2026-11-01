namespace Navigatueur.App.Shell;

/// <summary>
/// Actions the panels and internal pages ask their window to perform. One
/// entry point keyed by command id keeps menus, keyboard shortcuts and the
/// Settings page all going through the same code path.
/// </summary>
public interface IShellCommands
{
    /// <summary>Runs a <see cref="Core.Shortcuts.ShortcutCommands"/> or <see cref="ShellCommandIds"/> command.</summary>
    void Execute(string commandId);

    void NavigateActiveTab(string input);

    /// <summary>Opens <paramref name="url"/> in the current tab, or in a new tab behind the current one.</summary>
    void OpenUrl(string url, bool inBackgroundTab);

    /// <summary>Gives keyboard focus back to the page.</summary>
    void FocusPage();

    void StartWindowDrag();
}

/// <summary>Window-level commands that have no keyboard shortcut of their own.</summary>
public static class ShellCommandIds
{
    public const string Stop = "page.stop";
    public const string SaveAs = "page.saveAs";
    public const string Find = "page.find";
    public const string Translate = "page.translate";
    public const string PictureInPicture = "page.pip";
    public const string ZoomIn = "zoom.in";
    public const string ZoomOut = "zoom.out";
    public const string ZoomReset = "zoom.reset";
    public const string ToggleMusic = "music.toggle";
    public const string OpenExtensions = "extensions.open";
    public const string InstallUpdate = "update.install";
    public const string Minimize = "window.minimize";
    public const string ToggleMaximize = "window.maximize";
    public const string CloseWindow = "window.close";
    public const string ToggleSidebarDock = "sidebar.toggleDock";
    public const string HideToolbar = "toolbar.hide";
    public const string HideTabs = "tabs.hide";
}
