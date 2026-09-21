#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod blocklist;
mod history;
mod search;
mod settings;
mod sysinfo;
mod tabs;
mod update;
mod url_helper;

use blocklist::Blocklists;
use history::History;
use serde::Serialize;
use settings::AppSettings;
use std::collections::HashSet;
use std::sync::Mutex;
use tabs::{Group, Tab, TabManager};
use tauri::{
    webview::WebviewBuilder, Emitter, LogicalPosition, LogicalSize, Manager, WebviewUrl,
    WindowEvent,
};

/// Height of the chrome strip in logical pixels when the toolbar is expanded.
const CHROME_EXPANDED: f64 = 84.0;
/// Height when the toolbar has retracted — the tab strip alone stays visible,
/// mirroring ToolbarWindow's collapse-to-a-handle behaviour on Windows.
const CHROME_COLLAPSED: f64 = 46.0;

struct AppState {
    settings: Mutex<AppSettings>,
    lists: Blocklists,
    history: History,
    manager: Mutex<TabManager>,
    bypass: Mutex<HashSet<String>>,
    /// Current chrome height, so tab webviews can be laid out beneath it.
    chrome_height: Mutex<f64>,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
struct BrowserState {
    tabs: Vec<Tab>,
    groups: Vec<Group>,
    saved_groups: Vec<settings::SavedGroup>,
    active: Option<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct EngineDto {
    id: String,
    display_name: String,
    action_url: String,
}

// ------------------------------------------------------------------ helpers

fn emit_state(app: &tauri::AppHandle) {
    let Some(state) = app.try_state::<AppState>() else { return };
    let manager = state.manager.lock().unwrap();
    let _ = app.emit_to(
        "chrome",
        "state-changed",
        BrowserState {
            tabs: manager.tabs.clone(),
            groups: manager.groups.clone(),
            saved_groups: manager.saved_groups.clone(),
            active: manager.active.clone(),
        },
    );
}

fn persist(state: &AppState) {
    let mut settings = state.settings.lock().unwrap();
    state.manager.lock().unwrap().write_session(&mut settings);
    let _ = settings::save(&settings);
}

/// Lays the active tab out beneath the chrome and parks every other tab at zero
/// size. WebKitGTK has no per-webview visibility flag through Tauri, so an
/// inactive tab is shrunk rather than hidden — it keeps running, which is what
/// a background tab should do.
fn layout(app: &tauri::AppHandle) {
    let Some(state) = app.try_state::<AppState>() else { return };
    let Some(window) = app.get_window("main") else { return };
    let scale = window.scale_factor().unwrap_or(1.0);
    let Ok(size) = window.inner_size() else { return };
    let logical = size.to_logical::<f64>(scale);
    let chrome = *state.chrome_height.lock().unwrap();

    if let Some(view) = app.get_webview("chrome") {
        let _ = view.set_size(LogicalSize::new(logical.width, chrome));
    }

    let manager = state.manager.lock().unwrap();
    let active = manager.active.clone();
    for tab in manager.tabs.iter() {
        let Some(view) = app.get_webview(&tab.id) else { continue };
        if Some(&tab.id) == active.as_ref() {
            let _ = view.set_position(LogicalPosition::new(0.0, chrome));
            let _ = view.set_size(LogicalSize::new(
                logical.width,
                (logical.height - chrome).max(0.0),
            ));
        } else {
            let _ = view.set_size(LogicalSize::new(0.0, 0.0));
        }
    }
}

/// Creates the actual webview for a tab that has none.
fn spawn_webview(app: &tauri::AppHandle, id: &str, url: &str) -> Result<(), String> {
    if app.get_webview(id).is_some() {
        return Ok(());
    }
    let window = app.get_window("main").ok_or("main window is gone")?;

    let webview_url = if url.is_empty() || url.starts_with("navigatueur://") {
        WebviewUrl::App("newtab.html".into())
    } else {
        WebviewUrl::External(url.parse().map_err(|_| "invalid URL")?)
    };

    let builder = WebviewBuilder::new(id, webview_url)
        .initialization_script(include_str!("../../ui/inject.js"))
        .on_navigation(navigation_guard(app.clone(), id.to_string()));

    window
        .add_child(
            builder,
            LogicalPosition::new(0.0, CHROME_EXPANDED),
            LogicalSize::new(0.0, 0.0),
        )
        .map_err(|e| e.to_string())?;
    Ok(())
}

fn apply_live_change(app: &tauri::AppHandle, change: &tabs::LiveSetChange) {
    for id in &change.suspend {
        if let Some(view) = app.get_webview(id) {
            let _ = view.close();
        }
    }
    for id in &change.resume {
        let url = app
            .try_state::<AppState>()
            .and_then(|s| s.manager.lock().unwrap().find(id).map(|t| t.url.clone()))
            .unwrap_or_default();
        let _ = spawn_webview(app, id, &url);
    }
}

/// The custom cursor artwork, shared with the Windows build rather than
/// duplicated. Encoded once on first use, since it never changes at runtime.
fn cursor_data_uri() -> &'static str {
    use std::sync::OnceLock;
    static CACHE: OnceLock<String> = OnceLock::new();
    CACHE.get_or_init(|| {
        const PNG: &[u8] =
            include_bytes!("../../../src/Navigatueur.App/Resources/Cursors/cursor.png");
        format!("data:image/png;base64,{}", base64(PNG))
    })
}

/// Minimal base64 encoder — one call site, not worth a dependency.
fn base64(bytes: &[u8]) -> String {
    const TABLE: &[u8; 64] =
        b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::with_capacity(bytes.len().div_ceil(3) * 4);
    for chunk in bytes.chunks(3) {
        let b = [chunk[0], *chunk.get(1).unwrap_or(&0), *chunk.get(2).unwrap_or(&0)];
        let n = (b[0] as u32) << 16 | (b[1] as u32) << 8 | b[2] as u32;
        out.push(TABLE[(n >> 18 & 63) as usize] as char);
        out.push(TABLE[(n >> 12 & 63) as usize] as char);
        out.push(if chunk.len() > 1 { TABLE[(n >> 6 & 63) as usize] as char } else { '=' });
        out.push(if chunk.len() > 2 { TABLE[(n & 63) as usize] as char } else { '=' });
    }
    out
}

/// Pushes the appearance settings the injected script needs (it cannot read
/// them itself — remote pages get no IPC). Called on every navigation.
fn push_page_config(app: &tauri::AppHandle, id: &str) {
    let Some(state) = app.try_state::<AppState>() else { return };
    let (trail, accent) = {
        let s = state.settings.lock().unwrap();
        (s.is_cursor_trail_enabled, s.accent_color_hex.clone())
    };
    let cursor = if trail { cursor_data_uri() } else { "" };

    if let Some(view) = app.get_webview(id) {
        let _ = view.eval(&format!(
            "window.__nvSetConfig&&window.__nvSetConfig({{trail:{trail},accent:{},cursor:{}}})",
            serde_json::to_string(&accent).unwrap_or_else(|_| "\"#4C8DFF\"".into()),
            if cursor.is_empty() { "null".to_string() }
            else { serde_json::to_string(cursor).unwrap_or_else(|_| "null".into()) },
        ));
    }
}

/// Runs the per-host cosmetic/scriptlet payload in a tab, unless the user
/// turned the blocker off globally or for that tab.
fn inject_cosmetics(app: &tauri::AppHandle, id: &str, host: &str) {
    let Some(state) = app.try_state::<AppState>() else { return };
    if !state.settings.lock().unwrap().is_ad_block_enabled {
        return;
    }
    if state
        .manager
        .lock()
        .unwrap()
        .find(id)
        .map(|t| t.is_ad_block_disabled)
        .unwrap_or(false)
    {
        return;
    }
    let Some(script) = state.lists.page_script(host) else { return };
    if let Some(view) = app.get_webview(id) {
        let _ = view.eval(&script);
    }
}

// ----------------------------------------------------------------- commands

#[tauri::command]
fn get_state(state: tauri::State<AppState>) -> BrowserState {
    let manager = state.manager.lock().unwrap();
    BrowserState {
        tabs: manager.tabs.clone(),
        groups: manager.groups.clone(),
        saved_groups: manager.saved_groups.clone(),
        active: manager.active.clone(),
    }
}

#[tauri::command]
fn get_settings(state: tauri::State<AppState>) -> AppSettings {
    state.settings.lock().unwrap().clone()
}

#[tauri::command]
fn save_settings(new_settings: AppSettings, state: tauri::State<AppState>) -> Result<(), String> {
    let mut guard = state.settings.lock().unwrap();
    *guard = new_settings;
    settings::save(&guard).map_err(|e| e.to_string())
}

/// One entry point for every individual preference, so the settings page does
/// not have to read-modify-write the whole document (which would race with the
/// session autosave).
#[tauri::command]
fn set_setting(
    app: tauri::AppHandle,
    key: String,
    value: serde_json::Value,
    state: tauri::State<AppState>,
) -> Result<(), String> {
    {
        let mut s = state.settings.lock().unwrap();
        let as_str = || value.as_str().unwrap_or_default().to_string();
        match key.as_str() {
            "themeMode" => s.theme_mode = as_str(),
            "accentColorHex" => s.accent_color_hex = as_str(),
            "searchEngine" => s.search_engine = as_str(),
            "addressBarPosition" => s.address_bar_position = as_str(),
            "addressBarSize" => s.address_bar_size = as_str(),
            "homePageUrl" => s.home_page_url = as_str(),
            "isCursorTrailEnabled" => s.is_cursor_trail_enabled = value.as_bool().unwrap_or(true),
            "isAdBlockEnabled" => s.is_ad_block_enabled = value.as_bool().unwrap_or(true),
            "isPhishingProtectionEnabled" => {
                s.is_phishing_protection_enabled = value.as_bool().unwrap_or(true)
            }
            "isSidebarPinned" => s.is_sidebar_pinned = value.as_bool().unwrap_or(false),
            "hasSeenWelcome" => s.has_seen_welcome = value.as_bool().unwrap_or(true),
            "chromeBackgroundImagePath" => {
                s.chrome_background_image_path = value.as_str().map(str::to_string)
            }
            "newTabBackgroundImagePath" => {
                s.new_tab_background_image_path = value.as_str().map(str::to_string)
            }
            other => return Err(format!("unknown setting: {other}")),
        }
        settings::save(&s).map_err(|e| e.to_string())?;
    }

    // The chrome and any open settings page both re-read on this.
    let _ = app.emit("settings-changed", ());
    Ok(())
}

/// Copies a user-picked image into the app's own data folder, mirroring
/// `ThemeService.CopyIntoBackgrounds` — the original path could move or be
/// deleted, so the stored setting must never point at it.
#[tauri::command]
fn set_background_image(
    app: tauri::AppHandle,
    which: String,
    source_path: String,
    state: tauri::State<AppState>,
) -> Result<String, String> {
    let dir = settings::backgrounds_dir();
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;

    let extension = std::path::Path::new(&source_path)
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or("img");
    let destination = dir.join(format!("{which}.{extension}"));
    std::fs::copy(&source_path, &destination).map_err(|e| e.to_string())?;

    let stored = destination.to_string_lossy().to_string();
    {
        let mut s = state.settings.lock().unwrap();
        match which.as_str() {
            "chrome" => s.chrome_background_image_path = Some(stored.clone()),
            "newtab" => s.new_tab_background_image_path = Some(stored.clone()),
            other => return Err(format!("unknown background: {other}")),
        }
        settings::save(&s).map_err(|e| e.to_string())?;
    }
    let _ = app.emit("settings-changed", ());
    Ok(stored)
}

#[tauri::command]
fn get_search_engines() -> Vec<EngineDto> {
    search::ENGINES
        .iter()
        .map(|e| EngineDto {
            id: e.id.into(),
            display_name: e.display_name.into(),
            action_url: e.action_url.into(),
        })
        .collect()
}

#[tauri::command]
fn get_group_colors() -> Vec<(String, String)> {
    settings::GROUP_COLORS
        .iter()
        .map(|(n, h)| (n.to_string(), h.to_string()))
        .collect()
}

#[tauri::command]
fn get_history(state: tauri::State<AppState>) -> Vec<history::HistoryEntry> {
    state.history.entries()
}

#[tauri::command]
fn clear_history(state: tauri::State<AppState>) {
    state.history.clear();
}

#[tauri::command]
fn get_memory_usage() -> u64 {
    sysinfo::used_megabytes()
}

#[tauri::command]
fn check_update() -> update::UpdateStatus {
    update::check()
}

/// Port of `MainWindow.FindAddressBarSuggestion`: history hostnames first, then
/// a handful of common sites for a cold history, shortest match wins.
#[tauri::command]
fn address_bar_suggest(prefix: String, state: tauri::State<AppState>) -> Option<String> {
    const COMMON_SITES: &[&str] = &[
        "youtube.com", "google.com", "github.com", "wikipedia.org", "reddit.com",
        "twitter.com", "amazon.com", "netflix.com", "gmail.com", "twitch.tv",
        "instagram.com", "linkedin.com", "spotify.com", "discord.com",
    ];

    if prefix.is_empty() || prefix.contains(' ') || prefix.contains('/') {
        return None;
    }
    let lowered = prefix.to_ascii_lowercase();

    let mut candidates: Vec<String> = state
        .history
        .entries()
        .iter()
        .filter_map(|e| url::Url::parse(&e.url).ok())
        .filter_map(|u| u.host_str().map(|h| h.trim_start_matches("www.").to_string()))
        .collect();
    candidates.extend(COMMON_SITES.iter().map(|s| s.to_string()));

    candidates
        .into_iter()
        .filter(|h| h.to_ascii_lowercase().starts_with(&lowered) && h.len() > prefix.len())
        .min_by_key(|h| h.len())
}

// ------------------------------------------------------------- tab commands

#[tauri::command]
fn tab_create(
    app: tauri::AppHandle,
    url: Option<String>,
    state: tauri::State<AppState>,
) -> Result<String, String> {
    let target = url.unwrap_or_else(|| state.settings.lock().unwrap().home_page_url.clone());
    let id = {
        let mut manager = state.manager.lock().unwrap();
        let active = manager.active.clone();
        match active {
            Some(after) => manager.open_after(&after, target.clone()),
            None => manager.open(target.clone()),
        }
    };

    spawn_webview(&app, &id, &target)?;
    let change = state.manager.lock().unwrap().activate(&id);
    apply_live_change(&app, &change);
    layout(&app);
    emit_state(&app);
    persist(&state);
    Ok(id)
}

#[tauri::command]
fn tab_close(app: tauri::AppHandle, id: String, state: tauri::State<AppState>) {
    if let Some(view) = app.get_webview(&id) {
        let _ = view.close();
    }
    let next = state.manager.lock().unwrap().close(&id);

    // Closing the last tab reopens a blank one rather than leaving an empty
    // window, matching the Windows build.
    let is_empty = state.manager.lock().unwrap().tabs.is_empty();
    if is_empty {
        let home = state.settings.lock().unwrap().home_page_url.clone();
        let fresh = state.manager.lock().unwrap().open(home.clone());
        let _ = spawn_webview(&app, &fresh, &home);
        let change = state.manager.lock().unwrap().activate(&fresh);
        apply_live_change(&app, &change);
    } else if let Some(next) = next {
        let change = state.manager.lock().unwrap().activate(&next);
        apply_live_change(&app, &change);
    }

    layout(&app);
    emit_state(&app);
    persist(&state);
}

#[tauri::command]
fn tab_activate(app: tauri::AppHandle, id: String, state: tauri::State<AppState>) {
    let change = state.manager.lock().unwrap().activate(&id);
    apply_live_change(&app, &change);
    layout(&app);
    emit_state(&app);
}

#[tauri::command]
fn tab_navigate(
    app: tauri::AppHandle,
    id: String,
    input: String,
    state: tauri::State<AppState>,
) -> Result<String, String> {
    let engine = state.settings.lock().unwrap().search_engine.clone();
    let url = url_helper::normalize(&input, search::resolve(&engine).action_url);
    if url.is_empty() {
        return Ok(String::new());
    }

    // An internal page is served from the bundle, not navigated to as a URL.
    if url.starts_with("navigatueur://") {
        if let Some(view) = app.get_webview(&id) {
            let _ = view.eval("location.replace('newtab.html')");
        }
        return Ok(url);
    }

    let view = app.get_webview(&id).ok_or("unknown tab")?;
    view.navigate(url.parse().map_err(|_| "invalid URL")?)
        .map_err(|e| e.to_string())?;
    Ok(url)
}

#[tauri::command]
fn tab_history_go(app: tauri::AppHandle, id: String, delta: i32) {
    if let Some(view) = app.get_webview(&id) {
        let _ = view.eval(&format!("history.go({delta})"));
    }
}

#[tauri::command]
fn tab_reload(app: tauri::AppHandle, id: String) {
    if let Some(view) = app.get_webview(&id) {
        let _ = view.eval("location.reload()");
    }
}

#[tauri::command]
fn tab_stop(app: tauri::AppHandle, id: String) {
    if let Some(view) = app.get_webview(&id) {
        let _ = view.eval("window.stop()");
    }
}

/// Toggles a boolean on a tab. Zoom and mute additionally act on the webview.
#[tauri::command]
fn tab_set_flag(
    app: tauri::AppHandle,
    id: String,
    flag: String,
    value: bool,
    state: tauri::State<AppState>,
) {
    {
        let mut manager = state.manager.lock().unwrap();
        let Some(tab) = manager.find_mut(&id) else { return };
        match flag.as_str() {
            "isPinned" => {
                tab.is_pinned = value;
                if value {
                    tab.is_suspended = false;
                }
            }
            "isMuted" => tab.is_muted = value,
            "isAdBlockDisabled" => tab.is_ad_block_disabled = value,
            _ => return,
        }
    }

    if flag == "isMuted" {
        if let Some(view) = app.get_webview(&id) {
            // WebKitGTK's own mute flag is not reachable through Tauri, so this
            // mutes the media elements directly and keeps watching for new ones.
            let _ = view.eval(&format!(
                "(function(){{var m={};document.querySelectorAll('video,audio')\
                 .forEach(function(e){{e.muted=m}});window.__nvMuted=m;}})()",
                value
            ));
        }
    }

    if flag == "isAdBlockDisabled" {
        if let Some(view) = app.get_webview(&id) {
            let _ = view.eval("location.reload()");
        }
    }

    emit_state(&app);
    persist(&state);
}

#[tauri::command]
fn tab_set_zoom(app: tauri::AppHandle, id: String, zoom: f64, state: tauri::State<AppState>) {
    let clamped = zoom.clamp(0.25, 5.0);
    if let Some(tab) = state.manager.lock().unwrap().find_mut(&id) {
        tab.zoom = clamped;
    }
    if let Some(view) = app.get_webview(&id) {
        let _ = view.set_zoom(clamped);
    }
    emit_state(&app);
}

/// Same Yandex page-translation proxy the Windows build uses. Passing only the
/// target language lets Yandex auto-detect the source, which is the form that
/// actually works — an explicit "auto" source is not reliably honoured.
#[tauri::command]
fn tab_translate(app: tauri::AppHandle, id: String, state: tauri::State<AppState>) {
    let url = state
        .manager
        .lock()
        .unwrap()
        .find(&id)
        .map(|t| t.url.clone())
        .unwrap_or_default();
    if url.is_empty() || url.starts_with("navigatueur://") {
        return;
    }
    let encoded: String = url
        .bytes()
        .map(|b| match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => {
                (b as char).to_string()
            }
            _ => format!("%{b:02X}"),
        })
        .collect();

