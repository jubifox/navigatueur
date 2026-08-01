using Xunit;

namespace Navigatueur.Core.Tests;

public class UrlHelperTests
{
    [Theory]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("http://example.com/path", "http://example.com/path")]
    public void Normalize_AlreadyHasScheme_PassesThrough(string input, string expected)
    {
        Assert.Equal(expected, UrlHelper.Normalize(input));
    }

    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("example.com/path", "https://example.com/path")]
    [InlineData("localhost:5000", "https://localhost:5000")]
    public void Normalize_LooksLikeHost_PrependsHttps(string input, string expected)
    {
        Assert.Equal(expected, UrlHelper.Normalize(input));
    }

    [Fact]
    public void Normalize_PlainText_BecomesSearchQuery()
    {
        var result = UrlHelper.Normalize("meilleur navigateur web");

        Assert.StartsWith("https://www.bing.com/search?q=", result);
        Assert.Contains("meilleur", result);
    }

    [Fact]
    public void Normalize_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, UrlHelper.Normalize("   "));
    }
}
