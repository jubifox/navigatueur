namespace Navigatueur.Core.Sites;

/// <summary>
/// Decides where the engine's own "auto dark mode" (Chromium's force-dark,
/// the one behind chrome://flags#enable-force-dark) is switched on: everywhere
/// or nowhere by default, with per-site choices on top.
///
/// A choice is stored against a host without its "www." and also covers that
/// host's subdomains, the most specific stored host winning — so turning it
/// off for "google.com" also covers "mail.google.com", unless that one has a
/// choice of its own. Only choices that differ from the default are kept:
/// flipping a site back to what the default says removes its entry.
/// </summary>
public static class ForceDarkRules
{
    /// <summary>Navigatueur's own virtual hosts: they follow the app's theme and are never darkened.</summary>
    private static readonly string[] AppHosts =
    {
        InternalPages.HostName,
        "navigatueur.warning",
        "navigatueur.userbg",
    };

    /// <summary>The host a site choice is stored under, or null for a page that can't be darkened (internal page, about:blank, file…).</summary>
    public static string? SiteKey(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        if (AppHosts.Contains(host))
        {
            return null;
        }

        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    public static bool IsEnabledFor(string? url, bool enabledByDefault, IReadOnlyDictionary<string, bool> sites) =>
        SiteKey(url) is { } key && Resolve(key, enabledByDefault, sites);

    /// <summary>Records that <paramref name="url"/>'s site should (not) be darkened. No-op for a page that can't be.</summary>
    public static void SetForSite(string? url, bool enabled, bool enabledByDefault, IDictionary<string, bool> sites)
    {
        if (SiteKey(url) is not { } key)
        {
            return;
        }

        sites.Remove(key);
        if (Resolve(key, enabledByDefault, AsReadOnly(sites)) != enabled)
        {
            sites[key] = enabled;
        }
    }

    /// <summary>Drops entries a hand-edited file could hold that no URL can ever match (blank, "www." prefixed, upper case…).</summary>
    public static void Normalize(IDictionary<string, bool> sites)
    {
        foreach (var key in sites.Keys.ToList())
        {
            var clean = key.Trim().ToLowerInvariant();
            clean = clean.StartsWith("www.", StringComparison.Ordinal) ? clean[4..] : clean;
            if (clean == key)
            {
                continue;
            }

            var value = sites[key];
            sites.Remove(key);
            if (clean.Length > 0 && !sites.ContainsKey(clean))
            {
                sites[clean] = value;
            }
        }
    }

    private static bool Resolve(string host, bool enabledByDefault, IReadOnlyDictionary<string, bool> sites)
    {
        for (var candidate = host; candidate.Length > 0; candidate = Parent(candidate))
        {
            if (sites.TryGetValue(candidate, out var choice))
            {
                return choice;
            }
        }

        return enabledByDefault;
    }

    private static string Parent(string host)
    {
        var dot = host.IndexOf('.');
        return dot < 0 ? string.Empty : host[(dot + 1)..];
    }

    private static IReadOnlyDictionary<string, bool> AsReadOnly(IDictionary<string, bool> sites) =>
        sites as IReadOnlyDictionary<string, bool> ?? new Dictionary<string, bool>(sites);
}
