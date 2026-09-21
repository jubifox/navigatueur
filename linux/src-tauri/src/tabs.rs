//! Port of `TabManagerService` + the state half of `BrowserTabViewModel` (C#).
//!
//! The Rust side is authoritative for tab state; the chrome webview renders it
//! and drives it through commands. That mirrors the Windows split, where the
//! service owned the collections and the XAML just bound to them.
//!
//! Suspension works the same way it does on Windows — at most `MAX_LIVE_TABS`
//! tabs keep a live web view, evicted least-recently-used, and anything idle
//! for `IDLE_SUSPEND` is dropped too. Pinned tabs and tabs currently playing
//! audio are never evicted. The difference is only in the mechanism: there a
//! suspended tab disposed its CoreWebView2, here it closes its Tauri webview
//! and gets a fresh one on re-activation.

use crate::settings::{AppSettings, SavedGroup, SessionGroup, SessionTab, GROUP_COLORS};
use serde::{Deserialize, Serialize};
use std::time::{Duration, Instant};

const MAX_LIVE_TABS: usize = 3;
const IDLE_SUSPEND: Duration = Duration::from_secs(15 * 60);

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Tab {
    /// Also the Tauri webview label while the tab is live.
    pub id: String,
    pub url: String,
    pub title: String,
    /// data: URI of the page favicon, or None.
    pub favicon: Option<String>,
    pub group_id: Option<String>,
    pub is_pinned: bool,
    pub is_muted: bool,
    pub is_playing_audio: bool,
    pub is_loading: bool,
    /// True while this tab has no live webview.
    pub is_suspended: bool,
    /// Per-tab escape hatch for the rare page a domain-list blocker breaks.
    pub is_ad_block_disabled: bool,
    /// The page's `<meta name="theme-color">`, driving the chrome gradient.
    pub site_accent_color_hex: Option<String>,
    pub can_go_back: bool,
    pub can_go_forward: bool,
    pub zoom: f64,
    #[serde(skip, default = "Instant::now")]
    pub last_activated_at: Instant,
}

