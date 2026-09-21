//! Port of `UpdateService` (C#): checks GitHub Releases for a newer build.
//!
//! The Windows build downloads a .exe installer and hands off to it. There is
//! no equivalent handoff here — an AppImage is replaced by overwriting the
//! file — so this reports the release and lets the user fetch it, rather than
//! silently swapping the binary out from under a running process.

use serde::Serialize;
use std::time::Duration;

const OWNER: &str = "jubifox";
const REPO: &str = "navigatueur";

#[derive(Debug, Clone, Serialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct UpdateStatus {
    pub is_update_available: bool,
    pub current_version: String,
    pub latest_version: Option<String>,
    pub download_url: Option<String>,
    /// Set when the check itself failed, so the UI can say so instead of
    /// silently claiming the app is up to date.
    pub error: Option<String>,
}

pub fn current_version() -> String {
    env!("CARGO_PKG_VERSION").to_string()
}

pub fn check() -> UpdateStatus {
    let current = current_version();
    let mut status = UpdateStatus {
        current_version: current.clone(),
        ..Default::default()
    };

    let response = ureq::builder()
        .timeout(Duration::from_secs(15))
        .build()
        .get(&format!("https://api.github.com/repos/{OWNER}/{REPO}/releases/latest"))
        .set("User-Agent", "Navigatueur-UpdateChecker")
        .call();

    let body = match response.and_then(|r| r.into_string().map_err(Into::into)) {
        Ok(body) => body,
        Err(e) => {
            status.error = Some(e.to_string());
            return status;
        }
    };

    let Ok(json) = serde_json::from_str::<serde_json::Value>(&body) else {
        status.error = Some("réponse GitHub illisible".into());
        return status;
    };

    let Some(tag) = json.get("tag_name").and_then(|t| t.as_str()) else {
        return status;
    };
    let latest = tag.trim_start_matches(['v', 'V']).to_string();

    if compare_versions(&latest, &current) != std::cmp::Ordering::Greater {
        return status;
    }

    // Prefer the AppImage asset; a release that only ships the Windows
    // installer is not an update this build can act on.
    let asset = json
        .get("assets")
        .and_then(|a| a.as_array())
        .and_then(|assets| {
            assets.iter().find(|a| {
                a.get("name")
                    .and_then(|n| n.as_str())
                    .map(|n| n.to_ascii_lowercase().ends_with(".appimage"))
                    .unwrap_or(false)
            })
        })
        .and_then(|a| a.get("browser_download_url"))
        .and_then(|u| u.as_str())
        .map(str::to_string);

    let Some(download_url) = asset else {
        return status;
    };

    status.is_update_available = true;
    status.latest_version = Some(latest);
    status.download_url = Some(download_url);
    status
}

/// Numeric, component-wise comparison — "0.10.0" must sort above "0.9.0",
/// which a plain string compare gets wrong.
fn compare_versions(a: &str, b: &str) -> std::cmp::Ordering {
    let parse = |v: &str| -> Vec<u32> {
        v.split(['.', '-', '+'])
            .map(|p| p.parse::<u32>().unwrap_or(0))
            .collect()
    };
    let (a, b) = (parse(a), parse(b));
    for i in 0..a.len().max(b.len()) {
        let ordering = a.get(i).copied().unwrap_or(0).cmp(&b.get(i).copied().unwrap_or(0));
        if ordering != std::cmp::Ordering::Equal {
            return ordering;
        }
    }
    std::cmp::Ordering::Equal
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cmp::Ordering;

    #[test]
    fn compares_numerically_not_lexically() {
        assert_eq!(compare_versions("0.10.0", "0.9.0"), Ordering::Greater);
        assert_eq!(compare_versions("0.15.0", "0.15.0"), Ordering::Equal);
        assert_eq!(compare_versions("1.0", "1.0.0"), Ordering::Equal);
        assert_eq!(compare_versions("0.15.1", "0.15.0"), Ordering::Greater);
    }

    #[test]
    fn a_missing_component_counts_as_zero() {
        assert_eq!(compare_versions("2", "2.0.0"), Ordering::Equal);
        assert_eq!(compare_versions("2.1", "2.0.9"), Ordering::Greater);
    }
}
