namespace Navigatueur.Core;

/// <summary>
/// Navigatueur's own pages (new tab, settings, history) are plain HTML files
/// served from a WebView2 virtual host, so they load as real https URLs. The
/// address bar never shows that implementation detail: it shows a short
/// navigatueur:// address instead, and typing that address navigates back to
/// the real one.
/// </summary>
public static class InternalPages
{
    public const string HostName = "navigatueur.home";

    /// <summary>Trailing slash included, so a prefix check can't be fooled by "navigatueur.home.evil.com".</summary>
    public const string Origin = "https://" + HostName + "/";

    public const string Scheme = "navigatueur://";

    public const string NewTabUrl = Origin + "index.html";
    public const string SettingsUrl = Origin + "settings.html";
    public const string HistoryUrl = Origin + "history.html";

    private static readonly (string ShortName, string Url)[] Pages =
    {
        ("newtab", NewTabUrl),
        ("settings", SettingsUrl),
        ("history", HistoryUrl),
    };

    public static bool IsInternal(string? url) =>
        url is not null && url.StartsWith(Origin, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What the address bar shows for <paramref name="url"/>: empty for the new
    /// tab page (like every browser's own start page), "navigatueur://settings"
    /// style for the other internal pages, the URL itself otherwise. A query
    /// string or fragment on an internal page (e.g. a settings section) is kept.
    /// </summary>
    public static string ToDisplayUrl(string url)
    {
        if (!IsInternal(url))
        {
            return url;
        }

        var path = url[Origin.Length..];
        var suffixIndex = path.IndexOfAny(new[] { '?', '#' });
        var file = suffixIndex < 0 ? path : path[..suffixIndex];
        var suffix = suffixIndex < 0 ? string.Empty : path[suffixIndex..];

        foreach (var (shortName, pageUrl) in Pages)
        {
            if (string.Equals(Origin + file, pageUrl, StringComparison.OrdinalIgnoreCase))
            {
                return shortName == "newtab" && suffix.Length == 0 ? string.Empty : Scheme + shortName + suffix;
            }
        }

        return url;
    }

    /// <summary>Maps typed "navigatueur://settings" (any case, with or without a trailing slash) to the page's real URL; null when <paramref name="input"/> isn't such an address.</summary>
    public static string? FromDisplayUrl(string input)
    {
        var trimmed = input.Trim();
        if (!trimmed.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = trimmed[Scheme.Length..];
        var suffixIndex = rest.IndexOfAny(new[] { '?', '#' });
        var name = (suffixIndex < 0 ? rest : rest[..suffixIndex]).TrimEnd('/');
        var suffix = suffixIndex < 0 ? string.Empty : rest[suffixIndex..];

        foreach (var (shortName, pageUrl) in Pages)
        {
            if (string.Equals(name, shortName, StringComparison.OrdinalIgnoreCase))
            {
                return pageUrl + suffix;
            }
        }

        return NewTabUrl;
    }
}
