"use strict";
// Navigatueur for Linux: the Windows app's interface and pages on Chromium
// (Electron). Everything app-wide lives here; each window is a NavWindow.
const path = require("path");
const fs = require("fs");
const crypto = require("crypto");
const { pathToFileURL } = require("url");
const { app, protocol, net, ipcMain, dialog, session: electronSession, BrowserWindow, nativeTheme, webContents: allWebContents } = require("electron");
const { Store } = require("./store");
const { NavWindow } = require("./window");
const { Bridge } = require("./bridge");
const { Protection, ForceDark } = require("./protection");
const shortcuts = require("../core/shortcuts");
const urlCore = require("../core/url");

app.setName("Navigatueur");
// Its own data folder when asked (tests), and never the Windows app's (%APPDATA%/Navigatueur) when run from source on Windows.
if (process.env.NAVIGATUEUR_USER_DATA) app.setPath("userData", process.env.NAVIGATUEUR_USER_DATA);
else if (process.platform === "win32") app.setPath("userData", path.join(app.getPath("appData"), "Navigatueur-Linux-dev"));
// Wayland when the desktop runs it, X11 otherwise.
app.commandLine.appendSwitch("ozone-platform-hint", "auto");
app.commandLine.appendSwitch("enable-features", "WaylandWindowDecorations,VaapiVideoDecodeLinuxGL");

protocol.registerSchemesAsPrivileged([
  { scheme: "navigatueur", privileges: { standard: true, secure: true, supportFetchAPI: true, stream: true } },
]);

const resourcesDir = app.isPackaged ? process.resourcesPath : path.join(__dirname, "..", "..", "resources");
const windowsResources = app.isPackaged ? process.resourcesPath : path.join(__dirname, "..", "..", "..", "src", "Navigatueur.App", "Resources");

class Navigatueur {
  constructor() {
    this.windows = new Set();
    this.resourcesDir = resourcesDir;
    this.downloadItems = new Map();
    this.downloads = [];
    this.capturingShortcut = false;
    this.update = { available: false, latestVersion: null, downloading: false, ready: false };
  }

  start() {
    this.store = new Store();
    this.forceDark = new ForceDark(this.store);
    this.protection = new Protection(this.store, windowsResources);
    this.bridge = new Bridge(this);
    this.shortcutMap = shortcuts.buildMap(this.store.settings.shortcutOverrides);
    nativeTheme.themeSource = this.store.settings.themeMode === "Light" ? "light" : "dark";

    this.store.on("settings", () => this.onSettingsChanged());
    this.store.on("forcedark", () => this.forEachTab((tab) => tab.applyForceDark(tab.url)) || this.invalidatePages());
    this.store.on("bookmarks", () => this.invalidatePages(true));
    this.store.on("history", () => this.invalidatePages());

    this.protection.start();
    this.setUpIpc();

    const first = new NavWindow(this, { restore: this.store.settings.startupBehavior !== "HomePage", persists: true });
    this.windows.add(first);
    this.primary = first;
    const startUrl = process.argv.slice(app.isPackaged ? 1 : 2).find((a) => /^(https?|file):/i.test(a));
    if (startUrl) first.newTab(startUrl);

    this.setUpUpdater();
  }

  // ------------------------------------------------------------ sessions

  setUpSession(ses, isPrivate) {
    if (ses.__navigatueur) return;
    ses.__navigatueur = true;

    // Chromium's own user agent, without the "Electron" and app tokens some sites react to.
    ses.setUserAgent(ses.getUserAgent().replace(/\s(Electron|navigatueur|Navigatueur)\/\S+/g, ""));

    ses.protocol.handle("navigatueur", (request) => {
      const u = new URL(request.url);
      const roots = {
        home: path.join(windowsResources, "Pages"),
        warning: path.join(windowsResources, "Warning"),
        userbg: this.store.backgroundsDir,
      };
      const root = roots[u.hostname];
      if (!root) return new Response("Introuvable", { status: 404 });
      const file = path.normalize(path.join(root, decodeURIComponent(u.pathname)));
      if (!file.startsWith(root) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) return new Response("Introuvable", { status: 404 });
      return net.fetch(pathToFileURL(file).toString());
    });

    ses.setPermissionRequestHandler((wc, permission, callback, details) => this.askPermission(wc, permission, callback, details, isPrivate));
    ses.setPermissionCheckHandler((wc, permission, origin) => {
      if (["fullscreen", "pointerLock", "clipboard-sanitized-write", "media-key-system", "speaker-selection"].includes(permission)) return true;
      const kind = this.permissionKind(permission);
      return kind ? this.store.permission(origin, kind) === true : false;
    });
    ses.on("will-download", (_e, item, wc) => this.trackDownload(item, wc));
    this.protection.attach(ses);
  }

