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

        return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
    }
}
