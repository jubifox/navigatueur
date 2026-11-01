namespace Navigatueur.Core.Settings;

/// <summary>
/// Persisted snapshot of a tab group, saved on shutdown and replayed on the
/// next launch so groups (and their collapsed state) survive a restart.
/// </summary>
public sealed class SessionGroupState
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;

    public bool IsCollapsed { get; set; }

    /// <summary>Glyph shown inside the group's folder icon — one of GroupIcons' keys, empty for a plain folder.</summary>
    public string Icon { get; set; } = string.Empty;
}
