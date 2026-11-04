using Navigatueur.Core.Settings;
using Navigatueur.Core.Sites;

namespace Navigatueur.App.Services;

/// <summary>
/// The forced dark mode's switches: on or off for every site (Settings →
/// Apparence), and per site from the "⋯" menu. Each tab applies the answer
/// to its own engine (see BrowserTabViewModel.ApplyForceDarkAsync).
///
/// A choice made in a private window is only remembered for this session:
/// writing it to settings.json would leave a list of the sites visited there.
/// </summary>
public sealed class ForceDarkService
{
    private readonly AppSettings _settings;
    private readonly Dictionary<string, bool> _privateSites = new();

    public ForceDarkService(AppSettings settings)
    {
        _settings = settings;
        _settings.ForceDarkSites ??= new();
    }

    /// <summary>Raised after any switch changed: every live tab re-checks its page.</summary>
    public event EventHandler? Changed;

    public bool IsEnabledByDefault => _settings.IsForceDarkEnabled;

    /// <summary>How many sites have a choice of their own (shown in Settings, with a reset button).</summary>
    public int SiteChoiceCount => _settings.ForceDarkSites.Count;

    public bool IsEnabledFor(string? url, bool isPrivate)
    {
        var saved = ForceDarkRules.IsEnabledFor(url, _settings.IsForceDarkEnabled, _settings.ForceDarkSites);
        return isPrivate ? ForceDarkRules.IsEnabledFor(url, saved, _privateSites) : saved;
    }

    /// <summary>The page can be darkened at all (not one of Navigatueur's own pages, not about:blank…).</summary>
    public static bool Applies(string? url) => ForceDarkRules.SiteKey(url) is not null;

    public void SetEnabledByDefault(bool enabled)
    {
        if (_settings.IsForceDarkEnabled == enabled)
        {
            return;
        }

        _settings.IsForceDarkEnabled = enabled;
        Save();
    }

    public void SetForSite(string? url, bool enabled, bool isPrivate)
    {
        if (isPrivate)
        {
            var saved = ForceDarkRules.IsEnabledFor(url, _settings.IsForceDarkEnabled, _settings.ForceDarkSites);
            ForceDarkRules.SetForSite(url, enabled, saved, _privateSites);
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        ForceDarkRules.SetForSite(url, enabled, _settings.IsForceDarkEnabled, _settings.ForceDarkSites);
        Save();
    }

    public void ClearSiteChoices()
    {
        _settings.ForceDarkSites.Clear();
        _privateSites.Clear();
        Save();
    }

    private void Save()
    {
        AppServices.Settings.Save(_settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
