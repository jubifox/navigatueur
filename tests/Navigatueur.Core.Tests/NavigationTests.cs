using Navigatueur.Core.Bookmarks;
using Navigatueur.Core.Navigation;
using Navigatueur.Core.Tabs;
using Xunit;

namespace Navigatueur.Core.Tests;

public class InternalPagesTests
{
    [Theory]
    [InlineData("https://navigatueur.home/index.html", "")]
    [InlineData("https://navigatueur.home/settings.html", "navigatueur://settings")]
    [InlineData("https://navigatueur.home/settings.html#interface", "navigatueur://settings#interface")]
    [InlineData("https://NAVIGATUEUR.HOME/history.html", "navigatueur://history")]
    [InlineData("https://example.com/", "https://example.com/")]
    [InlineData("https://navigatueur.home.evil.com/settings.html", "https://navigatueur.home.evil.com/settings.html")]
    public void ToDisplayUrl(string url, string expected)
    {
        Assert.Equal(expected, InternalPages.ToDisplayUrl(url));
    }

    [Theory]
    [InlineData("navigatueur://settings", "https://navigatueur.home/settings.html")]
    [InlineData("NAVIGATUEUR://History/", "https://navigatueur.home/history.html")]
    [InlineData("navigatueur://settings#raccourcis", "https://navigatueur.home/settings.html#raccourcis")]
    [InlineData("navigatueur://unknown", "https://navigatueur.home/index.html")]
    public void FromDisplayUrl(string input, string expected)
    {
        Assert.Equal(expected, InternalPages.FromDisplayUrl(input));
    }

    [Fact]
    public void DisplayUrl_RoundTrips()
    {
        foreach (var url in new[] { InternalPages.SettingsUrl, InternalPages.HistoryUrl })
        {
            Assert.Equal(url, InternalPages.FromDisplayUrl(InternalPages.ToDisplayUrl(url)));
        }
    }

    [Fact]
    public void UrlHelper_MapsInternalAddresses()
    {
        Assert.Equal(InternalPages.SettingsUrl, UrlHelper.Normalize("navigatueur://settings"));
    }

    [Fact]
    public void UrlHelper_UsesSelectedSearchEngine()
    {
        Assert.Equal(
            "https://duckduckgo.com/?q=chat%20noir",
            UrlHelper.Normalize("chat noir", "https://duckduckgo.com/?q="));
    }
}

public class BookmarkRulesTests
{
    [Theory]
    [InlineData("https://Example.com/", "https://example.com")]
    [InlineData("https://example.com/page#section", "https://example.com/page")]
    [InlineData("HTTPS://EXAMPLE.COM:443/a", "https://example.com/a")]
    public void AreSame_IgnoresCosmeticDifferences(string a, string b)
    {
        Assert.True(BookmarkRules.AreSame(a, b));
    }

    [Theory]
    [InlineData("https://example.com/Page", "https://example.com/page")]
    [InlineData("https://example.com/?q=1", "https://example.com/?q=2")]
    [InlineData("http://example.com", "https://example.com")]
    public void AreSame_KeepsSignificantDifferences(string a, string b)
    {
        Assert.False(BookmarkRules.AreSame(a, b));
    }

    [Fact]
    public void IndexOf_FindsEquivalentUrl()
    {
        var list = new List<Bookmark>
        {
            new() { Url = "https://a.example/" },
            new() { Url = "https://b.example/docs" },
        };

        Assert.Equal(1, BookmarkRules.IndexOf(list, "https://B.example/docs#top"));
        Assert.Equal(-1, BookmarkRules.IndexOf(list, "https://c.example/"));
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("https://navigatueur.home/index.html", false)]
    [InlineData("https://navigatueur.warning/index.html", false)]
    [InlineData("about:blank", false)]
    [InlineData("", false)]
    public void CanBookmark(string url, bool expected)
    {
        Assert.Equal(expected, BookmarkRules.CanBookmark(url));
    }
}

public class ClosedTabHistoryTests
{
    [Fact]
    public void Pop_ReturnsMostRecentFirst()
    {
        var history = new ClosedTabHistory();
        history.Push(new ClosedTabRecord("https://a", "A", 0, null, false));
        history.Push(new ClosedTabRecord("https://b", "B", 1, null, false));

        Assert.True(history.TryPop(out var first));
        Assert.Equal("https://b", first!.Url);
        Assert.True(history.TryPop(out var second));
        Assert.Equal("https://a", second!.Url);
        Assert.False(history.TryPop(out _));
    }

    [Fact]
    public void Capacity_DropsOldest()
    {
        var history = new ClosedTabHistory(capacity: 2);
        history.Push(new ClosedTabRecord("https://1", "", 0, null, false));
        history.Push(new ClosedTabRecord("https://2", "", 0, null, false));
        history.Push(new ClosedTabRecord("https://3", "", 0, null, false));

        Assert.Equal(2, history.Count);
        history.TryPop(out _);
        history.TryPop(out var last);
        Assert.Equal("https://2", last!.Url);
    }

    [Fact]
    public void EmptyUrl_IsIgnored()
    {
        var history = new ClosedTabHistory();
        history.Push(new ClosedTabRecord(" ", "", 0, null, false));

        Assert.Equal(0, history.Count);
    }
}

public class AddressSuggestionsTests
{
    private static readonly SuggestionSource[] Sources =
    {
        new("https://www.youtube.com/", "YouTube", SuggestionKind.History),
        new("https://github.com/jubifox/navigatueur", "jubifox/navigatueur: Navigateur", SuggestionKind.Bookmark),
        new("https://fr.wikipedia.org/wiki/Navigateur_web", "Navigateur web — Wikipédia", SuggestionKind.History),
        new("https://www.youtube.com/", "YouTube (dupe)", SuggestionKind.History),
        new("https://navigatueur.home/settings.html", "Paramètres", SuggestionKind.History),
    };

    [Fact]
    public void HostPrefix_RanksFirst()
    {
        var results = AddressSuggestions.Rank("you", Sources);

        Assert.Single(results);
        Assert.Equal("https://www.youtube.com/", results[0].Url);
    }

    [Fact]
    public void TitleWordMatch_AndBookmarkBoost()
    {
        var results = AddressSuggestions.Rank("navig", Sources);

        Assert.Equal(2, results.Count);
        Assert.Equal(SuggestionKind.Bookmark, results[0].Kind);
    }

    [Fact]
    public void InternalPages_AreNeverSuggested()
    {
        Assert.Empty(AddressSuggestions.Rank("param", Sources));
    }

    [Fact]
    public void EmptyQuery_ReturnsNothing()
    {
        Assert.Empty(AddressSuggestions.Rank("  ", Sources));
    }
}
