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

        let lists = Self { ads, phishing };
        lists.spawn_refresh();
        lists
    }

    fn spawn_refresh(&self) {
        let ads = Arc::clone(&self.ads);
        let phishing = Arc::clone(&self.phishing);

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
    fn empty_set_matches_nothing() {
        assert!(!DomainSet::default().matches("anything.com"));
    }
}
