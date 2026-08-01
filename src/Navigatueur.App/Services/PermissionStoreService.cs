using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Navigatueur.App.Services;

/// <summary>
/// Remembers per-origin site permission decisions (camera/mic/location/...)
/// across restarts, so the user isn't re-prompted on every visit once
/// they've made a choice with "Se souvenir" checked.
/// </summary>
public sealed class PermissionStoreService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Navigatueur", "permissions.json");

    private readonly Dictionary<string, Dictionary<string, bool>> _decisions;

    public PermissionStoreService()
    {
        _decisions = Load();
    }

    public bool TryGetDecision(string uri, CoreWebView2PermissionKind kind, out bool allowed)
    {
        allowed = false;
        var origin = GetOrigin(uri);
        if (origin is null || !_decisions.TryGetValue(origin, out var perKind))
        {
            return false;
        }

        return perKind.TryGetValue(kind.ToString(), out allowed);
    }

    public void SetDecision(string uri, CoreWebView2PermissionKind kind, bool allowed)
    {
        var origin = GetOrigin(uri);
        if (origin is null)
        {
            return;
        }

        if (!_decisions.TryGetValue(origin, out var perKind))
        {
            perKind = new Dictionary<string, bool>();
            _decisions[origin] = perKind;
        }

        perKind[kind.ToString()] = allowed;
        Save();
    }

    private static string? GetOrigin(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.Host : null;

    private static Dictionary<string, Dictionary<string, bool>> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new Dictionary<string, Dictionary<string, bool>>();
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, bool>>>(json)
                   ?? new Dictionary<string, Dictionary<string, bool>>();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new Dictionary<string, Dictionary<string, bool>>();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_decisions));
        }
        catch (IOException)
        {
            // Non-fatal: the in-memory decision above still applies for this session.
        }
    }
}
