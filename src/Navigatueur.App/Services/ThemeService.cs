using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Navigatueur.Core.Settings;

namespace Navigatueur.App.Services;

/// <summary>
/// Drives the app's color theme and background images by replacing the
/// shared brush entries declared in Theme.xaml (see that file for the token
/// hierarchy). Every consumer uses DynamicResource for these keys, so a
/// theme or accent change updates the whole UI live — including the panels,
/// which live in their own child windows but resolve resources through the
/// application dictionary all the same.
/// </summary>
public partial class ThemeService : ObservableObject
{
    private sealed record Palette(
        Color WindowBackground,
        Color Panel,
        Color PanelBorder,
        Color Surface,
        Color SurfaceHover,
        Color SurfaceActive,
        Color Text,
        Color TextSecondary,
        Color TextTertiary);

    private static readonly Palette Dark = new(
        WindowBackground: Rgb(0x0C, 0x0D, 0x0F),
        Panel: Rgb(0x16, 0x17, 0x1A),
        PanelBorder: Rgb(0x26, 0x27, 0x2C),
        Surface: Rgb(0x1F, 0x20, 0x24),
        SurfaceHover: Rgb(0x25, 0x26, 0x2B),
        SurfaceActive: Rgb(0x2C, 0x2D, 0x33),
        Text: Rgb(0xEC, 0xEC, 0xEF),
        TextSecondary: Rgb(0xA1, 0xA1, 0xAA),
        TextTertiary: Rgb(0x6E, 0x6E, 0x78));

    private static readonly Palette Light = new(
        WindowBackground: Rgb(0xE6, 0xE6, 0xEA),
        Panel: Rgb(0xFB, 0xFB, 0xFC),
        PanelBorder: Rgb(0xE1, 0xE1, 0xE5),
        Surface: Rgb(0xF1, 0xF1, 0xF4),
        SurfaceHover: Rgb(0xEA, 0xEA, 0xEE),
        SurfaceActive: Rgb(0xE1, 0xE1, 0xE7),
        Text: Rgb(0x18, 0x18, 0x1B),
        TextSecondary: Rgb(0x5F, 0x5F, 0x68),
        TextTertiary: Rgb(0x8E, 0x8E, 0x96));

    /// <summary>Private windows keep the user's light/dark choice but swap in a violet tint, so they're unmistakable at a glance.</summary>
    private static readonly Palette PrivateDark = Dark with
    {
        WindowBackground = Rgb(0x0E, 0x0B, 0x14),
        Panel = Rgb(0x1A, 0x15, 0x24),
        PanelBorder = Rgb(0x2E, 0x26, 0x3D),
        Surface = Rgb(0x23, 0x1C, 0x31),
        SurfaceHover = Rgb(0x2A, 0x22, 0x3A),
        SurfaceActive = Rgb(0x33, 0x29, 0x46),
    };

    private static readonly Palette PrivateLight = Light with
    {
        WindowBackground = Rgb(0xE7, 0xE2, 0xF0),
        Panel = Rgb(0xF8, 0xF5, 0xFD),
        PanelBorder = Rgb(0xE0, 0xD8, 0xEE),
        Surface = Rgb(0xEF, 0xEA, 0xF8),
        SurfaceHover = Rgb(0xE8, 0xE1, 0xF4),
        SurfaceActive = Rgb(0xDF, 0xD6, 0xEF),
    };

    public const string PrivateAccentHex = "#8B5CF6";

    public static readonly string BackgroundsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Navigatueur", "Backgrounds");

