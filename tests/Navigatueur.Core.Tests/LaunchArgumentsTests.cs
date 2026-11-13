using Xunit;

namespace Navigatueur.Core.Tests;

public class LaunchArgumentsTests
{
    private static bool NoFiles(string _) => false;

    [Fact]
    public void Links_AreOpenedAsTheyCome()
    {
        var urls = LaunchArguments.ParseUrls(new[] { "https://example.com/page?q=1#x", "http://example.org" }, NoFiles);

        Assert.Equal(new[] { "https://example.com/page?q=1#x", "http://example.org" }, urls);
    }

    [Fact]
    public void QuotedArguments_AreUnquoted()
    {
        Assert.Equal(new[] { "https://example.com/" }, LaunchArguments.ParseUrls(new[] { "\"https://example.com/\"" }, NoFiles));
    }

    [Fact]
    public void AFileThatExists_BecomesAFileUrl()
    {
        var urls = LaunchArguments.ParseUrls(new[] { @"C:\Docs\ma page.html" }, path => path == @"C:\Docs\ma page.html");

        Assert.Equal(new[] { "file:///C:/Docs/ma%20page.html" }, urls);
    }

    [Fact]
    public void AMissingFile_IsIgnored()
    {
        Assert.Empty(LaunchArguments.ParseUrls(new[] { @"C:\Docs\absente.html" }, NoFiles));
    }

    [Theory]
    [InlineData("--some-flag")]
    [InlineData("bonjour")]
    [InlineData("example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:a@b.c")]
    [InlineData("")]
    public void AnythingElse_IsIgnored_NeverSearched(string arg)
    {
        Assert.Empty(LaunchArguments.ParseUrls(new[] { arg }, NoFiles));
    }

    [Fact]
    public void InternalAddresses_MapToTheirPages()
    {
        Assert.Equal(new[] { InternalPages.SettingsUrl }, LaunchArguments.ParseUrls(new[] { "navigatueur://settings" }, NoFiles));
    }

    [Fact]
    public void NoArguments_NothingToOpen()
    {
        Assert.Empty(LaunchArguments.ParseUrls(null));
        Assert.Empty(LaunchArguments.ParseUrls(Array.Empty<string>()));
    }

    [Fact]
    public void ALongListIsCapped()
    {
        var many = Enumerable.Range(0, 50).Select(i => $"https://example.com/{i}");

        Assert.Equal(LaunchArguments.MaxUrls, LaunchArguments.ParseUrls(many, NoFiles).Count);
    }
}
