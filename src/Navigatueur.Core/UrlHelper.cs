namespace Navigatueur.Core;

public static class UrlHelper
{
    private const string SearchEngineUrlPrefix = "https://www.bing.com/search?q=";

    /// <summary>
    /// Turns free-form address-bar text into a navigable URL: passes through
    /// text that already looks like a URL, and turns everything else into a
    /// search-engine query.
    /// </summary>
    public static string Normalize(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
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

        return SearchEngineUrlPrefix + Uri.EscapeDataString(trimmed);
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
}
