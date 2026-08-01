using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;

namespace Navigatueur.App.Services;

/// <summary>Wraps one CoreWebView2DownloadOperation, translating its events into observable progress for the downloads panel.</summary>
public partial class DownloadItemViewModel : ObservableObject
{
    private readonly CoreWebView2DownloadOperation _operation;

    public DownloadItemViewModel(CoreWebView2DownloadOperation operation)
    {
        _operation = operation;
        FileName = Path.GetFileName(operation.ResultFilePath);

        operation.BytesReceivedChanged += (_, _) => UpdateProgress();
        operation.StateChanged += (_, _) => UpdateProgress();
        UpdateProgress();
    }

    public string FileName { get; }

    private string FilePath => _operation.ResultFilePath;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private bool isInProgress;

    [ObservableProperty]
    private string statusText = string.Empty;

    private void UpdateProgress()
    {
        var total = _operation.TotalBytesToReceive;
        Progress = total is > 0 ? (double)_operation.BytesReceived / total.Value : 0;
        IsInProgress = _operation.State == CoreWebView2DownloadState.InProgress;
        StatusText = _operation.State switch
        {
            CoreWebView2DownloadState.InProgress => Progress.ToString("P0"),
            CoreWebView2DownloadState.Completed => "Terminé",
            CoreWebView2DownloadState.Interrupted => "Annulé",
            _ => string.Empty,
        };
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (File.Exists(FilePath))
        {
            Process.Start("explorer.exe", $"/select,\"{FilePath}\"");
        }
    }
}
