//! Port of `Navigatueur.Core.Settings` (C#). Field names keep the C# PascalCase
//! spelling via serde renames, so a settings.json written by the Windows build
//! is readable here and vice-versa.
//!
//! Paths follow the XDG spec instead of %LocalAppData%:
//!   settings    -> $XDG_CONFIG_HOME/navigatueur       (~/.config/navigatueur)
//!   caches/data -> $XDG_DATA_HOME/navigatueur         (~/.local/share/navigatueur)
//! Both live under $HOME, which is what keeps the app installable without root.

use serde::{Deserialize, Serialize};
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct AppSettings {
    pub home_page_url: String,
    pub window_width: f64,
    pub window_height: f64,
    pub window_left: Option<f64>,
    pub window_top: Option<f64>,

    /// "Light" or "Dark".
    pub theme_mode: String,
    pub accent_color_hex: String,
    /// Local copy under the app's own data folder — never the original
    /// user-picked path, which could move or be deleted.
    pub chrome_background_image_path: Option<String>,
    pub new_tab_background_image_path: Option<String>,

    /// One of the ids in `search::ENGINES`.
    pub search_engine: String,
    /// "Top", "Bottom" or "Sidebar".
    pub address_bar_position: String,
    /// "Small", "Normal" or "Large".
    pub address_bar_size: String,
    pub is_cursor_trail_enabled: bool,

    pub is_ad_block_enabled: bool,
    pub is_phishing_protection_enabled: bool,

    /// Tab groups from the previous session, restored before `tabs` so tabs can
    /// be reassigned to them by `SessionTab::group_id`.
    pub groups: Vec<SessionGroup>,
    /// Open tabs from the previous session, reopened on next launch.
    pub tabs: Vec<SessionTab>,
    /// Index into `tabs` of the tab that was active on shutdown.
    pub active_tab_index: i32,
    /// Groups explicitly saved by the user, reopenable on demand — independent
    /// of the current session.
    pub saved_groups: Vec<SavedGroup>,

    /// False until the welcome screen has been shown once.
    pub has_seen_welcome: bool,
    /// UI-only on Windows; persisted here so the sidebar keeps its state.
    pub is_sidebar_pinned: bool,
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            home_page_url: "navigatueur://newtab".into(),
            window_width: 1280.0,
            window_height: 800.0,
            window_left: None,
            window_top: None,
            theme_mode: "Dark".into(),
            accent_color_hex: "#4C8DFF".into(),
            chrome_background_image_path: None,
            new_tab_background_image_path: None,
            search_engine: "Bing".into(),
            address_bar_position: "Top".into(),
            address_bar_size: "Normal".into(),
            is_cursor_trail_enabled: true,
            is_ad_block_enabled: true,
            is_phishing_protection_enabled: true,
            groups: Vec::new(),
            tabs: Vec::new(),
            active_tab_index: -1,
            saved_groups: Vec::new(),
            has_seen_welcome: false,
            is_sidebar_pinned: false,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct SessionTab {
    pub url: String,
    pub title: String,
    pub group_id: Option<String>,
    pub is_pinned: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct SessionGroup {
    pub id: String,
    pub name: String,
    pub color_hex: String,
    pub is_collapsed: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct SavedGroup {
    pub id: String,
    pub name: String,
    pub color_hex: String,
    pub urls: Vec<String>,
}

/// The six group colours offered by the Windows build, same names and hexes.
pub const GROUP_COLORS: &[(&str, &str)] = &[
    ("Bleu", "#4C8DFF"),
    ("Vert", "#4FE0A0"),
    ("Ambre", "#E0A52A"),
    ("Rouge", "#E04F4F"),
    ("Violet", "#B14FE0"),
    ("Cyan", "#4FD1E0"),
];

pub fn config_dir() -> PathBuf {
    dirs::config_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("navigatueur")
}

pub fn data_dir() -> PathBuf {
    dirs::data_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("navigatueur")
}

/// Where user-picked background images are copied, mirroring the Windows
/// build's Backgrounds folder.
pub fn backgrounds_dir() -> PathBuf {
    data_dir().join("backgrounds")
}

fn settings_path() -> PathBuf {
    config_dir().join("settings.json")
}

pub fn load() -> AppSettings {
    // A corrupt or unreadable file must not stop the browser from starting —
    // same forgiving behaviour as the C# serializer, which falls back to defaults.
    std::fs::read_to_string(settings_path())
        .ok()
        .and_then(|json| serde_json::from_str(&json).ok())
        .unwrap_or_default()
}

pub fn save(settings: &AppSettings) -> std::io::Result<()> {
    let dir = config_dir();
    std::fs::create_dir_all(&dir)?;
    let json = serde_json::to_string_pretty(settings)
        .map_err(|e| std::io::Error::new(std::io::ErrorKind::InvalidData, e))?;

    // Write-then-rename so a crash mid-save cannot truncate an existing profile.
    let tmp = dir.join("settings.json.tmp");
    std::fs::write(&tmp, json)?;
    std::fs::rename(tmp, settings_path())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn defaults_round_trip() {
        let json = serde_json::to_string(&AppSettings::default()).unwrap();
        let back: AppSettings = serde_json::from_str(&json).unwrap();
        assert_eq!(back.accent_color_hex, "#4C8DFF");
        assert_eq!(back.search_engine, "Bing");
    }

    #[test]
    fn reads_partial_json_from_the_windows_build() {
        // Unknown/missing fields must fall back to defaults rather than fail.
        let back: AppSettings =
            serde_json::from_str(r#"{"ThemeMode":"Light","SomethingElse":42}"#).unwrap();
        assert_eq!(back.theme_mode, "Light");
        assert_eq!(back.window_width, 1280.0);
    }

    #[test]
    fn reads_a_windows_session_with_groups() {
        let json = r##"{
            "Groups":[{"Id":"g1","Name":"Travail","ColorHex":"#4FE0A0","IsCollapsed":true}],
            "Tabs":[{"Url":"https://example.com","GroupId":"g1","IsPinned":true}],
            "ActiveTabIndex":0
        }"##;
        let s: AppSettings = serde_json::from_str(json).unwrap();
        assert_eq!(s.groups[0].name, "Travail");
        assert!(s.groups[0].is_collapsed);
        assert_eq!(s.tabs[0].group_id.as_deref(), Some("g1"));
        assert!(s.tabs[0].is_pinned);
    }
}
