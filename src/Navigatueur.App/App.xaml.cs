using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Navigatueur.App.Services;
using Navigatueur.Core;

namespace Navigatueur.App;

public partial class App : Application
{
    private SingleInstance? _singleInstance;

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

        // Links Windows hands over (default browser, "Ouvrir avec"): to the copy
        // already running if there is one — this process then has nothing to do.
        var urls = LaunchArguments.ParseUrls(e.Args);
        _singleInstance = SingleInstance.Claim(urls, out var handedOver);
        if (_singleInstance is null && handedOver)
        {
            Shutdown();
            return;
        }

        // Off the UI thread, while the window builds; the engine waits for it (see StartupWork).
        WebView2EnvironmentService.StartupWork = Task.Run(BuiltInExtensions.UpgradeInstalledCopies);

        // Not App.xaml's StartupUri anymore: that activates MainWindow via
        // XAML/reflection, which requires a genuine zero-argument CLR
        // constructor — MainWindow's constructor now takes optional
        // parameters (tabManager/isPrimary, used by "Nouvelle fenêtre" to
        // open a second independent window), which is a compile-time C#
        // convenience, not a real parameterless overload, so the reflection
        // lookup found nothing and crashed on every launch. Calling the
        // constructor directly from code resolves the defaults normally.
        var window = new MainWindow();
        window.Show();
        window.OpenFromOutside(urls, bringToFront: false);

        if (_singleInstance is not null)
        {
            _singleInstance.Received = received => Dispatcher.BeginInvoke(() =>
                (MainWindow as MainWindow ?? window).OpenFromOutside(received, bringToFront: true));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
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
}
