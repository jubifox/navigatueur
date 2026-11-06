"use strict";
// One tab: a Chromium page in a WebContentsView, created when the tab is
// first shown and destroyed when it is put to sleep (its address, title,
// icon, picture and back/forward history stay) — like the Windows app's tabs.
const path = require("path");
const crypto = require("crypto");
const { WebContentsView } = require("electron");
const { NEW_TAB_URL, isInternal, displayHost } = require("../core/url");
const { errorPage } = require("./error-page");

const PAGE_PRELOAD = path.join(__dirname, "..", "preload", "page.js");

// Same scripts as the Windows app's automatic picture-in-picture (BrowserTabViewModel).
const ENTER_PIP = `(async () => {
  if (!document.pictureInPictureEnabled) return 'unsupported';
  if (document.pictureInPictureElement) return 'already';
  let best = null, bestArea = 0;
  for (const v of document.querySelectorAll('video')) {
    if (v.paused || v.ended || v.readyState < 2 || v.muted || v.volume === 0 || v.disablePictureInPicture) continue;
    const r = v.getBoundingClientRect();
    const area = r.width * r.height;
    if (r.width < 200 || r.height < 110 || area <= bestArea) continue;
    best = v; bestArea = area;
  }
  if (!best) return 'none';
  await best.requestPictureInPicture();
  window.__navigatueurAutoPip = best;
  return 'ok';
})()`;
const WATCH_PIP = `new Promise(resolve => {
  const v = window.__navigatueurAutoPip;
  if (!v || document.pictureInPictureElement !== v) return resolve('gone');
  v.addEventListener('leavepictureinpicture', () => resolve('left'), { once: true });
})`;
const EXIT_PIP = `(() => {
  const v = window.__navigatueurAutoPip;
  window.__navigatueurAutoPip = null;
  if (v && document.pictureInPictureElement === v) document.exitPictureInPicture().catch(() => {});
})()`;

class Tab {
  constructor(window, init = {}) {
    this.window = window;
    this.id = init.id || crypto.randomUUID();
    this.url = init.url || NEW_TAB_URL;
    this.title = init.title || "";
    this.favicon = init.favicon || null;
    this.groupId = init.groupId || null;
    this.pinned = !!init.pinned;
    this.parentId = init.parentId || null;
    this.history = init.history || null; // { entries: [{url, title}], index }
    this.thumbnail = null;
    this.loading = false;
    this.canGoBack = false;
    this.canGoForward = false;
    this.audible = false;
    this.muted = false;
    this.crashed = false;
    this.inAutoPip = false;
    this.zoom = 1;
    this.lastActive = Date.now();
    this.view = null;
    this.forceDarkApplied = false;
    this.pendingOpener = init.webContents || null;
  }

  get wc() {
    return this.view && !this.view.webContents.isDestroyed() ? this.view.webContents : null;
  }

  get isLive() {
    return !!this.wc;
  }

  get displayTitle() {
    if (isInternal(this.url)) {
      if (this.url.includes("settings.html")) return "Paramètres";
      if (this.url.includes("history.html")) return "Historique";
      return "Nouvel onglet";
    }
    return this.title || displayHost(this.url) || this.url;
  }

  /** Creates the page (first show, or waking up from sleep). */
  ensureView(webContents) {
    if (this.wc) return this.view;
    const view = webContents
      ? new WebContentsView({ webContents })
      : new WebContentsView({
          webPreferences: {
            session: this.window.session,
            preload: PAGE_PRELOAD,
            contextIsolation: true,
            sandbox: true,
            spellcheck: true,
            autoplayPolicy: "document-user-activation-required",
          },
        });
    this.view = view;
    view.setBackgroundColor(this.window.palette().window);
    this.attach(view.webContents);
    if (!webContents) {
      if (this.history && this.history.entries && this.history.entries.length) {
        view.webContents.navigationHistory
          .restore({ entries: this.history.entries, index: this.history.index })
          .catch(() => view.webContents.loadURL(this.url).catch(() => {}));
      } else {
        view.webContents.loadURL(this.url).catch(() => {});
      }
    }
    return view;
  }

