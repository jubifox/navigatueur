namespace Navigatueur.Core.Shortcuts;

/// <summary>Stable command ids — the keys of AppSettings.ShortcutOverrides, so they must never be renamed.</summary>
public static class ShortcutCommands
{
    public const string NewTab = "tab.new";
    public const string CloseTab = "tab.close";
    public const string NextTab = "tab.next";
    public const string PreviousTab = "tab.previous";
    public const string ReopenClosedTab = "tab.reopen";
    public const string ShowTabs = "tabs.show";
    public const string FocusAddressBar = "address.focus";
    public const string Reload = "page.reload";
    public const string Back = "page.back";
    public const string Forward = "page.forward";
    public const string ToggleFullScreen = "view.fullscreen";
    public const string ToggleBookmark = "bookmark.toggle";
    public const string NewWindow = "window.new";
    public const string NewPrivateWindow = "window.private";
    public const string OpenHistory = "history.open";
    public const string OpenDownloads = "downloads.open";
    public const string OpenSettings = "settings.open";

    /// <summary>Ctrl+1…Ctrl+8 jump to that tab; fixed, not customizable.</summary>
    public const string SelectTabPrefix = "tab.select.";

    /// <summary>Ctrl+9 jumps to the last tab whatever the count; fixed, not customizable.</summary>
    public const string SelectLastTab = "tab.select.last";
}

/// <summary>One command that can be bound to keys, with its French label for Settings.</summary>
public sealed record ShortcutDefinition(
    string Id,
    string Label,
    string Category,
    IReadOnlyList<ShortcutGesture> Defaults,
    bool IsCustomizable = true);

public static class ShortcutCatalog
{
    public const string CategoryTabs = "Onglets";
    public const string CategoryNavigation = "Navigation";
    public const string CategoryWindow = "Fenêtre et outils";

    public static IReadOnlyList<ShortcutDefinition> All { get; } = Build();

    public static ShortcutDefinition? Find(string id) => All.FirstOrDefault(d => d.Id == id);

    private static List<ShortcutDefinition> Build()
    {
        static ShortcutDefinition Def(string id, string label, string category, params string[] gestures) =>
            new(id, label, category, gestures.Select(ShortcutGesture.Parse).ToList());

        var list = new List<ShortcutDefinition>
        {
            Def(ShortcutCommands.NewTab, "Nouvel onglet", CategoryTabs, "Ctrl+T"),
            Def(ShortcutCommands.CloseTab, "Fermer l'onglet", CategoryTabs, "Ctrl+W", "Ctrl+F4"),
            Def(ShortcutCommands.NextTab, "Onglet suivant", CategoryTabs, "Ctrl+Tab", "Ctrl+PageDown"),
            Def(ShortcutCommands.PreviousTab, "Onglet précédent", CategoryTabs, "Ctrl+Shift+Tab", "Ctrl+PageUp"),
            Def(ShortcutCommands.ReopenClosedTab, "Rouvrir le dernier onglet fermé", CategoryTabs, "Ctrl+Shift+T"),
            Def(ShortcutCommands.ShowTabs, "Afficher la colonne d'onglets", CategoryTabs, "Ctrl+Shift+A"),

            Def(ShortcutCommands.FocusAddressBar, "Barre d'adresse", CategoryNavigation, "Ctrl+L", "Alt+D", "F6"),
            Def(ShortcutCommands.Reload, "Actualiser", CategoryNavigation, "Ctrl+R", "F5"),
            Def(ShortcutCommands.Back, "Page précédente", CategoryNavigation, "Alt+Left"),
            Def(ShortcutCommands.Forward, "Page suivante", CategoryNavigation, "Alt+Right"),
            Def(ShortcutCommands.ToggleBookmark, "Ajouter ou retirer des favoris", CategoryNavigation, "Ctrl+D"),

            Def(ShortcutCommands.ToggleFullScreen, "Plein écran", CategoryWindow, "F11"),
            Def(ShortcutCommands.NewWindow, "Nouvelle fenêtre", CategoryWindow, "Ctrl+N"),
            Def(ShortcutCommands.NewPrivateWindow, "Nouvelle fenêtre privée", CategoryWindow, "Ctrl+Shift+N"),
            Def(ShortcutCommands.OpenHistory, "Historique", CategoryWindow, "Ctrl+H"),
            Def(ShortcutCommands.OpenDownloads, "Téléchargements", CategoryWindow, "Ctrl+J"),
            Def(ShortcutCommands.OpenSettings, "Paramètres", CategoryWindow, "Ctrl+Comma"),
        };

        for (var i = 1; i <= 8; i++)
        {
            list.Add(new ShortcutDefinition(
                ShortcutCommands.SelectTabPrefix + i,
                $"Aller à l'onglet {i}",
                CategoryTabs,
                new[] { ShortcutGesture.Parse($"Ctrl+{i}") },
                IsCustomizable: false));
        }

        list.Add(new ShortcutDefinition(
            ShortcutCommands.SelectLastTab,
            "Aller au dernier onglet",
            CategoryTabs,
            new[] { ShortcutGesture.Parse("Ctrl+9") },
            IsCustomizable: false));

        return list;
    }
}
