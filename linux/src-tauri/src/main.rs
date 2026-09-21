// Hide the console window on Windows — irrelevant for this build but keeps the
// crate buildable on both platforms for anyone cross-checking the port.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod blocklist;
mod history;
mod search;
mod settings;
mod url_helper;

use blocklist::Blocklists;
use history::History;
use serde::Serialize;
use settings::AppSettings;
use std::collections::{HashMap, HashSet};
use std::sync::Mutex;
use tauri::{
    webview::WebviewBuilder, Emitter, LogicalPosition, LogicalSize, Manager, WebviewUrl,
    WindowEvent,
};

/// Height of the chrome strip (tab bar + toolbar) in logical pixels. The tab
/// webviews start immediately below it.
const CHROME_HEIGHT: f64 = 84.0;

struct AppState {
    settings: Mutex<AppSettings>,
    lists: Blocklists,
    history: History,
    /// Tab label -> last known URL, kept so the address bar can be repopulated
    /// when the user switches tabs.
    tab_urls: Mutex<HashMap<String, String>>,
    active_tab: Mutex<Option<String>>,
    next_tab_id: Mutex<u32>,
    /// Hosts the user chose to visit anyway from the warning page. Session-only
    /// and never persisted — an override should not outlive the browser.
    bypass: Mutex<HashSet<String>>,
}

