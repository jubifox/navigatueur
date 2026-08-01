using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Navigatueur.App.ViewModels;

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

    /// <summary>Not persisted — toggled by the "Renommer" context menu action to swap the header into an editable TextBox.</summary>
    [ObservableProperty]
    private bool isEditingName;

    public TabGroup(string name, string colorHex)
    {
        this.name = name;
        this.colorHex = colorHex;
    }
}
