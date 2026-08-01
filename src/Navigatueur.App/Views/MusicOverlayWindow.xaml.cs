using System.Windows;
using System.Windows.Input;
using Navigatueur.App.Services;

namespace Navigatueur.App.Views;

/// <summary>
/// Detachable floating overlay: always-on-top, no taskbar entry, draggable by
/// its own background, showing/driving whatever the OS-wide SMTC session is
/// currently playing.
/// </summary>
public partial class MusicOverlayWindow : Window
{
    private bool _allowClose;

    public MusicOverlayWindow()
    {
        InitializeComponent();
        DataContext = AppServices.MusicController;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    /// <summary>Closes for real, bypassing the "click ✕ just hides" behavior used while the app is running.</summary>
    public void ShutDown()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