impl Tab {
    fn new(id: String, url: String) -> Self {
        Self {
            id,
            url,
            title: String::new(),
            favicon: None,
            group_id: None,
            is_pinned: false,
            is_muted: false,
            is_playing_audio: false,
            is_loading: false,
            is_suspended: false,
            is_ad_block_disabled: false,
            site_accent_color_hex: None,
            can_go_back: false,
            can_go_forward: false,
            zoom: 1.0,
            last_activated_at: Instant::now(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Group {
    pub id: String,
    pub name: String,
    pub color_hex: String,
    pub is_collapsed: bool,
}

#[derive(Default)]
pub struct TabManager {
    pub tabs: Vec<Tab>,
    pub groups: Vec<Group>,
    pub saved_groups: Vec<SavedGroup>,
    pub active: Option<String>,
    /// Most-recently-activated first; the LRU window for live web views.
    live_order: Vec<String>,
    next_id: u32,
    /// Private windows never write session or saved groups to settings.json.
    pub is_private: bool,
}

/// What `activate` wants the caller to do to the actual Tauri webviews.
#[derive(Debug, Default)]
pub struct LiveSetChange {
    /// Tabs whose webview should be created or shown.
    pub resume: Vec<String>,
    /// Tabs whose webview should be destroyed.
    pub suspend: Vec<String>,
}

impl TabManager {
    pub fn new(is_private: bool) -> Self {
        Self { is_private, ..Default::default() }
    }

    fn mint_id(&mut self) -> String {
        self.next_id += 1;
        format!("tab-{}", self.next_id)
    }

    pub fn find(&self, id: &str) -> Option<&Tab> {
        self.tabs.iter().find(|t| t.id == id)
    }

    pub fn find_mut(&mut self, id: &str) -> Option<&mut Tab> {
        self.tabs.iter_mut().find(|t| t.id == id)
    }

    pub fn open(&mut self, url: String) -> String {
        let id = self.mint_id();
        self.tabs.push(Tab::new(id.clone(), url));
        id
    }

    /// Inserts a new tab directly after `after_id`, which is what a link opened
    /// in a new tab should do rather than jumping to the end of the strip.
    pub fn open_after(&mut self, after_id: &str, url: String) -> String {
        let id = self.mint_id();
        let mut tab = Tab::new(id.clone(), url);
        // A child tab inherits its opener's group, matching Chrome/Edge.
        if let Some(parent) = self.find(after_id) {
            tab.group_id = parent.group_id.clone();
        }
        match self.tabs.iter().position(|t| t.id == after_id) {
            Some(index) => self.tabs.insert(index + 1, tab),
            None => self.tabs.push(tab),
        }
        id
    }

    pub fn close(&mut self, id: &str) -> Option<String> {
        let index = self.tabs.iter().position(|t| t.id == id)?;
        self.tabs.remove(index);
        self.live_order.retain(|l| l != id);

        if self.active.as_deref() != Some(id) {
            return None;
        }

        // Fall back to the tab that slid into the closed one's position, or the
        // last one if it was at the end — same rule as the Windows build.
        let next = self
            .tabs
            .get(index.min(self.tabs.len().saturating_sub(1)))
            .map(|t| t.id.clone());
        self.active = next.clone();
        next
    }

    /// Marks `id` active and returns which webviews must now be created or torn
    /// down to respect the live-tab cap.
    pub fn activate(&mut self, id: &str) -> LiveSetChange {
        let mut change = LiveSetChange::default();
        if self.find(id).is_none() {
            return change;
        }

        self.active = Some(id.to_string());

        let is_pinned = self.find(id).map(|t| t.is_pinned).unwrap_or(false);
        if let Some(tab) = self.find_mut(id) {
            tab.last_activated_at = Instant::now();
            if tab.is_suspended {
                tab.is_suspended = false;
                change.resume.push(id.to_string());
            }
        }

        // Pinned tabs stay outside the LRU window — always live, never evicted.
        if is_pinned {
            return change;
        }

        self.live_order.retain(|l| l != id);
        self.live_order.insert(0, id.to_string());

        while self.live_order.len() > MAX_LIVE_TABS {
            // Evict from the back, skipping anything still playing audio; if
            // every candidate is, exceed the cap rather than cut sound off.
            let Some(pos) = self.live_order.iter().rposition(|l| {
                self.tabs
                    .iter()
                    .find(|t| &t.id == l)
                    .map(|t| !t.is_playing_audio)
                    .unwrap_or(true)
            }) else {
                break;
            };

            let evicted = self.live_order.remove(pos);
            if let Some(tab) = self.find_mut(&evicted) {
                tab.is_suspended = true;
            }
            change.suspend.push(evicted);
        }

        change
    }

    /// Drops web views for tabs untouched for longer than `IDLE_SUSPEND`.
    pub fn suspend_idle(&mut self) -> Vec<String> {
        let active = self.active.clone();
        let mut suspended = Vec::new();

        for index in (0..self.live_order.len()).rev() {
            let id = self.live_order[index].clone();
            if active.as_deref() == Some(id.as_str()) {
                continue;
            }
            let Some(tab) = self.find(&id) else { continue };
            if tab.is_pinned || tab.is_playing_audio {
                continue;
            }
            if tab.last_activated_at.elapsed() <= IDLE_SUSPEND {
                continue;
            }

            self.live_order.remove(index);
            if let Some(tab) = self.find_mut(&id) {
                tab.is_suspended = true;
            }
            suspended.push(id);
        }

        suspended
    }

    /// Ctrl+Tab / Ctrl+Shift+Tab: wraps around in either direction.
    pub fn adjacent(&self, direction: i32) -> Option<String> {
        if self.tabs.is_empty() {
            return None;
        }
        let current = self
            .active
            .as_ref()
            .and_then(|a| self.tabs.iter().position(|t| &t.id == a))
            .map(|i| i as i32)
            .unwrap_or(-1);
        let len = self.tabs.len() as i32;
        let next = ((current + direction) % len + len) % len;
        Some(self.tabs[next as usize].id.clone())
    }

    /// Ctrl+1..Ctrl+8.
    pub fn at_index(&self, index: usize) -> Option<String> {
        self.tabs.get(index).map(|t| t.id.clone())
    }

    /// Ctrl+9 always jumps to the last tab, whatever the count.
    pub fn last(&self) -> Option<String> {
        self.tabs.last().map(|t| t.id.clone())
    }

    // ------------------------------------------------------------- groups

    pub fn create_group(&mut self, name: String, color_hex: String) -> String {
        let id = format!("group-{}", self.groups.len() + 1);
        self.groups.push(Group { id: id.clone(), name, color_hex, is_collapsed: false });
        id
    }

    /// Colours cycle through the palette so consecutive groups look distinct.
    pub fn next_group_color(&self) -> String {
        GROUP_COLORS[self.groups.len() % GROUP_COLORS.len()].1.to_string()
    }

    pub fn create_group_for_tab(&mut self, tab_id: &str) -> String {
        let color = self.next_group_color();
        let name = format!("Groupe {}", self.groups.len() + 1);
        let group_id = self.create_group(name, color);
        if let Some(tab) = self.find_mut(tab_id) {
            tab.group_id = Some(group_id.clone());
        }
        group_id
    }

    /// Ungroups every member (they stay open) and drops the group. Any saved
    /// snapshot is deliberately untouched — it is independent by design.
    pub fn delete_group(&mut self, group_id: &str) {
        for tab in self.tabs.iter_mut() {
            if tab.group_id.as_deref() == Some(group_id) {
                tab.group_id = None;
            }
        }
        self.groups.retain(|g| g.id != group_id);
    }

    /// Dropping one tab onto another groups them, as in Chrome/Edge: the source
    /// joins the target's group, or a fresh group is made for both.
    pub fn group_tabs(&mut self, source: &str, target: &str) {
        if source == target {
            return;
        }
        let target_group = match self.find(target).and_then(|t| t.group_id.clone()) {
            Some(existing) => existing,
            None => {
                let color = self.next_group_color();
                let name = format!("Groupe {}", self.groups.len() + 1);
                let id = self.create_group(name, color);
                if let Some(t) = self.find_mut(target) {
                    t.group_id = Some(id.clone());
                }
                id
            }
        };

        if let Some(s) = self.find_mut(source) {
            s.group_id = Some(target_group);
        }
        self.move_after(source, target);
    }

    /// Moves `source` to sit just before or after `target`, adopting its group.
    pub fn reorder(&mut self, source: &str, target: &str, insert_after: bool) {
        if source == target {
            return;
        }
        let group = self.find(target).and_then(|t| t.group_id.clone());
        if let Some(s) = self.find_mut(source) {
            s.group_id = group;
        }
        if insert_after {
            self.move_after(source, target);
        } else {
            self.move_before(source, target);
        }
    }

    fn move_after(&mut self, source: &str, target: &str) {
        let Some(from) = self.tabs.iter().position(|t| t.id == source) else { return };
        let tab = self.tabs.remove(from);
        let Some(to) = self.tabs.iter().position(|t| t.id == target) else {
            self.tabs.insert(from.min(self.tabs.len()), tab);
            return;
        };
        self.tabs.insert(to + 1, tab);
    }

    fn move_before(&mut self, source: &str, target: &str) {
        let Some(from) = self.tabs.iter().position(|t| t.id == source) else { return };
        let tab = self.tabs.remove(from);
        let Some(to) = self.tabs.iter().position(|t| t.id == target) else {
            self.tabs.insert(from.min(self.tabs.len()), tab);
            return;
        };
        self.tabs.insert(to, tab);
    }

    // ------------------------------------------------------- saved groups

    pub fn save_group(&mut self, group_id: &str) {
        let Some(group) = self.groups.iter().find(|g| g.id == group_id) else { return };
        let urls: Vec<String> = self
            .tabs
            .iter()
            .filter(|t| t.group_id.as_deref() == Some(group_id))
            .map(|t| t.url.clone())
            .collect();
        if urls.is_empty() {
            return;
        }

        let snapshot = SavedGroup {
            id: group.id.clone(),
            name: group.name.clone(),
            color_hex: group.color_hex.clone(),
            urls,
        };
        self.saved_groups.retain(|g| g.id != group_id);
        self.saved_groups.push(snapshot);
    }

    /// Reopens a saved group; every tab starts suspended except the first, the
    /// same lazy-load approach session restore uses.
    pub fn open_saved_group(&mut self, saved_id: &str) -> Option<String> {
        let saved = self.saved_groups.iter().find(|g| g.id == saved_id)?.clone();
        let group_id = self.create_group(saved.name.clone(), saved.color_hex.clone());

        let mut first = None;
        for url in saved.urls {
            let id = self.mint_id();
            let mut tab = Tab::new(id.clone(), url);
            tab.group_id = Some(group_id.clone());
            tab.is_suspended = true;
            self.tabs.push(tab);
            first.get_or_insert(id);
        }
        first
    }

    // ----------------------------------------------------------- session

    /// Rebuilds tabs and groups from the previous session. Everything starts
    /// suspended so reopening a large session does not spawn many web
    /// processes at once; the active tab is resumed by the caller.
    pub fn restore(&mut self, settings: &AppSettings) -> Option<String> {
        for group in &settings.groups {
            self.groups.push(Group {
                id: group.id.clone(),
                name: group.name.clone(),
                color_hex: group.color_hex.clone(),
                is_collapsed: group.is_collapsed,
            });
        }
        self.saved_groups = settings.saved_groups.clone();

        for state in &settings.tabs {
            let id = self.mint_id();
            let mut tab = Tab::new(id, state.url.clone());
            tab.title = state.title.clone();
            tab.group_id = state.group_id.clone();
            tab.is_pinned = state.is_pinned;
            tab.is_suspended = true;
            self.tabs.push(tab);
        }

        if self.tabs.is_empty() {
            return None;
        }

        let index = if settings.active_tab_index >= 0
            && (settings.active_tab_index as usize) < self.tabs.len()
        {
            settings.active_tab_index as usize
        } else {
            0
        };
        Some(self.tabs[index].id.clone())
    }

    /// Writes the current tabs, groups and saved groups back into `settings`.
    pub fn write_session(&self, settings: &mut AppSettings) {
        if self.is_private {
            return; // never let a private window overwrite the real profile
        }

        settings.groups = self
            .groups
            .iter()
            .map(|g| SessionGroup {
                id: g.id.clone(),
                name: g.name.clone(),
                color_hex: g.color_hex.clone(),
                is_collapsed: g.is_collapsed,
            })
            .collect();

        settings.tabs = self
            .tabs
            .iter()
            .map(|t| SessionTab {
                url: t.url.clone(),
                title: t.title.clone(),
                group_id: t.group_id.clone(),
                is_pinned: t.is_pinned,
            })
            .collect();

        settings.active_tab_index = self
            .active
            .as_ref()
            .and_then(|a| self.tabs.iter().position(|t| &t.id == a))
            .map(|i| i as i32)
            .unwrap_or(-1);

        settings.saved_groups = self.saved_groups.clone();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn manager_with(count: usize) -> TabManager {
        let mut m = TabManager::new(false);
        for i in 0..count {
            m.open(format!("https://example.com/{i}"));
        }
        m
    }

    #[test]
    fn live_set_evicts_least_recently_used() {
        let mut m = manager_with(5);
        let ids: Vec<String> = m.tabs.iter().map(|t| t.id.clone()).collect();
        for id in &ids[..4] {
            m.activate(id);
        }
        // With a cap of 3, activating a 4th must have suspended the first.
        let change = m.activate(&ids[4]);
        assert!(!change.suspend.is_empty());
        assert!(m.find(&ids[0]).unwrap().is_suspended);
        assert!(!m.find(&ids[4]).unwrap().is_suspended);
    }

    #[test]
    fn audio_playing_tabs_are_not_evicted() {
        let mut m = manager_with(4);
        let ids: Vec<String> = m.tabs.iter().map(|t| t.id.clone()).collect();
        for id in &ids {
            m.activate(id);
        }
        // Everything live is producing sound: the cap is exceeded rather than
        // cutting a tab off mid-playback.
        for t in m.tabs.iter_mut() {
            t.is_playing_audio = true;
        }
        let change = m.activate(&ids[0]);
        assert!(change.suspend.is_empty());
    }

    #[test]
    fn pinned_tabs_stay_outside_the_lru() {
        let mut m = manager_with(5);
        let ids: Vec<String> = m.tabs.iter().map(|t| t.id.clone()).collect();
        m.find_mut(&ids[0]).unwrap().is_pinned = true;
        for id in &ids {
            m.activate(id);
        }
        assert!(!m.find(&ids[0]).unwrap().is_suspended);
    }

    #[test]
    fn closing_the_active_tab_falls_through_to_its_neighbour() {
        let mut m = manager_with(3);
        let ids: Vec<String> = m.tabs.iter().map(|t| t.id.clone()).collect();
        m.activate(&ids[1]);
        let next = m.close(&ids[1]);
        assert_eq!(next, Some(ids[2].clone()));
    }

    #[test]
    fn adjacent_wraps_in_both_directions() {
        let mut m = manager_with(3);
        let ids: Vec<String> = m.tabs.iter().map(|t| t.id.clone()).collect();
        m.activate(&ids[0]);
        assert_eq!(m.adjacent(-1), Some(ids[2].clone()));
        m.activate(&ids[2]);
        assert_eq!(m.adjacent(1), Some(ids[0].clone()));
    }

    #[test]
    fn dropping_a_tab_onto_another_groups_both() {
        let mut m = manager_with(2);
        let ids: Vec<String> = m.tabs.iter().map(|t| t.id.clone()).collect();
        m.group_tabs(&ids[0], &ids[1]);
        let g = m.find(&ids[1]).unwrap().group_id.clone();
        assert!(g.is_some());
        assert_eq!(m.find(&ids[0]).unwrap().group_id, g);
    }

    #[test]
    fn deleting_a_group_keeps_its_tabs_open() {
        let mut m = manager_with(2);
        let ids: Vec<String> = m.tabs.iter().map(|t| t.id.clone()).collect();
        let g = m.create_group_for_tab(&ids[0]);
        m.delete_group(&g);
        assert_eq!(m.tabs.len(), 2);
        assert!(m.find(&ids[0]).unwrap().group_id.is_none());
    }

    #[test]
    fn session_round_trips_through_settings() {
        let mut m = manager_with(2);
        let ids: Vec<String> = m.tabs.iter().map(|t| t.id.clone()).collect();
        let g = m.create_group_for_tab(&ids[0]);
        m.find_mut(&ids[1]).unwrap().is_pinned = true;
        m.activate(&ids[1]);
        m.save_group(&g);

        let mut settings = AppSettings::default();
        m.write_session(&mut settings);

        let mut restored = TabManager::new(false);
        let active = restored.restore(&settings);
        assert_eq!(restored.tabs.len(), 2);
        assert_eq!(restored.groups.len(), 1);
        assert_eq!(restored.saved_groups.len(), 1);
        assert_eq!(active, Some(restored.tabs[1].id.clone()));
        assert!(restored.tabs[1].is_pinned);
    }

    #[test]
    fn a_private_manager_never_writes_session_state() {
        let mut m = TabManager::new(true);
        m.open("https://secret.example".into());
        let mut settings = AppSettings::default();
        m.write_session(&mut settings);
        assert!(settings.tabs.is_empty());
    }

    #[test]
    fn a_new_tab_lands_next_to_its_opener_and_inherits_its_group() {
        let mut m = manager_with(3);
        let ids: Vec<String> = m.tabs.iter().map(|t| t.id.clone()).collect();
        let g = m.create_group_for_tab(&ids[0]);
        let child = m.open_after(&ids[0], "https://child.example".into());
        assert_eq!(m.tabs[1].id, child);
        assert_eq!(m.find(&child).unwrap().group_id, Some(g));
    }
}
