using Navigatueur.Core.Settings;
using Xunit;

namespace Navigatueur.Core.Tests;

public class InterfaceSettingsTests
{
    [Fact]
    public void Defaults_AreImmersiveLeft()
    {
        var settings = new InterfaceSettings();

        Assert.Equal(InterfaceMode.Immersive, settings.ModeValue);
        Assert.Equal(PanelSide.Left, settings.SidebarSideValue);
        Assert.Equal(PanelBehavior.AutoHide, settings.ResolveToolbarBehavior());
        Assert.Equal(PanelBehavior.AutoHide, settings.ResolveSidebarBehavior());
    }

    [Fact]
    public void Classic_DocksBoth_EvenIfCustomFlagsSayOtherwise()
    {
        var settings = new InterfaceSettings { Mode = "Classic", ToolbarAutoHide = true, SidebarAutoHide = true };

        Assert.Equal(PanelBehavior.Docked, settings.ResolveToolbarBehavior());
        Assert.Equal(PanelBehavior.Docked, settings.ResolveSidebarBehavior());
    }

    [Theory]
    [InlineData(true, false, PanelBehavior.AutoHide, PanelBehavior.Docked)]
    [InlineData(false, true, PanelBehavior.Docked, PanelBehavior.AutoHide)]
    public void Custom_UsesPerPanelFlags(bool toolbarAuto, bool sidebarAuto, PanelBehavior toolbar, PanelBehavior sidebar)
    {
        var settings = new InterfaceSettings { Mode = "Custom", ToolbarAutoHide = toolbarAuto, SidebarAutoHide = sidebarAuto };

        Assert.Equal(toolbar, settings.ResolveToolbarBehavior());
        Assert.Equal(sidebar, settings.ResolveSidebarBehavior());
    }

    [Theory]
    [InlineData(PanelBehavior.AutoHide, PanelBehavior.AutoHide, InterfaceMode.Immersive)]
    [InlineData(PanelBehavior.Docked, PanelBehavior.Docked, InterfaceMode.Classic)]
    [InlineData(PanelBehavior.AutoHide, PanelBehavior.Docked, InterfaceMode.Custom)]
    [InlineData(PanelBehavior.Docked, PanelBehavior.AutoHide, InterfaceMode.Custom)]
    public void ApplyBehaviors_PicksMatchingPreset(PanelBehavior toolbar, PanelBehavior sidebar, InterfaceMode expected)
    {
        var settings = new InterfaceSettings();
        settings.ApplyBehaviors(toolbar, sidebar);

        Assert.Equal(expected, settings.ModeValue);
        Assert.Equal(toolbar, settings.ResolveToolbarBehavior());
        Assert.Equal(sidebar, settings.ResolveSidebarBehavior());
    }

    [Fact]
    public void UnknownNames_FallBackToDefaults()
    {
        var settings = new InterfaceSettings { Mode = "Floating", SidebarSide = "Top" };

        Assert.Equal(InterfaceMode.Immersive, settings.ModeValue);
        Assert.Equal(PanelSide.Left, settings.SidebarSideValue);
    }

    [Fact]
    public void ModeNames_AreCaseInsensitive()
    {
        var settings = new InterfaceSettings { Mode = "classic", SidebarSide = "RIGHT" };

        Assert.Equal(InterfaceMode.Classic, settings.ModeValue);
        Assert.Equal(PanelSide.Right, settings.SidebarSideValue);
    }

    [Fact]
    public void Normalize_ClampsOutOfRangeValues()
    {
        var settings = new InterfaceSettings
        {
            Mode = "nonsense",
            SidebarWidth = 5,
            RevealDelayMilliseconds = -40,
            HideDelayMilliseconds = 999_999,
            EdgeTriggerSize = 0,
        };

        settings.Normalize();

        Assert.Equal("Immersive", settings.Mode);
        Assert.Equal(InterfaceSettings.MinSidebarWidth, settings.SidebarWidth);
        Assert.Equal(0, settings.RevealDelayMilliseconds);
        Assert.Equal(InterfaceSettings.MaxHideDelayMilliseconds, settings.HideDelayMilliseconds);
        Assert.Equal(InterfaceSettings.MinEdgeTriggerSize, settings.EdgeTriggerSize);
    }

    [Fact]
    public void SideTriggerZone_IsWiderThanTopByDefault()
    {
        var settings = new InterfaceSettings();

        Assert.Equal(6, settings.EdgeTriggerSize);
        Assert.Equal(18, settings.SidebarEdgeTriggerSize);
    }

    [Theory]
    [InlineData(0, InterfaceSettings.MinEdgeTriggerSize)]
    [InlineData(500, InterfaceSettings.MaxEdgeTriggerSize)]
    [InlineData(25, 25)]
    public void Normalize_ClampsSideTriggerZone(int value, int expected)
    {
        var settings = new InterfaceSettings { SidebarEdgeTriggerSize = value };

        settings.Normalize();

        Assert.Equal(expected, settings.SidebarEdgeTriggerSize);
    }

    [Fact]
    public void ExistingFileWithoutSideTriggerZone_GetsTheWiderDefault_AndKeepsItsTopZone()
    {
        // A v0.18.0 settings.json: the shared EdgeTriggerSize is stored, the side-specific key doesn't exist yet.
        var settings = AppSettingsSerializer.Deserialize("""{ "Interface": { "EdgeTriggerSize": 9, "HasSeenIntro": true } }""");

        Assert.Equal(9, settings.Interface.EdgeTriggerSize);
        Assert.Equal(18, settings.Interface.SidebarEdgeTriggerSize);
    }

    [Fact]
    public void NewTabButton_StaysAtTheBottomByDefault_AndRepairsUnknownValues()
    {
        Assert.Equal(NewTabButtonPosition.Bottom, new InterfaceSettings().NewTabButtonPlacementValue);

        var settings = new InterfaceSettings { NewTabButtonPlacement = "Sideways" };
        settings.Normalize();
        Assert.Equal("Bottom", settings.NewTabButtonPlacement);

        settings.NewTabButtonPlacement = "aftertabs";
        settings.Normalize();
        Assert.Equal(NewTabButtonPosition.AfterTabs, settings.NewTabButtonPlacementValue);
        Assert.Equal("AfterTabs", settings.NewTabButtonPlacement);
    }

    [Theory]
    [InlineData(null, null, true, "newtab.jpg")]            // older file, no panel image: follows the new-tab page
    [InlineData(null, "chrome.png", false, "chrome.png")]    // older file with its own panel image: keeps it
    [InlineData(true, "chrome.png", true, "newtab.jpg")]     // chosen explicitly
    [InlineData(false, null, false, null)]                   // own image chosen off, none picked: no image
    public void ChromeBackground_FollowsTheNewTabPage_UnlessThePanelsHaveTheirOwn(bool? follows, string? chrome, bool expectedFollows, string? expectedImage)
    {
        var settings = new AppSettings
        {
            NewTabBackgroundImagePath = "newtab.jpg",
            ChromeBackgroundImagePath = chrome,
            ChromeUsesNewTabBackground = follows,
        };

        Assert.Equal(expectedFollows, settings.ResolveChromeUsesNewTabBackground());
        Assert.Equal(expectedImage, settings.ResolveChromeBackgroundImagePath());
    }

    [Fact]
    public void Normalize_NaNWidth_ResetsToDefault()
    {
        var settings = new InterfaceSettings { SidebarWidth = double.NaN };

        settings.Normalize();

        Assert.Equal(280, settings.SidebarWidth);
    }
}
