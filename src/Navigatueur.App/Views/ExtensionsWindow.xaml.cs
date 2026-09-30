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

    /// <summary>
    /// For an extension added before v0.17.0 started tracking install folders
    /// (see InstalledExtension.NeedsFolderLocation) — asks once for its
    /// unpacked folder again so "Ouvrir" can work from then on, without
    /// removing/re-adding the extension itself.
    /// </summary>
    private async void OnLocateExtensionFolderClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledExtension extension)
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = $"Localiser le dossier de « {extension.Name} » (contenant manifest.json)" };
        if (dialog.ShowDialog() == true)
        {
            await AppServices.Extensions.SetManifestFolderAsync(extension.Id, dialog.FolderName);
        }
    }
}
