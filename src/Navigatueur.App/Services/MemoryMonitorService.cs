using System.Diagnostics;
using System.Management;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Navigatueur.App.Services;

/// <summary>
/// Approximates the app's own RAM footprint: the current process plus every
/// "msedgewebview2" process whose command line references *our* WebView2
/// user-data folder (<see cref="WebView2EnvironmentService.UserDataFolder"/>).
/// Filtering via WMI's Win32_Process.CommandLine — instead of summing every
/// msedgewebview2.exe on the machine — avoids counting RAM used by other,
/// unrelated WebView2-based apps that might happen to be running alongside
/// this one.
/// </summary>
public partial class MemoryMonitorService : ObservableObject
{
    private readonly DispatcherTimer _timer;

    [ObservableProperty]
    private long usedMegabytes;

    public MemoryMonitorService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        long currentProcessBytes;
        using (var current = Process.GetCurrentProcess())
        {
            currentProcessBytes = current.WorkingSet64;
        }

        // WMI queries are slow enough to be worth keeping off the UI thread;
        // the DispatcherTimer.Tick handler awaits this and resumes on the UI
        // thread automatically to set the observable property.
        var ownWebViewBytes = await Task.Run(SumOwnWebViewProcesses);

        UsedMegabytes = (currentProcessBytes + ownWebViewBytes) / (1024 * 1024);
    }

    private static long SumOwnWebViewProcesses()
    {
        long total = 0;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'msedgewebview2.exe'");

            foreach (var result in searcher.Get())
            {
                using var process = (ManagementObject)result;

                var commandLine = process["CommandLine"] as string;
                if (commandLine is null ||
                    commandLine.IndexOf(WebView2EnvironmentService.UserDataFolder, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var pid = (uint)process["ProcessId"];
                try
                {
                    using var osProcess = Process.GetProcessById((int)pid);
                    total += osProcess.WorkingSet64;
                }
                catch (ArgumentException)
                {
                    // Process exited between the WMI query and this lookup.
                }
            }
        }
        catch (ManagementException)
        {
            // WMI unavailable/misbehaving: fall back to reporting just the app's own process.
        }

        return total;
    }
}
