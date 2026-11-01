using System.Text.Json.Serialization;

namespace Navigatueur.Core.Settings;

/// <summary>The three display presets offered in Settings.</summary>
public enum InterfaceMode
{
    /// <summary>Toolbar and tab column hidden, revealed by moving the mouse to their edge.</summary>
    Immersive,

    /// <summary>Toolbar and tab column always visible; the page is laid out next to them.</summary>
    Classic,

    /// <summary>Each panel independently auto-hides or stays docked.</summary>
    Custom,
}

/// <summary>Which window edge the tab column lives on.</summary>
public enum PanelSide
{
    Left,
    Right,
}

/// <summary>How one panel (toolbar or tab column) behaves, once the mode has been resolved.</summary>
public enum PanelBehavior
{
    /// <summary>Hidden by default, floats over the page when revealed — the page never resizes.</summary>
    AutoHide,

    /// <summary>Always visible, and the page is inset so it never sits underneath.</summary>
    Docked,
}

/// <summary>
/// Everything about the immersive shell that Settings exposes. Stored as a
/// nested object of <see cref="AppSettings"/> so an older settings.json
/// (which doesn't have it) simply deserializes to these defaults.
///
/// Mode and side are stored as plain strings rather than enums on purpose:
/// System.Text.Json throws on an unknown enum name, and SettingsService
/// treats any JSON error as "reset everything to defaults" — a single
/// hand-edited typo would otherwise wipe the user's whole session. The
/// typed <see cref="ModeValue"/>/<see cref="SidebarSideValue"/> accessors
/// fall back to the default instead.
/// </summary>
public sealed class InterfaceSettings
{
    public const double MinSidebarWidth = 200;
    public const double MaxSidebarWidth = 440;
    public const int MaxRevealDelayMilliseconds = 1500;
    public const int MaxHideDelayMilliseconds = 5000;
    public const int MinEdgeTriggerSize = 1;
    public const int MaxEdgeTriggerSize = 40;

    /// <summary>One of <see cref="InterfaceMode"/>'s names.</summary>
    public string Mode { get; set; } = nameof(InterfaceMode.Immersive);

    /// <summary>Only consulted in <see cref="InterfaceMode.Custom"/>.</summary>
    public bool ToolbarAutoHide { get; set; } = true;

    /// <summary>Only consulted in <see cref="InterfaceMode.Custom"/>.</summary>
    public bool SidebarAutoHide { get; set; } = true;

    /// <summary>One of <see cref="PanelSide"/>'s names.</summary>
    public string SidebarSide { get; set; } = nameof(PanelSide.Left);

    public double SidebarWidth { get; set; } = 280;

    /// <summary>Master switch for hover reveal. When off, auto-hidden panels only appear through the keyboard or the toolbar button.</summary>
    public bool IsEdgeRevealEnabled { get; set; } = true;

    /// <summary>How long the mouse has to rest in an edge's trigger zone before its panel slides in — the guard against accidental reveals.</summary>
    public int RevealDelayMilliseconds { get; set; } = 150;

    /// <summary>How long a revealed panel lingers once the mouse has left it.</summary>
    public int HideDelayMilliseconds { get; set; } = 600;

    /// <summary>Thickness, in device-independent pixels, of the band along the top edge that counts as "at the edge" (toolbar).</summary>
    public int EdgeTriggerSize { get; set; } = 6;

    /// <summary>
    /// Same, for the side edge that reveals the tab column. Kept separate and
    /// wider than the top one: the side of a page rarely holds anything
    /// clickable, while the top band overlaps every site's own header.
    /// </summary>
    public int SidebarEdgeTriggerSize { get; set; } = 18;

    public bool AreAnimationsEnabled { get; set; } = true;

    /// <summary>Rounded corners and drop shadows on floating panels. Off forces flat, opaque panels.</summary>
    public bool ArePanelEffectsEnabled { get; set; } = true;

    /// <summary>Set once the user has been shown how the immersive mode works (first run, or first launch after upgrading).</summary>
    public bool HasSeenIntro { get; set; }

    [JsonIgnore]
    public InterfaceMode ModeValue =>
        Enum.TryParse<InterfaceMode>(Mode, ignoreCase: true, out var mode) && Enum.IsDefined(mode)
            ? mode
            : InterfaceMode.Immersive;

    [JsonIgnore]
    public PanelSide SidebarSideValue =>
        Enum.TryParse<PanelSide>(SidebarSide, ignoreCase: true, out var side) && Enum.IsDefined(side)
            ? side
            : PanelSide.Left;

    public PanelBehavior ResolveToolbarBehavior() => ModeValue switch
    {
        InterfaceMode.Classic => PanelBehavior.Docked,
        InterfaceMode.Custom => ToolbarAutoHide ? PanelBehavior.AutoHide : PanelBehavior.Docked,
        _ => PanelBehavior.AutoHide,
    };

    public PanelBehavior ResolveSidebarBehavior() => ModeValue switch
    {
        InterfaceMode.Classic => PanelBehavior.Docked,
        InterfaceMode.Custom => SidebarAutoHide ? PanelBehavior.AutoHide : PanelBehavior.Docked,
        _ => PanelBehavior.AutoHide,
    };

    /// <summary>
    /// Sets both panel behaviors at once, picking whichever preset describes
    /// the result — so flipping the tab column to docked from the toolbar
    /// button while in Immersive lands in Custom, and docking both again lands
    /// back in Classic rather than a Custom that merely looks like it.
    /// </summary>
    public void ApplyBehaviors(PanelBehavior toolbar, PanelBehavior sidebar)
    {
        ToolbarAutoHide = toolbar == PanelBehavior.AutoHide;
        SidebarAutoHide = sidebar == PanelBehavior.AutoHide;

        Mode = (toolbar, sidebar) switch
        {
            (PanelBehavior.AutoHide, PanelBehavior.AutoHide) => nameof(InterfaceMode.Immersive),
            (PanelBehavior.Docked, PanelBehavior.Docked) => nameof(InterfaceMode.Classic),
            _ => nameof(InterfaceMode.Custom),
        };
    }

    /// <summary>Clamps every numeric value into its supported range and repairs unknown enum names, so a hand-edited file can't produce a 0px-wide column or a negative delay.</summary>
    public void Normalize()
    {
        Mode = ModeValue.ToString();
        SidebarSide = SidebarSideValue.ToString();
        SidebarWidth = double.IsFinite(SidebarWidth) ? Math.Clamp(SidebarWidth, MinSidebarWidth, MaxSidebarWidth) : 280;
        RevealDelayMilliseconds = Math.Clamp(RevealDelayMilliseconds, 0, MaxRevealDelayMilliseconds);
        HideDelayMilliseconds = Math.Clamp(HideDelayMilliseconds, 0, MaxHideDelayMilliseconds);
        EdgeTriggerSize = Math.Clamp(EdgeTriggerSize, MinEdgeTriggerSize, MaxEdgeTriggerSize);
        SidebarEdgeTriggerSize = Math.Clamp(SidebarEdgeTriggerSize, MinEdgeTriggerSize, MaxEdgeTriggerSize);
    }
}
