using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace Navigatueur.App.Services;

/// <summary>
/// Native request-level ad/tracker blocking (no third-party browser
/// extensions), plus cosmetic element hiding (cookie banners, newsletter
/// nags, ad containers) for annoyance removal on top of that — domain and
/// cosmetic rules extracted from EasyList, EasyPrivacy and uBO's own
/// Annoyances list (the same lists uBlock Origin ships by default).
///
/// Ships a static snapshot (Resources/AdBlock/blocklist-domains.txt, ~94k
/// domains) so network blocking works from the very first launch, then
/// refreshes it from the live lists at most once a day in the background and
/// caches the result under %LocalAppData%\Navigatueur\AdBlock. Cosmetic
/// rules are cache-only (no bundled snapshot — they're a smaller, lower-
/// stakes enhancement, fine to pick up after the first successful refresh).
/// </summary>
public sealed class AdBlockService
{
    private static readonly string[] ListUrls =
    {
        "https://easylist.to/easylist/easylist.txt",
        "https://easylist.to/easylist/easyprivacy.txt",
        "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/annoyances-others.txt",
        // uBO's own curated supplementary list — includes the property-nulling
        // rules that stop YouTube's player from seeing ad data in the first
        // place (network-only blocking can't do this: YouTube serves ads from
        // the same googlevideo.com CDN as the actual video).
        "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/filters.txt",
    };

    /// <summary>
    /// Only a bare "||domain^" with no trailing "$options" counts as a global
    /// block. A rule like "||imgur.com^$domain=ghostbin.me|up-load.io" only
    /// means "block imgur.com when embedded on ghostbin.me/up-load.io" — the
    /// previous, looser regex imported these as blanket blocks, which is what
    /// broke sites that legitimately use i.imgur.com (e.g. Fribbels HSR
    /// Optimizer's image upload) for something EasyList never meant to block
    /// globally.
    /// </summary>
    private static readonly Regex DomainRulePattern = new(
        @"^\|\|([a-zA-Z0-9.\-]+)\^$", RegexOptions.Compiled);

    /// <summary>
    /// Domain-scoped element-hiding rules only ("domain##selector") — generic,
    /// no-domain rules ("##selector") are skipped to keep the blast radius of
    /// a bad/overly-broad selector contained to the sites that opted into it.
    /// Scriptlet injection ("##+js(...)") isn't supported, so it's excluded too.
    /// </summary>
    private static readonly Regex CosmeticRulePattern = new(
        @"^([a-zA-Z0-9.,~\-]+)##(?!\+js\()(.+)$", RegexOptions.Compiled);

    private static readonly string[] ExtendedCssMarkers =
    {
        ":has-text(", ":matches-css(", ":xpath(", ":min-text-length(",
        ":remove(", ":style(", ":upward(", ":watch-attr(", ":matches-path(",
    };

    /// <summary>
    /// Only uBO's "set" scriptlet ("domain##+js(set, chain, value)") is
    /// supported — it traps a property chain (e.g.
    /// "ytInitialPlayerResponse.adPlacements") so the page always reads back
    /// a fixed value no matter what the page's own script assigns, which is
    /// exactly how YouTube's ad data gets neutralized before its player ever
    /// sees it. Every other scriptlet name in uBO's library depends on its
    /// internal runtime helpers (safeSelf, anti-detection cloaking, etc.) that
    /// aren't safe to approximate from memory, so they're deliberately not
    /// implemented rather than shipped as a guess.
    /// </summary>
    private static readonly Regex SetScriptletPattern = new(
        @"^([a-zA-Z0-9.,~\-]+)##\+js\(set,\s*([^,]+),\s*(.+)\)$", RegexOptions.Compiled);

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(1);

    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Navigatueur", "AdBlock");

    private static readonly string CacheFilePath = Path.Combine(CacheDirectory, "domains.txt");

    private static readonly string CosmeticCacheFilePath = Path.Combine(CacheDirectory, "cosmetic.json");

    private static readonly string SetScriptletCacheFilePath = Path.Combine(CacheDirectory, "set-scriptlets.json");