#[derive(Serialize)]
struct TabHandle {
    label: String,
    url: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct EngineDto {
    id: String,
    display_name: String,
    action_url: String,
}

// ---------------------------------------------------------------- commands

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
fn get_history(state: tauri::State<AppState>) -> Vec<history::HistoryEntry> {
    state.history.entries()
}

#[tauri::command]
fn clear_history(state: tauri::State<AppState>) {
    state.history.clear();
}

/// Turns address-bar text into a URL using the engine from settings, so the
/// chrome never has to know about search-engine configuration.
#[tauri::command]
fn normalize_url(input: String, state: tauri::State<AppState>) -> String {
    let engine_id = state.settings.lock().unwrap().search_engine.clone();
    url_helper::normalize(&input, search::resolve(&engine_id).action_url)
}

#[tauri::command]
fn tab_create(
    app: tauri::AppHandle,
    url: Option<String>,
    state: tauri::State<AppState>,
) -> Result<TabHandle, String> {
    let label = {
        let mut next = state.next_tab_id.lock().unwrap();
        *next += 1;
        format!("tab-{}", *next)
    };

    let window = app.get_window("main").ok_or("main window is gone")?;
    let size = window.inner_size().map_err(|e| e.to_string())?;
    let scale = window.scale_factor().unwrap_or(1.0);
    let logical = size.to_logical::<f64>(scale);

    let target = url.unwrap_or_default();
    let webview_url = if target.is_empty() {
        WebviewUrl::App("newtab.html".into())
    } else {
        WebviewUrl::External(target.parse().map_err(|_| "invalid URL")?)
    };

    let builder = WebviewBuilder::new(&label, webview_url)
        .initialization_script(include_str!("../../ui/inject.js"))
        .on_navigation(navigation_guard(app.clone(), label.clone()));

    window
        .add_child(
            builder,
            LogicalPosition::new(0.0, CHROME_HEIGHT),
            LogicalSize::new(logical.width, (logical.height - CHROME_HEIGHT).max(0.0)),
        )
        .map_err(|e| e.to_string())?;

    state
        .tab_urls
        .lock()
        .unwrap()
        .insert(label.clone(), String::new());
    *state.active_tab.lock().unwrap() = Some(label.clone());

    Ok(TabHandle { label, url: String::new() })
}

#[tauri::command]
fn tab_navigate(
    app: tauri::AppHandle,
    label: String,
    input: String,
    state: tauri::State<AppState>,
) -> Result<String, String> {
    let engine_id = state.settings.lock().unwrap().search_engine.clone();
    let url = url_helper::normalize(&input, search::resolve(&engine_id).action_url);
    if url.is_empty() {
        return Ok(String::new());
    }

    let webview = app.get_webview(&label).ok_or("unknown tab")?;
    webview
        .navigate(url.parse().map_err(|_| "invalid URL")?)
        .map_err(|e| e.to_string())?;
    Ok(url)
}

#[tauri::command]
fn tab_close(app: tauri::AppHandle, label: String, state: tauri::State<AppState>) {
    if let Some(webview) = app.get_webview(&label) {
        let _ = webview.close();
    }
    state.tab_urls.lock().unwrap().remove(&label);
    let mut active = state.active_tab.lock().unwrap();
    if active.as_deref() == Some(label.as_str()) {
        *active = None;
    }
}

/// Shows one tab and hides the rest. Tauri has no per-webview visibility flag
/// on Linux, so inactive tabs are parked at zero size — they keep running
/// (audio, timers) exactly like a background tab should.
#[tauri::command]
fn tab_activate(
    app: tauri::AppHandle,
    label: String,
    state: tauri::State<AppState>,
) -> Result<(), String> {
    let window = app.get_window("main").ok_or("main window is gone")?;
    let scale = window.scale_factor().unwrap_or(1.0);
    let logical = window.inner_size().map_err(|e| e.to_string())?.to_logical::<f64>(scale);

    let labels: Vec<String> = state.tab_urls.lock().unwrap().keys().cloned().collect();
    for other in labels {
        let Some(webview) = app.get_webview(&other) else { continue };
        if other == label {
            let _ = webview.set_position(LogicalPosition::new(0.0, CHROME_HEIGHT));
            let _ = webview.set_size(LogicalSize::new(
                logical.width,
                (logical.height - CHROME_HEIGHT).max(0.0),
            ));
        } else {
            let _ = webview.set_size(LogicalSize::new(0.0, 0.0));
        }
    }

    *state.active_tab.lock().unwrap() = Some(label);
    Ok(())
}

#[tauri::command]
fn tab_history_go(app: tauri::AppHandle, label: String, delta: i32) {
    // `history.go` is used rather than a native back/forward call so the same
    // code path works for both directions and needs no webview capability.
    if let Some(webview) = app.get_webview(&label) {
        let _ = webview.eval(&format!("history.go({delta})"));
    }
}

#[tauri::command]
fn tab_reload(app: tauri::AppHandle, label: String) {
    if let Some(webview) = app.get_webview(&label) {
        let _ = webview.eval("location.reload()");
    }
}

/// Called from the warning page's "Continuer quand même" link: whitelists the
/// host for the rest of the session, then retries the navigation that was
/// refused. Scoped to the host, so allowing one page does not unblock the
/// whole list.
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

    // Retry in whichever tab is showing the warning.
    let active = state.active_tab.lock().unwrap().clone();
    if let Some(label) = active {
        if let Some(webview) = app.get_webview(&label) {
            webview.navigate(parsed).map_err(|e| e.to_string())?;
        }
    }
    Ok(())
}

// ------------------------------------------------------------ nav guard

