using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Navigatueur.App.Services;

public sealed class WebView2EnvironmentService
{
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

    public Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        return _environmentTask ??= CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
    }
}
