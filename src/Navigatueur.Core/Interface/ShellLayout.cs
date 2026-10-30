using Navigatueur.Core.Settings;

namespace Navigatueur.Core.Interface;

/// <summary>An axis-aligned rectangle in device-independent pixels.</summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public static LayoutRect Empty => default;
}

/// <summary>Everything <see cref="ShellLayout.Compute"/> needs about the window and the panels' current state.</summary>
/// <param name="ViewportWidth">Width of the window's visible client area.</param>
/// <param name="ViewportHeight">Height of the window's visible client area.</param>
/// <param name="ToolbarHeight">Height of the toolbar's body, shadow excluded.</param>
/// <param name="SidebarWidth">Width of the tab column's body, shadow excluded.</param>
/// <param name="Side">Edge the tab column is attached to.</param>
/// <param name="Toolbar">Resolved toolbar behavior.</param>
/// <param name="Sidebar">Resolved tab column behavior.</param>
/// <param name="ToolbarProgress">0 = fully hidden, 1 = fully revealed (animated). Ignored when docked.</param>
/// <param name="SidebarProgress">0 = fully hidden, 1 = fully revealed (animated). Ignored when docked.</param>
/// <param name="ShadowSize">Room reserved past a floating panel's inner edge for its drop shadow; 0 for flat panels.</param>
/// <param name="FrameInset">Gap kept between the page and the window edges so the native resize borders stay reachable (0 when maximized or fullscreen).</param>
public readonly record struct ShellLayoutInput(
    double ViewportWidth,
    double ViewportHeight,
    double ToolbarHeight,
    double SidebarWidth,
    PanelSide Side,
    PanelBehavior Toolbar,
    PanelBehavior Sidebar,
    double ToolbarProgress,
    double SidebarProgress,
    double ShadowSize,
    double FrameInset);

/// <summary>Where every layer of the shell goes. Panel rects include their shadow area.</summary>
public readonly record struct ShellLayoutResult(
    LayoutRect Content,
    LayoutRect Toolbar,
    LayoutRect Sidebar,
    bool IsToolbarVisible,
    bool IsSidebarVisible,
    bool ToolbarHasShadow,
    bool SidebarHasShadow);

/// <summary>
/// The shell's geometry, kept free of WPF so it can be unit-tested.
///
/// Docked panels push the page aside; auto-hidden panels never touch the
/// page's rectangle — they slide over it, which is the whole point of the
/// immersive mode (no reflow, no jump, every time a panel appears).
/// When both panels show, the tab column tucks under the toolbar instead of
/// overlapping its ends, so back/forward and the window buttons are never
/// covered whichever side the column is on.
/// </summary>
public static class ShellLayout
{
    public static ShellLayoutResult Compute(ShellLayoutInput input)
    {
        var width = Math.Max(0, input.ViewportWidth);
        var height = Math.Max(0, input.ViewportHeight);

        var toolbarDocked = input.Toolbar == PanelBehavior.Docked;
        var sidebarDocked = input.Sidebar == PanelBehavior.Docked;

        var toolbarProgress = toolbarDocked ? 1 : Math.Clamp(input.ToolbarProgress, 0, 1);
        var sidebarProgress = sidebarDocked ? 1 : Math.Clamp(input.SidebarProgress, 0, 1);

        var toolbarShadow = toolbarDocked ? 0 : Math.Max(0, input.ShadowSize);
        var sidebarShadow = sidebarDocked ? 0 : Math.Max(0, input.ShadowSize);

        // Toolbar: full width, slides down from above the top edge.
        var toolbarOuterHeight = input.ToolbarHeight + toolbarShadow;
        var toolbarY = -(1 - toolbarProgress) * toolbarOuterHeight;
        var toolbar = new LayoutRect(0, toolbarY, width, toolbarOuterHeight);
        var toolbarBodyBottom = toolbarProgress > 0 ? Math.Max(0, toolbarY + input.ToolbarHeight) : 0;

        // Tab column: starts where the toolbar's body ends, slides in from its edge.
        var sidebarOuterWidth = input.SidebarWidth + sidebarShadow;
        var sidebarTop = toolbarBodyBottom;
        var sidebarHeight = Math.Max(0, height - sidebarTop);
        var sidebarX = input.Side == PanelSide.Left
            ? -(1 - sidebarProgress) * sidebarOuterWidth
            : width - sidebarProgress * sidebarOuterWidth;
        var sidebar = new LayoutRect(sidebarX, sidebarTop, sidebarOuterWidth, sidebarHeight);

        // Page: inset only by docked panels (or the resize frame).
        var frame = Math.Max(0, input.FrameInset);
        var contentTop = toolbarDocked ? input.ToolbarHeight : frame;
        var contentLeft = sidebarDocked && input.Side == PanelSide.Left ? input.SidebarWidth : frame;
        var contentRight = sidebarDocked && input.Side == PanelSide.Right ? input.SidebarWidth : frame;
        var content = new LayoutRect(
            contentLeft,
            contentTop,
            Math.Max(0, width - contentLeft - contentRight),
            Math.Max(0, height - contentTop - frame));

        return new ShellLayoutResult(
            content,
            toolbar,
            sidebar,
            IsToolbarVisible: toolbarProgress > 0,
            IsSidebarVisible: sidebarProgress > 0,
            ToolbarHasShadow: toolbarShadow > 0,
            SidebarHasShadow: sidebarShadow > 0);
    }
}
