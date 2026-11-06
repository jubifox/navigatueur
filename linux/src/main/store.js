"use strict";
// Saved data, as JSON files in the user's config folder (~/.config/Navigatueur):
// settings (same names as the Windows app's settings.json where the setting
// is the same), history, bookmarks, session, remembered permissions.
const fs = require("fs");
const path = require("path");
const { EventEmitter } = require("events");
const { app } = require("electron");
const { NEW_TAB_URL, normalizeKey, isWebUrl } = require("../core/url");

const DEFAULT_SETTINGS = {
  themeMode: "Dark",
  accentColorHex: "#4C8DFF",
  searchEngine: "Bing",
  homePageUrl: NEW_TAB_URL,
  startupBehavior: "RestoreSession",
  newTabBackground: null,
  chromeUsesNewTabBackground: true,
  chromeBackground: null,
  cursorTrail: true,
  isAdBlockEnabled: true,
  isPhishingProtectionEnabled: true,
  tabSuspendMinutes: 15,
  maxLiveTabs: 4,
  isForceDarkEnabled: false,
  forceDarkSites: {},
  isAutoPictureInPictureEnabled: true,
  shortcutOverrides: {},
  interface: {
    mode: "Immersive",
    toolbarAutoHide: true,
    sidebarAutoHide: true,
    sidebarSide: "Left",
    sidebarWidth: 280,
    newTabButtonPlacement: "Bottom",
    isEdgeRevealEnabled: true,
    revealDelayMilliseconds: 150,
    hideDelayMilliseconds: 600,
    edgeTriggerSize: 6,
    sidebarEdgeTriggerSize: 18,
    areAnimationsEnabled: true,
    arePanelEffectsEnabled: true,
    areTabPreviewsEnabled: true,
  },
  window: { width: 1280, height: 800, x: null, y: null, maximized: false },
  lastUpdateCheck: 0,
};

function merge(defaults, value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return structuredClone(defaults);
  const out = {};
  for (const key of Object.keys(defaults)) {
    const d = defaults[key];
    const v = value[key];
    if (d && typeof d === "object" && !Array.isArray(d) && Object.keys(d).length > 0) out[key] = merge(d, v);
    else if (v === undefined || (d !== null && typeof d !== typeof v && !(d === null))) out[key] = structuredClone(d);
    else out[key] = v;
  }
  return out;
}

class JsonFile {
  constructor(file, fallback, debounce = 400) {
    this.file = file;
    this.fallback = fallback;
    this.debounce = debounce;
    this.timer = null;
    this.value = this.read();
  }

  read() {
    try {
      return JSON.parse(fs.readFileSync(this.file, "utf8"));
    } catch {
      return structuredClone(this.fallback);
    }
  }

  save() {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.flush(), this.debounce);
  }

  flush() {
    clearTimeout(this.timer);
    try {
      fs.mkdirSync(path.dirname(this.file), { recursive: true });
      const temp = this.file + ".tmp";
      fs.writeFileSync(temp, JSON.stringify(this.value, null, 1));
      fs.renameSync(temp, this.file);
    } catch (e) {
      console.error("Écriture impossible", this.file, e);
    }
  }
}

class Store extends EventEmitter {
  constructor() {
    super();
    const dir = app.getPath("userData");
    this.dir = dir;
    this.backgroundsDir = path.join(dir, "Backgrounds");
    this.settingsFile = new JsonFile(path.join(dir, "settings.json"), {});
    this.settings = merge(DEFAULT_SETTINGS, this.settingsFile.value);
    this.settings.interface.sidebarWidth = Math.min(440, Math.max(200, Number(this.settings.interface.sidebarWidth) || 280));
    this.historyFile = new JsonFile(path.join(dir, "history.json"), [], 1500);
    this.bookmarksFile = new JsonFile(path.join(dir, "bookmarks.json"), []);
    this.sessionFile = new JsonFile(path.join(dir, "session.json"), { tabs: [], groups: [], activeTabId: null }, 1000);
    this.permissionsFile = new JsonFile(path.join(dir, "permissions.json"), []);
    if (!Array.isArray(this.historyFile.value)) this.historyFile.value = [];
    if (!Array.isArray(this.bookmarksFile.value)) this.bookmarksFile.value = [];
    if (!Array.isArray(this.permissionsFile.value)) this.permissionsFile.value = [];
  }

