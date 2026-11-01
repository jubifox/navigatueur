using Navigatueur.Core.Settings;

namespace Navigatueur.App.Services;

/// <summary>
/// Single place the immersive shell's settings are read and changed, so every
/// open window (and an open Settings page) reacts to a change made anywhere —
/// the toolbar's dock button, the Settings page, the welcome screen.
/// </summary>
public sealed class InterfaceSettingsService
{
    private readonly AppSettings _settings;

    public InterfaceSettingsService(AppSettings settings)
    {
        _settings = settings;
        _settings.Interface ??= new InterfaceSettings();
        _settings.Interface.Normalize();
    }

    /// <summary>Raised after any change has been applied and saved.</summary>
    public event EventHandler? Changed;

    public InterfaceSettings Current => _settings.Interface;

    public PanelBehavior ToolbarBehavior => Current.ResolveToolbarBehavior();

    public PanelBehavior SidebarBehavior => Current.ResolveSidebarBehavior();

    public PanelSide SidebarSide => Current.SidebarSideValue;

    public void Update(Action<InterfaceSettings> change)
    {
        change(Current);
        Current.Normalize();
        AppServices.Settings.Save(_settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies a change live without writing it to disk — for continuous edits like dragging the column's edge; finish with <see cref="Update"/>.</summary>
    public void Preview(Action<InterfaceSettings> change)
    {
        change(Current);
        Current.Normalize();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Flips the tab column between docked and auto-hide, keeping the toolbar's behavior.</summary>
    public void ToggleSidebarDocked() => Update(s => s.ApplyBehaviors(
        s.ResolveToolbarBehavior(),
        s.ResolveSidebarBehavior() == PanelBehavior.Docked ? PanelBehavior.AutoHide : PanelBehavior.Docked));
}
