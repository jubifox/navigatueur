using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Media.Control;

namespace Navigatueur.App.Services;

/// <summary>
/// Wraps the OS-wide System Media Transport Controls (SMTC) so the floating
/// overlay can show/drive whatever is currently playing (browser tab, Spotify,
/// etc.) without any app-specific integration. Also derives the overlay's
/// background/foreground colors from the current cover art so it visually
/// matches whatever is playing.
/// </summary>
public partial class MusicControllerService : ObservableObject
{
    private static readonly Color FallbackBackground = Color.FromRgb(0x1e, 0x1f, 0x22);
    private static readonly Color FallbackForeground = Colors.White;

    private readonly DispatcherTimer _timer;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _lastTrackKey;

    [ObservableProperty]
    private bool hasSession;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string artist = string.Empty;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private string sourceAppName = string.Empty;

    [ObservableProperty]
    private ImageSource? cover;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private Brush backgroundBrush = new SolidColorBrush(FallbackBackground);

    [ObservableProperty]
    private Brush foregroundBrush = new SolidColorBrush(FallbackForeground);

    [ObservableProperty]
    private Brush fadeBrush = MakeFadeBrush(FallbackBackground);

    public MusicControllerService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception)
        {
            // No media platform support (e.g. running under an older Windows build) — overlay just stays empty.
        }
    }

    private async Task RefreshAsync()
    {
        if (_manager is null)
        {
            return;
        }

        _session = _manager.GetCurrentSession();
        if (_session is null)
        {
            HasSession = false;
            return;
        }

        try
        {
            var properties = await _session.TryGetMediaPropertiesAsync();
            var playback = _session.GetPlaybackInfo();
            var timeline = _session.GetTimelineProperties();

            HasSession = true;
            Title = properties.Title ?? string.Empty;
            Artist = properties.Artist ?? string.Empty;
            IsPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            SourceAppName = PrettifySourceAppId(_session.SourceAppUserModelId);

            var duration = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position - timeline.StartTime;
            Progress = duration > TimeSpan.Zero ? Math.Clamp(position / duration, 0, 1) : 0;

            var trackKey = $"{Title}|{Artist}";
            if (trackKey != _lastTrackKey)
            {
                _lastTrackKey = trackKey;
                await LoadCoverAsync(properties.Thumbnail);
            }
        }
        catch (Exception)
        {
            HasSession = false;
        }
    }

    private async Task LoadCoverAsync(Windows.Storage.Streams.IRandomAccessStreamReference? thumbnailRef)
    {
        if (thumbnailRef is null)
        {
            Cover = null;
            BackgroundBrush = new SolidColorBrush(FallbackBackground);
            ForegroundBrush = new SolidColorBrush(FallbackForeground);
            FadeBrush = MakeFadeBrush(FallbackBackground);
            return;
        }

        try
        {
            using var stream = await thumbnailRef.OpenReadAsync();
            using var netStream = stream.AsStreamForRead();
            using var memory = new MemoryStream();
            await netStream.CopyToAsync(memory);
            memory.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            Cover = bitmap;

            var (background, foreground) = ExtractDominantColors(bitmap);
            BackgroundBrush = new SolidColorBrush(background).AlsoFreeze();
            ForegroundBrush = new SolidColorBrush(foreground).AlsoFreeze();
            FadeBrush = MakeFadeBrush(background);
        }
        catch (Exception)
        {
            Cover = null;
            BackgroundBrush = new SolidColorBrush(FallbackBackground);
            ForegroundBrush = new SolidColorBrush(FallbackForeground);
            FadeBrush = MakeFadeBrush(FallbackBackground);
        }
    }

    /// <summary>
    /// Horizontal wash in the dominant color, opaque on the left and fading to
    /// transparent by the time it reaches the cover art on the right, so the
    /// two blend into each other instead of meeting at a hard edge.
    /// </summary>
    private static Brush MakeFadeBrush(Color color)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(color, 0.0),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.62),
            },
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Reads the cover at full resolution and samples pixels on a grid
    /// (true nearest-neighbor — no resize filter involved, so no blended
    /// pixel ever enters the count) into finely-quantized RGB buckets to find
    /// the two most common colors — background takes the most dominant, text
    /// the second. Near-white and near-black pixels are excluded (paper/line
    /// art, not the subject's own color); everything else is kept exactly as
    /// read, so the result is always a real color from the cover.
    /// </summary>
    private static (Color background, Color foreground) ExtractDominantColors(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        const int gridSize = 64;
        var stepX = Math.Max(1, width / gridSize);
        var stepY = Math.Max(1, height / gridSize);

        var buckets = new Dictionary<int, (int Count, long R, long G, long B)>();
        for (var y = 0; y < height; y += stepY)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < width; x += stepX)
            {
                var offset = rowOffset + x * 4;
                byte b = pixels[offset];
                byte g = pixels[offset + 1];
                byte r = pixels[offset + 2];
                byte a = pixels[offset + 3];
                if (a < 128)
                {
                    continue;
                }

                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                if (max > 235 && min > 200)
                {
                    continue; // near white (paper/background, not the subject)
                }

                if (max < 25)
                {
                    continue; // near black (line art/shadow, not the subject)
                }

                var key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                buckets.TryGetValue(key, out var agg);
                buckets[key] = (agg.Count + 1, agg.R + r, agg.G + g, agg.B + b);
            }
        }

        if (buckets.Count == 0)
        {
            return (FallbackBackground, FallbackForeground);
        }

        var ranked = buckets.Values.OrderByDescending(v => v.Count).Take(2).ToList();
        Color ToColor((int Count, long R, long G, long B) v) =>
            Color.FromRgb((byte)(v.R / v.Count), (byte)(v.G / v.Count), (byte)(v.B / v.Count));

        var background = ToColor(ranked[0]);
        var foreground = ranked.Count > 1 ? ToColor(ranked[1]) : FallbackForeground;

        return (background, EnsureReadable(background, foreground));
    }

    private static Color EnsureReadable(Color background, Color candidate)
    {
        static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        if (Math.Abs(Luminance(background) - Luminance(candidate)) > 0.35)
        {
            return candidate;
        }

        return Luminance(background) > 0.5 ? Colors.Black : Colors.White;
    }

    /// <summary>
    /// SMTC only exposes the source as a package AUMID (e.g.
    /// "Microsoft.MicrosoftEdge_8wekyb3d8bbwe!MSEDGE"), not the per-tab site
    /// like Windows' own Now Playing flyout shows — there's no public API for
    /// that. Best effort: strip the package suffix and map known browsers to
    /// a readable name, otherwise show the raw id.
    /// </summary>
    private static string PrettifySourceAppId(string? aumid)
    {
        if (string.IsNullOrEmpty(aumid))
        {
            return string.Empty;
        }

        var name = aumid.Split('!')[0];
        var underscoreIndex = name.IndexOf('_');
        if (underscoreIndex > 0)
        {
            name = name[..underscoreIndex];
        }

        return name switch
        {
            "Microsoft.MicrosoftEdge" or "MSEdge" => "Microsoft Edge",
            "Google.Chrome" or "chrome" => "Google Chrome",
            _ => name,
        };
    }

    [RelayCommand]
    private void PlayPause() => _ = _session?.TryTogglePlayPauseAsync();

    [RelayCommand]
    private void Next() => _ = _session?.TrySkipNextAsync();

    [RelayCommand]
    private void Previous() => _ = _session?.TrySkipPreviousAsync();

    [RelayCommand]
    private void SeekBack() => Seek(TimeSpan.FromSeconds(-10));

    [RelayCommand]
    private void SeekForward() => Seek(TimeSpan.FromSeconds(10));

    private void Seek(TimeSpan delta)
    {
        if (_session is null)
        {
            return;
        }

        var timeline = _session.GetTimelineProperties();
        var target = timeline.Position + delta;
        if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }

        _ = _session.TryChangePlaybackPositionAsync(target.Ticks);
    }
}

file static class BrushExtensions
{
    public static Brush AlsoFreeze(this Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
