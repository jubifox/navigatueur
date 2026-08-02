using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Navigatueur.App.Services;
using Navigatueur.App.ViewModels;

namespace Navigatueur.App.Views;

public partial class SettingsWindow : Window
{
    private const string ImageFilter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp";

    /// <summary>Fixed so every hue along the slider reads as vividly "accent-colored" rather than pastel or muddy.</summary>
    private const double HueSaturation = 0.65;
    private const double HueValue = 0.85;

    private readonly ObservableCollection<SearchEngineChoice> _engines = new();
    private bool _isLoaded;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = AppServices.Theme;

        foreach (var engine in SearchEngineService.Engines)
        {
            _engines.Add(new SearchEngineChoice(engine.Id, engine.DisplayName)
            {
                IsSelected = engine.Id == AppServices.SearchEngine.EngineId,
            });
        }

        EngineList.ItemsSource = _engines;

        HueSlider.Value = EstimateHue(AppServices.Theme.AccentColorHex);
        _isLoaded = true;

        CurrentVersionText.Text = $"Version actuelle : {AppServices.Update.CurrentVersion}";
        InstallUpdateButton.Visibility = AppServices.Update.IsUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
        if (AppServices.Update.IsUpdateAvailable)
        {
            UpdateStatusText.Text = $"Une nouvelle version ({AppServices.Update.LatestVersion}) est disponible.";
        }
    }

    private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
    {
        CheckForUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "Vérification en cours...";
        try
        {
            await AppServices.Update.CheckForUpdateAsync();
            if (AppServices.Update.IsUpdateAvailable)
            {
                UpdateStatusText.Text = $"Une nouvelle version ({AppServices.Update.LatestVersion}) est disponible.";
                InstallUpdateButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateStatusText.Text = "Navigatueur est à jour.";
                InstallUpdateButton.Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    private async void OnInstallUpdateClick(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            $"Une nouvelle version ({AppServices.Update.LatestVersion}) est disponible. Télécharger et installer maintenant ? L'application va se fermer pendant l'installation.",
            "Mise à jour disponible",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await AppServices.Update.DownloadAndInstallAsync();
        }
    }

    private void OnHueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded)
        {
            return;
        }

        var color = HsvToColor(e.NewValue, HueSaturation, HueValue);
        HuePreviewBrush.Color = color;
        AppServices.Theme.SetAccentColor($"#{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    private static Color HsvToColor(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        var m = value - c;

        var (r1, g1, b1) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromRgb(
            (byte)Math.Clamp((r1 + m) * 255, 0, 255),
            (byte)Math.Clamp((g1 + m) * 255, 0, 255),
            (byte)Math.Clamp((b1 + m) * 255, 0, 255));
    }

    /// <summary>Reverse of HsvToColor, just enough to position the slider under the current accent color on open.</summary>
    private static double EstimateHue(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;
            if (delta <= 0)
            {
                return 0;
            }

            double hue;
            if (max == r)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                hue = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                hue = 60 * (((r - g) / delta) + 4);
            }

            return hue < 0 ? hue + 360 : hue;
        }
        catch (FormatException)
        {
            return 220;
        }
    }

    private void OnEngineClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
        {
            return;
        }

        foreach (var engine in _engines)
        {
            engine.IsSelected = engine.Id == id;
        }

        AppServices.SearchEngine.SetEngine(id);
    }

    private void OnDarkClick(object sender, RoutedEventArgs e) => AppServices.Theme.SetThemeMode("Dark");

    private void OnLightClick(object sender, RoutedEventArgs e) => AppServices.Theme.SetThemeMode("Light");

    private void OnAccentSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            AppServices.Theme.SetAccentColor(hex);
        }
    }

    private void OnChooseChromeBackgroundClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = ImageFilter };
        if (dialog.ShowDialog() == true)
        {
            AppServices.Theme.SetChromeBackgroundImage(dialog.FileName);
        }
    }

    private void OnResetChromeBackgroundClick(object sender, RoutedEventArgs e) =>
        AppServices.Theme.ClearChromeBackgroundImage();

    private void OnChooseNewTabBackgroundClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = ImageFilter };
        if (dialog.ShowDialog() == true)
        {
            AppServices.Theme.SetNewTabBackgroundImage(dialog.FileName);
        }
    }

    private void OnResetNewTabBackgroundClick(object sender, RoutedEventArgs e) =>
        AppServices.Theme.ClearNewTabBackgroundImage();
}