  permissionKind(permission, details = {}) {
    if (permission === "media") {
      const types = details.mediaTypes || [];
      if (types.includes("video") && types.includes("audio")) return "camera+microphone";
      return types.includes("video") ? "camera" : "microphone";
    }
    return { geolocation: "geolocation", notifications: "notifications", "clipboard-read": "clipboard-read", midi: "midi", midiSysex: "midi", "display-capture": "screen", "idle-detection": "idle" }[permission] || null;
  }

  async askPermission(wc, permission, callback, details, isPrivate) {
    if (["fullscreen", "pointerLock", "clipboard-sanitized-write", "media-key-system", "speaker-selection", "keyboardLock"].includes(permission)) return callback(true);
    const kind = this.permissionKind(permission, details);
    if (!kind) return callback(false);
    let origin = details.requestingUrl || "";
    try {
      origin = new URL(origin).origin;
    } catch {}
    const remembered = isPrivate ? null : this.store.permission(origin, kind);
    if (remembered !== null) return callback(remembered);
    const what = {
      geolocation: "connaître votre position",
      notifications: "vous envoyer des notifications",
      "clipboard-read": "lire le presse-papiers",
      camera: "utiliser la caméra",
      microphone: "utiliser le micro",
      "camera+microphone": "utiliser la caméra et le micro",
      midi: "accéder à vos appareils MIDI",
      screen: "partager votre écran",
      idle: "savoir quand vous êtes inactif",
    }[kind];
    const owner = [...this.windows].find((w) => w.tabForWebContents(wc));
    const { response, checkboxChecked } = await dialog.showMessageBox(owner ? owner.win : undefined, {
      type: "question",
      message: `${urlCore.displayHost(origin) || origin} demande à ${what}`,
      buttons: ["Bloquer", "Autoriser"],
      defaultId: 1,
      cancelId: 0,
      checkboxLabel: "Se souvenir de ce choix",
      checkboxChecked: !isPrivate,
    });
    const allowed = response === 1;
    if (checkboxChecked && !isPrivate) this.store.rememberPermission(origin, kind, allowed);
    callback(allowed);
  }

  // ----------------------------------------------------------- downloads

  trackDownload(item, wc) {
    const id = crypto.randomUUID();
    const dir = app.getPath("downloads");
    let name = item.getFilename();
    let target = path.join(dir, name);
    for (let n = 1; fs.existsSync(target); n++) {
      const ext = path.extname(name);
      target = path.join(dir, `${path.basename(name, ext)} (${n})${ext}`);
    }
    item.setSavePath(target);
    const record = { id, name: path.basename(target), path: target, url: item.getURL(), received: 0, total: item.getTotalBytes(), state: "progressing", startedAt: Date.now() };
    this.downloads.unshift(record);
    this.downloads = this.downloads.slice(0, 50);
    this.downloadItems.set(id, item);
    const owner = [...this.windows].find((w) => w.tabForWebContents(wc)) || this.focusedWindow();
    const refresh = () => {
      for (const w of this.windows) {
        w.downloads = this.downloads;
        w.pushStateSoon();
      }
    };
    item.on("updated", (_e, state) => {
      record.received = item.getReceivedBytes();
      record.total = item.getTotalBytes();
      record.state = state === "interrupted" ? "interrupted" : "progressing";
      refresh();
    });
    item.once("done", (_e, state) => {
      record.state = state;
      record.received = item.getReceivedBytes();
      this.downloadItems.delete(id);
      refresh();
      if (owner) owner.notifyDownload(record);
    });
    refresh();
    if (owner) owner.sendToToolbar("download-started", record);
  }

  cancelDownload(id) {
    const item = this.downloadItems.get(id);
    if (item) item.cancel();
  }

  // ---------------------------------------------------------------- IPC

  windowForSender(sender) {
    for (const w of this.windows) {
      if (w.toolbar.webContents === sender || w.sidebar.webContents === sender) return w;
    }
    return null;
  }

  pageOwner(sender) {
    for (const w of this.windows) {
      const tab = w.tabForWebContents(sender);
      if (tab) return { win: w, tab };
    }
    return { win: null, tab: null };
  }

  setUpIpc() {
    ipcMain.on("shell", (event, type, data) => {
      const w = this.windowForSender(event.sender);
      if (w) w.onShell(type, data, event.sender);
    });
    ipcMain.handle("shell-invoke", (event, type, data) => {
      const w = this.windowForSender(event.sender);
      if (!w) return null;
      if (type === "suggest") return w.suggest(String(data || ""));
      return null;
    });
    ipcMain.on("nv", (event, message) => {
      const url = event.sender.getURL();
      if (!urlCore.isInternal(url)) return; // Only Navigatueur's own pages may talk to the app.
      const { win, tab } = this.pageOwner(event.sender);
      this.bridge.handle(event.sender, message, win, tab);
    });
    ipcMain.handle("page-config", () => ({ trail: this.store.settings.cursorTrail, accent: this.store.settings.accentColorHex }));
  }

