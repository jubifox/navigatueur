namespace Navigatueur.Core.Tabs;

/// <summary>One choice in the tab-group icon picker: a small glyph drawn inside the group's folder icon.</summary>
/// <param name="Key">Stored in settings.json (session and saved groups); also names the glyph geometry, "GroupGlyph" + Key.</param>
/// <param name="Label">Shown as the picker button's tooltip.</param>
public sealed record GroupIcon(string Key, string Label);

/// <summary>
/// The glyphs a tab group's folder icon can carry, to tell groups apart at a
/// glance (a play button for video sites, a note for music…). The empty key
/// means a plain folder.
/// </summary>
public static class GroupIcons
{
    public const string None = "";

    public static IReadOnlyList<GroupIcon> All { get; } = new GroupIcon[]
    {
        new("Play", "Vidéo"),
        new("Music", "Musique"),
        new("Game", "Jeux"),
        new("Chat", "Discussions"),
        new("Work", "Travail"),
        new("Code", "Développement"),
        new("Shop", "Achats"),
        new("Book", "Lecture et études"),
        new("News", "Actualités"),
        new("Star", "Favoris"),
    };

    /// <summary>The canonical key for <paramref name="key"/> (case-insensitive), or <see cref="None"/> for anything unknown.</summary>
    public static string Normalize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return None;
        }

        foreach (var icon in All)
        {
            if (string.Equals(icon.Key, key.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return icon.Key;
            }
        }

        return None;
    }
}