    private static readonly string SnapshotFilePath = Path.Combine(
        AppContext.BaseDirectory, "Resources", "AdBlock", "blocklist-domains.txt");

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _timer;

    private volatile HashSet<string> _blockedDomains;
    private volatile string _cosmeticScript;

    public AdBlockService()
    {
        _blockedDomains = LoadFromDisk(CacheFilePath) ?? LoadFromDisk(SnapshotFilePath) ?? new HashSet<string>();
        _cosmeticScript = BuildInjectionScript(
            LoadJsonFromDisk<Dictionary<string, List<string>>>(CosmeticCacheFilePath) ?? new Dictionary<string, List<string>>(),
            LoadJsonFromDisk<Dictionary<string, List<string[]>>>(SetScriptletCacheFilePath) ?? new Dictionary<string, List<string[]>>());

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _timer.Tick += async (_, _) => await RefreshIfDueAsync();
        _timer.Start();

        _ = RefreshIfDueAsync();
    }

    public bool IsBlocked(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        var set = _blockedDomains;
        var remainder = host;
        while (true)
        {
            if (set.Contains(remainder))
            {
                return true;
            }

            var dot = remainder.IndexOf('.');
            if (dot < 0)
            {
                return false;
            }

            remainder = remainder[(dot + 1)..];
        }
    }

    /// <summary>
    /// A self-contained script (safe to inject on every page, every site):
    /// it looks up the current hostname against the cosmetic rule table
    /// client-side and, only if there's a match, hides those elements.
    /// </summary>
    public string GetCosmeticInjectionScript() => _cosmeticScript;

    private async Task RefreshIfDueAsync()
    {
        if (File.Exists(CacheFilePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFilePath) < RefreshInterval)
        {
            return;
        }

        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cosmetic = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var setScriptlets = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var url in ListUrls)
            {
                var text = await _httpClient.GetStringAsync(url);
                ExtractRules(text, domains, cosmetic, setScriptlets);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Offline, or the lists are unreachable right now — keep using whatever is already loaded.
            return;
        }

        if (domains.Count == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllLinesAsync(CacheFilePath, domains);
            await File.WriteAllTextAsync(CosmeticCacheFilePath, JsonSerializer.Serialize(cosmetic));
            await File.WriteAllTextAsync(SetScriptletCacheFilePath, JsonSerializer.Serialize(setScriptlets));
        }
        catch (IOException)
        {
            // Non-fatal: the in-memory data below is still updated for this session.
        }

