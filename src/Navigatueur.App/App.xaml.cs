using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Navigatueur.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LoadAppCursor();
    }

    /// <summary>
    /// Loads the app's custom cursor once and stores it as a resource so the
    /// implicit Window style in Theme.xaml can apply it everywhere via
    /// DynamicResource — the .cur file is deployed as Content (copied to the
    /// output folder), not a compiled Resource, so it has to be loaded from
    /// disk rather than a pack URI.
    /// </summary>
    private static void LoadAppCursor()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Cursors", "NavigatueurCursor.cur");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            Current.Resources["AppCursor"] = new Cursor(stream);
        }
        catch (Exception)
        {
            // Missing/corrupt cursor asset (including a .cur saved with a
            // PNG-compressed image, which WPF's Cursor(Stream) can't parse —
            // it needs the classic BMP-based DIB cursor format) — fall back
            // to the OS default silently rather than crashing startup.
        }
    }
}
