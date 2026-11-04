using Navigatueur.Core.Settings;
using Navigatueur.Core.Sites;
using Xunit;

namespace Navigatueur.Core.Tests;

public class ForceDarkRulesTests
{
    private static readonly Dictionary<string, bool> None = new();

    [Theory]
    [InlineData("https://www.example.com/page", "example.com")]
    [InlineData("http://Mail.Google.com/", "mail.google.com")]
    [InlineData("https://example.com:8443/x", "example.com")]
    public void SiteKey_IsTheHostWithoutWww(string url, string expected) =>
        Assert.Equal(expected, ForceDarkRules.SiteKey(url));

    [Theory]
    [InlineData(InternalPages.NewTabUrl)]
    [InlineData(InternalPages.SettingsUrl)]
    [InlineData("https://navigatueur.warning/index.html?url=x")]
    [InlineData("about:blank")]
    [InlineData("file:///C:/page.html")]
    [InlineData("edge://settings")]
    [InlineData("pas une adresse")]
    [InlineData(null)]
    public void PagesThatCannotBeDarkened_HaveNoKey_AndAreNeverDarkened(string? url)
    {
        Assert.Null(ForceDarkRules.SiteKey(url));
        Assert.False(ForceDarkRules.IsEnabledFor(url, enabledByDefault: true, None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithoutChoices_TheDefaultApplies(bool enabledByDefault) =>
        Assert.Equal(enabledByDefault, ForceDarkRules.IsEnabledFor("https://example.com", enabledByDefault, None));

    [Fact]
    public void AChoiceCoversSubdomains_TheMostSpecificWins()
    {
        var sites = new Dictionary<string, bool> { ["google.com"] = false, ["docs.google.com"] = true };

        Assert.False(ForceDarkRules.IsEnabledFor("https://www.google.com/search?q=x", true, sites));
        Assert.False(ForceDarkRules.IsEnabledFor("https://mail.google.com/", true, sites));
        Assert.True(ForceDarkRules.IsEnabledFor("https://docs.google.com/document/1", true, sites));
        Assert.True(ForceDarkRules.IsEnabledFor("https://example.com/", true, sites));
        Assert.False(ForceDarkRules.IsEnabledFor("https://notgoogle.com/", false, sites));
    }

    [Fact]
    public void SetForSite_KeepsOnlyChoicesThatDifferFromTheDefault()
    {
        var sites = new Dictionary<string, bool>();

        ForceDarkRules.SetForSite("https://www.example.com/a", false, enabledByDefault: true, sites);
        Assert.Equal(new Dictionary<string, bool> { ["example.com"] = false }, sites);

        ForceDarkRules.SetForSite("https://example.com/b", true, enabledByDefault: true, sites);
        Assert.Empty(sites);

        ForceDarkRules.SetForSite("https://example.com/", true, enabledByDefault: true, sites);
        Assert.Empty(sites);
    }

    [Fact]
    public void SetForSite_OnASubdomain_OverridesItsParentOnly()
    {
        var sites = new Dictionary<string, bool> { ["google.com"] = false };

        ForceDarkRules.SetForSite("https://mail.google.com/", true, enabledByDefault: true, sites);

        Assert.True(ForceDarkRules.IsEnabledFor("https://mail.google.com/", true, sites));
        Assert.False(ForceDarkRules.IsEnabledFor("https://google.com/", true, sites));

        // Back to what the parent says: the subdomain's entry goes away.
        ForceDarkRules.SetForSite("https://mail.google.com/", false, enabledByDefault: true, sites);
        Assert.Equal(new Dictionary<string, bool> { ["google.com"] = false }, sites);
    }

    [Fact]
    public void SetForSite_IgnoresPagesThatCannotBeDarkened()
    {
        var sites = new Dictionary<string, bool>();

        ForceDarkRules.SetForSite(InternalPages.NewTabUrl, true, enabledByDefault: false, sites);

        Assert.Empty(sites);
    }

    [Fact]
    public void Normalize_RepairsHandEditedKeys()
    {
        var sites = new Dictionary<string, bool> { ["WWW.Example.com"] = false, [" "] = true, ["ok.org"] = true };

        ForceDarkRules.Normalize(sites);

        Assert.Equal(new Dictionary<string, bool> { ["example.com"] = false, ["ok.org"] = true }, sites);
    }

    [Fact]
    public void Settings_DefaultToNoForcedDark_AndAutomaticPictureInPicture()
    {
        var settings = AppSettingsSerializer.Deserialize("{}");

        Assert.False(settings.IsForceDarkEnabled);
        Assert.Empty(settings.ForceDarkSites);
        Assert.True(settings.IsAutoPictureInPictureEnabled);
        Assert.True(settings.Interface.AreTabPreviewsEnabled);
    }

    [Fact]
    public void Settings_RepairANullSiteList()
    {
        var settings = AppSettingsSerializer.Deserialize("{\"ForceDarkSites\":null,\"IsForceDarkEnabled\":true}");

        Assert.True(settings.IsForceDarkEnabled);
        Assert.NotNull(settings.ForceDarkSites);
    }
}
