using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Navigatueur.App.Interop;
using Navigatueur.Core.Interface;

namespace Navigatueur.App.Shell;

/// <summary>
/// Everything about the main window's frame now that it has no title bar:
/// maximize without spilling off-screen, true fullscreen (F11), dragging and
/// resizing from the panels, and Windows 11 rounded corners.
/// </summary>
public sealed class WindowFrameController
{
    /// <summary>
    /// Width of the invisible resize band along each edge of a restored
    /// window. The page is inset by this much: WebView2's own child window
    /// would otherwise cover the band and swallow every resize attempt.
    /// </summary>
    public const double ResizeBorder = 4;

    private readonly Window _window;
    private readonly FrameworkElement _root;
    private WindowState _stateBeforeFullScreen;
    private Rect _boundsBeforeFullScreen;

    public WindowFrameController(Window window, FrameworkElement root)
    {
        _window = window;
        _root = root;

        _window.SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(_window).Handle;
            HwndSource.FromHwnd(Handle)?.AddHook(FrameWindowHook);
            ApplyRoundedCorners();
            UpdateMaximizedInset();
        };
        _window.StateChanged += (_, _) =>
        {
            UpdateMaximizedInset();
            FrameChanged?.Invoke(this, EventArgs.Empty);
        };
        _window.LocationChanged += (_, _) => UpdateMaximizedInset();

