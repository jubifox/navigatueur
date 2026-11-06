"use strict";
// The Windows app's internal pages (new tab, Settings, history) run here
// unchanged: this answers their "nv:*" messages the way the Windows app's
// InternalPageBridge does, from this app's settings.
const fs = require("fs");
const path = require("path");
const { app, dialog, shell } = require("electron");
const url = require("../core/url");
const shortcuts = require("../core/shortcuts");

class Bridge {
  constructor(nav) {
    this.nav = nav; // the Navigatueur app object
  }

  get store() {
    return this.nav.store;
  }

  backgroundUrl(file) {
    if (!file) return null;
    const full = path.join(this.store.backgroundsDir, file);
    if (!fs.existsSync(full)) return null;
    return `navigatueur://userbg/${encodeURIComponent(file)}?v=${fs.statSync(full).mtimeMs | 0}`;
  }

  buildState(isPrivate) {
    const s = this.store.settings;
    const ui = s.interface;
    const behavior = (auto) => (ui.mode === "Classic" ? false : ui.mode === "Custom" ? auto : true);
    const map = this.nav.shortcutMap;
    const update = this.nav.updateInfo();
    return {
      type: "nv:state",
      isPrivate,
      platform: "linux",
      theme: {
        mode: s.themeMode === "Light" ? "Light" : "Dark",
        accent: isPrivate ? "#8B5CF6" : s.accentColorHex,
        newTabBackground: this.backgroundUrl(s.newTabBackground),
        hasChromeBackground: !!s.chromeBackground,
        chromeFollowsNewTab: s.chromeUsesNewTabBackground,
        cursorTrail: s.cursorTrail,
        forceDark: s.isForceDarkEnabled,
        forceDarkSites: Object.keys(s.forceDarkSites).length,
      },
      interface: {
        mode: ui.mode,
        toolbarAutoHide: behavior(ui.toolbarAutoHide),
        sidebarAutoHide: behavior(ui.sidebarAutoHide),
        sidebarSide: ui.sidebarSide,
        newTabButton: ui.newTabButtonPlacement,
        sidebarWidth: ui.sidebarWidth,
        edgeReveal: ui.isEdgeRevealEnabled,
        revealDelay: ui.revealDelayMilliseconds,
        hideDelay: ui.hideDelayMilliseconds,
        triggerSize: ui.edgeTriggerSize,
        sidebarTriggerSize: ui.sidebarEdgeTriggerSize,
        animations: ui.areAnimationsEnabled,
        panelEffects: ui.arePanelEffectsEnabled,
        tabPreviews: ui.areTabPreviewsEnabled,
      },
      navigation: {
        startup: s.startupBehavior,
        homePage: s.homePageUrl === url.NEW_TAB_URL ? "" : s.homePageUrl,
        searchEngine: url.engine(s.searchEngine).id,
        searchAction: url.engine(s.searchEngine).action,
        engines: url.SEARCH_ENGINES.map((e) => ({ id: e.id, name: e.name })),
        adBlock: s.isAdBlockEnabled,
        phishing: s.isPhishingProtectionEnabled,
        suspendMinutes: s.tabSuspendMinutes,
        maxLiveTabs: s.maxLiveTabs,
        autoPip: s.isAutoPictureInPictureEnabled,
        rememberedPermissions: this.store.permissionsFile.value.length,
      },
      shortcuts: shortcuts.CATALOG.map((d) => ({
        id: d.id,
        label: d.label,
        category: d.category,
        gestures: (map.get(d.id) || []).map(shortcuts.toDisplay),
        isDefault: s.shortcutOverrides[d.id] === undefined,
      })),
      fixedShortcuts: [
        { label: "Aller à l'onglet 1 à 8", gesture: "Ctrl + 1…8" },
        { label: "Aller au dernier onglet", gesture: "Ctrl + 9" },
        { label: "Rechercher dans la page", gesture: "Ctrl + F" },
        { label: "Zoom", gesture: "Ctrl + molette, Ctrl + +/−/0" },
        { label: "Quitter la barre d'adresse", gesture: "Échap" },
      ],
      bookmarks: this.store.bookmarks.slice(-12).reverse().map((b) => ({ url: b.url, title: b.title, host: url.displayHost(b.url) || b.url })),
      about: {
        version: app.getVersion(),
        updateAvailable: update.available,
        latestVersion: update.latestVersion,
        engineVersion: `Chromium ${process.versions.chrome}`,
        engineBundled: true,
      },
    };
  }

  post(wc, message) {
    if (wc && !wc.isDestroyed()) wc.send("nv", message);
  }

