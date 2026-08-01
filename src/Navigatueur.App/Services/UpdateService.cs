using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Navigatueur.App.Services;

/// <summary>
/// Checks GitHub Releases for a newer build than the one currently running.
/// The toolbar's update button stays hidden until <see cref="IsUpdateAvailable"/>
/// flips true; clicking it downloads the new installer and hands off to it.
///
/// </summary>
public partial class UpdateService : ObservableObject
{
    private const string GitHubOwner = "jubifox";
    private const string GitHubRepo = "navigatueur";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly HttpClient _httpClient;
    private readonly DispatcherTimer _timer;

    [ObservableProperty]
    private bool isUpdateAvailable;

    [ObservableProperty]
    private string? latestVersion;

    [ObservableProperty]
    private string? downloadUrl;

    [ObservableProperty]
    private bool isDownloading;

    public Version CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public UpdateService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Navigatueur-UpdateChecker");

        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += async (_, _) => await CheckForUpdateAsync();
        _timer.Start();

        _ = CheckForUpdateAsync();
    }

    public async Task CheckForUpdateAsync()
    {
        try
        {
            var json = await _httpClient.GetStringAsync(
                $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest");

            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            if (tag is null)
            {
                return;
            }

            var versionText = tag.TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out var remoteVersion) || remoteVersion <= CurrentVersion)
            {
                IsUpdateAvailable = false;
                return;
            }

            string? assetUrl = null;
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    if (name is not null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        asset.TryGetProperty("browser_download_url", out var urlProp))
                    {
                        assetUrl = urlProp.GetString();
                        break;
                    }
                }
            }

            if (assetUrl is null)
            {
                return;
            }

            LatestVersion = versionText;
            DownloadUrl = assetUrl;
            IsUpdateAvailable = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Offline, repo not published yet, rate-limited, ... — keep whatever was already known.
        }
    }

    /// <summary>Downloads the new installer to Temp, launches it, then closes this instance so the installer can replace its files.</summary>
    public async Task DownloadAndInstallAsync()
    {
        if (DownloadUrl is null || IsDownloading)
        {
            return;
        }

        IsDownloading = true;
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"NavigatueurSetup-{LatestVersion}.exe");

            var responseStream = await _httpClient.GetStreamAsync(DownloadUrl);
            await using (responseStream)
            {
                var fileStream = File.Create(tempPath);
                await using (fileStream)
                {
                    await responseStream.CopyToAsync(fileStream);
                }
            }

            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            IsDownloading = false;
        }
    }
}
