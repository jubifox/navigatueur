//! Port of `AdBlockService` + `PhishingProtectionService` (C#).
//!
//! Same two-stage design as the Windows build: a snapshot is bundled with the
//! app so protection works on first launch, then a background refresh pulls the
//! live lists into a cache under $XDG_DATA_HOME. Ad lists churn slowly (refresh
//! daily); phishing domains are mostly taken down within days (refresh 6-hourly).

use std::collections::HashSet;
use std::path::{Path, PathBuf};
use std::sync::{Arc, RwLock};
use std::time::{Duration, SystemTime};

const AD_LIST_URLS: &[&str] = &[
    "https://easylist.to/easylist/easylist.txt",
    "https://easylist.to/easylist/easyprivacy.txt",
    "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/annoyances-others.txt",
    "https://raw.githubusercontent.com/uBlockOrigin/uAssets/master/filters/filters.txt",
];

const PHISHING_LIST_URL: &str =
    "https://raw.githubusercontent.com/mitchellkrogza/Phishing.Database/master/phishing-domains-ACTIVE.txt";

const AD_REFRESH: Duration = Duration::from_secs(24 * 60 * 60);
const PHISHING_REFRESH: Duration = Duration::from_secs(6 * 60 * 60);

/// Domain-scoped element-hiding rules: host -> CSS selectors.
pub type CosmeticRules = std::collections::HashMap<String, Vec<String>>;

/// Domain-scoped uBO `set` scriptlets: host -> (property chain, value) pairs.
pub type SetScriptlets = std::collections::HashMap<String, Vec<(String, String)>>;

#[derive(Default)]
pub struct DomainSet {
    domains: HashSet<String>,
}

impl DomainSet {
    /// Walks the host up its parent domains, so a rule for "doubleclick.net"
    /// also blocks "ad.g.doubleclick.net". Identical to the C# suffix walk.
    pub fn matches(&self, host: &str) -> bool {
        if host.is_empty() || self.domains.is_empty() {
            return false;
        }

        let mut remainder = host.trim_start_matches("www.");
        loop {
            if self.domains.contains(remainder) {
                return true;
            }
            match remainder.find('.') {
                Some(dot) => remainder = &remainder[dot + 1..],
                None => return false,
            }
        }
    }

    pub fn len(&self) -> usize {
        self.domains.len()
    }

    fn from_lines(text: &str) -> Self {
        let mut domains = HashSet::new();
        for line in text.lines() {
            let line = line.trim();
            // Skip blanks, comments, and the "0.0.0.0 host" hosts-file form's IP column.
            if line.is_empty() || line.starts_with('#') || line.starts_with('!') {
                continue;
            }
            let candidate = line.split_whitespace().last().unwrap_or(line);
            if candidate.contains('.') && !candidate.contains('/') {
                domains.insert(candidate.to_ascii_lowercase());
            }
        }
        Self { domains }
    }

    /// Extracts domain-scoped element-hiding rules (`domain##selector`).
    ///
    /// Generic, domain-less rules (`##selector`) are deliberately skipped, as
    /// on Windows: one bad or overly broad selector would then apply to every
    /// site instead of only the ones that opted into it. Extended-CSS
    /// procedural selectors (`:has-text()`, `:upward()`, …) are skipped too —
    /// they need uBO's own matching engine, which this does not reimplement.
    pub fn cosmetic_rules(text: &str) -> CosmeticRules {
        const EXTENDED_CSS_MARKERS: &[&str] = &[
            ":has-text(", ":matches-css(", ":xpath(", ":min-text-length(",
            ":remove(", ":style(", ":upward(", ":watch-attr(", ":matches-path(",
        ];

        let mut rules: CosmeticRules = std::collections::HashMap::new();
        for line in text.lines() {
            let line = line.trim();
            let Some(split) = line.find("##") else { continue };
            if split == 0 {
                continue; // generic rule, intentionally ignored
            }
            let (domains, selector) = (&line[..split], &line[split + 2..]);
            if selector.is_empty() || selector.starts_with("+js(") {
                continue;
            }
            if EXTENDED_CSS_MARKERS.iter().any(|m| selector.contains(m)) {
                continue;
            }
            if !domains
                .chars()
                .all(|c| c.is_ascii_alphanumeric() || ".,~-_".contains(c))
            {
                continue;
            }

            for domain in domains.split(',') {
                // A leading '~' means "everywhere except here" — an exception
                // this simplified engine cannot express, so it is dropped.
                if domain.starts_with('~') || domain.is_empty() || !domain.contains('.') {
                    continue;
                }
                rules
                    .entry(domain.to_ascii_lowercase())
                    .or_default()
                    .push(selector.to_string());
            }
        }
        rules
    }

