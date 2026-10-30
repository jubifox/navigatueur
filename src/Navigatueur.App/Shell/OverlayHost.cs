using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Navigatueur.App.Interop;

namespace Navigatueur.App.Shell;

/// <summary>
/// Hosts a WPF panel in a *child* window of the main window, so it can sit
/// on top of WebView2.
///
/// Why: WebView2 renders the page into a native HWND. WPF draws everything
/// else of a window into one surface underneath its child HWNDs, so no WPF
/// element in that surface can ever appear over the page ("airspace").
/// Earlier versions worked around this with separate top-level windows
/// glued to the main one (TabSidebarWindow, WebViewOverlayWindow), which
/// lagged behind during moves and made the app feel like several programs.
/// A child window has neither problem: it belongs to the main window, moves
/// with it for free, and can be ordered above the WebView2 HWND.
///
/// The panel's HwndSource asks for per-pixel transparency, which Windows 8+
/// supports for child windows: rounded corners and shadows over the page are
/// real. <see cref="SupportsTransparency"/> reports what the system actually
/// granted so panels can fall back to a flat, opaque look otherwise.
///
/// Positioning is ordinary WPF layout (Canvas.Left/Top/Width/Height): HwndHost
/// moves the HWND after every layout pass, and <see cref="OnWindowPositionChanged"/>
/// re-asserts the top of the Z-order each time, so a tab's WebView2 created
/// later can never end up above a panel.
///
/// Keyboard: the panel's HwndSource is registered as this host's child
/// keyboard sink, WPF's own scheme for nested HwndSources. Keys typed in a
/// panel are pre-processed once, by the main window (so window-level
/// shortcuts see them first), then forwarded here and on into the panel —
/// never processed twice.
/// </summary>
public sealed class OverlayHost : HwndHost
{
    private HwndSource? _source;
    private FrameworkElement? _child;
    private ChildKeyboardSite? _keyboardSite;

    /// <summary>The panel shown in this host. Can be set before or after the window is created.</summary>
    public FrameworkElement? Child
    {
        get => _child;
        set
        {
            _child = value;
            if (_source is not null)
            {
                _source.RootVisual = value;
            }
        }
    }

    /// <summary>
    /// Whether to ask for a layered (per-pixel transparent) child window. Read
    /// once, when the window is created: turning panel effects off in Settings
    /// gives plain opaque child windows from the next launch on — the safe
    /// fallback should a machine ever compose layered child windows badly.
    /// </summary>
    public bool RequestTransparency { get; set; } = true;

    /// <summary>True once created if the panel window really has per-pixel transparency.</summary>
    public bool SupportsTransparency { get; private set; }

    /// <summary>Raised once the child window exists (and <see cref="SupportsTransparency"/> is known).</summary>
    public event EventHandler? SourceCreated;

    /// <summary>
    /// Given a cursor position in physical screen pixels, returns true when a
    /// mouse action there belongs to the main window rather than the panel —
    /// i.e. the point is on the main window's resize border, which a panel
    /// flush with the window edge would otherwise swallow.
    /// </summary>
    public Func<Point, bool>? PassThroughHitTest { get; set; }

    public void BringToFront()
    {
        if (Handle != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(
                Handle,
                NativeMethods.HWND_TOP,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_ASYNCWINDOWPOS);
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var parameters = new HwndSourceParameters("NavigatueurPanel")
        {
            ParentWindow = hwndParent.Handle,
            WindowStyle = NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPSIBLINGS | NativeMethods.WS_CLIPCHILDREN,
            PositionX = 0,
            PositionY = 0,
            Width = 1,
            Height = 1,
            UsesPerPixelTransparency = RequestTransparency,
        };

        _source = new HwndSource(parameters);
        SupportsTransparency = _source.UsesPerPixelOpacity;
        if (SupportsTransparency && _source.CompositionTarget is { } target)
        {
            target.BackgroundColor = Colors.Transparent;
        }

        _source.RootVisual = _child;
        _source.AddHook(PanelWindowHook);
        ((IKeyboardInputSink)_source).KeyboardInputSite = ((IKeyboardInputSink)this).RegisterKeyboardInputSink(_source);
        SourceCreated?.Invoke(this, EventArgs.Empty);
        return new HandleRef(this, _source.Handle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_source is null)
        {
            return;
        }

        _source.RemoveHook(PanelWindowHook);
        _keyboardSite?.Unregister();
        _keyboardSite = null;
        _source.RootVisual = null;
        _source.Dispose();
        _source = null;
    }

    /// <summary>Same as HwndHost's own implementation, except it places the panel at the top of the Z-order instead of leaving the order alone.</summary>
    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        if (Handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOP,
            (int)rcBoundingBox.X,
            (int)rcBoundingBox.Y,
            (int)rcBoundingBox.Width,
            (int)rcBoundingBox.Height,
            NativeMethods.SWP_ASYNCWINDOWPOS | NativeMethods.SWP_NOCOPYBITS | NativeMethods.SWP_NOACTIVATE);
    }

    // ----------------------------------------------- keyboard forwarding

    private IKeyboardInputSink? ChildSink => _keyboardSite?.Sink;

    protected override IKeyboardInputSite RegisterKeyboardInputSinkCore(IKeyboardInputSink sink) =>
        _keyboardSite = new ChildKeyboardSite(sink);

    protected override bool TranslateAcceleratorCore(ref MSG msg, ModifierKeys modifiers) =>
        ChildSink?.TranslateAccelerator(ref msg, modifiers) ?? false;

    protected override bool TranslateCharCore(ref MSG msg, ModifierKeys modifiers) =>
        ChildSink?.TranslateChar(ref msg, modifiers) ?? false;

    protected override bool OnMnemonicCore(ref MSG msg, ModifierKeys modifiers) =>
        ChildSink?.OnMnemonic(ref msg, modifiers) ?? false;

    protected override bool TabIntoCore(TraversalRequest request) =>
        ChildSink?.TabInto(request) ?? false;

    /// <summary>Links the panel's HwndSource to this host. Tabbing past the panel's last control wraps around inside it rather than escaping into the page.</summary>
    private sealed class ChildKeyboardSite : IKeyboardInputSite
    {
        public ChildKeyboardSite(IKeyboardInputSink sink)
        {
            Sink = sink;
        }

        public IKeyboardInputSink? Sink { get; private set; }

        IKeyboardInputSink IKeyboardInputSite.Sink => Sink!;

        public void Unregister() => Sink = null;

        public bool OnNoMoreTabStops(TraversalRequest request) => false;
    }

    private IntPtr PanelWindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_NCHITTEST && PassThroughHitTest is { } passThrough)
        {
            // lParam packs signed 16-bit screen coordinates (negative on monitors left of/above the primary).
            var x = unchecked((short)(lParam.ToInt64() & 0xFFFF));
            var y = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
            if (passThrough(new Point(x, y)))
            {
                // Same thread as the main window, so Windows then asks it — and
                // its WindowChrome answers with the right resize edge.
                handled = true;
                return new IntPtr(NativeMethods.HTTRANSPARENT);
            }
        }

        return IntPtr.Zero;
    }
}
