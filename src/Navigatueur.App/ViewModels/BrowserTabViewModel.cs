using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;
using Navigatueur.App.Services;
using Navigatueur.Core;

namespace Navigatueur.App.ViewModels;

public partial class BrowserTabViewModel : ObservableObject
{
    private readonly TabManagerService _tabManager;

    public TabManagerService TabManager => _tabManager;

    public Guid Id { get; } = Guid.NewGuid();

    public string InitialUrl { get; }

    public ObservableCollection<TabContextMenuEntry> ContextMenuItems { get; } = new();

    [ObservableProperty]
    private string title = "Nouvel onglet";

    [ObservableProperty]
    private string addressBarText;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private bool isSuspended;

    [ObservableProperty]
    private DateTimeOffset lastActivatedAt = DateTimeOffset.Now;

    [ObservableProperty]
    private Guid? groupId;

    public bool IsGrouped => GroupId is not null;

    [ObservableProperty]
    private bool isPinned;

    [ObservableProperty]
    private bool isMuted;

    /// <summary>Per-tab escape hatch for the rare page a domain-list ad blocker still breaks, without touching global settings.</summary>
    [ObservableProperty]
    private bool isAdBlockDisabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool canGoBack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    private bool canGoForward;

    private const string WarningPageUrl = "https://navigatueur.warning/index.html";
    private const string WarningContinueUrl = "https://navigatueur.warning/continue";

    private readonly HashSet<string> _phishingBypassedUrls = new();

    private CoreWebView2? _coreWebView2;

    private readonly EventHandler<CoreWebView2NavigationStartingEventArgs> _onNavigationStarting;
    private readonly EventHandler<CoreWebView2NavigationCompletedEventArgs> _onNavigationCompleted;
    private readonly EventHandler<CoreWebView2SourceChangedEventArgs> _onSourceChanged;
    private readonly EventHandler<object> _onDocumentTitleChanged;

    public BrowserTabViewModel(string initialUrl, TabManagerService tabManager)
    {
        InitialUrl = initialUrl;
        addressBarText = initialUrl;
        _tabManager = tabManager;

        _onNavigationStarting = OnNavigationStarting;
        _onNavigationCompleted = (_, _) =>
        {
            IsLoading = false;
            CanGoBack = _coreWebView2?.CanGoBack ?? false;
            CanGoForward = _coreWebView2?.CanGoForward ?? false;
        };
        _onSourceChanged = (_, _) => AddressBarText = _coreWebView2?.Source ?? AddressBarText;
        _onDocumentTitleChanged = (_, _) =>
            Title = string.IsNullOrWhiteSpace(_coreWebView2?.DocumentTitle)
                ? AddressBarText
                : _coreWebView2!.DocumentTitle;

        _tabManager.Groups.CollectionChanged += OnGroupsCollectionChanged;
        RefreshContextMenuItems();
    }

    private void OnGroupsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshContextMenuItems();

    /// <summary>
    /// Cancels navigation to a known phishing domain and redirects to an
    /// internal warning page instead. The warning page's "Continuer quand
    /// même" link re-navigates through a special /continue marker URL that
    /// this same handler recognizes to let that one URL through once.
    /// </summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        IsLoading = true;

