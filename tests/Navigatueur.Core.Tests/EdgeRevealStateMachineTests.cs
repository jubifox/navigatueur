using Navigatueur.Core.Interface;
using Xunit;

namespace Navigatueur.Core.Tests;

public class EdgeRevealStateMachineTests
{
    private static readonly TimeSpan Reveal = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan Hide = TimeSpan.FromMilliseconds(600);

    private static EdgeRevealSample At(
        int ms,
        bool zone = false,
        bool over = false,
        bool held = false,
        bool button = false,
        bool suppressed = false) =>
        new(TimeSpan.FromMilliseconds(ms), zone, over, held, button, suppressed);

    [Fact]
    public void RestingInZone_RevealsOnlyAfterDelay()
    {
        var machine = new EdgeRevealStateMachine(Reveal, Hide);

        machine.Update(At(0, zone: true));
        machine.Update(At(100, zone: true));
        Assert.False(machine.IsRevealed);

        Assert.True(machine.Update(At(150, zone: true)));
        Assert.True(machine.IsRevealed);
    }

    [Fact]
    public void PassingThroughZone_DoesNotReveal()
    {
        var machine = new EdgeRevealStateMachine(Reveal, Hide);

        machine.Update(At(0, zone: true));
        machine.Update(At(100));
        machine.Update(At(125, zone: true));
        machine.Update(At(250, zone: true));

        Assert.False(machine.IsRevealed); // dwell restarted at 125, only 125ms so far
    }

    [Fact]
    public void MouseButtonHeld_InZone_NeverReveals()
    {
        var machine = new EdgeRevealStateMachine(Reveal, Hide);

        for (var t = 0; t <= 1000; t += 25)
        {
            machine.Update(At(t, zone: true, button: true));
        }

        Assert.False(machine.IsRevealed);
    }

    [Fact]
    public void ReleasingButton_RestartsDwell()
    {
        var machine = new EdgeRevealStateMachine(Reveal, Hide);

        machine.Update(At(0, zone: true, button: true));
        machine.Update(At(300, zone: true));
        Assert.False(machine.IsRevealed);
        machine.Update(At(450, zone: true));
        Assert.True(machine.IsRevealed);
    }

    [Fact]
    public void ZeroRevealDelay_RevealsOnFirstSample()
    {
        var machine = new EdgeRevealStateMachine(TimeSpan.Zero, Hide);

        Assert.True(machine.Update(At(0, zone: true)));
    }

    [Fact]
    public void Revealed_StaysWhileOverPanel_HidesAfterDelayOnceLeft()
    {
        var machine = Revealed();

        machine.Update(At(1000, over: true));
        machine.Update(At(3000, over: true));
        Assert.True(machine.IsRevealed);

        machine.Update(At(3100));
        machine.Update(At(3600));
        Assert.True(machine.IsRevealed);

        Assert.True(machine.Update(At(3700)));
        Assert.False(machine.IsRevealed);
    }

    [Fact]
    public void ReturningBeforeHideDelay_CancelsHide()
    {
        var machine = Revealed();

        machine.Update(At(1000));
        machine.Update(At(1500, over: true));
        machine.Update(At(1600));
        machine.Update(At(2100));
        Assert.True(machine.IsRevealed); // countdown restarted at 1600

        machine.Update(At(2200));
        Assert.False(machine.IsRevealed);
    }

    [Fact]
    public void Held_KeepsRevealedIndefinitely()
    {
        var machine = Revealed();

        for (var t = 1000; t <= 20000; t += 500)
        {
            machine.Update(At(t, held: true));
        }

        Assert.True(machine.IsRevealed);
    }

    [Fact]
    public void HeldWhileHidden_RevealsImmediately()
    {
        var machine = new EdgeRevealStateMachine(Reveal, Hide);

        Assert.True(machine.Update(At(0, held: true)));
    }

    [Fact]
    public void ClickingIntoPage_HidesImmediately()
    {
        var machine = Revealed();

        Assert.True(machine.Update(At(1000, button: true)));
        Assert.False(machine.IsRevealed);
    }

    [Fact]
    public void ClickingInsidePanel_DoesNotHide()
    {
        var machine = Revealed();

        machine.Update(At(1000, over: true, button: true));

        Assert.True(machine.IsRevealed);
    }

    [Fact]
    public void Suppression_HidesAndBlocksReveal()
    {
        var machine = Revealed();

        Assert.True(machine.Update(At(1000, over: true, suppressed: true)));
        for (var t = 1000; t <= 2000; t += 25)
        {
            machine.Update(At(t, zone: true, suppressed: true));
        }

        Assert.False(machine.IsRevealed);
    }

    [Fact]
    public void KeyboardReveal_SurvivesGracePeriodWithZeroHideDelay()
    {
        var machine = new EdgeRevealStateMachine(Reveal, TimeSpan.Zero);
        machine.Reveal(TimeSpan.FromMilliseconds(1000));

        machine.Update(At(1100));
        machine.Update(At(1500));
        Assert.True(machine.IsRevealed);

        machine.Update(At(1900));
        Assert.False(machine.IsRevealed);
    }

    [Fact]
    public void KeyboardReveal_ThenFocusHeld_StaysRevealed()
    {
        var machine = new EdgeRevealStateMachine(Reveal, Hide);
        machine.Reveal(TimeSpan.FromMilliseconds(1000));

        machine.Update(At(1100, held: true));
        machine.Update(At(9000, held: true));

        Assert.True(machine.IsRevealed);
    }

    [Fact]
    public void Hide_ResetsDwell()
    {
        var machine = new EdgeRevealStateMachine(Reveal, Hide);
        machine.Update(At(0, zone: true));
        machine.Hide();
        machine.Update(At(100, zone: true));

        Assert.False(machine.IsRevealed);
    }

    private static EdgeRevealStateMachine Revealed()
    {
        var machine = new EdgeRevealStateMachine(Reveal, Hide);
        machine.Update(At(0, zone: true));
        machine.Update(At(200, zone: true));
        Assert.True(machine.IsRevealed);
        return machine;
    }
}
