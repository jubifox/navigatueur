using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Navigatueur.Core;

namespace Navigatueur.App.Services;

/// <summary>
/// Last known favicon of each site, kept on disk so a tab that isn't loaded —
/// unloaded to save memory, or restored after a restart — still shows its
/// site's icon instead of a generic globe. One small PNG per host under
/// %LocalAppData%\Navigatueur\Favicons; private windows read it but never
/// write to it, so they leave no trace there.
/// </summary>
public static class FaviconCache
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Navigatueur", "Favicons");

    private static readonly ConcurrentDictionary<string, ImageSource?> Memory = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The site's cached icon, or null if none is known yet (or it's one of Navigatueur's own pages).</summary>
    public static ImageSource? TryGet(string? url)
    {
        if (KeyFor(url) is not { } host)
        {
            return null;
        }

        return Memory.GetOrAdd(host, static key =>
        {
            var path = Path.Combine(Folder, key + ".png");
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return Decode(File.ReadAllBytes(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
            {
                return null;
            }
        });
    }

    /// <summary>Remembers <paramref name="png"/> as the icon of <paramref name="url"/>'s site and returns it decoded.</summary>
    public static ImageSource? Store(string? url, byte[] png)
    {
        var image = Decode(png);
        if (image is null || KeyFor(url) is not { } host)
        {
            return image;
        }

        Memory[host] = image;
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder, host + ".png"), png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still shown for this run; just won't survive a restart.
        }

        return image;
    }

    public static ImageSource? Decode(byte[] png)
    {
        if (png.Length == 0)
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(png);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Lower-cased host of a web page, usable as a file name (hosts are only letters, digits, dots and dashes); null otherwise.</summary>
    public static string? KeyFor(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || InternalPages.IsInternal(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            string.IsNullOrEmpty(uri.IdnHost))
        {
            return null;
        }

        var host = uri.IdnHost.ToLowerInvariant();
        return host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') ? host : null;
    }
}