  invalidatePages() {
    for (const w of this.windows) {
      for (const tab of w.tabs) {
        if (tab.wc && urlCore.isInternal(tab.url)) this.bridge.post(tab.wc, this.bridge.buildState(w.isPrivate));
      }
    }
  }

  onSettingsChanged() {
    this.shortcutMap = shortcuts.buildMap(this.store.settings.shortcutOverrides);
    nativeTheme.themeSource = this.store.settings.themeMode === "Light" ? "light" : "dark";
    this.protection.refresh();
    for (const w of this.windows) {
      w.layout();
      w.pushState();
      for (const tab of w.tabs) if (tab.wc) tab.wc.send("page-config", { trail: this.store.settings.cursorTrail, accent: this.store.settings.accentColorHex });
    }
    this.invalidatePages();
  }

  forEachTab(fn) {
    for (const w of this.windows) for (const tab of w.tabs) fn(tab, w);
  }

  chromeBackgroundUrl() {
    const s = this.store.settings;
    const file = s.chromeUsesNewTabBackground ? s.newTabBackground : s.chromeBackground;
    return this.bridge.backgroundUrl(file);
  }

  // ------------------------------------------------------------- windows

  focusedWindow() {
    return [...this.windows].find((w) => w.win.isFocused()) || this.primary || [...this.windows][0] || null;
  }

  openWindow(options) {
    const w = new NavWindow(this, { ...options, persists: false });
    this.windows.add(w);
    return w;
  }

  windowClosed(w) {
    this.windows.delete(w);
    if (w === this.primary) this.primary = [...this.windows].find((x) => !x.isPrivate) || null;
    if (!this.windows.size) app.quit();
  }

  // ------------------------------------------------------------- updates

  setUpUpdater() {
    // Updates replace the AppImage in place; a .deb install is updated by its own package.
    if (!process.env.APPIMAGE) return;
    try {
      const { autoUpdater } = require("electron-updater");
      this.autoUpdater = autoUpdater;
      autoUpdater.autoDownload = false;
      autoUpdater.on("update-available", (info) => {
        this.update = { ...this.update, available: true, latestVersion: info.version };
        this.refreshUpdateUi();
      });
      autoUpdater.on("update-downloaded", () => {
        this.update.ready = true;
        autoUpdater.quitAndInstall();
      });
      autoUpdater.on("error", () => {
        this.update.downloading = false;
        this.refreshUpdateUi();
      });
      setTimeout(() => this.checkForUpdate(true), 8000);
    } catch (e) {
      console.error("Mises à jour indisponibles", e);
    }
  }

  async checkForUpdate() {
    if (!this.autoUpdater) return;
    try {
      await this.autoUpdater.checkForUpdates();
    } catch {}
    this.store.updateSettings((s) => (s.lastUpdateCheck = Date.now()));
  }

  installUpdate() {
    if (!this.autoUpdater || !this.update.available || this.update.downloading) return;
    this.update.downloading = true;
    this.refreshUpdateUi();
    this.store.flushAll();
    this.autoUpdater.downloadUpdate().catch(() => {
      this.update.downloading = false;
      this.refreshUpdateUi();
    });
  }

  updateInfo() {
    return { ...this.update };
  }

  refreshUpdateUi() {
    for (const w of this.windows) w.pushState();
    this.invalidatePages();
  }
}

const nav = new Navigatueur();

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on("second-instance", (_e, argv) => {
    const w = nav.focusedWindow();
    if (!w) return;
    const link = argv.find((a) => /^(https?|file):/i.test(a));
    if (link) w.newTab(link);
    if (w.win.isMinimized()) w.win.restore();
    w.win.focus();
  });

  app.whenReady().then(() => nav.start());

  app.on("before-quit", () => {
    for (const w of nav.windows) if (w.persists) w.persist();
    if (nav.store) nav.store.flushAll();
  });

  app.on("window-all-closed", () => app.quit());

  // Links opened from extensions' and popups' own windows also get the shortcuts.
  app.on("web-contents-created", (_e, contents) => {
    if (contents.getType() === "window") {
      contents.setWindowOpenHandler((details) => {
        const w = nav.focusedWindow();
        if (w) w.newTab(details.url);
        return { action: "deny" };
      });
    }
  });
}

module.exports = { nav, BrowserWindow, allWebContents, electronSession };