    /// Extracts uBO `set` scriptlets (`domain##+js(set, chain, value)`).
    ///
    /// Only `set` is supported, exactly as on Windows: it traps a property
    /// chain (e.g. `ytInitialPlayerResponse.adPlacements`) so the page always
    /// reads back a fixed value. That is how YouTube's ad payload is
    /// neutralised before its player sees it — network blocking cannot do it,
    /// since the ads come from the same CDN as the video. Every other
    /// scriptlet depends on uBO's internal runtime helpers and is left out
    /// rather than approximated.
    pub fn set_scriptlets(text: &str) -> SetScriptlets {
        let mut out: SetScriptlets = std::collections::HashMap::new();
        for line in text.lines() {
            let line = line.trim();
            let Some(split) = line.find("##+js(set,") else { continue };
            if split == 0 {
                continue;
            }
            let domains = &line[..split];
            let args = line[split + "##+js(set,".len()..].trim_end_matches(')');
            let mut parts = args.splitn(2, ',');
            let (Some(chain), Some(value)) = (parts.next(), parts.next()) else { continue };
            let (chain, value) = (chain.trim(), value.trim());
            if chain.is_empty() || value.is_empty() {
                continue;
            }

            for domain in domains.split(',') {
                if domain.starts_with('~') || domain.is_empty() || !domain.contains('.') {
                    continue;
                }
                out.entry(domain.to_ascii_lowercase())
                    .or_default()
                    .push((chain.to_string(), value.to_string()));
            }
        }
        out
    }

    /// Extracts blanket network rules from EasyList syntax.
    ///
    /// Only a bare `||domain^` with no trailing `$options` counts. A rule like
    /// `||imgur.com^$domain=ghostbin.me` means "block imgur.com *when embedded
    /// on* ghostbin.me" — importing those as global blocks is what broke sites
    /// legitimately using i.imgur.com in an earlier version of the C# service.
    fn from_easylist(text: &str) -> Self {
        let mut domains = HashSet::new();
        for line in text.lines() {
            let line = line.trim();
            let Some(rest) = line.strip_prefix("||") else {
                continue;
            };
            let Some(host) = rest.strip_suffix('^') else {
                continue;
            };
            if host.is_empty()
                || host.contains('/')
                || host.contains('*')
                || host.contains('$')
                || !host.contains('.')
            {
                continue;
            }
            domains.insert(host.to_ascii_lowercase());
        }
        Self { domains }
    }
}

pub struct Blocklists {
    pub ads: Arc<RwLock<DomainSet>>,
    pub phishing: Arc<RwLock<DomainSet>>,
    pub cosmetic: Arc<RwLock<CosmeticRules>>,
    pub scriptlets: Arc<RwLock<SetScriptlets>>,
}

impl Blocklists {
    /// Loads cache-then-snapshot for both lists, then kicks off a background
    /// refresh. Never blocks startup on the network.
    pub fn load(resource_dir: &Path) -> Self {
        let cache = settings_cache_dir();

        let ads = Arc::new(RwLock::new(load_first_available(
            &[
                cache.join("adblock-domains.txt"),
                resource_dir.join("blocklist-domains.txt"),
            ],
            DomainSet::from_lines,
        )));

        let phishing = Arc::new(RwLock::new(load_first_available(
            &[
                cache.join("phishing-domains.txt"),
                resource_dir.join("phishing-domains.txt"),
            ],
            DomainSet::from_lines,
        )));

        // Cosmetic rules are cache-only: no bundled snapshot, since they are a
        // smaller, lower-stakes enhancement that is fine to pick up after the
        // first successful refresh. Same call as the Windows build makes.
        let cosmetic = Arc::new(RwLock::new(
            load_json(&cache.join("cosmetic.json")).unwrap_or_default(),
        ));
        let scriptlets = Arc::new(RwLock::new(
            load_json(&cache.join("scriptlets.json")).unwrap_or_default(),
        ));

        let lists = Self { ads, phishing, cosmetic, scriptlets };
        lists.spawn_refresh();
        lists
    }

