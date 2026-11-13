using System.Diagnostics;
using Microsoft.Win32;

namespace Navigatueur.App.Services;

/// <summary>
/// Whether Windows knows Navigatueur as a browser (the installer registers
/// it, see installer/Navigatueur.iss) and whether the user made it the
/// default one. Windows doesn't let an app make itself the default: the
/// button opens the page of Windows Settings where the user decides.
/// </summary>
public static class DefaultBrowser
{
    public const string ProgId = "NavigatueurHTML";
    public const string RegisteredName = "Navigatueur";

    /// <summary>Installed with its installer, so Windows lists it among browsers (a development build isn't).</summary>
    public static bool IsRegistered =>
        ReadString(Registry.LocalMachine, @"SOFTWARE\RegisteredApplications", RegisteredName) is not null ||
        ReadString(Registry.CurrentUser, @"SOFTWARE\RegisteredApplications", RegisteredName) is not null;

    /// <summary>Web links (https) open with Navigatueur.</summary>
    public static bool IsDefault => string.Equals(UserChoice("https"), ProgId, StringComparison.OrdinalIgnoreCase);

    /// <summary>The program Windows opens a link of <paramref name="scheme"/> with, as the user chose it.</summary>
    private static string? UserChoice(string scheme)
    {
        var key = $@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}";
        // Recent Windows 11 builds keep the choice under "UserChoiceLatest".
        return ReadString(Registry.CurrentUser, key + @"\UserChoiceLatest", "ProgId")
            ?? ReadString(Registry.CurrentUser, key + @"\UserChoice", "ProgId");
    }

    private static string? ReadString(RegistryKey root, string path, string name)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Windows Settings, on Navigatueur's own "default apps" page when Windows supports it, else on the list.</summary>
    public static void OpenWindowsSettings()
    {
        foreach (var target in new[] { $"ms-settings:defaultapps?registeredAppMachine={RegisteredName}", "ms-settings:defaultapps" })
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Try the next one.
            }
        }
    }
}
