using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Navigatueur.App.Services;

/// <summary>
/// One Navigatueur per Windows user. When Windows opens a link with
/// Navigatueur (it's the default browser, or chosen in "Ouvrir avec") while
/// it's already running, the new process hands the link to the running one
/// through a named pipe and leaves; the link opens in a new tab there.
/// Launching it again without a link brings the running window forward.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(string pipeName, Mutex mutex)
    {
        _pipeName = pipeName;
        _mutex = mutex;
    }

    /// <summary>Set by the primary instance: called (on a background thread) with the links another launch handed over — empty to just come forward.</summary>
    public Action<IReadOnlyList<string>>? Received { get; set; }

    private static string UserKey => WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;

    /// <summary>
    /// The running instance's handle when this process is the first one;
    /// null when another instance runs — the links were then handed to it
    /// (or <paramref name="handedOver"/> is false if it didn't answer).
    /// </summary>
    public static SingleInstance? Claim(IReadOnlyList<string> urls, out bool handedOver)
    {
        handedOver = false;
        var key = UserKey;
        var pipeName = $"Navigatueur-{key}";
        var mutex = new Mutex(initiallyOwned: true, $"Local\\Navigatueur-{key}", out var createdNew);

        if (createdNew)
        {
            var instance = new SingleInstance(pipeName, mutex);
            instance.Listen();
            return instance;
        }

        mutex.Dispose();
        handedOver = HandOver(pipeName, urls);
        return null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private static bool HandOver(string pipeName, IReadOnlyList<string> urls)
    {
        // This process was started by the user's click: it may pass that right
        // to come to the front on to the running copy (Windows keeps background
        // processes from stealing the focus otherwise).
        AllowSetForegroundWindow(-1);

        // The first instance may still be starting: give it a moment to open its pipe.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(3000);
                var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(urls));
                client.Write(payload, 0, payload.Length);
                client.Flush();
                return true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }

        return false;
    }

    private void Listen()
    {
        var thread = new Thread(() =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    server.WaitForConnectionAsync(_stop.Token).GetAwaiter().GetResult();

                    using var reader = new MemoryStream();
                    server.CopyTo(reader);
                    var urls = Parse(reader.ToArray());
                    Received?.Invoke(urls);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    // A launch that gave up halfway, or garbage: wait for the next one.
                }
            }
        })
        {
            IsBackground = true,
            Name = "Navigatueur single instance",
        };
        thread.Start();
    }

    /// <summary>Only links survive: what another process sends is re-checked as if it came from the command line.</summary>
    private static IReadOnlyList<string> Parse(byte[] payload)
    {
        if (payload.Length == 0 || payload.Length > 64 * 1024)
        {
            return Array.Empty<string>();
        }

        var urls = JsonSerializer.Deserialize<string[]>(payload) ?? Array.Empty<string>();
        return Core.LaunchArguments.ParseUrls(urls);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread any more: nothing to release.
        }

        _mutex.Dispose();
    }
}
