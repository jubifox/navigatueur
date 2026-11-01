using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Navigatueur.App.Services;

/// <summary>
/// Creates the Chromium engine every tab runs on.
///
/// Navigatueur ships its own copy of the engine: a WebView2 "Fixed Version"
/// runtime unpacked into <see cref="BundledRuntimeFolder"/> by the release
/// build (packaging/windows/build-release.ps1). With it, the browser no
/// longer depends on the separately installed "Microsoft Edge WebView2
/// Runtime" — one application, one install. Development builds (and any
/// install without that folder) fall back to the system runtime.
///
/// Chromium still runs its usual helper processes (browser, renderers, GPU,
/// network): that split is its security sandbox, not a second application.
/// </summary>
public sealed class WebView2EnvironmentService
{
    /// <summary>Folder, next to Navigatueur.exe, holding the bundled engine (msedgewebview2.exe and its files).</summary>
    public static string BundledRuntimeFolder { get; } = Path.Combine(AppContext.BaseDirectory, "WebView2Runtime");

    /// <summary>
    /// Unique per-app profile folder. Also used by <see cref="MemoryMonitorService"/>
    /// to recognize which "msedgewebview2.exe" processes on the machine belong to
    /// this app (vs. some other WebView2-based app that happens to be running).
    /// </summary>
    public static string UserDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Navigatueur", "WebView2");

    private readonly string _userDataFolder;
    private Task<CoreWebView2Environment>? _environmentTask;

    public WebView2EnvironmentService()
        : this(UserDataFolder)
    {
    }

    /// <summary>Used for the private-browsing profile: a separate, throwaway user-data folder so cookies/history/cache never touch the normal profile.</summary>
    public WebView2EnvironmentService(string userDataFolder)
    {
        _userDataFolder = userDataFolder;
    }

    public static bool UsesBundledRuntime => File.Exists(Path.Combine(BundledRuntimeFolder, "msedgewebview2.exe"));

    private static string? BrowserExecutableFolder => UsesBundledRuntime ? BundledRuntimeFolder : null;

    /// <summary>Engine version in use, or null when no runtime can be found at all.</summary>
    public string? RuntimeVersion
    {
        get
        {
            try
            {
                return CoreWebView2Environment.GetAvailableBrowserVersionString(BrowserExecutableFolder);
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or FileNotFoundException or ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Work that must be over before the engine starts — it reads extension
    /// files when it starts, so updating the built-in extensions' files has to
    /// happen first (see BuiltInExtensions.UpgradeInstalledCopies, started by App).
    /// </summary>
    public static Task StartupWork { get; set; } = Task.CompletedTask;

    public Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        // A failed attempt (no runtime yet) isn't cached: installing the runtime then opening a tab works without a restart.
        if (_environmentTask is { IsFaulted: true } or { IsCanceled: true })
        {
            _environmentTask = null;
        }

        return _environmentTask ??= CreateEnvironmentAsync();
    }

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        try
        {
            await StartupWork;
        }
        catch (Exception ex)
        {
            // Never keep the browser from starting over it.
            CrashLog.Write("Préparation des extensions intégrées", ex);
        }

        return await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: BrowserExecutableFolder,
            userDataFolder: _userDataFolder,
            options: new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true });
    }
}