        // A window that starts maximized gets its final size after SourceInitialized
        // and never raises StateChanged: SizeChanged is what catches that case.
        _window.SizeChanged += (_, _) => UpdateMaximizedInset();
        _window.DpiChanged += (_, _) => UpdateMaximizedInset();
    }

    /// <summary>Raised when the window enters/leaves fullscreen or maximized state — anything that changes <see cref="FrameInset"/>.</summary>
    public event EventHandler? FrameChanged;

    public IntPtr Handle { get; private set; }

    public bool IsFullScreen { get; private set; }

    public bool IsMaximized => _window.WindowState == WindowState.Maximized;

    /// <summary>How far the page sits from the window edges: the resize band when restored, nothing when maximized or fullscreen.</summary>
    public double FrameInset => IsFullScreen || IsMaximized ? 0 : ResizeBorder;

    /// <summary>The window's normal-state bounds, whatever state it's in — what session restore should save.</summary>
    public Rect RestoreBounds => IsFullScreen
        ? _boundsBeforeFullScreen
        : _window.WindowState == WindowState.Normal
            ? new Rect(_window.Left, _window.Top, _window.Width, _window.Height)
            : _window.RestoreBounds;

    public bool WasMaximizedBeforeFullScreen => IsFullScreen && _stateBeforeFullScreen == WindowState.Maximized;

    public void ToggleFullScreen()
    {
        if (IsFullScreen)
        {
            ExitFullScreen();
        }
        else
        {
            EnterFullScreen();
        }
    }

    public void EnterFullScreen()
    {
        if (IsFullScreen || Handle == IntPtr.Zero || !NativeMethods.TryGetMonitorInfo(Handle, out var monitor))
        {
            return;
        }

        _stateBeforeFullScreen = _window.WindowState;
        _boundsBeforeFullScreen = RestoreBounds;
        IsFullScreen = true;

        if (_window.WindowState != WindowState.Normal)
        {
            _window.WindowState = WindowState.Normal;
        }

        SetResizeBorder(0);
        var bounds = ToDeviceIndependent(monitor.rcMonitor);
        _window.Left = bounds.Left;
        _window.Top = bounds.Top;
        _window.Width = bounds.Width;
        _window.Height = bounds.Height;

        UpdateMaximizedInset();
        FrameChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ExitFullScreen()
    {
        if (!IsFullScreen)
        {
            return;
        }

        IsFullScreen = false;
        SetResizeBorder(ResizeBorder);
        _window.Left = _boundsBeforeFullScreen.Left;
        _window.Top = _boundsBeforeFullScreen.Top;
        _window.Width = _boundsBeforeFullScreen.Width;
        _window.Height = _boundsBeforeFullScreen.Height;
        _window.WindowState = _stateBeforeFullScreen == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;

        UpdateMaximizedInset();
        FrameChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleMaximize()
    {
        if (IsFullScreen)
        {
            ExitFullScreen();
            return;
        }

        _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    public void Minimize() => _window.WindowState = WindowState.Minimized;

    /// <summary>
    /// Starts the native move loop as if the user had grabbed a title bar.
    /// Works from a panel's own child window, and — unlike Window.DragMove —
    /// also from a maximized window (dragging restores it, like any caption).
    /// </summary>
    public void StartDrag()
    {
        if (IsFullScreen || Handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
    }

    /// <summary>True when a physical screen point is on the resize band of a restored window — see OverlayHost.PassThroughHitTest.</summary>
    public bool IsOnResizeBorder(Point screenPoint)
    {
        if (IsFullScreen || _window.WindowState != WindowState.Normal || Handle == IntPtr.Zero ||
            !NativeMethods.GetWindowRect(Handle, out var rect))
        {
            return false;
        }

        var band = ResizeBorder * GetScale();
        return screenPoint.X >= rect.Left && screenPoint.X < rect.Right
            && screenPoint.Y >= rect.Top && screenPoint.Y < rect.Bottom
            && (screenPoint.X < rect.Left + band || screenPoint.X >= rect.Right - band
                || screenPoint.Y < rect.Top + band || screenPoint.Y >= rect.Bottom - band);
    }

    /// <summary>
    /// Makes "maximized" mean "exactly the monitor's work area". Without a
    /// frame, Windows otherwise maximizes the window to the frame-inflated
    /// size: it spills a few pixels off every edge and slides under the
    /// taskbar — which also put the immersive edge zones off-screen, out of
    /// the mouse's reach (seen on the Windows CI run). With an auto-hidden
    /// taskbar, a small gap is kept on its edge (see MaximizedBounds), or
    /// Windows would take the window for a fullscreen app and never let the
    /// taskbar slide back in.
    /// </summary>
    private IntPtr FrameWindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_GETMINMAXINFO && !IsFullScreen && NativeMethods.TryGetMonitorInfo(hwnd, out var monitor))
        {
            var info = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
            var placement = MaximizedBounds.Compute(ToPixelRect(monitor.rcMonitor), ToPixelRect(monitor.rcWork), GetAutoHideEdges(monitor.rcMonitor));
            info.ptMaxPosition.X = placement.X;
            info.ptMaxPosition.Y = placement.Y;
            info.ptMaxSize.X = placement.Width;
            info.ptMaxSize.Y = placement.Height;
            Marshal.StructureToPtr(info, lParam, false);
        }

        return IntPtr.Zero;
    }

    private NativeMethods.RECT _autoHideCacheMonitor;
    private ScreenEdges _autoHideCacheEdges;
    private long _autoHideCacheExpiry;

    /// <summary>
    /// Which edges of this monitor hold an auto-hidden taskbar. WM_GETMINMAXINFO
    /// arrives on every step of a drag-resize and each query is a round trip
    /// to Explorer, so the answer is reused for a couple of seconds.
    /// </summary>
    private ScreenEdges GetAutoHideEdges(NativeMethods.RECT monitor)
    {
        var now = Environment.TickCount64;
        if (now < _autoHideCacheExpiry && _autoHideCacheMonitor.Equals(monitor))
        {
            return _autoHideCacheEdges;
        }

        var edges = ScreenEdges.None;
        foreach (var (appBarEdge, screenEdge) in new[]
                 {
                     (NativeMethods.ABE_LEFT, ScreenEdges.Left),
                     (NativeMethods.ABE_TOP, ScreenEdges.Top),
                     (NativeMethods.ABE_RIGHT, ScreenEdges.Right),
                     (NativeMethods.ABE_BOTTOM, ScreenEdges.Bottom),
                 })
        {
            if (NativeMethods.HasAutoHideAppBar(appBarEdge, monitor))
            {
                edges |= screenEdge;
            }
        }

        _autoHideCacheMonitor = monitor;
        _autoHideCacheEdges = edges;
        _autoHideCacheExpiry = now + 2000;
        return edges;
    }

    private static PixelRect ToPixelRect(NativeMethods.RECT rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    /// <summary>
    /// Safety net for <see cref="FrameWindowHook"/>: a chrome-less maximized window is sized past the monitor's work area by
    /// the width of the (invisible) frame it no longer draws, so its outer few
    /// pixels end up off-screen. Measure the overhang and pad the content by it.
    /// </summary>
    private void UpdateMaximizedInset()
    {
        if (Handle == IntPtr.Zero)
        {
            return;
        }

        if (!IsMaximized || IsFullScreen ||
            !NativeMethods.GetWindowRect(Handle, out var window) ||
            !NativeMethods.TryGetMonitorInfo(Handle, out var monitor))
        {
            _root.Margin = new Thickness(0);
            return;
        }

        var scale = GetScale();
        var work = monitor.rcWork;
        _root.Margin = new Thickness(
            Math.Max(0, work.Left - window.Left) / scale,
            Math.Max(0, work.Top - window.Top) / scale,
            Math.Max(0, window.Right - work.Right) / scale,
            Math.Max(0, window.Bottom - work.Bottom) / scale);
    }

    private void ApplyRoundedCorners()
    {
        // Windows 11 only; older versions reject the attribute and keep square corners.
        var preference = NativeMethods.DWMWCP_ROUND;
        try
        {
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private void SetResizeBorder(double thickness)
    {
        if (WindowChrome.GetWindowChrome(_window) is { } chrome)
        {
            chrome.ResizeBorderThickness = new Thickness(thickness);
        }
    }

    private double GetScale() => VisualTreeHelper.GetDpi(_window).DpiScaleX;

    private Rect ToDeviceIndependent(NativeMethods.RECT rect)
    {
        var scale = VisualTreeHelper.GetDpi(_window);
        return new Rect(
            rect.Left / scale.DpiScaleX,
            rect.Top / scale.DpiScaleY,
            (rect.Right - rect.Left) / scale.DpiScaleX,
            (rect.Bottom - rect.Top) / scale.DpiScaleY);
    }
}