        _blockedDomains = domains;
        _cosmeticScript = BuildInjectionScript(cosmetic, setScriptlets);
    }

    private static void ExtractRules(
        string listText,
        HashSet<string> domains,
        Dictionary<string, List<string>> cosmetic,
        Dictionary<string, List<string[]>> setScriptlets)
    {
        foreach (var line in listText.AsSpan().EnumerateLines())
        {
            var text = line.ToString();

            var domainMatch = DomainRulePattern.Match(text);
            if (domainMatch.Success)
            {
                domains.Add(domainMatch.Groups[1].Value);
                continue;
            }

            var setMatch = SetScriptletPattern.Match(text);
            if (setMatch.Success)
            {
                var chain = setMatch.Groups[2].Value.Trim();
                var value = setMatch.Groups[3].Value.Trim();
                foreach (var domain in ExpandDomains(setMatch.Groups[1].Value))
                {
                    if (!setScriptlets.TryGetValue(domain, out var rules))
                    {
                        rules = new List<string[]>();
                        setScriptlets[domain] = rules;
                    }

                    rules.Add(new[] { chain, value });
                }

                continue;
            }

            var cosmeticMatch = CosmeticRulePattern.Match(text);
            if (!cosmeticMatch.Success)
            {
                continue;
            }

            var selector = cosmeticMatch.Groups[2].Value;
            if (ExtendedCssMarkers.Any(selector.Contains))
            {
                continue; // uBO extended-CSS syntax (:has-text, :remove, ...) isn't valid CSS — would silently no-op anyway, but skip explicitly for clarity.
            }

            foreach (var domain in ExpandDomains(cosmeticMatch.Groups[1].Value))
            {
                if (!cosmetic.TryGetValue(domain, out var selectors))
                {
                    selectors = new List<string>();
                    cosmetic[domain] = selectors;
                }

                selectors.Add(selector);
            }
        }
    }

    private static IEnumerable<string> ExpandDomains(string domainList)
    {
        foreach (var domain in domainList.Split(','))
        {
            if (domain.Length > 0 && domain[0] != '~')
            {
                yield return domain; // Skip negated ("~domain") entries — safe default is to just not apply there, not to invert anything.
            }
        }
    }

    private static string BuildInjectionScript(
        Dictionary<string, List<string>> cosmetic,
        Dictionary<string, List<string[]>> setScriptlets)
    {
        if (cosmetic.Count == 0 && setScriptlets.Count == 0)
        {
            return string.Empty;
        }

        var hideRulesJson = JsonSerializer.Serialize(cosmetic);
        var setRulesJson = JsonSerializer.Serialize(setScriptlets);
        return $$"""
        (function() {
          var hideRules = {{hideRulesJson}};
          var setRules = {{setRulesJson}};
          var host = location.hostname;
          var selectors = [];
          var setters = [];
          while (host) {
            if (hideRules[host]) selectors = selectors.concat(hideRules[host]);
            if (setRules[host]) setters = setters.concat(setRules[host]);
            var dot = host.indexOf('.');
            if (dot < 0) break;
            host = host.slice(dot + 1);
          }

          if (selectors.length > 0) {
            var css = selectors.map(function(s) { return s + '{display:none!important}'; }).join('\n');
            function injectCss() {
              try {
                var style = document.createElement('style');
                style.textContent = css;
                (document.head || document.documentElement).appendChild(style);
              } catch (e) {}
            }
            injectCss();
            document.addEventListener('DOMContentLoaded', injectCss);
          }

          if (setters.length > 0) {
            var parseValue = function(raw) {
              if (raw === 'undefined') return undefined;
              if (raw === 'false') return false;
              if (raw === 'true') return true;
              if (raw === 'null') return null;
              if (raw === "''" || raw === '""' || raw === '') return '';
              if (raw === 'noopFunc') return function() {};
              if (raw === 'trueFunc') return function() { return true; };
              if (raw === 'falseFunc') return function() { return false; };
              if (/^-?\d+$/.test(raw)) return parseInt(raw, 10);
              if (raw.length > 1 && (raw[0] === "'" || raw[0] === '"') && raw[raw.length - 1] === raw[0]) {
                return raw.slice(1, -1);
              }
              return undefined;
            };
            // Faithful (simplified) port of uBO's set-constant scriptlet: traps a
            // property chain so it always reads back a fixed value, no matter what
            // the page's own script assigns — e.g. neutralizes
            // ytInitialPlayerResponse.adPlacements before YouTube's player reads it.
            var trapChain = function(owner, chain, value) {
              var pos = chain.indexOf('.');
              if (pos === -1) {
                try {
                  Object.defineProperty(owner, chain, {
                    configurable: true,
                    get: function() { return value; },
                    set: function() {},
                  });
                } catch (e) {}
                return;
              }
              var prop = chain.slice(0, pos);
              var rest = chain.slice(pos + 1);
              var v = owner[prop];
              if (v && typeof v === 'object') {
                trapChain(v, rest, value);
                return;
              }
              try {
                Object.defineProperty(owner, prop, {
                  configurable: true,
                  get: function() { return v; },
                  set: function(a) {
                    v = a;
                    if (a && typeof a === 'object') trapChain(a, rest, value);
                  },
                });
              } catch (e) {}
            };
            for (var i = 0; i < setters.length; i++) {
              try {
                trapChain(window, setters[i][0], parseValue(setters[i][1]));
              } catch (e) {}
            }
          }
        })();
        """;
    }

    private static HashSet<string>? LoadFromDisk(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return new HashSet<string>(File.ReadLines(path), StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static T? LoadJsonFromDisk<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }
}