  // ------------------------------------------------------------ settings

  updateSettings(change) {
    change(this.settings);
    this.settingsFile.value = this.settings;
    this.settingsFile.save();
    this.emit("settings");
  }

  // ------------------------------------------------------------- history

  get history() {
    return this.historyFile.value;
  }

  recordVisit(url, title) {
    if (!isWebUrl(url)) return;
    const list = this.historyFile.value;
    const now = Date.now();
    const last = list[list.length - 1];
    if (last && last.url === url && now - last.visitedAt < 60000) {
      last.visitedAt = now;
      if (title) last.title = title;
    } else {
      list.push({ url, title: title || "", visitedAt: now });
    }
    if (list.length > 5000) list.splice(0, list.length - 5000);
    this.historyFile.save();
    this.emit("history");
  }

  updateTitle(url, title) {
    if (!title) return;
    const list = this.historyFile.value;
    for (let i = list.length - 1; i >= Math.max(0, list.length - 20); i--) {
      if (list[i].url === url) {
        if (list[i].title !== title) {
          list[i].title = title;
          this.historyFile.save();
        }
        return;
      }
    }
  }

  removeHistory(url, visitedAt) {
    const time = new Date(visitedAt).getTime();
    this.historyFile.value = this.historyFile.value.filter((e) => !(e.url === url && Math.abs(e.visitedAt - time) < 1000));
    this.historyFile.save();
    this.emit("history");
  }

  clearHistory() {
    this.historyFile.value = [];
    this.historyFile.save();
    this.emit("history");
  }

  searchHistory(query, limit) {
    const q = (query || "").trim().toLowerCase();
    const out = [];
    for (let i = this.history.length - 1; i >= 0 && out.length < limit; i--) {
      const e = this.history[i];
      if (!q || e.url.toLowerCase().includes(q) || (e.title || "").toLowerCase().includes(q)) out.push(e);
    }
    return out;
  }

  // ----------------------------------------------------------- bookmarks

  get bookmarks() {
    return this.bookmarksFile.value;
  }

  isBookmarked(url) {
    if (!url) return false;
    const key = normalizeKey(url);
    return this.bookmarks.some((b) => normalizeKey(b.url) === key);
  }

  toggleBookmark(url, title) {
    const key = normalizeKey(url);
    const index = this.bookmarks.findIndex((b) => normalizeKey(b.url) === key);
    if (index >= 0) this.bookmarks.splice(index, 1);
    else this.bookmarks.push({ url, title: title || url, addedAt: Date.now() });
    this.bookmarksFile.save();
    this.emit("bookmarks");
    return index < 0;
  }

  removeBookmark(url) {
    const key = normalizeKey(url);
    this.bookmarksFile.value = this.bookmarks.filter((b) => normalizeKey(b.url) !== key);
    this.bookmarksFile.save();
    this.emit("bookmarks");
  }

  // --------------------------------------------------------- permissions

  permission(origin, kind) {
    const found = this.permissionsFile.value.find((p) => p.origin === origin && p.kind === kind);
    return found ? found.allowed : null;
  }

  rememberPermission(origin, kind, allowed) {
    this.permissionsFile.value = this.permissionsFile.value.filter((p) => !(p.origin === origin && p.kind === kind));
    this.permissionsFile.value.push({ origin, kind, allowed });
    this.permissionsFile.save();
    this.emit("settings");
  }

  clearPermissions() {
    this.permissionsFile.value = [];
    this.permissionsFile.save();
    this.emit("settings");
  }

  // ------------------------------------------------------------- session

  get session() {
    return this.sessionFile.value;
  }

  saveSession(session) {
    this.sessionFile.value = session;
    this.sessionFile.save();
  }

  flushAll() {
    for (const f of [this.settingsFile, this.historyFile, this.bookmarksFile, this.sessionFile, this.permissionsFile]) {
      if (f.timer) f.flush();
    }
  }
}

module.exports = { Store, DEFAULT_SETTINGS };
