using Navigatueur.Core.Settings;

namespace Navigatueur.App.Services;

/// <summary>
/// Minimal composition root: this app is a single-user desktop shell with a
/// handful of long-lived singletons, so a small static holder is enough —
/// no DI container needed.
/// </summary>
public static class AppServices
{
    public static SettingsService Settings { get; } = new();

    public static WebView2EnvironmentService WebView2Environment { get; } = new();

    public static MemoryMonitorService MemoryMonitor { get; } = new();

    public static MusicControllerService MusicController { get; } = new();

    public static AdBlockService AdBlock { get; } = new();

    public static PhishingProtectionService PhishingProtection { get; } = new();

    public static PermissionStoreService PermissionStore { get; } = new();

    public static AppSettings CurrentSettings { get; } = Settings.Load();

    public static ThemeService Theme { get; } = new(CurrentSettings);

    public static SearchEngineService SearchEngine { get; } = new(CurrentSettings);

    public static UpdateService Update { get; } = new();

    public static DownloadManagerService Downloads { get; } = new();

    public static ExtensionService Extensions { get; } = new();

    public static HistoryService History { get; } = new();

    public static TabManagerService TabManager { get; } = new(CurrentSettings);

    /// <summary>
    /// Set by MainWindow's constructor to its own "really quit" logic (bypassing
    /// the tray-background guard). Callers that need a guaranteed real exit —
    /// e.g. UpdateService before handing off to the new installer — must use
    /// this instead of Application.Current.Shutdown(): WPF cancels shutdown
    /// entirely if any window's Closing handler sets e.Cancel, which ours does
    /// whenever the music overlay is open, silently leaving the app running
    /// while a second installer launches against it.
    /// </summary>
    public static Action? RequestForceQuit { get; set; }
}