        if (e.Uri.StartsWith(WarningContinueUrl, StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            var target = ExtractQueryParam(e.Uri, "url");
            if (!string.IsNullOrEmpty(target))
            {
                _phishingBypassedUrls.Add(target);
                _coreWebView2?.Navigate(target);
            }

            return;
        }

        if (_phishingBypassedUrls.Remove(e.Uri))
        {
            return;
        }

        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && AppServices.PhishingProtection.IsPhishing(uri.Host))
        {
            e.Cancel = true;
            IsLoading = false;
            _coreWebView2?.Navigate($"{WarningPageUrl}?url={Uri.EscapeDataString(e.Uri)}");
        }
    }

    private static string? ExtractQueryParam(string url, string key)
    {
        var queryIndex = url.IndexOf('?');
        if (queryIndex < 0)
        {
            return null;
        }

        foreach (var pair in url[(queryIndex + 1)..].Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == key)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    partial void OnGroupIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(IsGrouped));
        RefreshContextMenuItems();
    }

    private void RefreshContextMenuItems()
    {
        ContextMenuItems.Clear();

        ContextMenuItems.Add(new TabContextMenuEntry(
            IsPinned ? "Détacher" : "Épingler",
            new RelayCommand(() => IsPinned = !IsPinned)));

        ContextMenuItems.Add(new TabContextMenuEntry(
            IsMuted ? "Réactiver le son" : "Couper le son",
            ToggleMuteCommand));

        ContextMenuItems.Add(new TabContextMenuEntry(
            IsAdBlockDisabled ? "Réactiver le bloqueur sur ce site" : "Désactiver le bloqueur sur ce site",
            new RelayCommand(() => IsAdBlockDisabled = !IsAdBlockDisabled)));

        ContextMenuItems.Add(new TabContextMenuEntry(
            "Nouveau groupe...",
            new RelayCommand(() => _tabManager.CreateGroupForTab(this))));

        if (GroupId is not null)
        {
            ContextMenuItems.Add(new TabContextMenuEntry(
                "Retirer du groupe",
                new RelayCommand(() => _tabManager.RemoveFromGroup(this))));
        }

        foreach (var group in _tabManager.Groups)
        {
            if (group.Id == GroupId)
            {
                continue;
            }

            ContextMenuItems.Add(new TabContextMenuEntry(
                $"Ajouter à « {group.Name} »",
                new RelayCommand(() => _tabManager.AssignToGroup(this, group))));
        }

        ContextMenuItems.Add(new TabContextMenuEntry(
            "Fermer",
            new RelayCommand(() => _tabManager.CloseTab(this))));
    }

    partial void OnIsPinnedChanged(bool value)
    {
        if (value)
        {
            IsSuspended = false;
        }

        RefreshContextMenuItems();
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (_coreWebView2 is not null)
        {
            _coreWebView2.IsMuted = value;
        }

        RefreshContextMenuItems();
    }

    partial void OnIsAdBlockDisabledChanged(bool value)
    {
        RefreshContextMenuItems();
        _coreWebView2?.Reload();
    }

    public void AttachCoreWebView2(CoreWebView2 coreWebView2)
    {
        _coreWebView2 = coreWebView2;
        _coreWebView2.IsMuted = IsMuted;

        _coreWebView2.NavigationStarting += _onNavigationStarting;
        _coreWebView2.NavigationCompleted += _onNavigationCompleted;
        _coreWebView2.SourceChanged += _onSourceChanged;
        _coreWebView2.DocumentTitleChanged += _onDocumentTitleChanged;
    }

    /// <summary>
    /// Unhooks from the CoreWebView2 before it gets disposed, so the tab can
    /// be resumed later with a fresh WebView2 instance without leaking handlers.
    /// </summary>
    public void DetachCoreWebView2()
    {
        if (_coreWebView2 is null)
        {
            return;
        }

        _coreWebView2.NavigationStarting -= _onNavigationStarting;
        _coreWebView2.NavigationCompleted -= _onNavigationCompleted;
        _coreWebView2.SourceChanged -= _onSourceChanged;
        _coreWebView2.DocumentTitleChanged -= _onDocumentTitleChanged;
        _coreWebView2 = null;
        IsLoading = false;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => _coreWebView2?.GoBack();

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => _coreWebView2?.GoForward();

    [RelayCommand]
    private void Reload() => _coreWebView2?.Reload();

    [RelayCommand]
    private void Stop() => _coreWebView2?.Stop();

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    [RelayCommand]
    private void Navigate()
    {
        if (_coreWebView2 is null || string.IsNullOrWhiteSpace(AddressBarText))
        {
            return;
        }

        _coreWebView2.Navigate(UrlHelper.Normalize(AddressBarText));
    }
}