/// Builds the per-tab navigation filter: phishing and ad/tracker domains are
/// refused before the request leaves the browser, and the chrome is told so it
/// can show the warning page.
fn navigation_guard(
    app: tauri::AppHandle,
    label: String,
) -> impl Fn(&tauri::Url) -> bool + Send + Sync + 'static {
    move |url: &tauri::Url| {
        let Some(state) = app.try_state::<AppState>() else {
            return true;
        };

        let host = url.host_str().unwrap_or("").to_ascii_lowercase();
        // An explicit user override wins over both lists.
        if !host.is_empty() && state.bypass.lock().unwrap().contains(&host) {
            return true;
        }

        let settings = state.settings.lock().unwrap();
        let phishing_on = settings.is_phishing_protection_enabled;
        let adblock_on = settings.is_ad_block_enabled;
        drop(settings);

        if phishing_on && !host.is_empty() {
            if state.lists.phishing.read().map(|s| s.matches(&host)).unwrap_or(false) {
                let _ = app.emit_to(
                    "chrome",
                    "navigation-blocked",
                    serde_json::json!({ "tab": label, "url": url.as_str(), "reason": "phishing" }),
                );
                return false;
            }
        }

        // Only top-level navigations reach this hook, so an ad domain here means
        // the user is being sent to one, not merely that a page embeds it.
        if adblock_on && !host.is_empty() {
            if state.lists.ads.read().map(|s| s.matches(&host)).unwrap_or(false) {
                let _ = app.emit_to(
                    "chrome",
                    "navigation-blocked",
                    serde_json::json!({ "tab": label, "url": url.as_str(), "reason": "ad" }),
                );
                return false;
            }
        }

        state
            .tab_urls
            .lock()
            .unwrap()
            .insert(label.clone(), url.as_str().to_string());
        state.history.record(url.as_str(), "");

        let _ = app.emit_to(
            "chrome",
            "tab-navigated",
            serde_json::json!({ "tab": label, "url": url.as_str() }),
        );
        true
    }
}

// ----------------------------------------------------------------- main

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_opener::init())
        .invoke_handler(tauri::generate_handler![
            get_settings,
            save_settings,
            get_search_engines,
            get_history,
            clear_history,
            normalize_url,
            tab_create,
            tab_navigate,
            tab_close,
            tab_activate,
            tab_history_go,
            tab_reload,
            allow_once,
        ])
        .setup(|app| {
            let resource_dir = app
                .path()
                .resource_dir()
                .map(|d| d.join("resources"))
                .unwrap_or_else(|_| std::path::PathBuf::from("resources"));

            let loaded = settings::load();
            let (w, h) = (loaded.window_width, loaded.window_height);

            app.manage(AppState {
                settings: Mutex::new(loaded),
                lists: Blocklists::load(&resource_dir),
                history: History::load(),
                tab_urls: Mutex::new(HashMap::new()),
                active_tab: Mutex::new(None),
                next_tab_id: Mutex::new(0),
                bypass: Mutex::new(HashSet::new()),
            });

            let window = tauri::window::WindowBuilder::new(app, "main")
                .title("Navigatueur")
                .inner_size(w, h)
                .min_inner_size(640.0, 480.0)
                .build()?;

            // The chrome is itself a webview pinned to the top of the window —
            // the Linux equivalent of the Windows build's ToolbarWindow, minus
            // the layered-window tricks that only existed to dodge WebView2's
            // airspace problem.
            window.add_child(
                WebviewBuilder::new("chrome", WebviewUrl::App("index.html".into())),
                LogicalPosition::new(0.0, 0.0),
                LogicalSize::new(w, CHROME_HEIGHT),
            )?;

            // Keep chrome and the active tab glued to the window as it resizes.
            let resize_handle = window.clone();
            let app_handle = app.handle().clone();
            window.on_window_event(move |event| {
                if let WindowEvent::Resized(_) = event {
                    let scale = resize_handle.scale_factor().unwrap_or(1.0);
                    let Ok(size) = resize_handle.inner_size() else { return };
                    let logical = size.to_logical::<f64>(scale);

                    if let Some(chrome) = app_handle.get_webview("chrome") {
                        let _ = chrome.set_size(LogicalSize::new(logical.width, CHROME_HEIGHT));
                    }
                    let Some(state) = app_handle.try_state::<AppState>() else { return };
                    let active = state.active_tab.lock().unwrap().clone();
                    if let Some(label) = active {
                        if let Some(webview) = app_handle.get_webview(&label) {
                            let _ = webview.set_size(LogicalSize::new(
                                logical.width,
                                (logical.height - CHROME_HEIGHT).max(0.0),
                            ));
                        }
                    }
                }
            });

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running Navigatueur");
}
