using System.IO;
using Navigatueur.Core.Settings;

namespace Navigatueur.App.Services;

public sealed class SettingsService
{
    /// <summary>
    /// The homepage default used before the built-in "new tab" page existed.
    /// A settings.json saved by an older build has this baked in explicitly,
    /// which would otherwise silently shadow the new default forever — so on
    /// load we treat it as "never customized" and migrate it forward.
    /// </summary>
    private const string LegacyDefaultHomePageUrl = "https://www.bing.com";

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Navigatueur");

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    /// <summary>True once <see cref="Load"/> has run and found no existing settings.json — i.e. this is a fresh install, worth showing the first-run welcome dialog for.</summary>
    public bool IsFirstRun { get; private set; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                IsFirstRun = true;
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            var settings = AppSettingsSerializer.Deserialize(json);

            if (settings.HomePageUrl == LegacyDefaultHomePageUrl)
            {
                settings.HomePageUrl = new AppSettings().HomePageUrl;
            }

            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsFilePath, AppSettingsSerializer.Serialize(settings));
    }
}
