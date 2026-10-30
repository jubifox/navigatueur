using System.Diagnostics.CodeAnalysis;

namespace Navigatueur.Core.Shortcuts;

/// <summary>
/// A key plus modifiers, independent of any UI framework so shortcut
/// definitions, user overrides and conflict checks can live (and be tested)
/// in Core. The UI layer translates its own key events into one of these.
///
/// <see cref="Key"/> is always one of the canonical names in
/// <see cref="CanonicalKeys"/> ("T", "5", "F11", "Tab", "PageDown", "Comma"…),
/// so two gestures that mean the same thing are always equal.
/// </summary>
public readonly record struct ShortcutGesture(bool Ctrl, bool Shift, bool Alt, string Key)
{
    private static readonly Dictionary<string, string> KeyAliases = BuildKeyAliases();

    /// <summary>Every key name a gesture can use, in canonical spelling.</summary>
    public static IReadOnlyCollection<string> CanonicalKeys => KeyAliases.Values.Distinct().ToList();

    /// <summary>Canonical, culture-neutral text form ("Ctrl+Shift+T") — what settings.json stores.</summary>
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        parts.Add(Key);
        return string.Join("+", parts);
    }

    /// <summary>French label for the UI, using the key names printed on French keyboards ("Ctrl + Maj + T").</summary>
    public string ToDisplayString()
    {
        var parts = new List<string>(4);
        if (Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (Shift)
        {
            parts.Add("Maj");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        parts.Add(Key switch
        {
            "Left" => "←",
            "Right" => "→",
            "Up" => "↑",
            "Down" => "↓",
            "PageUp" => "Page préc.",
            "PageDown" => "Page suiv.",
            "Escape" => "Échap",
            "Enter" => "Entrée",
            "Space" => "Espace",
            "Delete" => "Suppr",
            "Backspace" => "Retour arrière",
            "Home" => "Début",
            "End" => "Fin",
            "Comma" => ",",
            "Period" => ".",
            "Plus" => "+",
            "Minus" => "-",
            _ => Key,
        });
        return string.Join(" + ", parts);
    }

    /// <summary>
    /// A gesture worth binding globally: function keys may stand alone, but any
    /// other key needs Ctrl or Alt — binding a bare letter (or Shift+letter)
    /// would hijack ordinary typing in every page.
    /// </summary>
    public bool IsValidShortcut =>
        !string.IsNullOrEmpty(Key) && (Ctrl || Alt || IsFunctionKey(Key));

    public static ShortcutGesture Parse(string text) =>
        TryParse(text, out var gesture) ? gesture : throw new FormatException($"Raccourci invalide : « {text} »");

    /// <summary>Accepts "Ctrl+Shift+T", "ctrl + maj + t", "Control+PgDn", "F11"… Exactly one non-modifier key is required.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out ShortcutGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // "Ctrl++" means Ctrl + the Plus key: an empty token right after a
        // separator stands for a literal "+".
        var tokens = SplitTokens(text);
        bool ctrl = false, shift = false, alt = false;
        string? key = null;

        foreach (var raw in tokens)
        {
            var token = raw.Trim();
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                case "ctl":
                    ctrl = true;
                    continue;
                case "shift":
                case "maj":
                    shift = true;
                    continue;
                case "alt":
                case "option":
                    alt = true;
                    continue;
            }

            if (key is not null || !TryNormalizeKey(token, out var canonical))
            {
                return false;
            }

            key = canonical;
        }

        if (key is null)
        {
            return false;
        }

        gesture = new ShortcutGesture(ctrl, shift, alt, key);
        return true;
    }

    public static bool TryNormalizeKey(string name, [NotNullWhen(true)] out string? canonical) =>
        KeyAliases.TryGetValue(name.Trim(), out canonical);

    public static bool IsFunctionKey(string key) =>
        key.Length >= 2 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out var number) && number is >= 1 and <= 24;

    private static List<string> SplitTokens(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '+' && current.ToString().Trim().Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
            else if (c == '+')
            {
                // A '+' with nothing before it in this token is the Plus key itself.
                current.Append(c);
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.ToString().Trim().Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static Dictionary<string, string> BuildKeyAliases()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var c = 'A'; c <= 'Z'; c++)
        {
            map[c.ToString()] = c.ToString();
        }

        for (var d = 0; d <= 9; d++)
        {
            map[d.ToString()] = d.ToString();
        }

        for (var f = 1; f <= 24; f++)
        {
            map["F" + f] = "F" + f;
        }

        void Add(string canonical, params string[] aliases)
        {
            map[canonical] = canonical;
            foreach (var alias in aliases)
            {
                map[alias] = canonical;
            }
        }

        Add("Tab");
        Add("Enter", "Return", "Entrée", "Entree");
        Add("Escape", "Esc", "Échap", "Echap");
        Add("Space", "Espace", "Spacebar");
        Add("Backspace", "Back");
        Add("Delete", "Del", "Suppr");
        Add("Insert", "Ins");
        Add("Home");
        Add("End");
        Add("PageUp", "PgUp", "Prior");
        Add("PageDown", "PgDn", "Next");
        Add("Left", "←");
        Add("Right", "→");
        Add("Up", "↑");
        Add("Down", "↓");
        Add("Comma", ",");
        Add("Period", ".");
        Add("Plus", "+", "Add");
        Add("Minus", "-", "Subtract");
        return map;
    }
}
