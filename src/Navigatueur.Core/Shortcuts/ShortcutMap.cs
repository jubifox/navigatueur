namespace Navigatueur.Core.Shortcuts;

/// <summary>
/// The effective key bindings: catalog defaults with the user's overrides
/// applied. Lookups go both ways — gesture to command for dispatching key
/// presses, command to gestures for showing them in menus and Settings.
///
/// An override always wins: if the user binds Ctrl+R to "Nouvel onglet",
/// Ctrl+R stops reloading (reload keeps F5). An override that doesn't parse,
/// isn't a valid shortcut, or targets a fixed/unknown command is ignored, so a
/// bad settings.json entry degrades to the default instead of breaking keys.
/// </summary>
public sealed class ShortcutMap
{
    private readonly Dictionary<ShortcutGesture, string> _byGesture = new();
    private readonly Dictionary<string, List<ShortcutGesture>> _byCommand = new();

    private ShortcutMap()
    {
    }

    public static ShortcutMap Build(IReadOnlyDictionary<string, string>? overrides)
    {
        var map = new ShortcutMap();
        var overridden = new Dictionary<string, ShortcutGesture?>();

        if (overrides is not null)
        {
            foreach (var (id, text) in overrides)
            {
                if (ShortcutCatalog.Find(id) is not { IsCustomizable: true })
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    overridden[id] = null; // explicitly disabled
                }
                else if (ShortcutGesture.TryParse(text, out var gesture) && gesture.IsValidShortcut)
                {
                    overridden[id] = gesture;
                }
            }
        }

        // Defaults first, skipping commands the user overrode…
        foreach (var definition in ShortcutCatalog.All)
        {
            map._byCommand[definition.Id] = new List<ShortcutGesture>();
            if (overridden.ContainsKey(definition.Id))
            {
                continue;
            }

            foreach (var gesture in definition.Defaults)
            {
                map.Bind(definition.Id, gesture);
            }
        }

        // …then overrides, stealing their gesture from whichever default had it.
        foreach (var (id, gesture) in overridden)
        {
            if (gesture is { } value)
            {
                map.Bind(id, value);
            }
        }

        return map;
    }

    public bool TryGetCommand(ShortcutGesture gesture, out string commandId) =>
        _byGesture.TryGetValue(gesture, out commandId!);

    public IReadOnlyList<ShortcutGesture> GetGestures(string commandId) =>
        _byCommand.TryGetValue(commandId, out var list) ? list : Array.Empty<ShortcutGesture>();

    /// <summary>The command <paramref name="gesture"/> is currently bound to, other than <paramref name="exceptCommandId"/> — used by Settings to warn before reassigning.</summary>
    public string? FindConflict(ShortcutGesture gesture, string exceptCommandId) =>
        _byGesture.TryGetValue(gesture, out var owner) && owner != exceptCommandId ? owner : null;

    private void Bind(string commandId, ShortcutGesture gesture)
    {
        if (_byGesture.TryGetValue(gesture, out var previousOwner))
        {
            _byCommand[previousOwner].Remove(gesture);
        }

        _byGesture[gesture] = commandId;
        if (!_byCommand.TryGetValue(commandId, out var list))
        {
            list = new List<ShortcutGesture>();
            _byCommand[commandId] = list;
        }

        list.Add(gesture);
    }
}
