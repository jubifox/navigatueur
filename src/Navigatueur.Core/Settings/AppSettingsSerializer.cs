using System.Text.Json;

namespace Navigatueur.Core.Settings;

public static class AppSettingsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(settings, Options);

    public static AppSettings Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AppSettings();
        }

        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        Repair(settings);
        return settings;
    }

    /// <summary>
    /// An explicit <c>null</c> in the file deserializes as null even for
    /// properties that have an initializer — every consumer assumes these
    /// collections/objects exist, so they're restored here rather than
    /// null-checked everywhere.
    /// </summary>
    private static void Repair(AppSettings settings)
    {
        settings.Interface ??= new InterfaceSettings();
        settings.Interface.Normalize();
        settings.Groups ??= new();
        settings.Tabs ??= new();
        settings.SavedGroups ??= new();
        settings.ShortcutOverrides ??= new();
        settings.HomePageUrl ??= InternalPages.NewTabUrl;
        settings.TabSuspendMinutes = Math.Clamp(settings.TabSuspendMinutes, 0, 24 * 60);
        settings.MaxLiveTabs = Math.Clamp(settings.MaxLiveTabs, 1, 20);

        if (settings.StartupBehavior is not (StartupBehaviors.RestoreSession or StartupBehaviors.HomePage))
        {
            settings.StartupBehavior = StartupBehaviors.RestoreSession;
        }
    }
}
