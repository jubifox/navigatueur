using System.Windows.Input;
using Navigatueur.Core.Settings;
using Navigatueur.Core.Shortcuts;

namespace Navigatueur.App.Services;

/// <summary>
/// Turns WPF key events into shortcut commands, using the catalog defaults
/// plus whatever the user remapped in Settings.
///
/// Key presses reach this from two kinds of places: WPF elements (panels,
/// dialogs) and the page itself — the WebView2 control re-raises the page's
/// accelerator keys (anything with Ctrl/Alt, function keys) as ordinary WPF
/// PreviewKeyDown events, so shortcuts keep working with focus in the page
/// and every interface hidden.
/// </summary>
public sealed class ShortcutService
{
    private readonly AppSettings _settings;

    public ShortcutService(AppSettings settings)
    {
        _settings = settings;
        Map = ShortcutMap.Build(_settings.ShortcutOverrides);
    }

    public event EventHandler? Changed;

    public ShortcutMap Map { get; private set; }

    private DateTime _captureUntil = DateTime.MinValue;

    /// <summary>
    /// True while the Settings page is recording a new shortcut: key presses
    /// must reach the page instead of triggering whatever they're bound to
    /// (otherwise trying to assign Ctrl+T would just open a tab). Expires on
    /// its own, so a page that navigates away mid-capture can't leave every
    /// shortcut disabled.
    /// </summary>
    public bool IsCapturing => DateTime.UtcNow < _captureUntil;

    public void BeginCapture() => _captureUntil = DateTime.UtcNow.AddSeconds(20);

    public void EndCapture() => _captureUntil = DateTime.MinValue;

    public bool TryResolve(KeyEventArgs e, out string commandId)
    {
        commandId = string.Empty;
        return !IsCapturing && TryGetGesture(e, out var gesture) && Map.TryGetCommand(gesture, out commandId);
    }

    /// <summary>First bound gesture in French display form, for menus ("Ctrl + T"); empty when unbound.</summary>
    public string GetDisplayText(string commandId)
    {
        var gestures = Map.GetGestures(commandId);
        return gestures.Count == 0 ? string.Empty : gestures[0].ToDisplayString().Replace(" + ", "+");
    }

    /// <summary>Validates and stores a new gesture for <paramref name="commandId"/>; returns an error message, or null on success.</summary>
    public string? TrySetOverride(string commandId, string gestureText)
    {
        if (ShortcutCatalog.Find(commandId) is not { IsCustomizable: true })
        {
            return "Ce raccourci ne peut pas être modifié.";
        }

        if (!ShortcutGesture.TryParse(gestureText, out var gesture) || !gesture.IsValidShortcut)
        {
            return "Combinaison invalide : utilisez Ctrl ou Alt avec une touche, ou une touche de fonction.";
        }

        _settings.ShortcutOverrides[commandId] = gesture.ToString();
        Rebuild();
        return null;
    }

    public void DisableShortcut(string commandId)
    {
        _settings.ShortcutOverrides[commandId] = string.Empty;
        Rebuild();
    }

    public void ResetOverride(string commandId)
    {
        _settings.ShortcutOverrides.Remove(commandId);
        Rebuild();
    }

    public void ResetAll()
    {
        _settings.ShortcutOverrides.Clear();
        Rebuild();
    }

    public static bool TryGetGesture(KeyEventArgs e, out ShortcutGesture gesture)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };

        gesture = default;
        var name = ToCanonicalName(key);
        if (name is null)
        {
            return false;
        }

        var modifiers = Keyboard.Modifiers;
        gesture = new ShortcutGesture(
            (modifiers & ModifierKeys.Control) != 0,
            (modifiers & ModifierKeys.Shift) != 0,
            (modifiers & ModifierKeys.Alt) != 0,
            name);
        return true;
    }

    /// <summary>WPF key to Core's canonical key name; null for keys shortcuts never use (modifiers themselves, media keys…).</summary>
    public static string? ToCanonicalName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        >= Key.F1 and <= Key.F24 => "F" + (key - Key.F1 + 1),
        Key.Tab => "Tab",
        Key.Enter => "Enter",
        Key.Escape => "Escape",
        Key.Space => "Space",
        Key.Back => "Backspace",
        Key.Delete => "Delete",
        Key.Insert => "Insert",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.Up => "Up",
        Key.Down => "Down",
        Key.OemComma => "Comma",
        Key.OemPeriod => "Period",
        Key.OemPlus or Key.Add => "Plus",
        Key.OemMinus or Key.Subtract => "Minus",
        _ => null,
    };

    private void Rebuild()
    {
        Map = ShortcutMap.Build(_settings.ShortcutOverrides);
        AppServices.Settings.Save(_settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
