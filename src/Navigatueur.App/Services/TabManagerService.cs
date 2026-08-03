using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Navigatueur.App.Models;
using Navigatueur.App.ViewModels;
using Navigatueur.Core.Settings;

namespace Navigatueur.App.Services;

public partial class TabManagerService : ObservableObject
{
    private const int MaxLiveTabs = 3;
    private static readonly TimeSpan IdleSuspendThreshold = TimeSpan.FromMinutes(15);

    private readonly AppSettings _settings;
    private readonly List<BrowserTabViewModel> _liveOrder = new();
    private readonly DispatcherTimer _idleTimer;

    public ObservableCollection<BrowserTabViewModel> Tabs { get; } = new();

    public ObservableCollection<TabGroup> Groups { get; } = new();

    /// <summary>
    /// Flat, display-ready mix of <see cref="TabGroup"/> headers and their
    /// <see cref="BrowserTabViewModel"/> members, rebuilt wholesale on every
    /// change rather than diffed incrementally — tab counts are small enough
    /// for a personal browser that this stays cheap.
    /// </summary>
    public ObservableCollection<object> TabStripItems { get; } = new();

    /// <summary>Groups the user explicitly saved, reopenable on demand regardless of session restore.</summary>
    public ObservableCollection<SavedTabGroup> SavedGroups { get; } = new();

    /// <summary>"Ouvrir"/"Supprimer" entries for every saved group, for the toolbar's saved-groups menu.</summary>
    public ObservableCollection<TabContextMenuEntry> SavedGroupMenuItems { get; } = new();

    [ObservableProperty]
    private BrowserTabViewModel? activeTab;

    /// <summary>Which WebView2 profile this manager's tabs load into — a separate, throwaway one for private browsing.</summary>
    public WebView2EnvironmentService Environment { get; }

    /// <summary>True for the private-browsing window's manager: disables writing anything (saved groups, session state) to the shared settings.json.</summary>
    public bool IsPrivate { get; }

    public TabManagerService(AppSettings settings, WebView2EnvironmentService? environment = null, bool isPrivate = false)
    {
        _settings = settings;
        Environment = environment ?? AppServices.WebView2Environment;
        IsPrivate = isPrivate;

        Tabs.CollectionChanged += (_, _) => RebuildTabStripItems();
        Groups.CollectionChanged += (_, _) => RebuildTabStripItems();

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _idleTimer.Tick += (_, _) => SuspendIdleTabs();
        _idleTimer.Start();

        foreach (var saved in _settings.SavedGroups)
        {
            SavedGroups.Add(saved);
        }

        SavedGroups.CollectionChanged += (_, _) =>
        {
            RebuildSavedGroupMenuItems();
            foreach (var group in Groups)
            {
                RefreshGroupContextMenu(group);
            }
        };
        RebuildSavedGroupMenuItems();

        if (_settings.Tabs.Count > 0)
        {
            RestoreSession(_settings.Groups, _settings.Tabs, _settings.ActiveTabIndex);
        }
        else
        {
            OpenTab();
        }
    }

    /// <summary>Raised whenever a genuinely new tab is created via <see cref="OpenTab"/> — not for tabs recreated during session restore or bulk-reopen of a saved group, which shouldn't steal focus.</summary>
    public event Action<BrowserTabViewModel>? TabOpened;

    public BrowserTabViewModel OpenTab(string? url = null)
    {
        var tab = new BrowserTabViewModel(url ?? _settings.HomePageUrl, this);
        AttachTabHandlers(tab);
        Tabs.Add(tab);
        ActivateTab(tab);
        TabOpened?.Invoke(tab);
        return tab;
    }

