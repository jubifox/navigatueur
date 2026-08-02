using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Navigatueur.App.Models;
using Navigatueur.App.Services;

namespace Navigatueur.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly TabManagerService _tabManager;

    public ObservableCollection<BrowserTabViewModel> Tabs => _tabManager.Tabs;

    public ObservableCollection<object> TabStripItems => _tabManager.TabStripItems;

    public ObservableCollection<TabContextMenuEntry> SavedGroupMenuItems => _tabManager.SavedGroupMenuItems;

    public BrowserTabViewModel? ActiveTab => _tabManager.ActiveTab;

    public MemoryMonitorService MemoryMonitor => AppServices.MemoryMonitor;

    public ThemeService Theme => AppServices.Theme;

    public UpdateService Update => AppServices.Update;

    public DownloadManagerService Downloads => AppServices.Downloads;

    /// <summary>UI-only, not persisted. When pinned, the tab strip stays fully expanded and ignores hover.</summary>
    [ObservableProperty]
    private bool isSidebarPinned;

    /// <summary>Drives the tab strip's actual visual state (labels shown/hidden) — true when pinned, or currently hovered.</summary>
    [ObservableProperty]
    private bool isSidebarExpanded;

    public MainWindowViewModel(TabManagerService tabManager)
    {
        _tabManager = tabManager;
        _tabManager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TabManagerService.ActiveTab))
            {
                OnPropertyChanged(nameof(ActiveTab));
            }
        };

        // TabManagerService's own constructor guarantees at least one tab
        // exists by now (restored from the previous session, or a fresh
        // home tab), so there's nothing to open here.
    }

    [RelayCommand]
    private void NewTab() => _tabManager.OpenTab();

    [RelayCommand]
    private void ActivateTab(BrowserTabViewModel tab) => _tabManager.ActivateTab(tab);

    [RelayCommand]
    private void CloseTab(BrowserTabViewModel tab) => _tabManager.CloseTab(tab);

    [RelayCommand]
    private void ToggleGroupCollapsed(TabGroup group) => _tabManager.ToggleGroupCollapsed(group);

    [RelayCommand]
    private void ToggleSidebarPinned()
    {
        IsSidebarPinned = !IsSidebarPinned;
        IsSidebarExpanded = IsSidebarPinned;
    }
}
