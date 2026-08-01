using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Navigatueur.App.Services;
using Navigatueur.App.ViewModels;

namespace Navigatueur.App.Views;

public partial class WelcomeWindow : Window
{
    private readonly ObservableCollection<SearchEngineChoice> _engines = new();

    public WelcomeWindow()
    {
        InitializeComponent();

        foreach (var engine in SearchEngineService.Engines)
        {
            _engines.Add(new SearchEngineChoice(engine.Id, engine.DisplayName) { IsSelected = engine.Id == "Bing" });
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
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        var selected = _engines.FirstOrDefault(x => x.IsSelected)?.Id ?? "Bing";
        AppServices.SearchEngine.SetEngine(selected);
        Close();
    }
}