  /** One message from an internal page; [wc] is the page, [win]/[tab] where it lives. */
  async handle(wc, message, win, tab) {
    if (!message || typeof message.type !== "string") return;
    const isPrivate = !!(win && win.isPrivate);
    switch (message.type) {
      case "nv:hello":
        this.post(wc, this.buildState(isPrivate));
        break;
      case "nv:set":
        this.applySetting(message.key, message.value);
        break;
      case "nv:action":
        await this.runAction(message.name, wc, win, isPrivate);
        break;
      case "nv:open":
        if (win && typeof message.url === "string" && /^(https?|navigatueur):/i.test(message.url)) {
          if (message.background) win.newTab(message.url, { select: false, parent: tab });
          else if (tab) tab.load(message.url);
        }
        break;
      case "nv:navigate":
        if (tab && message.input) {
          const target = url.normalize(message.input, url.engine(this.store.settings.searchEngine).prefix);
          if (target) tab.load(target);
        }
        break;
      case "nv:history.query":
        this.post(wc, this.history(message.query, Math.min(2000, Math.max(1, message.limit | 0 || 300))));
        break;
      case "nv:history.delete":
        this.store.removeHistory(message.url, message.visitedAt);
        break;
      case "nv:bookmark.remove":
        this.store.removeBookmark(message.url);
        break;
      case "nv:shortcut.capture":
        this.nav.capturingShortcut = !!message.active;
        break;
      case "nv:shortcut.set": {
        const gesture = shortcuts.parse(message.gesture);
        const error = gesture ? shortcuts.conflict(message.id, gesture, this.nav.shortcutMap) : "Raccourci invalide.";
        if (!error) this.store.updateSettings((s) => (s.shortcutOverrides[message.id] = shortcuts.toText(gesture)));
        this.post(wc, { type: "nv:shortcut.result", id: message.id, ok: !error, error });
        break;
      }
      case "nv:shortcut.reset":
        this.store.updateSettings((s) => delete s.shortcutOverrides[message.id]);
        break;
      case "nv:shortcut.disable":
        this.store.updateSettings((s) => (s.shortcutOverrides[message.id] = ""));
        break;
    }
  }

  history(query, limit) {
    return {
      type: "nv:history",
      query,
      entries: this.store.searchHistory(query, limit).map((e) => ({
        url: e.url,
        title: e.title,
        host: url.displayHost(e.url) || e.url,
        visitedAt: new Date(e.visitedAt).toISOString(),
      })),
    };
  }

