using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Navigatueur.App.Interop;

namespace Navigatueur.App.Views;

/// <summary>
/// See the XAML doc comment for the full "why a separate window" rationale.
/// MainWindow creates one instance, owns it, and repositions/resizes it to
/// track the active tab's on-screen WebView2 bounds every time that changes
/// (window move/resize, sidebar pin toggle, etc.) via <see cref="SyncBounds"/>.
/// </summary>
public partial class WebViewOverlayWindow : Window
{
    private const double CornerRadius = 8;

    public Canvas TrailCanvas => TrailCanvasElement;

    public WebViewOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeClickThrough();
    }

    /// <summary>
    /// Without this, the layered window this becomes (via AllowsTransparency)
    /// would still intercept clicks/scrolls over its fully-transparent areas —
    /// per-pixel alpha doesn't imply click-through on its own in Win32. This
    /// is the standard fix: mark the whole window WS_EX_TRANSPARENT so every
    /// mouse message passes straight through to whatever's underneath it
    /// (the active tab's WebView2), regardless of what's drawn here.
    /// </summary>
    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TRANSPARENT);
    }

    /// <summary>Left/Top/Width/Height are in the same DIP coordinate space MainWindow's own Left/Top/ActualWidth/ActualHeight use — see RepositionWebViewOverlay for how the caller derives them.</summary>
    public void SyncBounds(double left, double top, double width, double height)
    {
        Left = left;
        Top = top;
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        RebuildCornerMask();
    }

    private void RebuildCornerMask()
    {
        var w = Width;
        var h = Height;
        if (w <= 0 || h <= 0)
        {
            CornerMaskPath.Data = null;
            return;
        }

        // EvenOdd fill of the full rect minus a rounded-rect of the same size
        // leaves exactly the four corner wedges — the "frame" a picture-frame
        // mask would produce, without any straight-edge border in between.
        var outer = new RectangleGeometry(new Rect(0, 0, w, h));
        var inner = new RectangleGeometry(new Rect(0, 0, w, h), CornerRadius, CornerRadius);
        CornerMaskPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
    }
}
