using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Navigatueur.Core.Settings;

namespace Navigatueur.App.Services;

/// <summary>
/// Drives the app's color theme and background images. Rather than swapping
/// resource dictionaries, this mutates the same shared, unfrozen
/// <see cref="SolidColorBrush"/> instances declared in Theme.xaml in place —
/// every StaticResource reference throughout the app already points at those
/// exact objects, so changing .Color on them updates the whole UI live with
/// no DynamicResource plumbing needed.
/// </summary>
public partial class ThemeService : ObservableObject
{
    private static readonly Color DarkBackground = Color.FromRgb(0x1E, 0x1F, 0x22);
    private static readonly Color DarkBorder = Color.FromRgb(0x3A, 0x3B, 0x3F);
    private static readonly Color DarkText = Color.FromRgb(0xE4, 0xE4, 0xE7);
    private static readonly Color DarkSurface = Color.FromRgb(0x2B, 0x2D, 0x31);

    private static readonly Color LightBackground = Color.FromRgb(0xF3, 0xF3, 0xF3);
    private static readonly Color LightBorder = Color.FromRgb(0xD0, 0xD0, 0xD0);
    private static readonly Color LightText = Color.FromRgb(0x1E, 0x1E, 0x1E);
    private static readonly Color LightSurface = Color.FromRgb(0xE4, 0xE4, 0xE4);

    public static readonly string BackgroundsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Navigatueur", "Backgrounds");

    private readonly AppSettings _settings;

    [ObservableProperty]
    private string themeMode;

    [ObservableProperty]
    private string accentColorHex;

    [ObservableProperty]
    private string? chromeBackgroundImagePath;

    [ObservableProperty]
    private string? newTabBackgroundImagePath;

    public ThemeService(AppSettings settings)
    {
        _settings = settings;
        themeMode = settings.ThemeMode;
        accentColorHex = settings.AccentColorHex;
        chromeBackgroundImagePath = settings.ChromeBackgroundImagePath;
        newTabBackgroundImagePath = settings.NewTabBackgroundImagePath;

        ApplyPalette();
    }

    public void SetThemeMode(string mode)
    {
        ThemeMode = mode;
        _settings.ThemeMode = mode;
        ApplyPalette();
        Persist();
    }

    public void SetAccentColor(string hex)
    {
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

    private static string CopyIntoBackgrounds(string sourcePath, string baseName)
    {
        Directory.CreateDirectory(BackgroundsDirectory);
        var destination = Path.Combine(BackgroundsDirectory, baseName + Path.GetExtension(sourcePath));
        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }

    private void Persist() => AppServices.Settings.Save(_settings);

    private void ApplyPalette()
    {
        var isDark = !string.Equals(ThemeMode, "Light", StringComparison.OrdinalIgnoreCase);

        SetBrush("ChromeBackgroundBrush", isDark ? DarkBackground : LightBackground);
        SetBrush("ChromeBorderBrush", isDark ? DarkBorder : LightBorder);
        SetBrush("ChromeTextBrush", isDark ? DarkText : LightText);
        SetBrush("SurfaceBrush", isDark ? DarkSurface : LightSurface);

        if (ColorConverter.ConvertFromString(AccentColorHex) is Color accent)
        {
            SetBrush("AccentBrush", accent);
        }
    }

    /// <summary>
    /// Replaces the resource entry outright rather than mutating the existing
    /// brush's .Color — brushes loaded from BAML are frozen by the XAML
    /// compiler, so an in-place mutation throws. Every consumer must use
    /// DynamicResource (not StaticResource) for these keys to pick up the
    /// replacement live.
    /// </summary>
    private static void SetBrush(string key, Color color) =>
        Application.Current.Resources[key] = new SolidColorBrush(color);
}
