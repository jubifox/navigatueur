using CommunityToolkit.Mvvm.ComponentModel;
using Navigatueur.Core.Settings;

namespace Navigatueur.App.Services;

/// <summary>ActionUrl is the new-tab search form's "action" — a GET form appends "?q=..." itself, so no query placeholder is needed here.</summary>
public sealed record SearchEngineOption(string Id, string DisplayName, string ActionUrl)
{
    /// <summary>What the address bar prepends to an escaped query — the same "q" parameter the new-tab form submits.</summary>
    public string QueryUrlPrefix => ActionUrl + "?q=";
}

/// <summary>Which search engine the address bar and the new-tab page's search box use — Bing by default, changeable in Settings or on first run.</summary>
public partial class SearchEngineService : ObservableObject
{
    public static readonly SearchEngineOption[] Engines =
    {
        new("Bing", "Bing", "https://www.bing.com/search"),
        new("Google", "Google", "https://www.google.com/search"),
        new("DuckDuckGo", "DuckDuckGo", "https://duckduckgo.com/"),
        new("Qwant", "Qwant", "https://www.qwant.com/"),
        new("Ecosia", "Ecosia", "https://www.ecosia.org/search"),
    };

    private readonly AppSettings _settings;

    [ObservableProperty]
    private string engineId;

    public SearchEngineService(AppSettings settings)
    {
        _settings = settings;
        engineId = settings.SearchEngine;
    }

    public SearchEngineOption CurrentEngine =>
        Engines.FirstOrDefault(e => e.Id == EngineId) ?? Engines[0];

    public void SetEngine(string id)
    {
        EngineId = id;
        _settings.SearchEngine = id;
        AppServices.Settings.Save(_settings);
    }
}
