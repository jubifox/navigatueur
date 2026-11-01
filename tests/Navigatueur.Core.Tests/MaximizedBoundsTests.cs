using Navigatueur.Core.Interface;
using Xunit;

namespace Navigatueur.Core.Tests;

public class MaximizedBoundsTests
{
    private static readonly PixelRect Monitor = new(0, 0, 2560, 1440);

    [Fact]
    public void VisibleTaskbar_FillsTheWorkArea()
    {
        var work = new PixelRect(0, 0, 2560, 1392); // taskbar docked at the bottom, reserving 48 px

        var placement = MaximizedBounds.Compute(Monitor, work, ScreenEdges.None);

        Assert.Equal(new MaximizedPlacement(0, 0, 2560, 1392), placement);
    }

    [Fact]
    public void AutoHideTaskbarAtBottom_LeavesAGapSoItCanSlideBackIn()
    {
        // Auto-hide: the work area is the whole monitor.
        var placement = MaximizedBounds.Compute(Monitor, Monitor, ScreenEdges.Bottom);

        Assert.Equal(new MaximizedPlacement(0, 0, 2560, 1440 - MaximizedBounds.AutoHideTaskbarGap), placement);
    }

    [Fact]
    public void AutoHideTaskbarAtTop_ShiftsTheWindowDown()
    {
        var placement = MaximizedBounds.Compute(Monitor, Monitor, ScreenEdges.Top);

        Assert.Equal(MaximizedBounds.AutoHideTaskbarGap, placement.Y);
        Assert.Equal(1440 - MaximizedBounds.AutoHideTaskbarGap, placement.Height);
    }

    [Theory]
    [InlineData(ScreenEdges.Left, 2, 0, 2558, 1440)]
    [InlineData(ScreenEdges.Right, 0, 0, 2558, 1440)]
    public void AutoHideTaskbarOnTheSide_TrimsThatSide(ScreenEdges edge, int x, int y, int width, int height)
    {
        var placement = MaximizedBounds.Compute(Monitor, Monitor, edge);

        Assert.Equal(new MaximizedPlacement(x, y, width, height), placement);
    }

    [Fact]
    public void EdgeAlreadyKeptClearByADockedBar_NeedsNoExtraGap()
    {
        // Something docked at the bottom reserves space; an auto-hide bar on that edge doesn't add to it.
        var work = new PixelRect(0, 0, 2560, 1400);

        var placement = MaximizedBounds.Compute(Monitor, work, ScreenEdges.Bottom);

        Assert.Equal(1400, placement.Height);
    }

    [Fact]
    public void SecondaryMonitor_PlacementIsRelativeToThatMonitor()
    {
        var secondary = new PixelRect(2560, -200, 4480, 880);

        var placement = MaximizedBounds.Compute(secondary, secondary, ScreenEdges.Bottom);

        Assert.Equal(new MaximizedPlacement(0, 0, 1920, 1078), placement);
    }
}