    if let Some(view) = app.get_webview(&id) {
        let target = format!("https://translate.yandex.com/translate?url={encoded}&lang=fr");
        if let Ok(parsed) = target.parse() {
            let _ = view.navigate(parsed);
        }
    }
}

#[tauri::command]
fn tab_picture_in_picture(app: tauri::AppHandle, id: String) {
    if let Some(view) = app.get_webview(&id) {
        let _ = view.eval(
            "(function(){if(document.pictureInPictureElement){document.exitPictureInPicture();return;}\
             var v=document.querySelector('video');\
             if(v&&v.requestPictureInPicture){v.requestPictureInPicture().catch(function(){});}})()",
        );
    }
}

/// Find-in-page. WebKitGTK's own find controller is not exposed through Tauri,
/// so this drives the page's `window.find`, which WebKit implements.
#[tauri::command]
fn tab_find(app: tauri::AppHandle, id: String, query: String, forward: bool) {
    if let Some(view) = app.get_webview(&id) {
        let escaped = query.replace('\\', "\\\\").replace('\'', "\\'");
        let _ = view.eval(&format!(
            "window.find('{escaped}', false, {}, true, false, true, false)",
            !forward
        ));
    }
}

// ----------------------------------------------------------- group commands

#[tauri::command]
fn group_action(
    app: tauri::AppHandle,
    action: String,
    id: String,
    arg: Option<String>,
    state: tauri::State<AppState>,
) {
    {
        let mut manager = state.manager.lock().unwrap();
        match action.as_str() {
            "createForTab" => {
                manager.create_group_for_tab(&id);
            }
            "rename" => {
                if let (Some(name), Some(group)) =
                    (arg.clone(), manager.groups.iter_mut().find(|g| g.id == id))
                {
                    group.name = name;
                }
            }
            "setColor" => {
                if let (Some(color), Some(group)) =
                    (arg.clone(), manager.groups.iter_mut().find(|g| g.id == id))
                {
                    group.color_hex = color;
                }
            }
            "toggleCollapsed" => {
                if let Some(group) = manager.groups.iter_mut().find(|g| g.id == id) {
                    group.is_collapsed = !group.is_collapsed;
                }
            }
            "delete" => manager.delete_group(&id),
            "save" => manager.save_group(&id),
            "deleteSaved" => manager.saved_groups.retain(|g| g.id != id),
            "assignTab" => {
                if let (Some(group_id), Some(tab)) = (arg.clone(), manager.find_mut(&id)) {
                    tab.group_id = Some(group_id);
                }
            }
            "removeTab" => {
                if let Some(tab) = manager.find_mut(&id) {
                    tab.group_id = None;
                }
            }
            _ => {}
        }
    }

    // Reopening a saved group creates tabs, so it needs the lock released first.
    if action == "openSaved" {
        let first = state.manager.lock().unwrap().open_saved_group(&id);
        if let Some(first) = first {
            let url = state
                .manager
                .lock()
                .unwrap()
                .find(&first)
                .map(|t| t.url.clone())
                .unwrap_or_default();
            let _ = spawn_webview(&app, &first, &url);
            let change = state.manager.lock().unwrap().activate(&first);
            apply_live_change(&app, &change);
            layout(&app);
        }
    }

    emit_state(&app);
    persist(&state);
}

