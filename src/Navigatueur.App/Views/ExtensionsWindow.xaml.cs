using System.Windows;
using Microsoft.Win32;
using Navigatueur.App.Services;

namespace Navigatueur.App.Views;

public partial class ExtensionsWindow : Window
{
    public ExtensionsWindow()
    {
        InitializeComponent();
        DataContext = AppServices.Extensions;
        AppServices.Extensions.PropertyChanged += OnExtensionsPropertyChanged;
        UpdateError();
    }

    private void OnExtensionsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExtensionService.LastError))
        {
            UpdateError();
        }
    }

    private void UpdateError()
    {
        var error = AppServices.Extensions.LastError;
        ErrorText.Text = error;
        ErrorText.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnAddExtensionClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choisir le dossier de l'extension (contenant manifest.json)" };
        if (dialog.ShowDialog() == true)
        {
            await AppServices.Extensions.AddExtensionAsync(dialog.FolderName);
        }
    }
}
