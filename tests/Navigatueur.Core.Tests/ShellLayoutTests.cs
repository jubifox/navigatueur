using Navigatueur.Core.Interface;
using Navigatueur.Core.Settings;
using Xunit;

namespace Navigatueur.Core.Tests;

public class ShellLayoutTests
{
    private const double Width = 1200;
    private const double Height = 800;
    private const double ToolbarHeight = 48;
    private const double SidebarWidth = 280;
    private const double Shadow = 16;

    private static ShellLayoutResult Compute(
        PanelBehavior toolbar,
        PanelBehavior sidebar,
        double toolbarProgress = 0,
        double sidebarProgress = 0,
        PanelSide side = PanelSide.Left,
        double frame = 0) =>
        ShellLayout.Compute(new ShellLayoutInput(
            Width, Height, ToolbarHeight, SidebarWidth, side, toolbar, sidebar,
            toolbarProgress, sidebarProgress, Shadow, frame));

    [Fact]
    public void Immersive_Hidden_PageFillsEverything_PanelsOffscreen()
    {
        var layout = Compute(PanelBehavior.AutoHide, PanelBehavior.AutoHide);

        Assert.Equal(new LayoutRect(0, 0, Width, Height), layout.Content);
        Assert.False(layout.IsToolbarVisible);
        Assert.False(layout.IsSidebarVisible);
        Assert.True(layout.Toolbar.Bottom <= 0);
        Assert.True(layout.Sidebar.Right <= 0);
    }

    [Fact]
    public void Immersive_Revealed_PanelsOverlayWithoutMovingPage()
    {
        var layout = Compute(PanelBehavior.AutoHide, PanelBehavior.AutoHide, 1, 1);

        Assert.Equal(new LayoutRect(0, 0, Width, Height), layout.Content);
        Assert.Equal(new LayoutRect(0, 0, Width, ToolbarHeight + Shadow), layout.Toolbar);
        Assert.Equal(0, layout.Sidebar.X);
        Assert.Equal(SidebarWidth + Shadow, layout.Sidebar.Width);
        Assert.True(layout.ToolbarHasShadow);
    }

    [Fact]
    public void BothRevealed_SidebarTucksUnderToolbar()
    {
        var layout = Compute(PanelBehavior.AutoHide, PanelBehavior.AutoHide, 1, 1);

        Assert.Equal(ToolbarHeight, layout.Sidebar.Y);
        Assert.Equal(Height - ToolbarHeight, layout.Sidebar.Height);
    }

    [Fact]
    public void SidebarAlone_SpansFullHeight()
    {
        var layout = Compute(PanelBehavior.AutoHide, PanelBehavior.AutoHide, 0, 1);

        Assert.Equal(0, layout.Sidebar.Y);
        Assert.Equal(Height, layout.Sidebar.Height);
    }

    [Fact]
    public void HalfwayAnimation_ToolbarPartlyAboveEdge()
    {
        var layout = Compute(PanelBehavior.AutoHide, PanelBehavior.AutoHide, 0.5, 0);

        Assert.Equal(-(ToolbarHeight + Shadow) / 2, layout.Toolbar.Y);
        Assert.True(layout.IsToolbarVisible);
    }

    [Fact]
    public void RightSide_SlidesInFromRightEdge()
    {
        var hidden = Compute(PanelBehavior.AutoHide, PanelBehavior.AutoHide, 0, 0, PanelSide.Right);
        var shown = Compute(PanelBehavior.AutoHide, PanelBehavior.AutoHide, 0, 1, PanelSide.Right);

        Assert.Equal(Width, hidden.Sidebar.X);
        Assert.Equal(Width - SidebarWidth - Shadow, shown.Sidebar.X);
        Assert.Equal(Width, shown.Sidebar.Right);
    }

    [Fact]
    public void Classic_PageInsetByBothPanels_NoShadows()
    {
        var layout = Compute(PanelBehavior.Docked, PanelBehavior.Docked);

        Assert.Equal(new LayoutRect(SidebarWidth, ToolbarHeight, Width - SidebarWidth, Height - ToolbarHeight), layout.Content);
        Assert.Equal(new LayoutRect(0, 0, Width, ToolbarHeight), layout.Toolbar);
        Assert.Equal(new LayoutRect(0, ToolbarHeight, SidebarWidth, Height - ToolbarHeight), layout.Sidebar);
        Assert.False(layout.ToolbarHasShadow);
        Assert.False(layout.SidebarHasShadow);
    }

    [Fact]
    public void Classic_RightSide_InsetsFromRight()
    {
        var layout = Compute(PanelBehavior.Docked, PanelBehavior.Docked, side: PanelSide.Right);

        Assert.Equal(0, layout.Content.X);
        Assert.Equal(Width - SidebarWidth, layout.Content.Width);
        Assert.Equal(Width - SidebarWidth, layout.Sidebar.X);
    }

    [Fact]
    public void Custom_DockedToolbar_AutoSidebar()
    {
        var layout = Compute(PanelBehavior.Docked, PanelBehavior.AutoHide, 0, 1);

        Assert.Equal(new LayoutRect(0, ToolbarHeight, Width, Height - ToolbarHeight), layout.Content);
        Assert.Equal(ToolbarHeight, layout.Sidebar.Y);
        Assert.True(layout.SidebarHasShadow);
    }

    [Fact]
    public void FrameInset_KeepsResizeBordersFree()
    {
        var layout = Compute(PanelBehavior.AutoHide, PanelBehavior.AutoHide, frame: 4);

        Assert.Equal(new LayoutRect(4, 4, Width - 8, Height - 8), layout.Content);
    }

    [Fact]
    public void ProgressOutOfRange_IsClamped()
    {
        var layout = Compute(PanelBehavior.AutoHide, PanelBehavior.AutoHide, 3, -2);

        Assert.Equal(0, layout.Toolbar.Y);
        Assert.False(layout.IsSidebarVisible);
    }
}
