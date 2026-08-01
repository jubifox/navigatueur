using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Navigatueur.App.Services;
using Navigatueur.App.ViewModels;

namespace Navigatueur.App.Views;

public partial class SettingsWindow : Window
{
    private const string ImageFilter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp";

    private readonly ObservableCollection<SearchEngineChoice> _engines = new();

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
