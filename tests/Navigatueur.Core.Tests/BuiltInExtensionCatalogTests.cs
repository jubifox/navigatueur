using Navigatueur.Core.Extensions;
using Xunit;

namespace Navigatueur.Core.Tests;

public class BuiltInExtensionCatalogTests
{
    private static string ShippedCatalogPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Navigatueur.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "Navigatueur.App", "Resources", "Extensions", "builtin-extensions.json");
    }

    private static BuiltInExtensionCatalog Valid() => new()
    {
        Extensions =
        {
            new BuiltInExtension
            {
                Id = "sample", Name = "Sample", Version = "1.2.3", Package = "sample-1.2.3.zip",
                Url = "https://example.org/releases/sample.zip", Sha256 = new string('a', 64), Folder = "Sample", Matches = { "Sample" },
            },
        },
    };

    [Fact]
    public void ShippedCatalog_IsValid_AndHoldsTheSevenCoreExtensions()
    {
        var catalog = BuiltInExtensionCatalog.Parse(File.ReadAllText(ShippedCatalogPath()));

        Assert.Empty(catalog.Validate());
        Assert.Equal(
            new[] { "betterttv", "consent-o-matic", "decentraleyes", "purple-adblock", "sponsorblock", "ublock-origin", "violentmonkey" },
            catalog.Extensions.Select(e => e.Id).OrderBy(id => id));
    }

    [Fact]
    public void ShippedCatalog_DownloadsEachPackageFromItsAuthorsRelease()
    {
        var catalog = BuiltInExtensionCatalog.Parse(File.ReadAllText(ShippedCatalogPath()));

        Assert.All(catalog.Extensions.Where(e => !e.IsFromWebStore), e =>
        {
            Assert.StartsWith("https://github.com/", e.Url);
            Assert.DoesNotContain("jubifox", e.Url, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/releases/download/", e.Url);
        });
    }

    [Fact]
    public void ShippedCatalog_NeverRedistributesBetterTtv()
    {
        // Its license forbids redistribution: it must come from the Chrome Web Store, under its real id.
        var catalog = BuiltInExtensionCatalog.Parse(File.ReadAllText(ShippedCatalogPath()));
        var betterTtv = catalog.Extensions.Single(e => e.Id == "betterttv");

        Assert.True(betterTtv.IsFromWebStore);
        Assert.Equal("ajopnjidmegmdimjlfnijceegpefgped", betterTtv.WebStoreId);
        Assert.Empty(betterTtv.Package);
        Assert.Empty(betterTtv.Url);
        Assert.All(catalog.Extensions.Where(e => !e.IsFromWebStore), e => Assert.DoesNotContain("betterttv", e.Package, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WebStoreExtensions_AreNeverUpgradedFromAPackage()
    {
        var extension = new BuiltInExtension { WebStoreId = new string('a', 32) };

        Assert.False(extension.IsNewerThan("1.0.0"));
    }

    [Fact]
    public void Validate_AcceptsAWebStoreEntry_ButNotOneMixedWithAPackage()
    {
        var catalog = Valid();
        catalog.Extensions.Add(new BuiltInExtension
        {
            Id = "store", Name = "Store", WebStoreId = "ajopnjidmegmdimjlfnijceegpefgped", Folder = "Store", Matches = { "Store" },
        });
        Assert.Empty(catalog.Validate());

        catalog.Extensions[1].Package = "store.zip";
        Assert.Contains(catalog.Validate(), p => p.Contains("isn't redistributed"));

        catalog.Extensions[1].Package = string.Empty;
        catalog.Extensions[1].WebStoreId = "not-an-id";
        Assert.Contains(catalog.Validate(), p => p.Contains("webStoreId"));
    }

    [Fact]
    public void ShippedCatalog_KeepsTheFoldersEarlierVersionsInstalledTo()
    {
        // Same folder => same path => same extension identity in the engine => the user's settings survive.
        var catalog = BuiltInExtensionCatalog.Parse(File.ReadAllText(ShippedCatalogPath()));
        var folders = catalog.Extensions.ToDictionary(e => e.Id, e => e.Folder);

        Assert.Equal("uBlock Origin", folders["ublock-origin"]);
        Assert.Equal("Consent-O-Matic", folders["consent-o-matic"]);
        Assert.Equal("SponsorBlock", folders["sponsorblock"]);
        Assert.Equal("Decentraleyes", folders["decentraleyes"]);
    }

    [Theory]
    [InlineData("SponsorBlock pour YouTube - Supprime les publicités intégrées", "sponsorblock")] // French localized name
    [InlineData("SponsorBlock for YouTube - Skip Sponsorships", "sponsorblock")]
    [InlineData("uBlock Origin", "ublock-origin")]
    [InlineData("violentmonkey", "violentmonkey")]
    [InlineData("BetterTTV", "betterttv")]
    [InlineData("Purple Ads Blocker", "purple-adblock")] // its manifest name
    public void FindByEngineName_RecognizesLocalizedNames(string engineName, string expectedId)
    {
        var catalog = BuiltInExtensionCatalog.Parse(File.ReadAllText(ShippedCatalogPath()));

        Assert.Equal(expectedId, catalog.FindByEngineName(engineName)?.Id);
    }

    [Theory]
    [InlineData("Microsoft Edge PDF Viewer")]
    [InlineData("Dark Reader")]
    [InlineData("")]
    [InlineData(null)]
    public void FindByEngineName_IgnoresOtherExtensions(string? engineName)
    {
        var catalog = BuiltInExtensionCatalog.Parse(File.ReadAllText(ShippedCatalogPath()));

        Assert.Null(catalog.FindByEngineName(engineName));
    }

    [Theory]
    [InlineData("1.72.2", true)]
    [InlineData("1.75.0", false)]
    [InlineData("1.76", false)]
    [InlineData("not a version", false)]
    [InlineData(null, false)]
    public void IsNewerThan_ComparesVersionsNumerically(string? installed, bool expected)
    {
        var extension = new BuiltInExtension { Version = "1.75.0" };

        Assert.Equal(expected, extension.IsNewerThan(installed));
    }

    [Fact]
    public void Validate_AcceptsAWellFormedCatalog()
    {
        Assert.Empty(Valid().Validate());
    }

    [Theory]
    [InlineData("..")]
    [InlineData("..\\Windows")]
    [InlineData("a/b")]
    [InlineData("C:\\Temp")]
    [InlineData("")]
    public void Validate_RejectsFoldersThatArentPlainNames(string folder)
    {
        var catalog = Valid();
        catalog.Extensions[0].Folder = folder;

        Assert.Contains(catalog.Validate(), p => p.Contains("folder"));
    }

    [Theory]
    [InlineData("sample.exe")]
    [InlineData("../sample.zip")]
    [InlineData("")]
    public void Validate_RejectsPackagesThatArentPlainZipOrCrxNames(string package)
    {
        var catalog = Valid();
        catalog.Extensions[0].Package = package;

        Assert.Contains(catalog.Validate(), p => p.Contains("package"));
    }

    [Fact]
    public void Validate_AcceptsACrxPackage()
    {
        var catalog = Valid();
        catalog.Extensions[0].Package = "sample-1.2.3.crx";

        Assert.Empty(catalog.Validate());
    }

    [Fact]
    public void Validate_RejectsBadHashesUrlsAndDuplicates()
    {
        var catalog = Valid();
        catalog.Extensions[0].Url = "http://example.org/releases/sample.zip";
        catalog.Extensions[0].Sha256 = "ABC";
        catalog.Extensions.Add(catalog.Extensions[0]);

        var problems = catalog.Validate();

        Assert.Contains(problems, p => p.Contains("https"));
        Assert.Contains(problems, p => p.Contains("sha256"));
        Assert.Contains(problems, p => p.Contains("duplicate id"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "extensions": [ { "id": "x", "name": "X", "version": "1.0", "package": "x.zip", "url": "http://insecure/x.zip", "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "folder": "X", "matches": [ "X" ] } ] }""")]
    public void ParseOrEmpty_InstallsNothingFromABrokenCatalog(string json)
    {
        Assert.True(BuiltInExtensionCatalog.ParseOrEmpty(json).IsEmpty);
    }
}
