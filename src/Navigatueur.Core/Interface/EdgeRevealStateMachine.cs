namespace Navigatueur.Core.Interface;

/// <summary>
/// One snapshot of everything that decides whether an auto-hidden panel
/// should be showing, sampled by the shell's cursor monitor.
/// </summary>
/// <param name="Time">Monotonic timestamp of the sample.</param>
/// <param name="InTriggerZone">The cursor sits in the thin band along this panel's window edge.</param>
/// <param name="OverPanel">The cursor is over the revealed panel, or within a small margin around it.</param>
/// <param name="IsHeld">Something keeps the panel open regardless of the mouse: keyboard focus inside it, one of its menus is open, a tab is being dragged…</param>
/// <param name="IsMouseButtonDown">A mouse button is pressed — the user is selecting text, dragging a scrollbar or resizing, not reaching for the panel.</param>
/// <param name="IsSuppressed">Reveals are off right now (a video is playing fullscreen, hover reveal is disabled…).</param>
public readonly record struct EdgeRevealSample(
    TimeSpan Time,
    bool InTriggerZone,
    bool OverPanel,
    bool IsHeld,
    bool IsMouseButtonDown,
    bool IsSuppressed);

/// <summary>
/// Decides when an edge panel appears and disappears. Pure logic with no
/// timers of its own — the caller feeds it samples, which keeps it testable
/// and lets both panels share one polling loop while staying independent.
///
/// The rules that keep reveals intentional:
///  - the cursor has to *rest* in the trigger zone for <see cref="RevealDelay"/>
///    with no mouse button held; passing through on the way somewhere else, or
///    pressing near the edge (a site's own header, a text selection), does nothing;
///  - once shown, the panel stays while the cursor is over it (plus a margin),
///    back in the trigger zone, or the panel is held, and leaves
///    <see cref="HideDelay"/> after none of that is true anymore;
///  - clicking into the page dismisses it immediately: the user is done with it.
/// </summary>
public sealed class EdgeRevealStateMachine
{
    /// <summary>Minimum time a keyboard-triggered reveal survives before the hide countdown may finish, even with a zero hide delay — focus moves into the panel asynchronously.</summary>
    private static readonly TimeSpan KeyboardRevealGrace = TimeSpan.FromMilliseconds(800);

    private TimeSpan? _zoneEnteredAt;
    private TimeSpan? _leftAt;
    private TimeSpan _graceUntil;

    public EdgeRevealStateMachine(TimeSpan revealDelay, TimeSpan hideDelay)
    {
        RevealDelay = revealDelay;
        HideDelay = hideDelay;
    }

    public TimeSpan RevealDelay { get; set; }

    public TimeSpan HideDelay { get; set; }

    public bool IsRevealed { get; private set; }

    /// <summary>Feeds one sample; returns true when <see cref="IsRevealed"/> changed as a result.</summary>
    public bool Update(EdgeRevealSample sample)
    {
        var wasRevealed = IsRevealed;

        if (sample.IsSuppressed && !sample.IsHeld)
        {
            Reset();
            IsRevealed = false;
            return wasRevealed;
        }

        if (!IsRevealed)
        {
            UpdateHidden(sample);
        }
        else
        {
            UpdateRevealed(sample);
        }

        return wasRevealed != IsRevealed;
    }

    /// <summary>Shows the panel now (keyboard shortcut, toolbar button). It then follows the normal hide rules, after a short grace period.</summary>
    public void Reveal(TimeSpan now)
    {
        IsRevealed = true;
        _zoneEnteredAt = null;
        _leftAt = null;
        _graceUntil = now + KeyboardRevealGrace;
    }

    public void Hide()
    {
        Reset();
        IsRevealed = false;
    }

    private void UpdateHidden(EdgeRevealSample sample)
    {
        if (sample.IsHeld)
        {
            Reveal(sample.Time);
            return;
        }

        if (!sample.InTriggerZone || sample.IsMouseButtonDown)
        {
            _zoneEnteredAt = null;
            return;
        }

        _zoneEnteredAt ??= sample.Time;
        if (sample.Time - _zoneEnteredAt.Value >= RevealDelay)
        {
            IsRevealed = true;
            _zoneEnteredAt = null;
            _leftAt = null;
            _graceUntil = TimeSpan.Zero;
        }
    }

    private void UpdateRevealed(EdgeRevealSample sample)
    {
        if (sample.IsHeld || sample.OverPanel)
        {
            _leftAt = null;
            return;
        }

        if (sample.IsMouseButtonDown && sample.Time >= _graceUntil)
        {
            Hide();
            return;
        }

        if (sample.InTriggerZone)
        {
            _leftAt = null;
            return;
        }

        _leftAt ??= sample.Time;
        var hideAt = _leftAt.Value + HideDelay;
        if (hideAt < _graceUntil)
        {
            hideAt = _graceUntil;
        }

        if (sample.Time >= hideAt)
        {
            Hide();
        }
    }

    private void Reset()
    {
        _zoneEnteredAt = null;
        _leftAt = null;
        _graceUntil = TimeSpan.Zero;
    }
}
