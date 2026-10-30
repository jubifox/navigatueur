namespace Navigatueur.Core;

public static class UrlHelper
{
    /// <summary>Bing's query URL — the default engine, and what <see cref="Normalize(string)"/> searches with.</summary>
    public const string DefaultSearchUrlPrefix = "https://www.bing.com/search?q=";

    /// <summary>
    /// Turns free-form address-bar text into a navigable URL: passes through
    /// text that already looks like a URL, and turns everything else into a
    /// search-engine query.
    /// </summary>
    public static string Normalize(string input) => Normalize(input, DefaultSearchUrlPrefix);

    /// <param name="searchUrlPrefix">The selected engine's query URL, ending right where the escaped query goes (e.g. "https://duckduckgo.com/?q=").</param>
    public static string Normalize(string input, string searchUrlPrefix)
    {
        var trimmed = input.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (InternalPages.FromDisplayUrl(trimmed) is { } internalUrl)
        {
            return internalUrl;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme is "http" or "https" or "file" or "about"))
        {
            return trimmed;
        }

        if (LooksLikeHost(trimmed))
        {
            return "https://" + trimmed;
        }

        return searchUrlPrefix + Uri.EscapeDataString(trimmed);
    }

    private static bool LooksLikeHost(string text)
    {
        if (text.Contains(' '))
        {
            return false;
        }

        if (text.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var hostPart = text.Split('/', 2)[0].Split(':', 2)[0];
        return hostPart.Contains('.') && !hostPart.EndsWith(".");
    }

    /// <summary>The host without a leading "www.", or null if <paramref name="url"/> isn't absolute.</summary>
    public static string? TryGetDisplayHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
    }
}
