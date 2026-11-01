using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Navigatueur.App.ViewModels;
using Navigatueur.Core.Tabs;

namespace Navigatueur.App.Models;

public partial class TabGroup : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    public ObservableCollection<TabContextMenuEntry> ContextMenuItems { get; } = new();

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string colorHex;

    [ObservableProperty]
    private bool isCollapsed;

    /// <summary>Glyph drawn inside the group's folder icon (GroupIcons key; empty for a plain folder).</summary>
    [ObservableProperty]
    private string icon;

    /// <summary>Not persisted — toggled by the "Renommer" context menu action to swap the header into an editable TextBox.</summary>
    [ObservableProperty]
    private bool isEditingName;

    /// <summary>Not persisted — opens the icon picker under the group's header.</summary>
    [ObservableProperty]
    private bool isPickingIcon;

    public TabGroup(string name, string colorHex, string? icon = null)
    {
        this.name = name;
        this.colorHex = colorHex;
        this.icon = GroupIcons.Normalize(icon);
    }

    partial void OnIconChanged(string value)
    {
        var normalized = GroupIcons.Normalize(value);
        if (normalized != value)
        {
            Icon = normalized;
        }
    }

    /// <summary>The picker's choices, bound by the TabsPanel.</summary>
    public static IReadOnlyList<GroupIcon> IconChoices => GroupIcons.All;

    /// <summary>Picks a glyph (or none, with an empty key) and closes the picker.</summary>
    [RelayCommand]
    private void ChooseIcon(string? key)
    {
        Icon = GroupIcons.Normalize(key);
        IsPickingIcon = false;
    }
}