  applySetting(key, value) {
    const asBool = typeof value === "boolean" ? value : null;
    const asNumber = typeof value === "number" && Number.isFinite(value) ? value : null;
    const asString = typeof value === "string" ? value : null;
    const store = this.store;
    const ui = (change) => store.updateSettings((s) => change(s.interface));
    switch (key) {
      case "theme.mode":
        if (asString) store.updateSettings((s) => (s.themeMode = asString === "Light" ? "Light" : "Dark"));
        break;
      case "theme.accent":
        if (asString && /^#[0-9a-f]{6}$/i.test(asString)) store.updateSettings((s) => (s.accentColorHex = asString.toUpperCase()));
        break;
      case "appearance.cursorTrail":
        if (asBool !== null) store.updateSettings((s) => (s.cursorTrail = asBool));
        break;
      case "appearance.chromeFollowsNewTab":
        if (asBool !== null) store.updateSettings((s) => (s.chromeUsesNewTabBackground = asBool));
        break;
      case "appearance.forceDark":
        if (asBool !== null) this.nav.forceDark.setEnabledByDefault(asBool);
        break;
      case "interface.mode":
        if (["Immersive", "Classic", "Custom"].includes(asString)) ui((i) => (i.mode = asString));
        break;
      case "interface.toolbarAutoHide":
      case "interface.sidebarAutoHide":
        if (asBool !== null) {
          ui((i) => {
            const toolbar = key === "interface.toolbarAutoHide" ? asBool : i.mode === "Classic" ? false : i.mode === "Custom" ? i.toolbarAutoHide : true;
            const sidebar = key === "interface.sidebarAutoHide" ? asBool : i.mode === "Classic" ? false : i.mode === "Custom" ? i.sidebarAutoHide : true;
            i.toolbarAutoHide = toolbar;
            i.sidebarAutoHide = sidebar;
            i.mode = toolbar && sidebar ? "Immersive" : !toolbar && !sidebar ? "Classic" : "Custom";
          });
        }
        break;
      case "interface.sidebarSide":
        if (["Left", "Right"].includes(asString)) ui((i) => (i.sidebarSide = asString));
        break;
      case "interface.newTabButton":
        if (["Bottom", "AfterTabs"].includes(asString)) ui((i) => (i.newTabButtonPlacement = asString));
        break;
      case "interface.sidebarWidth":
        if (asNumber !== null) ui((i) => (i.sidebarWidth = Math.min(440, Math.max(200, asNumber))));
        break;
      case "interface.edgeReveal":
        if (asBool !== null) ui((i) => (i.isEdgeRevealEnabled = asBool));
        break;
      case "interface.revealDelay":
        if (asNumber !== null) ui((i) => (i.revealDelayMilliseconds = Math.min(1500, Math.max(0, asNumber))));
        break;
      case "interface.hideDelay":
        if (asNumber !== null) ui((i) => (i.hideDelayMilliseconds = Math.min(5000, Math.max(0, asNumber))));
        break;
      case "interface.triggerSize":
        if (asNumber !== null) ui((i) => (i.edgeTriggerSize = Math.min(40, Math.max(1, asNumber))));
        break;
      case "interface.sidebarTriggerSize":
        if (asNumber !== null) ui((i) => (i.sidebarEdgeTriggerSize = Math.min(40, Math.max(1, asNumber))));
        break;
      case "interface.animations":
        if (asBool !== null) ui((i) => (i.areAnimationsEnabled = asBool));
        break;
      case "interface.panelEffects":
        if (asBool !== null) ui((i) => (i.arePanelEffectsEnabled = asBool));
        break;
      case "interface.tabPreviews":
        if (asBool !== null) ui((i) => (i.areTabPreviewsEnabled = asBool));
        break;
      case "startup.behavior":
        if (["RestoreSession", "HomePage"].includes(asString)) store.updateSettings((s) => (s.startupBehavior = asString));
        break;
      case "startup.homePage":
        if (asString !== null) {
          const home = asString.trim() ? url.normalize(asString, url.engine(store.settings.searchEngine).prefix) : url.NEW_TAB_URL;
          store.updateSettings((s) => (s.homePageUrl = home));
        }
        break;
      case "search.engine":
        if (url.SEARCH_ENGINES.some((e) => e.id === asString)) store.updateSettings((s) => (s.searchEngine = asString));
        break;
      case "privacy.adBlock":
        if (asBool !== null) {
          store.updateSettings((s) => (s.isAdBlockEnabled = asBool));
          this.nav.protection.refresh();
        }
        break;
      case "privacy.phishing":
        if (asBool !== null) store.updateSettings((s) => (s.isPhishingProtectionEnabled = asBool));
        break;
      case "perf.suspendMinutes":
        if (asNumber !== null) store.updateSettings((s) => (s.tabSuspendMinutes = Math.min(1440, Math.max(0, asNumber | 0))));
        break;
      case "perf.maxLiveTabs":
        if (asNumber !== null) store.updateSettings((s) => (s.maxLiveTabs = Math.min(20, Math.max(1, asNumber | 0))));
        break;
      case "media.autoPip":
        if (asBool !== null) store.updateSettings((s) => (s.isAutoPictureInPictureEnabled = asBool));
        break;
    }
  }

  async runAction(name, wc, win, isPrivate) {
    const store = this.store;
    switch (name) {
      case "background.newtab.choose":
      case "background.chrome.choose": {
        const owner = win ? win.win : undefined;
        const result = await dialog.showOpenDialog(owner, { filters: [{ name: "Images", extensions: ["jpg", "jpeg", "png", "bmp", "webp"] }], properties: ["openFile"] });
        if (result.canceled || !result.filePaths[0]) return;
        fs.mkdirSync(store.backgroundsDir, { recursive: true });
        const kind = name === "background.newtab.choose" ? "newtab" : "chrome";
        const target = `${kind}-${Date.now()}${path.extname(result.filePaths[0]).toLowerCase()}`;
        for (const f of fs.readdirSync(store.backgroundsDir)) if (f.startsWith(kind + "-")) fs.rmSync(path.join(store.backgroundsDir, f), { force: true });
        fs.copyFileSync(result.filePaths[0], path.join(store.backgroundsDir, target));
        store.updateSettings((s) => {
          if (kind === "newtab") s.newTabBackground = target;
          else {
            s.chromeBackground = target;
            s.chromeUsesNewTabBackground = false;
          }
        });
        break;
      }
      case "background.newtab.reset":
        store.updateSettings((s) => (s.newTabBackground = null));
        break;
      case "background.chrome.reset":
        store.updateSettings((s) => (s.chromeBackground = null));
        break;
      case "update.check":
        await this.nav.checkForUpdate(false);
        this.post(wc, { type: "nv:update.checked", available: this.nav.updateInfo().available });
        break;
      case "update.install":
        this.nav.installUpdate();
        break;
      case "history.clear":
        store.clearHistory();
        this.post(wc, this.history(null, 300));
        break;
      case "permissions.clear":
        store.clearPermissions();
        break;
      case "forceDark.reset":
        this.nav.forceDark.clear();
        break;
      case "shortcuts.resetAll":
        store.updateSettings((s) => (s.shortcutOverrides = {}));
        break;
      case "data.openFolder":
        shell.openPath(store.dir);
        break;
    }
    this.nav.invalidatePages();
  }
}

module.exports = { Bridge };
