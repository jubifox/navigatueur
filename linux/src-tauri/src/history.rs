//! Port of `HistoryService` (C#): newest-first, capped at 2000 entries, with
//! the same exclusion rules (about: URLs and the app's own internal pages are
//! never recorded) and the same "corrupt file, start fresh" tolerance.

use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use std::sync::Mutex;

const MAX_ENTRIES: usize = 2000;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct HistoryEntry {
    pub url: String,
    pub title: String,
    /// RFC 3339, matching the C# `DateTimeOffset` JSON shape.
    pub visited_at: String,
}

#[derive(Default)]
pub struct History {
    entries: Mutex<Vec<HistoryEntry>>,
}

fn history_path() -> PathBuf {
    crate::settings::data_dir().join("history.json")
}

impl History {
    pub fn load() -> Self {
        let entries = std::fs::read_to_string(history_path())
            .ok()
            .and_then(|json| serde_json::from_str::<Vec<HistoryEntry>>(&json).ok())
            .unwrap_or_default();
        Self { entries: Mutex::new(entries) }
    }

    pub fn record(&self, url: &str, title: &str) {
        // Internal pages and about: URLs are navigation plumbing, not places
        // the user visited — same filter as the Windows build.
        if url.trim().is_empty()
            || url.to_ascii_lowercase().starts_with("about:")
            || url.to_ascii_lowercase().contains("navigatueur.")
            || url.to_ascii_lowercase().starts_with("navigatueur://")
        {
            return;
        }

        let Ok(mut entries) = self.entries.lock() else {
            return;
        };
        entries.insert(
            0,
            HistoryEntry {
                url: url.to_string(),
                title: if title.trim().is_empty() { url.to_string() } else { title.to_string() },
                visited_at: now_rfc3339(),
            },
        );
        entries.truncate(MAX_ENTRIES);
        save(&entries);
    }

    pub fn entries(&self) -> Vec<HistoryEntry> {
        self.entries.lock().map(|e| e.clone()).unwrap_or_default()
    }

    pub fn clear(&self) {
        if let Ok(mut entries) = self.entries.lock() {
            entries.clear();
            save(&entries);
        }
    }
}

fn save(entries: &[HistoryEntry]) {
    // Best-effort: failing to persist history must never interrupt browsing.
    let path = history_path();
    if let Some(dir) = path.parent() {
        let _ = std::fs::create_dir_all(dir);
    }
    if let Ok(json) = serde_json::to_string(entries) {
        let _ = std::fs::write(path, json);
    }
}

/// Minimal RFC 3339 stamp from the Unix epoch — avoids pulling in `chrono`
/// for the one place the app needs a formatted date.
fn now_rfc3339() -> String {
    let secs = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0);

    let days = secs.div_euclid(86_400);
    let tod = secs.rem_euclid(86_400);
    let (h, mi, s) = (tod / 3600, (tod % 3600) / 60, tod % 60);
    let (y, mo, d) = civil_from_days(days);
    format!("{y:04}-{mo:02}-{d:02}T{h:02}:{mi:02}:{s:02}Z")
}

/// Howard Hinnant's days-from-civil inverse; correct for all proleptic
/// Gregorian dates, which is more than this needs but costs nothing.
fn civil_from_days(z: i64) -> (i64, u32, u32) {
    let z = z + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = (doy - (153 * mp + 2) / 5 + 1) as u32;
    let m = if mp < 10 { mp + 3 } else { mp - 9 } as u32;
    (if m <= 2 { y + 1 } else { y }, m, d)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn skips_internal_pages() {
        let h = History::default();
        h.record("about:blank", "");
        h.record("navigatueur://newtab", "");
        assert!(h.entries().is_empty());
    }

    #[test]
    fn falls_back_to_url_when_title_is_blank() {
        let h = History::default();
        h.record("https://example.com", "  ");
        assert_eq!(h.entries()[0].title, "https://example.com");
    }

    #[test]
    fn epoch_formats_correctly() {
        assert_eq!(civil_from_days(0), (1970, 1, 1));
        assert_eq!(civil_from_days(19_000), (2022, 1, 8));
    }
}
