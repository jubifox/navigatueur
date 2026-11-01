using Navigatueur.Core.Settings;
using Xunit;

namespace Navigatueur.Core.Tests;

public class AppSettingsSerializerTests
{
    [Fact]
    public void Deserialize_EmptyString_ReturnsDefaults()
    {
        var settings = AppSettingsSerializer.Deserialize(string.Empty);

        Assert.Equal(new AppSettings().HomePageUrl, settings.HomePageUrl);
        Assert.Equal(new AppSettings().WindowWidth, settings.WindowWidth);
        Assert.Null(settings.WindowLeft);
    }

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsValues()
    {
        var original = new AppSettings
        {
            HomePageUrl = "https://example.com",
            WindowWidth = 1024,
            WindowHeight = 768,
            WindowLeft = 50,
            WindowTop = 75,
        };

        var json = AppSettingsSerializer.Serialize(original);
        var roundTripped = AppSettingsSerializer.Deserialize(json);

        Assert.Equal(original.HomePageUrl, roundTripped.HomePageUrl);
        Assert.Equal(original.WindowWidth, roundTripped.WindowWidth);
        Assert.Equal(original.WindowHeight, roundTripped.WindowHeight);
        Assert.Equal(original.WindowLeft, roundTripped.WindowLeft);
        Assert.Equal(original.WindowTop, roundTripped.WindowTop);
    }

    /// <summary>A settings.json written by v0.17.2: removed keys (address bar position/size) and no "Interface" object.</summary>
    [Fact]
    public void Deserialize_LegacyV017File_KeepsDataAndAppliesNewDefaults()
    {
        const string legacy = """
            {
              "HomePageUrl": "https://navigatueur.home/index.html",
              "WindowWidth": 1500,
              "Tabs": [ { "Url": "https://example.com", "GroupId": null, "IsPinned": true } ],
              "ActiveTabIndex": 0,
              "ThemeMode": "Light",
              "AccentColorHex": "#E04F4F",
              "SearchEngine": "DuckDuckGo",
              "AddressBarPosition": "Sidebar",
              "AddressBarSize": "Large",
              "IsCursorTrailEnabled": false
            }
            """;

        var settings = AppSettingsSerializer.Deserialize(legacy);

        Assert.Equal(1500, settings.WindowWidth);
        Assert.Single(settings.Tabs);
        Assert.True(settings.Tabs[0].IsPinned);
        Assert.Equal("Light", settings.ThemeMode);
        Assert.Equal("DuckDuckGo", settings.SearchEngine);
        Assert.False(settings.IsCursorTrailEnabled);
        Assert.NotNull(settings.Interface);
        Assert.Equal(InterfaceMode.Immersive, settings.Interface.ModeValue);
        Assert.False(settings.Interface.HasSeenIntro);
        Assert.Equal(StartupBehaviors.RestoreSession, settings.StartupBehavior);
        Assert.True(settings.IsAdBlockEnabled);
    }

    [Fact]
    public void Deserialize_ExplicitNulls_AreRepaired()
    {
        var settings = AppSettingsSerializer.Deserialize("""{ "Interface": null, "Tabs": null, "ShortcutOverrides": null, "StartupBehavior": "Whatever", "MaxLiveTabs": 0 }""");

        Assert.NotNull(settings.Interface);
        Assert.NotNull(settings.Tabs);
        Assert.NotNull(settings.ShortcutOverrides);
        Assert.Equal(StartupBehaviors.RestoreSession, settings.StartupBehavior);
        Assert.Equal(1, settings.MaxLiveTabs);
    }

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsInterfaceAndShortcuts()
    {
        var original = new AppSettings();
        original.Interface.Mode = "Custom";
        original.Interface.SidebarSide = "Right";
        original.Interface.RevealDelayMilliseconds = 300;
        original.ShortcutOverrides["tab.new"] = "Ctrl+Shift+K";

        var roundTripped = AppSettingsSerializer.Deserialize(AppSettingsSerializer.Serialize(original));

        Assert.Equal(InterfaceMode.Custom, roundTripped.Interface.ModeValue);
        Assert.Equal(PanelSide.Right, roundTripped.Interface.SidebarSideValue);
        Assert.Equal(300, roundTripped.Interface.RevealDelayMilliseconds);
        Assert.Equal("Ctrl+Shift+K", roundTripped.ShortcutOverrides["tab.new"]);
    }

    [Fact]
    public void Default_HomePageUrl_IsNotEmpty()
    {
        var settings = new AppSettings();

        Assert.False(string.IsNullOrWhiteSpace(settings.HomePageUrl));
    }
}
