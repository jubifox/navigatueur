using System.Collections.ObjectModel;
using Navigatueur.Core.Settings;
using Navigatueur.Core.Tabs;
using Xunit;

namespace Navigatueur.Core.Tests;

public class TabGroupTests
{
    // ------------------------------------------------------------ reorder

    [Theory]
    [InlineData(0, 2, false, "B A C D")] // A dropped before C
    [InlineData(0, 2, true, "B C A D")]  // A dropped after C
    [InlineData(3, 1, false, "A D B C")] // D dropped before B
    [InlineData(3, 1, true, "A B D C")]  // D dropped after B
    [InlineData(1, 0, false, "B A C D")] // B dropped before A
    [InlineData(2, 3, true, "A B D C")]  // C dropped after D (to the end)
    public void MoveDestination_PutsTheItemWhereItWasDropped(int from, int target, bool after, string expected)
    {
        var list = new ObservableCollection<string> { "A", "B", "C", "D" };

        var destination = ListReorder.MoveDestination(from, target, after, list.Count);
        Assert.NotNull(destination);
        list.Move(from, destination!.Value);

        Assert.Equal(expected, string.Join(" ", list));
    }

    [Theory]
    [InlineData(1, 1, false)] // onto itself
    [InlineData(1, 2, false)] // before the next item: already there
    [InlineData(2, 1, true)]  // after the previous item: already there
    [InlineData(-1, 0, false)]
    [InlineData(0, 9, false)]
    public void MoveDestination_NoOpOrInvalid_ReturnsNull(int from, int target, bool after)
    {
        Assert.Null(ListReorder.MoveDestination(from, target, after, 4));
    }

    // -------------------------------------------------------------- icons

    [Fact]
    public void Icons_TenChoicesWithUniqueKeys()
    {
        Assert.Equal(10, GroupIcons.All.Count);
        Assert.Equal(GroupIcons.All.Count, GroupIcons.All.Select(i => i.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(GroupIcons.All, icon => Assert.False(string.IsNullOrWhiteSpace(icon.Label)));
    }

    [Theory]
    [InlineData("Play", "Play")]
    [InlineData("play", "Play")]
    [InlineData(" MUSIC ", "Music")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("Rocket", "")] // unknown (hand-edited, or from a newer version): plain folder
    public void Normalize_AcceptsKnownKeysOnly(string? key, string expected)
    {
        Assert.Equal(expected, GroupIcons.Normalize(key));
    }

    [Fact]
    public void OlderSettings_WithoutGroupIcons_LoadAsPlainFolders()
    {
        var settings = AppSettingsSerializer.Deserialize("""
            {
              "Groups": [ { "Id": "8f2b2c55-3a3e-4a8a-9d5b-1c2d3e4f5a6b", "Name": "Vidéos", "ColorHex": "#4C8DFF" } ],
              "SavedGroups": [ { "Id": "8f2b2c55-3a3e-4a8a-9d5b-1c2d3e4f5a6b", "Name": "Vidéos", "ColorHex": "#4C8DFF", "Urls": [ "https://youtube.com" ] } ]
            }
            """);

        Assert.Equal(string.Empty, settings.Groups[0].Icon);
        Assert.Equal(string.Empty, settings.SavedGroups[0].Icon);
    }

    [Fact]
    public void GroupIcon_SurvivesASaveAndReload()
    {
        var settings = new AppSettings();
        settings.Groups.Add(new SessionGroupState { Id = Guid.NewGuid(), Name = "Musique", ColorHex = "#4FE0A0", Icon = "Music" });

        var reloaded = AppSettingsSerializer.Deserialize(AppSettingsSerializer.Serialize(settings));

        Assert.Equal("Music", reloaded.Groups[0].Icon);
    }
}
