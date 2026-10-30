using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Navigatueur.App.Services;
using Navigatueur.App.ViewModels;
using Navigatueur.Core.Settings;

namespace Navigatueur.App.Views;

public partial class WelcomeWindow : Window
{
    private readonly ObservableCollection<SearchEngineChoice> _engines = new();
    private readonly bool _showEngineChoice;

    /// <param name="showEngineChoice">Only on a genuinely fresh install — someone upgrading already picked an engine.</param>
    public WelcomeWindow(bool showEngineChoice = true)
    {
        InitializeComponent();
        _showEngineChoice = showEngineChoice;

        foreach (var engine in SearchEngineService.Engines)
        {
            _engines.Add(new SearchEngineChoice(engine.Id, engine.DisplayName)
            {
                IsSelected = engine.Id == AppServices.SearchEngine.EngineId,
            });
        }

        EngineList.ItemsSource = _engines;
        EngineSection.Visibility = showEngineChoice ? Visibility.Visible : Visibility.Collapsed;

        var current = AppServices.Interface.Current.ModeValue;
        ClassicChoice.IsChecked = current == InterfaceMode.Classic;
        ImmersiveChoice.IsChecked = current != InterfaceMode.Classic;
    }

    private void OnBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        if (_showEngineChoice && _engines.FirstOrDefault(x => x.IsSelected) is { } engine)
        {
            AppServices.SearchEngine.SetEngine(engine.Id);
        }

        var classic = ClassicChoice.IsChecked == true;
        AppServices.Interface.Update(s =>
        {
            s.Mode = classic ? nameof(InterfaceMode.Classic) : nameof(InterfaceMode.Immersive);
            s.HasSeenIntro = true;
        });

        Close();
    }
}
