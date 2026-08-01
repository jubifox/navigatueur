using CommunityToolkit.Mvvm.ComponentModel;

namespace Navigatueur.App.ViewModels;

/// <summary>UI-side wrapper for a search engine option, used by both the first-run welcome dialog and the Settings window to show which one is currently selected.</summary>
public partial class SearchEngineChoice : ObservableObject
{
    public SearchEngineChoice(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; }

    public string DisplayName { get; }

    [ObservableProperty]
    private bool isSelected;
}