#[tauri::command]
fn tab_reorder(
    app: tauri::AppHandle,
    source: String,
    target: String,
    mode: String,
    state: tauri::State<AppState>,
) {
    {
        let mut manager = state.manager.lock().unwrap();
        match mode.as_str() {
            "group" => manager.group_tabs(&source, &target),
            "after" => manager.reorder(&source, &target, true),
            _ => manager.reorder(&source, &target, false),
        }
    }
    emit_state(&app);
    persist(&state);
}

// ------------------------------------------------------------ warning bypass

#[tauri::command]
fn allow_once(
    app: tauri::AppHandle,
    url: String,
    state: tauri::State<AppState>,
) -> Result<(), String> {
    let parsed: tauri::Url = url.parse().map_err(|_| "invalid URL")?;
    let host = parsed.host_str().unwrap_or("").to_ascii_lowercase();
    if host.is_empty() {
        return Err("URL has no host".into());
    }
    state.bypass.lock().unwrap().insert(host);

    let active = state.manager.lock().unwrap().active.clone();
    if let Some(id) = active {
        if let Some(view) = app.get_webview(&id) {
            view.navigate(parsed).map_err(|e| e.to_string())?;
        }
    }
    Ok(())
}

/// Reports the chrome's current height so tab layout follows the toolbar as it
/// expands and retracts.
#[tauri::command]
fn set_chrome_height(app: tauri::AppHandle, expanded: bool, state: tauri::State<AppState>) {
    *state.chrome_height.lock().unwrap() = if expanded { CHROME_EXPANDED } else { CHROME_COLLAPSED };
    layout(&app);
}

