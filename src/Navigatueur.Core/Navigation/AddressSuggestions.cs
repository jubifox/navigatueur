namespace Navigatueur.Core.Navigation;

public enum SuggestionKind
{
    Bookmark,
    History,
}

/// <summary>A page the address bar's dropdown can offer.</summary>
public sealed record SuggestionSource(string Url, string Title, SuggestionKind Kind);

public sealed record AddressSuggestion(string Url, string Title, SuggestionKind Kind, int Score);

/// <summary>
/// Ranks bookmarks and history against what's being typed. Deliberately
/// simple and predictable: a match at the start of the host beats one at the
/// start of a word of the title, which beats a match anywhere; bookmarks
/// get a boost; each page appears once.
/// </summary>
public static class AddressSuggestions
{
    public static IReadOnlyList<AddressSuggestion> Rank(string query, IEnumerable<SuggestionSource> sources, int limit = 6)
    {
        var needle = query.Trim();
        if (needle.Length == 0 || limit <= 0)
        {
            return Array.Empty<AddressSuggestion>();
        }

        var best = new Dictionary<string, AddressSuggestion>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.Url) || InternalPages.IsInternal(source.Url))
            {
                continue;
            }

            var score = Score(needle, source);
            if (score <= 0)
            {
                continue;
            }

            var key = Bookmarks.BookmarkRules.NormalizeKey(source.Url);
            if (!best.TryGetValue(key, out var existing) || existing.Score < score)
            {
                best[key] = new AddressSuggestion(source.Url, source.Title, source.Kind, score);
            }
        }

        return best.Values
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Url.Length)
            .Take(limit)
            .ToList();
    }

    private static int Score(string needle, SuggestionSource source)
    {
        var host = UrlHelper.TryGetDisplayHost(source.Url) ?? string.Empty;
        var urlWithoutScheme = StripScheme(source.Url);
        var title = source.Title ?? string.Empty;

        int score;
        if (host.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
        {
            score = 100;
        }
        else if (urlWithoutScheme.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
        {
            score = 90;
        }
        else if (StartsAnyWord(title, needle))
        {
            score = 70;
        }
        else if (urlWithoutScheme.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            score = 50;
        }
        else if (title.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            score = 40;
        }
        else
        {
            return 0;
        }

        return source.Kind == SuggestionKind.Bookmark ? score + 15 : score;
    }

    private static string StripScheme(string url)
    {
        var index = url.IndexOf("://", StringComparison.Ordinal);
        var rest = index < 0 ? url : url[(index + 3)..];
        return rest.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? rest[4..] : rest;
    }

    private static bool StartsAnyWord(string text, string needle)
    {
        foreach (var word in text.Split(new[] { ' ', '-', '|', '·', ':', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
