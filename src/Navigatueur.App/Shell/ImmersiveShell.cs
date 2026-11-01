using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Navigatueur.App.Interop;
using Navigatueur.App.Services;
using Navigatueur.Core.Interface;
using Navigatueur.Core.Settings;

namespace Navigatueur.App.Shell;

/// <summary>What the shell needs to know about a panel it shows and hides.</summary>
public interface IShellPanel
{
    /// <summary>True while something other than the mouse needs the panel open: keyboard focus inside, an open menu, a drag in progress…</summary>
    bool IsHeld { get; }

    /// <summary>
    /// Switches the panel between its floating look (auto-hide: rounded inner
    /// corners and a shadow over the page, when <paramref name="effects"/>) and
    /// its docked look (flush, flat, separated by a hairline).
    /// </summary>
    void ApplyChrome(bool floating, bool effects, PanelSide side, double shadowSize);
}

/// <summary>
/// Runs the immersive interface: decides when the toolbar and the tab column
/// show, animates them, and lays out the page and both panels.
///
/// The page is a WebView2 child window owned by another process, so WPF never
/// sees the mouse while it's over the page. The shell therefore samples the
/// cursor itself (<see cref="SampleInterval"/>) and feeds one
/// <see cref="EdgeRevealStateMachine"/> per panel; those decide, the shell
/// animates. All geometry comes from <see cref="ShellLayout"/>.
/// </summary>
public sealed class ImmersiveShell
{
    public const double ToolbarHeight = 48;
    public const double ShadowSize = 18;

    /// <summary>Extra room around a revealed panel that still counts as "over it", so a slightly overshooting mouse doesn't start the hide countdown.</summary>
    private const double HoverMargin = 20;

    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan IdleSampleInterval = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan RevealDuration = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(170);

    private readonly Window _window;
    private readonly FrameworkElement _viewport;
    private readonly FrameworkElement _content;
    private readonly OverlayHost _toolbarHost;
    private readonly OverlayHost _sidebarHost;
    private readonly OverlayHost _loadingHost;
    private readonly IShellPanel _toolbar;
    private readonly IShellPanel _sidebar;
    private readonly WindowFrameController _frame;
    private readonly InterfaceSettingsService _settings;
    private readonly Action _focusPage;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _sampler;
    private readonly EdgeRevealStateMachine _toolbarMachine;
    private readonly EdgeRevealStateMachine _sidebarMachine;
    private readonly Tween _toolbarTween = new();
    private readonly Tween _sidebarTween = new();
    private bool _isRendering;

    private PanelBehavior _toolbarBehavior;
    private PanelBehavior _sidebarBehavior;
    private bool _isPageFullScreen;
    private bool _isLoading;
    private ShellLayoutResult _layout;

    public ImmersiveShell(
        Window window,
        FrameworkElement viewport,
        FrameworkElement content,
        OverlayHost toolbarHost,
        OverlayHost sidebarHost,
        OverlayHost loadingHost,
        IShellPanel toolbar,
        IShellPanel sidebar,
        WindowFrameController frame,
        InterfaceSettingsService settings,
        Action focusPage)
    {
        _window = window;
        _viewport = viewport;
        _content = content;
        _toolbarHost = toolbarHost;
        _sidebarHost = sidebarHost;
        _loadingHost = loadingHost;
        _toolbar = toolbar;
        _sidebar = sidebar;
        _frame = frame;
        _settings = settings;
        _focusPage = focusPage;

        var current = settings.Current;
        _toolbarMachine = new EdgeRevealStateMachine(
            TimeSpan.FromMilliseconds(current.RevealDelayMilliseconds),
            TimeSpan.FromMilliseconds(current.HideDelayMilliseconds));
        _sidebarMachine = new EdgeRevealStateMachine(
            TimeSpan.FromMilliseconds(current.RevealDelayMilliseconds),
            TimeSpan.FromMilliseconds(current.HideDelayMilliseconds));

        _sampler = new DispatcherTimer(DispatcherPriority.Input) { Interval = SampleInterval };
        _sampler.Tick += (_, _) => Sample();

        _toolbarHost.PassThroughHitTest = _frame.IsOnResizeBorder;
        _sidebarHost.PassThroughHitTest = _frame.IsOnResizeBorder;
        _toolbarHost.SourceCreated += (_, _) => ApplyChrome();
        _sidebarHost.SourceCreated += (_, _) => ApplyChrome();

        _viewport.SizeChanged += (_, _) => ApplyLayout();
        _frame.FrameChanged += (_, _) => RefreshBehaviors();
        _settings.Changed += (_, _) => RefreshBehaviors();
        _window.StateChanged += (_, _) => UpdateSamplerState();
        _window.IsVisibleChanged += (_, _) => UpdateSamplerState();

        RefreshBehaviors();
        UpdateSamplerState();
    }