    private readonly AppSettings _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDark))]
    private string themeMode;

    [ObservableProperty]
    private string accentColorHex;

    [ObservableProperty]
    private string? chromeBackgroundImagePath;

    [ObservableProperty]
    private string? newTabBackgroundImagePath;

    [ObservableProperty]
    private bool isCursorTrailEnabled;

    public ThemeService(AppSettings settings)
    {
        _settings = settings;
        themeMode = settings.ThemeMode;
        accentColorHex = settings.AccentColorHex;
        chromeBackgroundImagePath = settings.ChromeBackgroundImagePath;
        newTabBackgroundImagePath = settings.NewTabBackgroundImagePath;
        isCursorTrailEnabled = settings.IsCursorTrailEnabled;

        ApplyPalette();
    }

    /// <summary>Raised after any visual change (mode, accent, backgrounds) has been applied — internal pages listen to restyle themselves.</summary>
    public event EventHandler? AppearanceChanged;

    public bool IsDark => !string.Equals(ThemeMode, "Light", StringComparison.OrdinalIgnoreCase);

    public void SetCursorTrailEnabled(bool enabled)
    {
        IsCursorTrailEnabled = enabled;
        _settings.IsCursorTrailEnabled = enabled;
        Persist();
    }

    public void SetThemeMode(string mode)
    {
        ThemeMode = mode == "Light" ? "Light" : "Dark";
        _settings.ThemeMode = ThemeMode;
        ApplyPalette();
        Persist();
    }

    public void SetAccentColor(string hex)
    {
        if (!TryParseColor(hex, out _))
        {
            return;
        }

        AccentColorHex = hex;
        _settings.AccentColorHex = hex;
        ApplyPalette();
        Persist();
    }

    public void SetChromeBackgroundImage(string sourcePath)
    {
        var stored = CopyIntoBackgrounds(sourcePath, "chrome");
        ChromeBackgroundImagePath = stored;
        _settings.ChromeBackgroundImagePath = stored;
        Persist();
    }

    public void ClearChromeBackgroundImage()
    {
        ChromeBackgroundImagePath = null;
        _settings.ChromeBackgroundImagePath = null;
        Persist();
    }

    public void SetNewTabBackgroundImage(string sourcePath)
    {
        var stored = CopyIntoBackgrounds(sourcePath, "newtab");
        NewTabBackgroundImagePath = stored;
        _settings.NewTabBackgroundImagePath = stored;
        Persist();
    }

    public void ClearNewTabBackgroundImage()
    {
        NewTabBackgroundImagePath = null;
        _settings.NewTabBackgroundImagePath = null;
        Persist();
    }

    /// <summary>
    /// Brushes that override the shared palette inside a private window's own
    /// element trees (main window and each panel root) — DynamicResource
    /// lookups hit these first, so private windows recolor without touching
    /// normal windows open alongside them.
    /// </summary>
    public ResourceDictionary CreatePrivateOverrides()
    {
        var dictionary = new ResourceDictionary();
        var palette = IsDark ? PrivateDark : PrivateLight;
        foreach (var (key, color) in Describe(palette))
        {
            dictionary[key] = Frozen(color);
        }

        var accent = (Color)ColorConverter.ConvertFromString(PrivateAccentHex);
        foreach (var (key, brush) in DescribeAccent(accent))
        {
            dictionary[key] = brush;
        }

        return dictionary;
    }

    private static string CopyIntoBackgrounds(string sourcePath, string baseName)
    {
        Directory.CreateDirectory(BackgroundsDirectory);
        var destination = Path.Combine(BackgroundsDirectory, baseName + Path.GetExtension(sourcePath));
        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }

    private void Persist()
    {
        AppServices.Settings.Save(_settings);
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyPalette()
    {
        var resources = Application.Current.Resources;
        foreach (var (key, color) in Describe(IsDark ? Dark : Light))
        {
            resources[key] = Frozen(color);
        }

        if (TryParseColor(AccentColorHex, out var accent))
        {
            foreach (var (key, brush) in DescribeAccent(accent))
            {
                resources[key] = brush;
            }
        }
    }

    private static IEnumerable<(string Key, Color Color)> Describe(Palette p)
    {
        yield return ("WindowBackgroundBrush", p.WindowBackground);
        yield return ("PanelBrush", p.Panel);
        yield return ("PanelBorderBrush", p.PanelBorder);
        yield return ("SurfaceBrush", p.Surface);
        yield return ("SurfaceHoverBrush", p.SurfaceHover);
        yield return ("SurfaceActiveBrush", p.SurfaceActive);
        yield return ("TextBrush", p.Text);
        yield return ("TextSecondaryBrush", p.TextSecondary);
        yield return ("TextTertiaryBrush", p.TextTertiary);

        // Pre-redesign aliases (music overlay, extension popup).
        yield return ("ChromeBackgroundBrush", p.Panel);
        yield return ("ChromeBorderBrush", p.PanelBorder);
        yield return ("ChromeTextBrush", p.Text);
    }

    private static IEnumerable<(string Key, Brush Brush)> DescribeAccent(Color accent)
    {
        yield return ("AccentBrush", Frozen(accent));
        yield return ("AccentSoftBrush", Frozen(Color.FromArgb(0x2E, accent.R, accent.G, accent.B)));

        // White text on a light accent (yellow, cyan…) is unreadable: switch to near-black past mid luminance.
        var luminance = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255;
        yield return ("AccentForegroundBrush", Frozen(luminance > 0.62 ? Rgb(0x10, 0x10, 0x12) : Colors.White));
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
        }

        color = default;
        return false;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
