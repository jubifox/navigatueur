using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Navigatueur.App.Models;
using Navigatueur.App.Services;
using Navigatueur.Core.Bookmarks;
using Navigatueur.Core.Settings;

namespace Navigatueur.App.ViewModels;

/// <summary>
/// State shared by one browser window and its two panels. The panels live in
/// their own child windows (see OverlayHost), so they can't inherit a
/// DataContext from the window: all three are given this same instance.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly TabManagerService _tabManager;
    private BrowserTabViewModel? _observedTab;

    public MainWindowViewModel(TabManagerService tabManager, bool isPrivate = false)
    {
        _tabManager = tabManager;
        IsPrivate = isPrivate;

        _tabManager.PropertyChanged += OnTabManagerPropertyChanged;
        _tabManager.Tabs.CollectionChanged += OnTabsChanged;
        _tabManager.SavedGroupMenuItems.CollectionChanged += OnTabsChanged;
        AppServices.Interface.Changed += OnInterfaceChanged;
        AppServices.Bookmarks.Changed += OnBookmarksChanged;
        ObserveActiveTab();
    }

    public TabManagerService TabManager => _tabManager;

    public ObservableCollection<BrowserTabViewModel> Tabs => _tabManager.Tabs;

    public ObservableCollection<object> TabStripItems => _tabManager.TabStripItems;

    public ObservableCollection<TabContextMenuEntry> SavedGroupMenuItems => _tabManager.SavedGroupMenuItems;

    public BrowserTabViewModel? ActiveTab => _tabManager.ActiveTab;

    public MemoryMonitorService MemoryMonitor => AppServices.MemoryMonitor;

    public ThemeService Theme => AppServices.Theme;

    public UpdateService Update => AppServices.Update;

    public DownloadManagerService Downloads => AppServices.Downloads;

    public ObservableCollection<Bookmark> Bookmarks => AppServices.Bookmarks.Bookmarks;

    public bool IsPrivate { get; }

    public int TabCount => Tabs.Count;

    public bool HasSavedGroups => SavedGroupMenuItems.Count > 0;

    /// <summary>Taskbar/Alt+Tab title: the page's title, since the window itself has no visible title bar any more.</summary>
    public string WindowTitle
    {
        get
        {
            var suffix = IsPrivate ? "Navigatueur — Navigation privée" : "Navigatueur";
            var title = ActiveTab?.Title;
            return string.IsNullOrWhiteSpace(title) ? suffix : $"{title} — {suffix}";
        }
    }

    public bool IsSidebarDocked => AppServices.Interface.SidebarBehavior == PanelBehavior.Docked;

    public bool IsSidebarOnRight => AppServices.Interface.SidebarSide == PanelSide.Right;

    /// <summary>Set by the window; drives the maximize/restore glyph.</summary>
    [ObservableProperty]
    private bool isMaximized;

    [ObservableProperty]
    private bool isFullScreen;

    /// <summary>Called when the window closes: the services outlive it, so their events must not keep it alive.</summary>
    public void Detach()
    {
        _tabManager.PropertyChanged -= OnTabManagerPropertyChanged;
        _tabManager.Tabs.CollectionChanged -= OnTabsChanged;
        _tabManager.SavedGroupMenuItems.CollectionChanged -= OnTabsChanged;
        AppServices.Interface.Changed -= OnInterfaceChanged;
        AppServices.Bookmarks.Changed -= OnBookmarksChanged;
        if (_observedTab is not null)
        {
            _observedTab.PropertyChanged -= OnActiveTabPropertyChanged;
            _observedTab = null;
        }
    }

    private void OnTabManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabManagerService.ActiveTab))
        {
            ObserveActiveTab();
            OnPropertyChanged(nameof(ActiveTab));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    private void OnTabsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TabCount));
        OnPropertyChanged(nameof(HasSavedGroups));
    }

    private void OnInterfaceChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsSidebarDocked));
        OnPropertyChanged(nameof(IsSidebarOnRight));
    }

    private void OnBookmarksChanged(object? sender, EventArgs e)
    {
        foreach (var tab in Tabs)
        {
            tab.RefreshBookmarkState();
        }
    }

    private void ObserveActiveTab()
    {
        if (_observedTab is not null)
        {
            _observedTab.PropertyChanged -= OnActiveTabPropertyChanged;
        }

        _observedTab = _tabManager.ActiveTab;
        if (_observedTab is not null)
        {
            _observedTab.PropertyChanged += OnActiveTabPropertyChanged;
            _observedTab.RefreshBookmarkState();
        }
    }

    private void OnActiveTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserTabViewModel.Title))
        {
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    [RelayCommand]
    private void NewTab() => _tabManager.OpenTab();

    [RelayCommand]
    private void ActivateTab(BrowserTabViewModel tab) => _tabManager.ActivateTab(tab);

    [RelayCommand]
    private void CloseTab(BrowserTabViewModel tab) => _tabManager.CloseTab(tab);

    [RelayCommand]
    private void ToggleGroupCollapsed(TabGroup group) => _tabManager.ToggleGroupCollapsed(group);
}
