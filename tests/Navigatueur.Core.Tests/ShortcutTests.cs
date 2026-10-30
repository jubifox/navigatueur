using Navigatueur.Core.Shortcuts;
using Xunit;

namespace Navigatueur.Core.Tests;

public class ShortcutGestureTests
{
    [Theory]
    [InlineData("Ctrl+Shift+T", true, true, false, "T")]
    [InlineData("ctrl + maj + t", true, true, false, "T")]
    [InlineData("Control+PgDn", true, false, false, "PageDown")]
    [InlineData("F11", false, false, false, "F11")]
    [InlineData("Alt+Left", false, false, true, "Left")]
    [InlineData("Ctrl+,", true, false, false, "Comma")]
    [InlineData("Ctrl++", true, false, false, "Plus")]
    [InlineData("Shift+Ctrl+Tab", true, true, false, "Tab")]
    public void TryParse_AcceptsCommonSpellings(string text, bool ctrl, bool shift, bool alt, string key)
    {
        Assert.True(ShortcutGesture.TryParse(text, out var gesture));
        Assert.Equal(new ShortcutGesture(ctrl, shift, alt, key), gesture);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+T+R")]
    [InlineData("Ctrl+Banana")]
    public void TryParse_RejectsInvalidText(string text)
    {
        Assert.False(ShortcutGesture.TryParse(text, out _));
    }

    [Fact]
    public void ToString_IsCanonicalAndRoundTrips()
    {
        var gesture = ShortcutGesture.Parse("shift+ctrl+t");

        Assert.Equal("Ctrl+Shift+T", gesture.ToString());
        Assert.Equal(gesture, ShortcutGesture.Parse(gesture.ToString()));
    }

    [Fact]
    public void ToDisplayString_UsesFrenchKeyNames()
    {
        Assert.Equal("Ctrl + Maj + T", ShortcutGesture.Parse("Ctrl+Shift+T").ToDisplayString());
        Assert.Equal("Alt + ←", ShortcutGesture.Parse("Alt+Left").ToDisplayString());
    }

    [Theory]
    [InlineData("Ctrl+T", true)]
    [InlineData("Alt+D", true)]
    [InlineData("F6", true)]
    [InlineData("T", false)]
    [InlineData("Shift+T", false)]
    public void IsValidShortcut_RequiresModifierUnlessFunctionKey(string text, bool expected)
    {
        Assert.Equal(expected, ShortcutGesture.Parse(text).IsValidShortcut);
    }
}

public class ShortcutMapTests
{
    [Theory]
    [InlineData("Ctrl+T", ShortcutCommands.NewTab)]
    [InlineData("Ctrl+W", ShortcutCommands.CloseTab)]
    [InlineData("Ctrl+Tab", ShortcutCommands.NextTab)]
    [InlineData("Ctrl+Shift+Tab", ShortcutCommands.PreviousTab)]
    [InlineData("Ctrl+L", ShortcutCommands.FocusAddressBar)]
    [InlineData("Ctrl+R", ShortcutCommands.Reload)]
    [InlineData("Ctrl+Shift+T", ShortcutCommands.ReopenClosedTab)]
    [InlineData("F11", ShortcutCommands.ToggleFullScreen)]
    [InlineData("Ctrl+3", "tab.select.3")]
    [InlineData("Ctrl+9", ShortcutCommands.SelectLastTab)]
    public void Defaults_CoverRequiredShortcuts(string text, string expected)
    {
        var map = ShortcutMap.Build(null);

        Assert.True(map.TryGetCommand(ShortcutGesture.Parse(text), out var command));
        Assert.Equal(expected, command);
    }

    [Fact]
    public void Catalog_HasNoDuplicateDefaultGestures()
    {
        var all = ShortcutCatalog.All.SelectMany(d => d.Defaults).ToList();

        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void Override_ReplacesDefault_AndStealsGestureFromOtherCommand()
    {
        var map = ShortcutMap.Build(new Dictionary<string, string>
        {
            [ShortcutCommands.NewTab] = "Ctrl+R",
        });

        Assert.True(map.TryGetCommand(ShortcutGesture.Parse("Ctrl+R"), out var command));
        Assert.Equal(ShortcutCommands.NewTab, command);
        Assert.False(map.TryGetCommand(ShortcutGesture.Parse("Ctrl+T"), out _));
        Assert.Equal(new[] { ShortcutGesture.Parse("F5") }, map.GetGestures(ShortcutCommands.Reload));
    }

    [Fact]
    public void EmptyOverride_DisablesCommand()
    {
        var map = ShortcutMap.Build(new Dictionary<string, string> { [ShortcutCommands.NewWindow] = "" });

        Assert.Empty(map.GetGestures(ShortcutCommands.NewWindow));
        Assert.False(map.TryGetCommand(ShortcutGesture.Parse("Ctrl+N"), out _));
    }

    [Theory]
    [InlineData("tab.new", "not a shortcut")]
    [InlineData("tab.new", "Shift+T")]
    [InlineData("no.such.command", "Ctrl+K")]
    [InlineData("tab.select.1", "Ctrl+K")]
    public void InvalidOverrides_AreIgnored(string id, string text)
    {
        var map = ShortcutMap.Build(new Dictionary<string, string> { [id] = text });

        Assert.True(map.TryGetCommand(ShortcutGesture.Parse("Ctrl+T"), out var command));
        Assert.Equal(ShortcutCommands.NewTab, command);
        Assert.True(map.TryGetCommand(ShortcutGesture.Parse("Ctrl+1"), out var select));
        Assert.Equal("tab.select.1", select);
        Assert.False(map.TryGetCommand(ShortcutGesture.Parse("Ctrl+K"), out _));
    }

    [Fact]
    public void FindConflict_ReportsOtherOwnerOnly()
    {
        var map = ShortcutMap.Build(null);
        var ctrlT = ShortcutGesture.Parse("Ctrl+T");

        Assert.Equal(ShortcutCommands.NewTab, map.FindConflict(ctrlT, ShortcutCommands.Reload));
        Assert.Null(map.FindConflict(ctrlT, ShortcutCommands.NewTab));
        Assert.Null(map.FindConflict(ShortcutGesture.Parse("Ctrl+K"), ShortcutCommands.NewTab));
    }
}
