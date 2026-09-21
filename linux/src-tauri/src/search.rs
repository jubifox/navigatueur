//! Port of `SearchEngineService` (C#). Same five engines, same ids, so the
//! `SearchEngine` value in settings.json means the same thing on both platforms.

pub struct SearchEngine {
    pub id: &'static str,
    pub display_name: &'static str,
    /// The search form's GET action; the query is appended as "?q=...".
    pub action_url: &'static str,
}

pub const ENGINES: &[SearchEngine] = &[
    SearchEngine { id: "Bing",       display_name: "Bing",       action_url: "https://www.bing.com/search" },
    SearchEngine { id: "Google",     display_name: "Google",     action_url: "https://www.google.com/search" },
    SearchEngine { id: "DuckDuckGo", display_name: "DuckDuckGo", action_url: "https://duckduckgo.com/" },
    SearchEngine { id: "Qwant",      display_name: "Qwant",      action_url: "https://www.qwant.com/" },
    SearchEngine { id: "Ecosia",     display_name: "Ecosia",     action_url: "https://www.ecosia.org/search" },
];

/// Falls back to the first engine (Bing) for an unknown id, matching the C#
/// `FirstOrDefault(...) ?? Engines[0]`.
pub fn resolve(id: &str) -> &'static SearchEngine {
    ENGINES.iter().find(|e| e.id == id).unwrap_or(&ENGINES[0])
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn resolves_known_engine() {
        assert_eq!(resolve("Qwant").action_url, "https://www.qwant.com/");
    }

    #[test]
    fn unknown_engine_falls_back_to_bing() {
        assert_eq!(resolve("Nonexistent").id, "Bing");
    }
}
