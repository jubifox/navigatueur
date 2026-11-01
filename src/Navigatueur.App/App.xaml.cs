using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Navigatueur.App.Services;

namespace Navigatueur.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashLog.Write("Exception non gérée (processus)", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write("Tâche en arrière-plan", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        LoadAppCursor();

        // Not App.xaml's StartupUri anymore: that activates MainWindow via
        // XAML/reflection, which requires a genuine zero-argument CLR
        // constructor — MainWindow's constructor now takes optional
        // parameters (tabManager/isPrimary, used by "Nouvelle fenêtre" to
        // open a second independent window), which is a compile-time C#
        // convenience, not a real parameterless overload, so the reflection
        // lookup found nothing and crashed on every launch. Calling the
        // constructor directly from code resolves the defaults normally.
        new MainWindow().Show();
    }

    /// <summary>
    /// Logs the error, then keeps the browser running if it's usable: one
    /// failing action (a menu command, a message from a page…) shouldn't take
    /// every open tab down with it. A failure before any window is on screen
    /// (startup) is left to end the process — a windowless process is worse.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write("Exception non gérée (interface)", e.Exception);
        e.Handled = MainWindow is { IsVisible: true };
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
