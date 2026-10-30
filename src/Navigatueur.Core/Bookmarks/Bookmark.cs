namespace Navigatueur.Core.Bookmarks;

public sealed class Bookmark
{
    public string Url { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset AddedAt { get; set; }
}

/// <summary>
/// Pure list operations behind the bookmark store, so the "is this page
/// already bookmarked" rule is defined (and tested) in one place: the same
/// page reached as "https://Example.com/" or "https://example.com" is one
/// bookmark, not two.
/// </summary>
public static class BookmarkRules
{
    /// <summary>Comparison key: scheme and host lowercased (they're case-insensitive), fragment and a lone trailing slash dropped. Path and query keep their case — they're significant.</summary>
    public static string NormalizeKey(string url)
    {
        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        var path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath;
        var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port;
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}{port}{path}{uri.Query}";
    }

    public static bool AreSame(string a, string b) =>
        string.Equals(NormalizeKey(a), NormalizeKey(b), StringComparison.Ordinal);

    public static int IndexOf(IReadOnlyList<Bookmark> bookmarks, string url)
    {
        var key = NormalizeKey(url);
        for (var i = 0; i < bookmarks.Count; i++)
        {
            if (NormalizeKey(bookmarks[i].Url) == key)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Only real web pages can be bookmarked — not the new tab page, the warning page, or about:blank.</summary>
    public static bool CanBookmark(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" or "file"
        && !InternalPages.IsInternal(url)
        && !uri.Host.StartsWith("navigatueur.", StringComparison.OrdinalIgnoreCase);
}