    /// Builds the script to run on a page served by `host`: hides that host's
    /// ad containers and installs its property traps. Returns None when no rule
    /// matches, so the common case injects nothing at all.
    ///
    /// Rules are looked up for the host and each of its parent domains, so a
    /// rule written for "youtube.com" also applies on "m.youtube.com".
    pub fn page_script(&self, host: &str) -> Option<String> {
        let host = host.trim_start_matches("www.").to_ascii_lowercase();

        let mut selectors: Vec<String> = Vec::new();
        let mut traps: Vec<(String, String)> = Vec::new();

        if let (Ok(cosmetic), Ok(scriptlets)) = (self.cosmetic.read(), self.scriptlets.read()) {
            let mut remainder = host.as_str();
            loop {
                if let Some(found) = cosmetic.get(remainder) {
                    selectors.extend(found.iter().cloned());
                }
                if let Some(found) = scriptlets.get(remainder) {
                    traps.extend(found.iter().cloned());
                }
                match remainder.find('.') {
                    Some(dot) => remainder = &remainder[dot + 1..],
                    None => break,
                }
            }
        }

        if selectors.is_empty() && traps.is_empty() {
            return None;
        }

        let selectors_json = serde_json::to_string(&selectors).ok()?;
        let traps_json = serde_json::to_string(&traps).ok()?;

        Some(format!(
            r#"(function(){{
  var sels = {selectors_json}, traps = {traps_json};
  if (sels.length) {{
    var style = document.createElement('style');
    style.textContent = sels.join(',') + '{{display:none !important}}';
    (document.head || document.documentElement).appendChild(style);
  }}
  traps.forEach(function(t) {{
    // Walk the chain, defining a getter on the leaf that always reports the
    // fixed value however the page later assigns it.
    var parts = t[0].split('.'), leaf = parts.pop(), obj = window;
    for (var i = 0; i < parts.length; i++) {{
      if (typeof obj[parts[i]] !== 'object' || obj[parts[i]] === null) obj[parts[i]] = {{}};
      obj = obj[parts[i]];
    }}
    var v = t[1] === 'undefined' ? undefined
          : t[1] === 'true' ? true : t[1] === 'false' ? false
          : t[1] === 'null' ? null
          : isNaN(Number(t[1])) ? t[1] : Number(t[1]);
    try {{
      Object.defineProperty(obj, leaf, {{ get: function() {{ return v; }},
                                          set: function() {{}}, configurable: false }});
    }} catch (e) {{ /* already non-configurable: leave the page alone */ }}
  }});
}})();"#
        ))
    }

    fn spawn_refresh(&self) {
        let ads = Arc::clone(&self.ads);
        let phishing = Arc::clone(&self.phishing);
        let cosmetic = Arc::clone(&self.cosmetic);
        let scriptlets = Arc::clone(&self.scriptlets);

        std::thread::spawn(move || {
            let cache = settings_cache_dir();
            let _ = std::fs::create_dir_all(&cache);

            loop {
                let ad_cache = cache.join("adblock-domains.txt");
                if is_stale(&ad_cache, AD_REFRESH) {
                    let mut merged = String::new();
                    let mut ok = true;
                    for url in AD_LIST_URLS {
                        match fetch(url) {
                            Ok(body) => merged.push_str(&body),
                            Err(_) => ok = false,
                        }
                    }
                    // Only overwrite the cache if every list came back, so a
                    // partial fetch can't silently shrink protection.
                    if ok && !merged.is_empty() {
                        let set = DomainSet::from_easylist(&merged);
                        if set.len() > 1000 {
                            let joined = set.domains.iter().cloned().collect::<Vec<_>>().join("\n");
                            let _ = std::fs::write(&ad_cache, joined);
                            if let Ok(mut guard) = ads.write() {
                                *guard = set;
                            }

                            let rules = DomainSet::cosmetic_rules(&merged);
                            if let Ok(json) = serde_json::to_string(&rules) {
                                let _ = std::fs::write(cache.join("cosmetic.json"), json);
                            }
                            if let Ok(mut guard) = cosmetic.write() {
                                *guard = rules;
                            }

                            let traps = DomainSet::set_scriptlets(&merged);
                            if let Ok(json) = serde_json::to_string(&traps) {
                                let _ = std::fs::write(cache.join("scriptlets.json"), json);
                            }
                            if let Ok(mut guard) = scriptlets.write() {
                                *guard = traps;
                            }
                        }
                    }
                }

                let ph_cache = cache.join("phishing-domains.txt");
                if is_stale(&ph_cache, PHISHING_REFRESH) {
                    if let Ok(body) = fetch(PHISHING_LIST_URL) {
                        let set = DomainSet::from_lines(&body);
                        if set.len() > 1000 {
                            let _ = std::fs::write(&ph_cache, &body);
                            if let Ok(mut guard) = phishing.write() {
                                *guard = set;
                            }
                        }
                    }
                }

                std::thread::sleep(Duration::from_secs(30 * 60));
            }
        });
    }
}