  attach(wc) {
    const win = this.window;
    const changed = () => win.tabChanged(this);
    this.forceDarkApplied = false;
    try {
      wc.debugger.attach("1.3");
    } catch {}
    this.applyForceDark(this.url);
    wc.setZoomFactor(this.zoom);
    wc.setAudioMuted(this.muted);

    wc.on("did-start-loading", () => {
      this.loading = true;
      this.crashed = false;
      changed();
    });
    wc.on("did-stop-loading", () => {
      this.loading = false;
      this.syncNavigation();
      changed();
      if (win.activeTab === this) setTimeout(() => win.capture(this), 900);
    });
    wc.on("did-start-navigation", (event) => {
      if (event.isMainFrame && !event.isSameDocument) this.applyForceDark(event.url);
    });
    wc.on("did-navigate", (_e, url) => {
      // The error page is shown in place of the address that failed: keep showing that address.
      if (url.startsWith("data:text/html;charset=utf-8,") && this.errorUrl) {
        this.url = this.errorUrl;
        this.syncNavigation();
        changed();
        return;
      }
      this.errorUrl = null;
      if (displayHost(url) !== displayHost(this.url)) this.favicon = null;
      this.url = url;
      this.syncNavigation();
      if (!win.isPrivate) win.app.store.recordVisit(url, wc.getTitle());
      changed();
    });
    wc.on("did-navigate-in-page", (_e, url, isMainFrame) => {
      if (!isMainFrame) return;
      this.url = url;
      this.syncNavigation();
      changed();
    });
    wc.on("page-title-updated", (_e, title) => {
      this.title = title;
      if (!win.isPrivate) win.app.store.updateTitle(this.url, title);
      changed();
    });
    wc.on("page-favicon-updated", (_e, favicons) => {
      this.favicon = (favicons || []).find((f) => /^(https?|data):/.test(f)) || null;
      changed();
    });
    wc.on("audio-state-changed", (event) => {
      this.audible = event.audible;
      changed();
    });
    wc.on("did-fail-load", (_e, code, description, url, isMainFrame) => {
      // -3 = aborted (a new navigation replaced it): not an error.
      if (!isMainFrame || code === -3) return;
      this.errorUrl = url;
      wc.loadURL(errorPage(url, code, description), { baseURLForDataURL: url }).catch(() => {});
    });
    wc.on("render-process-gone", (_e, details) => {
      if (details.reason === "clean-exit") return;
      this.crashed = true;
      this.loading = false;
      changed();
    });
    wc.on("enter-html-full-screen", () => win.setPageFullScreen(this, true));
    wc.on("leave-html-full-screen", () => win.setPageFullScreen(this, false));
    wc.on("found-in-page", (_e, result) => win.sendToToolbar("find-result", result));
    wc.on("context-menu", (_e, params) => win.pageContextMenu(this, params));
    wc.on("before-input-event", (event, input) => win.handleKey(event, input));
    wc.on("will-navigate", (event) => win.guardNavigation(this, event));
    wc.on("will-redirect", (event) => win.guardNavigation(this, event));
    wc.on("update-target-url", (_e, url) => win.sendToToolbar("hover-link", url));
    wc.setWindowOpenHandler((details) => win.openWindowRequest(this, details));
    wc.on("did-create-window", () => {});
  }

  syncNavigation() {
    const wc = this.wc;
    if (!wc) return;
    this.canGoBack = wc.navigationHistory.canGoBack();
    this.canGoForward = wc.navigationHistory.canGoForward();
  }

  /** The back/forward list, to bring the tab back after sleeping or a restart. */
  snapshotHistory() {
    const wc = this.wc;
    if (!wc) return this.history;
    try {
      const entries = wc.navigationHistory.getAllEntries().map((e) => ({ url: e.url, title: e.title }));
      if (entries.length) this.history = { entries: entries.slice(-50), index: Math.min(wc.navigationHistory.getActiveIndex(), 49) };
    } catch {}
    return this.history;
  }

  load(url) {
    this.url = url;
    if (this.wc) this.wc.loadURL(url).catch(() => {});
    else {
      this.history = null;
      this.window.tabChanged(this);
    }
  }

  /** Puts the tab to sleep: the page goes, its address and history stay. */
  suspend() {
    if (!this.wc) return;
    this.snapshotHistory();
    const view = this.view;
    this.view = null;
    this.loading = false;
    this.audible = false;
    try {
      view.webContents.debugger.detach();
    } catch {}
    view.webContents.close();
  }

  destroy() {
    if (!this.view) return;
    const view = this.view;
    this.view = null;
    if (!view.webContents.isDestroyed()) view.webContents.close();
  }

  // ---------------------------------------------------------- DevTools

  cdp(method, params) {
    const wc = this.wc;
    if (!wc || !wc.debugger.isAttached()) return Promise.resolve(null);
    return wc.debugger.sendCommand(method, params).catch(() => null);
  }

  async evaluate(expression, userGesture) {
    const result = await this.cdp("Runtime.evaluate", { expression, awaitPromise: true, userGesture, returnByValue: true });
    return result && result.result ? result.result.value : null;
  }

  /** Chromium's own auto dark mode for this page (Settings → Apparence), like the Windows app. */
  applyForceDark(url) {
    const wanted = this.window.app.forceDark.isEnabledFor(url, this.window.isPrivate);
    if (this.forceDarkApplied === wanted) return;
    this.forceDarkApplied = wanted;
    this.cdp("Emulation.setAutoDarkModeOverride", { enabled: wanted });
  }

  get isForceDark() {
    return this.forceDarkApplied;
  }

  // ----------------------------------------- automatic picture-in-picture

  async leftForAnotherTab() {
    if (!this.window.app.store.settings.isAutoPictureInPictureEnabled || !this.audible || !this.wc) return;
    const answer = await this.evaluate(ENTER_PIP, true);
    if (answer !== "ok") return;
    if (this.window.activeTab === this) {
      this.evaluate(EXIT_PIP, false);
      return;
    }
    this.inAutoPip = true;
    await this.evaluate(WATCH_PIP, false);
    this.inAutoPip = false;
  }

  returnedTo() {
    if (!this.inAutoPip) return;
    this.inAutoPip = false;
    this.evaluate(EXIT_PIP, false);
  }

  togglePictureInPicture() {
    this.evaluate(`(() => {
      window.__navigatueurAutoPip = null;
      if (document.pictureInPictureElement) return document.exitPictureInPicture();
      const v = [...document.querySelectorAll('video')].sort((a, b) => (b.clientWidth * b.clientHeight) - (a.clientWidth * a.clientHeight))[0];
      if (v) return v.requestPictureInPicture();
    })()`, true);
  }

  info() {
    return {
      id: this.id,
      url: this.url,
      title: this.displayTitle,
      favicon: this.favicon,
      groupId: this.groupId,
      pinned: this.pinned,
      loading: this.loading,
      audible: this.audible,
      muted: this.muted,
      suspended: !this.isLive,
      crashed: this.crashed,
      inAutoPip: this.inAutoPip,
      internal: isInternal(this.url),
      thumbnail: this.thumbnail,
    };
  }
}

module.exports = { Tab };
