using System.Windows.Controls;

namespace Navigatueur.App.Views;

/// <summary>
/// Implemented by any top-level window that hosts BrowserTabView instances
/// and wants their cursor trail to actually render over page content — see
/// WebViewOverlayWindow's doc comment for why that needs a dedicated overlay
/// window rather than a same-window Canvas. BrowserTabView looks this up via
/// Window.GetWindow(this) and no-ops if the host doesn't implement it, so
/// windows that don't bother (e.g. private browsing) simply get no trail
/// over page content instead of an exception.
/// </summary>
public interface IWebViewOverlayHost
{
    Canvas? WebViewTrailCanvas { get; }
}
