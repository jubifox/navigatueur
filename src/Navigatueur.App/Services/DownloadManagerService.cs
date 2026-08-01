using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Web.WebView2.Core;

namespace Navigatueur.App.Services;

/// <summary>Tracks downloads across every tab/window sharing the normal profile, for the toolbar's downloads button/panel.</summary>
public partial class DownloadManagerService : ObservableObject
{
    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = new();

    [ObservableProperty]
    private bool hasActiveDownloads;

    public void TrackDownload(CoreWebView2DownloadOperation operation)
    {
        var item = new DownloadItemViewModel(operation);
        Downloads.Insert(0, item);
        item.PropertyChanged += (_, _) => RefreshHasActiveDownloads();
        RefreshHasActiveDownloads();
    }

    private void RefreshHasActiveDownloads() =>
        HasActiveDownloads = Downloads.Any(d => d.IsInProgress);
}