    /// <summary>Cycles the active tab forward (+1) or backward (-1) through <see cref="Tabs"/>, wrapping around. Used by Ctrl+Tab / Ctrl+Shift+Tab.</summary>
    public void ActivateAdjacentTab(int direction)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        var currentIndex = ActiveTab is null ? -1 : Tabs.IndexOf(ActiveTab);
        var nextIndex = ((currentIndex + direction) % Tabs.Count + Tabs.Count) % Tabs.Count;
        ActivateTab(Tabs[nextIndex]);
    }

    /// <summary>Used by Ctrl+1..Ctrl+8 to jump straight to a tab by its position.</summary>
    public void ActivateTabAtIndex(int index)
    {
        if (index >= 0 && index < Tabs.Count)
        {
            ActivateTab(Tabs[index]);
        }
    }

    /// <summary>Used by Ctrl+9, matching the browser convention of always jumping to the last tab regardless of count.</summary>
    public void ActivateLastTab()
    {
        if (Tabs.Count > 0)
        {
            ActivateTab(Tabs[^1]);
        }
    }

    /// <summary>Pinning is toggled by the tab itself (context menu), so the strip needs to react even without going through a TabManagerService method.</summary>
    private void AttachTabHandlers(BrowserTabViewModel tab)
    {
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrowserTabViewModel.IsPinned))
            {
                RebuildTabStripItems();
            }
        };
    }

    /// <summary>
    /// Reopens the tabs (and groups) that were open on the previous shutdown.
    /// Every restored tab starts suspended — except the one that was active —
    /// so reopening a big session doesn't spin up several WebView2 processes
    /// at once; the rest load lazily as the user clicks into them.
    /// </summary>
    private void RestoreSession(
        IReadOnlyList<SessionGroupState> groupStates,
        IReadOnlyList<SessionTabState> tabStates,
        int activeIndex)
    {
        var groupIdMap = new Dictionary<Guid, TabGroup>();
        foreach (var groupState in groupStates)
        {
            var group = CreateGroup(groupState.Name, groupState.ColorHex);
            group.IsCollapsed = groupState.IsCollapsed;
            groupIdMap[groupState.Id] = group;
        }

        var restoredTabs = new List<BrowserTabViewModel>();
        foreach (var tabState in tabStates)
        {
            var url = IsLegacyBingHomePage(tabState.Url) ? _settings.HomePageUrl : tabState.Url;
            var tab = new BrowserTabViewModel(url, this) { IsSuspended = true };
            if (tabState.GroupId is { } persistedGroupId && groupIdMap.TryGetValue(persistedGroupId, out var group))
            {
                tab.GroupId = group.Id;
            }

            tab.IsPinned = tabState.IsPinned;

            AttachTabHandlers(tab);
            Tabs.Add(tab);
            restoredTabs.Add(tab);
        }

        if (restoredTabs.Count == 0)
        {
            OpenTab();
            return;
        }

        var indexToActivate = activeIndex >= 0 && activeIndex < restoredTabs.Count ? activeIndex : 0;
        ActivateTab(restoredTabs[indexToActivate]);
    }

    /// <summary>
    /// A tab persisted by an older build that still pointed at Bing (the old
    /// hardcoded default) was, in practice, just a homepage tab that had never
    /// been navigated away from — restore it to the current homepage instead
    /// of freezing it on the legacy URL forever.
    /// </summary>
    private static bool IsLegacyBingHomePage(string url) =>
        url.Equals("https://www.bing.com", StringComparison.OrdinalIgnoreCase) ||
        url.Equals("https://www.bing.com/", StringComparison.OrdinalIgnoreCase);

    public void ActivateTab(BrowserTabViewModel tab)
    {
        if (ActiveTab is not null)
        {
            ActiveTab.IsActive = false;
        }

        ActiveTab = tab;
        tab.IsActive = true;
        MarkLive(tab);
    }

    public void CloseTab(BrowserTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        Tabs.RemoveAt(index);
        _liveOrder.Remove(tab);

        if (Tabs.Count == 0)
        {
            OpenTab();
            return;
        }

        if (ActiveTab == tab)
        {
            var nextIndex = Math.Min(index, Tabs.Count - 1);
            ActivateTab(Tabs[nextIndex]);
        }
    }

    public TabGroup CreateGroup(string name, string colorHex)
    {
        var group = new TabGroup(name, colorHex);
        group.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TabGroup.IsCollapsed))
            {
                RebuildTabStripItems();
            }
            else if (e.PropertyName == nameof(TabGroup.ColorHex))
            {
                RefreshGroupContextMenu(group); // so the picker stops offering the color that's now already active
            }
        };
        Groups.Add(group);
        RefreshGroupContextMenu(group);
        return group;
    }

    /// <summary>Ungroups every member tab (they stay open) and removes the group itself. Any saved snapshot is untouched — it's independent on purpose.</summary>
    public void DeleteGroup(TabGroup group)
    {
        foreach (var tab in Tabs.Where(t => t.GroupId == group.Id).ToList())
        {
            tab.GroupId = null;
        }

        Groups.Remove(group);
    }

    private static readonly (string Name, string Hex)[] GroupColorChoices =
    {
        ("Bleu", "#4C8DFF"), ("Vert", "#4FE0A0"), ("Ambre", "#E0A52A"),
        ("Rouge", "#E04F4F"), ("Violet", "#B14FE0"), ("Cyan", "#4FD1E0"),
    };

    private void RefreshGroupContextMenu(TabGroup group)
    {
        group.ContextMenuItems.Clear();

        group.ContextMenuItems.Add(new TabContextMenuEntry(
            "Renommer...",
            new RelayCommand(() => group.IsEditingName = true)));

        foreach (var (name, hex) in GroupColorChoices)
        {
            if (string.Equals(group.ColorHex, hex, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            group.ContextMenuItems.Add(new TabContextMenuEntry(
                $"Couleur : {name}",
                new RelayCommand(() => group.ColorHex = hex)));
        }

        group.ContextMenuItems.Add(new TabContextMenuEntry(
            "Enregistrer le groupe",
            new RelayCommand(() => SaveGroup(group))));

        var saved = SavedGroups.FirstOrDefault(g => g.Id == group.Id);
        if (saved is not null)
        {
            group.ContextMenuItems.Add(new TabContextMenuEntry(
                "Supprimer la sauvegarde",
                new RelayCommand(() => DeleteSavedGroup(saved))));
        }

        group.ContextMenuItems.Add(new TabContextMenuEntry(
            "Supprimer le groupe",
            new RelayCommand(() => DeleteGroup(group))));
    }

    private void RebuildSavedGroupMenuItems()
    {
        SavedGroupMenuItems.Clear();

        foreach (var saved in SavedGroups)
        {
            var capturedSaved = saved;
            SavedGroupMenuItems.Add(new TabContextMenuEntry(
                $"Ouvrir « {saved.Name} »",
                new RelayCommand(() => OpenSavedGroup(capturedSaved))));
            SavedGroupMenuItems.Add(new TabContextMenuEntry(
                $"Supprimer « {saved.Name} »",
                new RelayCommand(() => DeleteSavedGroup(capturedSaved))));
        }
    }

    public void CreateGroupForTab(BrowserTabViewModel tab)
    {
        var color = GroupColorChoices[Groups.Count % GroupColorChoices.Length].Hex;
        var group = CreateGroup($"Groupe {Groups.Count + 1}", color);
        AssignToGroup(tab, group);
    }

    public void AssignToGroup(BrowserTabViewModel tab, TabGroup group)
    {
        tab.GroupId = group.Id;
        RebuildTabStripItems();
    }

    public void RemoveFromGroup(BrowserTabViewModel tab)
    {
        tab.GroupId = null;
        RebuildTabStripItems();
    }

    public void ToggleGroupCollapsed(TabGroup group) => group.IsCollapsed = !group.IsCollapsed;

    /// <summary>Moves <paramref name="source"/> to just before (or after) <paramref name="target"/> in the tab strip, adopting the target's group if different.</summary>
    public void ReorderTab(BrowserTabViewModel source, BrowserTabViewModel target, bool insertAfter = false)
    {
        if (source == target || !Tabs.Contains(source) || !Tabs.Contains(target))
        {
            return;
        }

        Tabs.Remove(source);
        var targetIndex = Tabs.IndexOf(target);
        source.GroupId = target.GroupId;
        Tabs.Insert(insertAfter ? targetIndex + 1 : targetIndex, source);
        RebuildTabStripItems();
    }

    /// <summary>
    /// Dropping one tab onto another (center of the row, not the edge) groups
    /// them, matching Chrome/Edge tab-group UX. If the target is already in a
    /// group, the source joins that group; otherwise a fresh group is created
    /// for both.
    /// </summary>
    public void GroupTabs(BrowserTabViewModel source, BrowserTabViewModel target)
    {
        if (source == target || !Tabs.Contains(source) || !Tabs.Contains(target))
        {
            return;
        }

        TabGroup group;
        if (target.GroupId is { } existingGroupId && Groups.FirstOrDefault(g => g.Id == existingGroupId) is { } existingGroup)
        {
            group = existingGroup;
        }
        else
        {
            var color = GroupColorChoices[Groups.Count % GroupColorChoices.Length].Hex;
            group = CreateGroup($"Groupe {Groups.Count + 1}", color);
            target.GroupId = group.Id;
        }

        source.GroupId = group.Id;

        Tabs.Remove(source);
        var targetIndex = Tabs.IndexOf(target);
        Tabs.Insert(targetIndex + 1, source);
        RebuildTabStripItems();
    }

    /// <summary>Persists <paramref name="group"/> and its current tabs' URLs so it can be reopened later via <see cref="OpenSavedGroup"/>.</summary>
    public void SaveGroup(TabGroup group)
    {
        var urls = Tabs.Where(t => t.GroupId == group.Id).Select(t => t.AddressBarText).ToList();
        if (urls.Count == 0)
        {
            return;
        }

        var existing = SavedGroups.FirstOrDefault(g => g.Id == group.Id);
        if (existing is not null)
        {
            SavedGroups.Remove(existing);
        }

        SavedGroups.Add(new SavedTabGroup { Id = group.Id, Name = group.Name, ColorHex = group.ColorHex, Urls = urls });
        PersistSavedGroups();
    }

    public void DeleteSavedGroup(SavedTabGroup saved)
    {
        SavedGroups.Remove(saved);
        PersistSavedGroups();
    }

    /// <summary>Reopens a saved group as a new live group, all tabs suspended except the first — same lazy-load approach as session restore.</summary>
    public void OpenSavedGroup(SavedTabGroup saved)
    {
        var group = CreateGroup(saved.Name, saved.ColorHex);

        BrowserTabViewModel? firstTab = null;
        foreach (var url in saved.Urls)
        {
            var tab = new BrowserTabViewModel(url, this) { IsSuspended = true, GroupId = group.Id };
            AttachTabHandlers(tab);
            Tabs.Add(tab);
            firstTab ??= tab;
        }

        if (firstTab is not null)
        {
            ActivateTab(firstTab);
        }
    }

    private void PersistSavedGroups()
    {
        if (IsPrivate)
        {
            return; // Never let a private window's throwaway AppSettings overwrite the real settings.json.
        }

        _settings.SavedGroups = SavedGroups.ToList();
        AppServices.Settings.Save(_settings);
    }

    private void MarkLive(BrowserTabViewModel tab)
    {
        tab.IsSuspended = false;
        tab.LastActivatedAt = DateTimeOffset.Now;

        if (tab.IsPinned)
        {
            return; // pinned tabs stay outside the LRU live-set — always live, never evicted.
        }

        _liveOrder.Remove(tab);
        _liveOrder.Insert(0, tab);

        while (_liveOrder.Count > MaxLiveTabs)
        {
            var evictIndex = -1;
            for (var i = _liveOrder.Count - 1; i >= 0; i--)
            {
                if (!_liveOrder[i].IsPlayingAudio)
                {
                    evictIndex = i;
                    break;
                }
            }

            if (evictIndex < 0)
            {
                break; // every live tab is currently playing audio — exceed the cap rather than cut one off.
            }

            var evicted = _liveOrder[evictIndex];
            _liveOrder.RemoveAt(evictIndex);
            evicted.IsSuspended = true;
        }
    }

    private void SuspendIdleTabs()
    {
        var now = DateTimeOffset.Now;
        for (var i = _liveOrder.Count - 1; i >= 0; i--)
        {
            var tab = _liveOrder[i];
            if (tab == ActiveTab || tab.IsPinned || tab.IsPlayingAudio)
            {
                continue;
            }

            if (now - tab.LastActivatedAt > IdleSuspendThreshold)
            {
                _liveOrder.RemoveAt(i);
                tab.IsSuspended = true;
            }
        }
    }

    private void RebuildTabStripItems()
    {
        TabStripItems.Clear();

        foreach (var tab in Tabs.Where(t => t.IsPinned))
        {
            TabStripItems.Add(tab);
        }

        foreach (var tab in Tabs.Where(t => !t.IsPinned && t.GroupId is null))
        {
            TabStripItems.Add(tab);
        }

        foreach (var group in Groups)
        {
            TabStripItems.Add(group);

            if (group.IsCollapsed)
            {
                continue;
            }

            foreach (var tab in Tabs.Where(t => !t.IsPinned && t.GroupId == group.Id))
            {
                TabStripItems.Add(tab);
            }
        }
    }
}
