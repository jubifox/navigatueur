namespace Navigatueur.Core.Interface;

/// <summary>Monitor edges, combinable.</summary>
[Flags]
public enum ScreenEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

/// <summary>A rectangle in physical screen pixels (right/bottom exclusive), as Win32 reports monitors.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom);

/// <summary>Where a maximized window goes: position relative to its monitor's top-left corner, and size.</summary>
public readonly record struct MaximizedPlacement(int X, int Y, int Width, int Height);

/// <summary>
/// The rectangle a chrome-less window should fill when maximized.
///
/// Normally that's the monitor's work area. But with the taskbar set to
/// auto-hide, the work area *is* the whole monitor, and Windows treats a
/// window covering the whole monitor as a fullscreen app: it then refuses to
/// slide the taskbar back in when the mouse reaches that edge. Leaving a
/// <see cref="AutoHideTaskbarGap"/>-pixel gap on the auto-hide edge keeps the
/// window "maximized, not fullscreen" — the same gap Chromium leaves.
/// </summary>
public static class MaximizedBounds
{
    public const int AutoHideTaskbarGap = 2;

    public static MaximizedPlacement Compute(PixelRect monitor, PixelRect work, ScreenEdges autoHideEdges)
    {
        var left = work.Left;
        var top = work.Top;
        var right = work.Right;
        var bottom = work.Bottom;

        // An auto-hidden taskbar reserves no space, so its edge of the work
        // area is the monitor's edge. If something else already keeps the
        // window off that edge (a docked app bar), no gap is needed.
        if (autoHideEdges.HasFlag(ScreenEdges.Left) && left == monitor.Left)
        {
            left += AutoHideTaskbarGap;
        }

        if (autoHideEdges.HasFlag(ScreenEdges.Top) && top == monitor.Top)
        {
            top += AutoHideTaskbarGap;
        }

        if (autoHideEdges.HasFlag(ScreenEdges.Right) && right == monitor.Right)
        {
            right -= AutoHideTaskbarGap;
        }

        if (autoHideEdges.HasFlag(ScreenEdges.Bottom) && bottom == monitor.Bottom)
        {
            bottom -= AutoHideTaskbarGap;
        }

        return new MaximizedPlacement(left - monitor.Left, top - monitor.Top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
