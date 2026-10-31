using System.IO;

namespace Navigatueur.App.Services;

/// <summary>
/// Appends unhandled exceptions to %LocalAppData%\Navigatueur\crash.log.
/// Without it, a failure on a user's machine (or the Windows CI smoke test)
/// leaves nothing to diagnose but "the window vanished".
/// </summary>
public static class CrashLog
{
    private const long MaxSizeBytes = 512 * 1024;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Navigatueur", "crash.log");

    public static void Write(string origin, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // Keep it bounded: a recurring error must not grow the file forever.
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxSizeBytes)
            {
                File.Delete(FilePath);
            }

            File.AppendAllText(
                FilePath,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {origin} — v{typeof(CrashLog).Assembly.GetName().Version}{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nowhere to write: nothing more can be done from a crash handler.
        }
    }
}