    /// <summary>Raised whenever a panel finishes appearing or starts disappearing — panels use it to manage focus.</summary>
    public event EventHandler? VisibilityChanged;

    public PanelBehavior ToolbarBehavior => _toolbarBehavior;

    public PanelBehavior SidebarBehavior => _sidebarBehavior;

    public bool IsToolbarShown => _toolbarBehavior == PanelBehavior.Docked || _toolbarTween.Target > 0;

    public bool IsSidebarShown => _sidebarBehavior == PanelBehavior.Docked || _sidebarTween.Target > 0;

    /// <summary>
    /// A page went fullscreen (HTML5 video…): both panels hide and nothing
    /// reveals until it exits, so the video is never covered by accident.
    /// </summary>
    public bool IsPageFullScreen
    {
        get => _isPageFullScreen;
        set
        {
            if (_isPageFullScreen == value)
            {
                return;
            }

            _isPageFullScreen = value;
            RefreshBehaviors();
        }
    }

    /// <summary>The active tab is loading; shows the thin progress line when the toolbar (which has its own) is hidden.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            ApplyLayout();
        }
    }

    public void RevealToolbar()
    {
        if (_toolbarBehavior == PanelBehavior.Docked)
        {
            return;
        }

        _toolbarMachine.Reveal(_clock.Elapsed);
        AnimateTo(_toolbarTween, 1);
    }

    public void HideToolbar()
    {
        if (_toolbarBehavior == PanelBehavior.Docked)
        {
            return;
        }

        _toolbarMachine.Hide();
        AnimateTo(_toolbarTween, 0);
    }

    public void RevealSidebar()
    {
        if (_sidebarBehavior == PanelBehavior.Docked)
        {
            return;
        }

        _sidebarMachine.Reveal(_clock.Elapsed);
        AnimateTo(_sidebarTween, 1);
    }

    public void HideSidebar()
    {
        if (_sidebarBehavior == PanelBehavior.Docked)
        {
            return;
        }

        _sidebarMachine.Hide();
        AnimateTo(_sidebarTween, 0);
    }

    /// <summary>Re-reads settings and fullscreen state: which panels auto-hide, timings, side, width.</summary>
    public void RefreshBehaviors()
    {
        var current = _settings.Current;
        var forceAutoHide = _frame.IsFullScreen || _isPageFullScreen;

        var toolbar = forceAutoHide ? PanelBehavior.AutoHide : current.ResolveToolbarBehavior();
        var sidebar = forceAutoHide ? PanelBehavior.AutoHide : current.ResolveSidebarBehavior();

        _toolbarMachine.RevealDelay = _sidebarMachine.RevealDelay = TimeSpan.FromMilliseconds(current.RevealDelayMilliseconds);
        _toolbarMachine.HideDelay = _sidebarMachine.HideDelay = TimeSpan.FromMilliseconds(current.HideDelayMilliseconds);

        ApplyBehavior(ref _toolbarBehavior, toolbar, _toolbarMachine, _toolbarTween);
        ApplyBehavior(ref _sidebarBehavior, sidebar, _sidebarMachine, _sidebarTween);

        if (_isPageFullScreen)
        {
            _toolbarMachine.Hide();
            _sidebarMachine.Hide();
            SnapTo(_toolbarTween, 0);
            SnapTo(_sidebarTween, 0);
        }

        ApplyChrome();
        ApplyLayout();
        UpdateSamplerState();
    }

    private void ApplyBehavior(ref PanelBehavior field, PanelBehavior value, EdgeRevealStateMachine machine, Tween tween)
    {
        if (value == PanelBehavior.Docked)
        {
            machine.Hide();
            SnapTo(tween, 1);
        }
        else if (field == PanelBehavior.Docked)
        {
            // Docked → auto-hide: tuck it away now; it's back on the next hover.
            machine.Hide();
            SnapTo(tween, 0);
        }

        field = value;
    }

    private void ApplyChrome()
    {
        var current = _settings.Current;
        var side = current.SidebarSideValue;
        _toolbar.ApplyChrome(
            _toolbarBehavior == PanelBehavior.AutoHide,
            current.ArePanelEffectsEnabled && _toolbarHost.SupportsTransparency,
            side,
            ShadowSize);
        _sidebar.ApplyChrome(
            _sidebarBehavior == PanelBehavior.AutoHide,
            current.ArePanelEffectsEnabled && _sidebarHost.SupportsTransparency,
            side,
            ShadowSize);
        ApplyLayout();
    }

    private bool ToolbarHasEffects => _settings.Current.ArePanelEffectsEnabled && _toolbarHost.SupportsTransparency;

    private void ApplyLayout()
    {
        var width = _viewport.ActualWidth;
        var height = _viewport.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var current = _settings.Current;
        var shadow = ToolbarHasEffects ? ShadowSize : 0;
        _layout = ShellLayout.Compute(new ShellLayoutInput(
            width,
            height,
            ToolbarHeight,
            current.SidebarWidth,
            current.SidebarSideValue,
            _toolbarBehavior,
            _sidebarBehavior,
            _toolbarTween.Value,
            _sidebarTween.Value,
            shadow,
            _frame.FrameInset));

        Place(_toolbarHost, _layout.Toolbar, _layout.IsToolbarVisible);
        Place(_sidebarHost, _layout.Sidebar, _layout.IsSidebarVisible);

        var content = _layout.Content;
        _content.Margin = new Thickness(content.X, content.Y, width - content.Right, height - content.Bottom);

        var showLoading = _isLoading && _toolbarBehavior == PanelBehavior.AutoHide && !_layout.IsToolbarVisible && !_isPageFullScreen;
        Place(_loadingHost, new LayoutRect(content.X, content.Y, content.Width, 3), showLoading);
    }

    private static void Place(FrameworkElement host, LayoutRect rect, bool visible)
    {
        if (!visible || rect.Width <= 0 || rect.Height <= 0)
        {
            host.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(host, Math.Round(rect.X));
        Canvas.SetTop(host, Math.Round(rect.Y));
        host.Width = Math.Round(rect.Width);
        host.Height = Math.Round(rect.Height);
        host.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------ sampling

    private void UpdateSamplerState()
    {
        var anyAutoHide = _toolbarBehavior == PanelBehavior.AutoHide || _sidebarBehavior == PanelBehavior.AutoHide;
        var shouldRun = anyAutoHide && _window.IsVisible && _window.WindowState != WindowState.Minimized;
        if (shouldRun && !_sampler.IsEnabled)
        {
            _sampler.Start();
        }
        else if (!shouldRun && _sampler.IsEnabled)
        {
            _sampler.Stop();
        }
    }

    private void Sample()
    {
        if (_frame.Handle == IntPtr.Zero || !_viewport.IsVisible || PresentationSource.FromVisual(_viewport) is null ||
            !NativeMethods.GetCursorPos(out var cursorPos))
        {
            return;
        }

        var cursor = new Point(cursorPos.X, cursorPos.Y);
        var rootUnderCursor = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(cursorPos), NativeMethods.GA_ROOT);
        var overWindow = rootUnderCursor == _frame.Handle;

        var topLeft = _viewport.PointToScreen(new Point(0, 0));
        var bottomRight = _viewport.PointToScreen(new Point(_viewport.ActualWidth, _viewport.ActualHeight));
        var scale = _viewport.ActualWidth > 0 ? (bottomRight.X - topLeft.X) / _viewport.ActualWidth : 1;

        // The edges the mouse can actually reach: if part of the window lies
        // off-screen, its edge zone moves to the screen's edge.
        if (NativeMethods.TryGetMonitorInfo(_frame.Handle, out var monitor))
        {
            var screen = monitor.rcMonitor;
            topLeft = new Point(Math.Max(topLeft.X, screen.Left), Math.Max(topLeft.Y, screen.Top));
            bottomRight = new Point(Math.Min(bottomRight.X, screen.Right), Math.Min(bottomRight.Y, screen.Bottom));
        }

        var current = _settings.Current;
        var trigger = current.EdgeTriggerSize * scale;
        var revealEnabled = current.IsEdgeRevealEnabled && overWindow;

        var withinX = cursor.X >= topLeft.X && cursor.X < bottomRight.X;
        var withinY = cursor.Y >= topLeft.Y && cursor.Y < bottomRight.Y;
        var inTopZone = revealEnabled && withinX && cursor.Y >= topLeft.Y && cursor.Y < topLeft.Y + trigger;
        var inSideZone = revealEnabled && withinY && (current.SidebarSideValue == PanelSide.Left
            ? cursor.X >= topLeft.X && cursor.X < topLeft.X + trigger
            : cursor.X < bottomRight.X && cursor.X >= bottomRight.X - trigger);

        var buttonDown = NativeMethods.IsAnyMouseButtonDown();
        var now = _clock.Elapsed;

        if (_toolbarBehavior == PanelBehavior.AutoHide)
        {
            var over = _layout.IsToolbarVisible && IsOver(cursor, BodyRect(_layout.Toolbar, isToolbar: true), scale);
            var sample = new EdgeRevealSample(now, inTopZone, over, _toolbar.IsHeld, buttonDown, _isPageFullScreen);
            if (_toolbarMachine.Update(sample))
            {
                AnimateTo(_toolbarTween, _toolbarMachine.IsRevealed ? 1 : 0);
            }
        }

        if (_sidebarBehavior == PanelBehavior.AutoHide)
        {
            var over = _layout.IsSidebarVisible && IsOver(cursor, BodyRect(_layout.Sidebar, isToolbar: false), scale);
            var sample = new EdgeRevealSample(now, inSideZone, over, _sidebar.IsHeld, buttonDown, _isPageFullScreen);
            if (_sidebarMachine.Update(sample))
            {
                AnimateTo(_sidebarTween, _sidebarMachine.IsRevealed ? 1 : 0);
            }
        }

        // Full rate only while it matters; a window the cursor isn't over is checked a few times a second.
        var busy = overWindow || _toolbarMachine.IsRevealed || _sidebarMachine.IsRevealed;
        var interval = busy ? SampleInterval : IdleSampleInterval;
        if (_sampler.Interval != interval)
        {
            _sampler.Interval = interval;
        }
    }

    /// <summary>The panel's visible body (its rect minus the shadow area), in viewport coordinates.</summary>
    private LayoutRect BodyRect(LayoutRect outer, bool isToolbar)
    {
        if (isToolbar)
        {
            return outer with { Height = ToolbarHeight };
        }

        var width = _settings.Current.SidebarWidth;
        return _settings.Current.SidebarSideValue == PanelSide.Left
            ? outer with { Width = width }
            : outer with { X = outer.Right - width, Width = width };
    }

    private bool IsOver(Point cursor, LayoutRect rect, double scale)
    {
        var topLeft = _viewport.PointToScreen(new Point(rect.X, rect.Y));
        var margin = HoverMargin * scale;
        return cursor.X >= topLeft.X - margin
            && cursor.X < topLeft.X + rect.Width * scale + margin
            && cursor.Y >= topLeft.Y - margin
            && cursor.Y < topLeft.Y + rect.Height * scale + margin;
    }

    // ----------------------------------------------------------- animation

    private sealed class Tween
    {
        public double Value;
        public double Target;
        public double From;
        public TimeSpan Start;
        public TimeSpan Duration;
        public bool IsRunning;
    }

    private void SnapTo(Tween tween, double value)
    {
        tween.Value = tween.Target = tween.From = value;
        tween.IsRunning = false;
    }

    private void AnimateTo(Tween tween, double target)
    {
        if (Math.Abs(tween.Target - target) < 0.0001 && (tween.IsRunning || Math.Abs(tween.Value - target) < 0.0001))
        {
            return;
        }

        var hiding = target < tween.Value;
        if (hiding)
        {
            MoveFocusOutOfHidingPanel(tween);
        }

        if (!_settings.Current.AreAnimationsEnabled)
        {
            SnapTo(tween, target);
            ApplyLayout();
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Appearing from fully hidden: make the panel's window exist on screen
        // right away (just off the edge), so focus can move into it before the
        // first animation frame — Ctrl+L types into the address bar immediately.
        if (!hiding && tween.Value <= 0)
        {
            tween.Value = 0.001;
            ApplyLayout();
        }

        tween.From = tween.Value;
        tween.Target = target;
        tween.Start = _clock.Elapsed;

        // Shorter when interrupted halfway, so reversing direction never feels sluggish.
        var full = hiding ? HideDuration : RevealDuration;
        tween.Duration = TimeSpan.FromTicks((long)(full.Ticks * Math.Max(0.25, Math.Abs(target - tween.Value))));
        tween.IsRunning = true;

        if (!_isRendering)
        {
            CompositionTarget.Rendering += OnRendering;
            _isRendering = true;
        }
    }

    private void MoveFocusOutOfHidingPanel(Tween tween)
    {
        var host = ReferenceEquals(tween, _toolbarTween) ? _toolbarHost : _sidebarHost;
        if (host.Child is { IsKeyboardFocusWithin: true })
        {
            _focusPage();
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var anyRunning = Step(_toolbarTween, now) | Step(_sidebarTween, now);
        ApplyLayout();

        if (!anyRunning)
        {
            CompositionTarget.Rendering -= OnRendering;
            _isRendering = false;
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool Step(Tween tween, TimeSpan now)
    {
        if (!tween.IsRunning)
        {
            return false;
        }

        var t = tween.Duration <= TimeSpan.Zero ? 1 : Math.Clamp((now - tween.Start) / tween.Duration, 0, 1);

        // Ease-out cubic when appearing (quick start, soft landing), ease-in when leaving.
        var eased = tween.Target > tween.From ? 1 - Math.Pow(1 - t, 3) : t * t * t;
        tween.Value = tween.From + (tween.Target - tween.From) * eased;

        if (t >= 1)
        {
            tween.Value = tween.Target;
            tween.IsRunning = false;
        }

        return tween.IsRunning;
    }
}