// ------------------------------------------------------------ nav guard

fn navigation_guard(
    app: tauri::AppHandle,
    id: String,
) -> impl Fn(&tauri::Url) -> bool + Send + Sync + 'static {
    move |url: &tauri::Url| {
        let Some(state) = app.try_state::<AppState>() else { return true };

        let host = url.host_str().unwrap_or("").to_ascii_lowercase();

        // An explicit user override wins over both lists.
        if !host.is_empty() && state.bypass.lock().unwrap().contains(&host) {
            return true;
        }

        let (phishing_on, adblock_on) = {
            let s = state.settings.lock().unwrap();
            (s.is_phishing_protection_enabled, s.is_ad_block_enabled)
        };

        if !host.is_empty() {
            let blocked_reason = if phishing_on
                && state.lists.phishing.read().map(|s| s.matches(&host)).unwrap_or(false)
            {
                Some("phishing")
            } else if adblock_on
                && state.lists.ads.read().map(|s| s.matches(&host)).unwrap_or(false)
            {
                // Only top-level navigations reach this hook, so an ad domain
                // here means the user is being sent to one, not merely that a
                // page embeds it.
                Some("ad")
            } else {
                None
            };

            if let Some(reason) = blocked_reason {
                let _ = app.emit_to(
                    "chrome",
                    "navigation-blocked",
                    serde_json::json!({ "tab": id, "url": url.as_str(), "reason": reason }),
                );
                return false;
            }
        }

        {
            let mut manager = state.manager.lock().unwrap();
            if let Some(tab) = manager.find_mut(&id) {
                tab.url = url.as_str().to_string();
                tab.title.clear();
                tab.can_go_back = true;
            }
        }
        state.history.record(url.as_str(), "");

        inject_cosmetics(&app, &id, &host);
        push_page_config(&app, &id);
        emit_state(&app);
        true
    }
}