fn settings_cache_dir() -> PathBuf {
    crate::settings::data_dir().join("lists")
}

fn load_first_available(paths: &[PathBuf], parse: fn(&str) -> DomainSet) -> DomainSet {
    for path in paths {
        if let Ok(text) = std::fs::read_to_string(path) {
            let set = parse(&text);
            if set.len() > 0 {
                return set;
            }
        }
    }
    DomainSet::default()
}

fn load_json<T: serde::de::DeserializeOwned>(path: &Path) -> Option<T> {
    serde_json::from_str(&std::fs::read_to_string(path).ok()?).ok()
}

fn is_stale(path: &Path, max_age: Duration) -> bool {
    match std::fs::metadata(path).and_then(|m| m.modified()) {
        Ok(modified) => SystemTime::now()
            .duration_since(modified)
            .map(|age| age > max_age)
            .unwrap_or(true),
        Err(_) => true,
    }
}

fn fetch(url: &str) -> Result<String, ureq::Error> {
    ureq::builder()
        .timeout(Duration::from_secs(30))
        .build()
        .get(url)
        .call()?
        .into_string()
        .map_err(Into::into)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn suffix_walk_blocks_subdomains() {
        let set = DomainSet::from_lines("doubleclick.net\nexample.com");
        assert!(set.matches("ad.g.doubleclick.net"));
        assert!(set.matches("doubleclick.net"));
        assert!(!set.matches("notdoubleclick.net"));
        assert!(!set.matches("google.com"));
    }

    #[test]
    fn easylist_ignores_option_scoped_rules() {
        let set = DomainSet::from_easylist(
            "||ads.example.com^\n||imgur.com^$domain=ghostbin.me\n||tracker.net^\n! comment",
        );
        assert!(set.matches("ads.example.com"));
        assert!(set.matches("tracker.net"));
        assert!(!set.matches("imgur.com"));
    }

    #[test]
    fn hosts_file_format_takes_the_host_column() {
        let set = DomainSet::from_lines("0.0.0.0 tracker.example\n127.0.0.1 ads.example");
        assert!(set.matches("tracker.example"));
        assert!(set.matches("ads.example"));
    }

    #[test]
    fn cosmetic_rules_are_domain_scoped() {
        let r = DomainSet::cosmetic_rules(
            "example.com##.ad-banner\n##.generic-everywhere\nfoo.net,bar.net##.promo",
        );
        assert_eq!(r.get("example.com").unwrap(), &vec![".ad-banner".to_string()]);
        // The generic (domain-less) rule must not have been imported at all.
        assert!(!r.values().any(|v| v.iter().any(|s| s == ".generic-everywhere")));
        assert_eq!(r.get("foo.net").unwrap(), &vec![".promo".to_string()]);
        assert_eq!(r.get("bar.net").unwrap(), &vec![".promo".to_string()]);
    }

    #[test]
    fn extended_css_selectors_are_skipped() {
        let r = DomainSet::cosmetic_rules("example.com##.x:has-text(Ad)\nexample.com##.plain");
        assert_eq!(r.get("example.com").unwrap(), &vec![".plain".to_string()]);
    }

    #[test]
    fn only_the_set_scriptlet_is_imported() {
        let r = DomainSet::set_scriptlets(
            "youtube.com##+js(set, ytInitialPlayerResponse.adPlacements, undefined)\n\
             example.com##+js(aopr, someOtherThing)",
        );
        assert_eq!(
            r.get("youtube.com").unwrap(),
            &vec![("ytInitialPlayerResponse.adPlacements".to_string(), "undefined".to_string())]
        );
        assert!(!r.contains_key("example.com"));
    }

    #[test]
    fn page_script_walks_up_to_parent_domains() {
        let lists = Blocklists {
            ads: Arc::new(RwLock::new(DomainSet::default())),
            phishing: Arc::new(RwLock::new(DomainSet::default())),
            cosmetic: Arc::new(RwLock::new(DomainSet::cosmetic_rules("youtube.com##.ad"))),
            scriptlets: Arc::new(RwLock::new(SetScriptlets::new())),
        };
        assert!(lists.page_script("m.youtube.com").is_some());
        assert!(lists.page_script("example.org").is_none());
    }

    #[test]
    fn empty_set_matches_nothing() {
        assert!(!DomainSet::default().matches("anything.com"));
    }
}
