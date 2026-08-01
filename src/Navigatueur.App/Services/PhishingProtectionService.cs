using System.IO;
using System.Net.Http;
using System.Windows.Threading;

namespace Navigatueur.App.Services;

/// <summary>
/// Blocks navigation to known-active phishing domains, sourced from the
/// community-maintained Phishing.Database project (plain one-domain-per-line
/// feed, no API key needed). Same shape as <see cref="AdBlockService"/>:
/// ships a bundled snapshot for day-one protection, then refreshes from the
/// live feed in the background — phishing domains churn much faster than ad
/// domains (most get taken down within days), so this refreshes every 6
/// hours rather than once a day.
/// </summary>
public sealed class PhishingProtectionService
{
    private const string ListUrl =
        "https://raw.githubusercontent.com/mitchellkrogza/Phishing.Database/master/phishing-domains-ACTIVE.txt";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Navigatueur", "Phishing");

    private static readonly string CacheFilePath = Path.Combine(CacheDirectory, "domains.txt");

    private static readonly string SnapshotFilePath = Path.Combine(
        AppContext.BaseDirectory, "Resources", "Phishing", "phishing-domains.txt");

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _timer;

    private volatile HashSet<string> _phishingDomains;

    public PhishingProtectionService()
    {
        _phishingDomains = LoadFromDisk(CacheFilePath) ?? LoadFromDisk(SnapshotFilePath) ?? new HashSet<string>();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _timer.Tick += async (_, _) => await RefreshIfDueAsync();
        _timer.Start();

        _ = RefreshIfDueAsync();
    }

    public bool IsPhishing(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        var set = _phishingDomains;
        var remainder = host;
        while (true)
        {
            if (set.Contains(remainder))
            {
                return true;
            }

            var dot = remainder.IndexOf('.');
            if (dot < 0)
            {
                return false;
            }

            remainder = remainder[(dot + 1)..];
        }
    }

    private async Task RefreshIfDueAsync()
    {
        if (File.Exists(CacheFilePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFilePath) < RefreshInterval)
        {
            return;
        }

        HashSet<string> downloaded;
        try
        {
            var text = await _httpClient.GetStringAsync(ListUrl);
            downloaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && trimmed[0] != '#')
                {
                    downloaded.Add(trimmed);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Offline, or the feed is unreachable right now — keep using whatever is already loaded.
            return;
        }

        if (downloaded.Count == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllLinesAsync(CacheFilePath, downloaded);
        }
        catch (IOException)
        {
            // Non-fatal: the in-memory set below is still updated for this session.
        }

        _phishingDomains = downloaded;
    }

    private static HashSet<string>? LoadFromDisk(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return new HashSet<string>(File.ReadLines(path), StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