// ----------------------------------------------------------------- main

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            get_state,
            get_settings,
            save_settings,
            set_setting,
            set_background_image,
            get_search_engines,
            get_group_colors,
            get_history,
            clear_history,
            get_memory_usage,
            check_update,
            address_bar_suggest,
            tab_create,
            tab_close,
            tab_activate,
            tab_navigate,
            tab_history_go,
            tab_reload,
            tab_stop,
            tab_set_flag,
            tab_set_zoom,
            tab_translate,
            tab_picture_in_picture,
            tab_find,
            group_action,
            tab_reorder,
            allow_once,
            set_chrome_height,
        ])
        .setup(|app| {
            let resource_dir = app
                .path()
                .resource_dir()
                .map(|d| d.join("resources"))
                .unwrap_or_else(|_| std::path::PathBuf::from("resources"));

            let loaded = settings::load();
            let (w, h) = (loaded.window_width, loaded.window_height);

            let mut manager = TabManager::new(false);
            let restored = manager.restore(&loaded);

            app.manage(AppState {
                settings: Mutex::new(loaded),
                lists: Blocklists::load(&resource_dir),
                history: History::load(),
                manager: Mutex::new(manager),
                bypass: Mutex::new(HashSet::new()),
                chrome_height: Mutex::new(CHROME_EXPANDED),
            });

            let window = tauri::window::WindowBuilder::new(app, "main")
                .title("Navigatueur")
                .inner_size(w, h)
                .min_inner_size(640.0, 480.0)
                .build()?;

            // The chrome is itself a webview pinned to the top of the window —
            // the Linux counterpart of ToolbarWindow + TabSidebarWindow, minus
            // the layered-window tricks that only existed to dodge WebView2's
            // airspace problem.
            window.add_child(
                WebviewBuilder::new("chrome", WebviewUrl::App("index.html".into())),
                LogicalPosition::new(0.0, 0.0),
                LogicalSize::new(w, CHROME_EXPANDED),
            )?;

            let handle = app.handle().clone();

            // Restore the previous session, or open a fresh home tab.
            match restored {
                Some(active) => {
                    let url = handle
                        .state::<AppState>()
                        .manager
                        .lock()
                        .unwrap()
                        .find(&active)
                        .map(|t| t.url.clone())
                        .unwrap_or_default();
                    spawn_webview(&handle, &active, &url).ok();
                    let change = handle.state::<AppState>().manager.lock().unwrap().activate(&active);
                    apply_live_change(&handle, &change);
                }
                None => {
                    let home = handle
                        .state::<AppState>()
                        .settings
                        .lock()
                        .unwrap()
                        .home_page_url
                        .clone();
                    let id = handle.state::<AppState>().manager.lock().unwrap().open(home.clone());
                    spawn_webview(&handle, &id, &home).ok();
                    let change = handle.state::<AppState>().manager.lock().unwrap().activate(&id);
                    apply_live_change(&handle, &change);
                }
            }
            layout(&handle);

            // Idle-suspend sweep and the periodic session autosave, both on the
            // same cadence the Windows timers used.
            let sweeper = handle.clone();
            std::thread::spawn(move || loop {
                std::thread::sleep(std::time::Duration::from_secs(60));
                let suspended = {
                    let Some(state) = sweeper.try_state::<AppState>() else { return };
                    // Bound to a local so the MutexGuard temporary is dropped
                    // before `state` goes out of scope at the end of the block.
                    let ids = state.manager.lock().unwrap().suspend_idle();
                    ids
                };
                for id in suspended {
                    if let Some(view) = sweeper.get_webview(&id) {
                        let _ = view.close();
                    }
                }
            });

            let saver = handle.clone();
            std::thread::spawn(move || loop {
                std::thread::sleep(std::time::Duration::from_secs(120));
                let Some(state) = saver.try_state::<AppState>() else { return };
                persist(&state);
            });

            let resize_handle = handle.clone();
            window.on_window_event(move |event| match event {
                WindowEvent::Resized(_) => layout(&resize_handle),
                WindowEvent::CloseRequested { .. } => {
                    // Persist geometry alongside the session so the window
                    // reopens where it was left.
                    let geometry = resize_handle.get_window("main").map(|win| {
                        let scale = win.scale_factor().unwrap_or(1.0);
                        let size = win
                            .inner_size()
                            .map(|s| s.to_logical::<f64>(scale))
                            .unwrap_or(LogicalSize::new(1280.0, 800.0));
                        let position = win
                            .outer_position()
                            .ok()
                            .map(|p| p.to_logical::<f64>(scale));
                        (size, position)
                    });

                    let state = resize_handle.state::<AppState>();
                    if let Some((size, position)) = geometry {
                        let mut s = state.settings.lock().unwrap();
                        s.window_width = size.width;
                        s.window_height = size.height;
                        if let Some(p) = position {
                            s.window_left = Some(p.x);
                            s.window_top = Some(p.y);
                        }
                    }
                    persist(&state);
                }
                _ => {}
            });

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running Navigatueur");
}

#[cfg(test)]
mod tests {
    use super::base64;

    #[test]
    fn base64_matches_rfc4648_including_padding() {
        // The three padding cases (0, 1, 2 leftover bytes) are the only place
        // a hand-rolled encoder usually goes wrong.
        assert_eq!(base64(b""), "");
        assert_eq!(base64(b"f"), "Zg==");
        assert_eq!(base64(b"fo"), "Zm8=");
        assert_eq!(base64(b"foo"), "Zm9v");
        assert_eq!(base64(b"foob"), "Zm9vYg==");
        assert_eq!(base64(b"fooba"), "Zm9vYmE=");
        assert_eq!(base64(b"foobar"), "Zm9vYmFy");
    }

    #[test]
    fn base64_handles_high_bytes() {
        assert_eq!(base64(&[0xFF, 0xFE, 0xFD]), "//79");
        assert_eq!(base64(&[0x00, 0x00, 0x00]), "AAAA");
    }
}
