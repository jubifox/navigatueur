"use strict";
// One browser window: the page, and the Windows app's immersive shell over it
// — a toolbar along the top and a tab column on one side, each either docked
// (the page is laid out next to it) or hidden until the mouse reaches its
// edge (it then floats over the page).
const path = require("path");
const fs = require("fs");
const crypto = require("crypto");
const { BaseWindow, WebContentsView, Menu, clipboard, dialog, screen, session: electronSession, shell, Notification, nativeImage } = require("electron");
const { Tab } = require("./tab");
const url = require("../core/url");
const { rank } = require("../core/suggestions");
const shortcuts = require("../core/shortcuts");

const SHELL_PRELOAD = path.join(__dirname, "..", "preload", "shell.js");
const SHELL_DIR = path.join(__dirname, "..", "shell");
const TOOLBAR_HEIGHT = 48;

const DARK = { window: "#0C0D0F", panel: "#16171A", panelBorder: "#26272C", surface: "#1F2024", surfaceHover: "#25262B", surfaceActive: "#2C2D33", text: "#ECECEF", text2: "#A1A1AA", text3: "#6E6E78" };
const LIGHT = { window: "#E6E6EA", panel: "#FBFBFC", panelBorder: "#E1E1E5", surface: "#F1F1F4", surfaceHover: "#EAEAEE", surfaceActive: "#E1E1E7", text: "#18181B", text2: "#5F5F68", text3: "#8E8E96" };
const PRIVATE_DARK = { window: "#0E0B14", panel: "#1A1524", panelBorder: "#2E263D", surface: "#231C31", surfaceHover: "#2A223A", surfaceActive: "#332946" };
const PRIVATE_LIGHT = { window: "#E7E2F0", panel: "#F8F5FD", panelBorder: "#E0D8EE", surface: "#EFEAF8", surfaceHover: "#E8E1F4", surfaceActive: "#DFD6EF" };
const GROUP_COLORS = [["Bleu", "#4C8DFF"], ["Vert", "#4FE0A0"], ["Ambre", "#E0A52A"], ["Rouge", "#E04F4F"], ["Violet", "#B14FE0"], ["Cyan", "#4FD1E0"]];

class NavWindow {
  constructor(app, options = {}) {
    this.app = app;
    this.store = app.store;
    this.isPrivate = !!options.isPrivate;
    this.persists = !!options.persists;
    this.session = this.isPrivate ? electronSession.fromPartition(`private-${crypto.randomUUID()}`) : electronSession.defaultSession;
    app.setUpSession(this.session, this.isPrivate);

    const saved = this.store.settings.window;
    this.win = new BaseWindow({
      width: saved.width || 1280,
      height: saved.height || 800,
      x: saved.x ?? undefined,
      y: saved.y ?? undefined,
      minWidth: 520,
      minHeight: 360,
      frame: false,
      backgroundColor: this.palette().window,
      title: this.isPrivate ? "Navigatueur — Navigation privée" : "Navigatueur",
      icon: nativeImage.createFromPath(path.join(app.resourcesDir, "icon.png")),
      show: false,
    });
    if (saved.maximized && !this.isPrivate) this.win.maximize();

    this.tabs = [];
    this.groups = [];
    this.activeId = null;
    this.closed = [];
    this.downloads = [];
    this.layoutState = { toolbarRevealed: false, sidebarRevealed: false, toolbarHeld: false, sidebarHeld: false, toolbarExtra: 0, sidebarExtra: 0, pageFullScreen: false, findOpen: false };
    this.edge = { toolbarSince: 0, sidebarSince: 0, toolbarOutside: 0, sidebarOutside: 0 };

    this.toolbar = this.createShellView("toolbar.html");
    this.sidebar = this.createShellView("sidebar.html");
    this.win.contentView.addChildView(this.sidebar);
    this.win.contentView.addChildView(this.toolbar);

    this.win.on("resize", () => this.layout());
    this.win.on("maximize", () => this.pushState());
    this.win.on("unmaximize", () => this.pushState());
    this.win.on("enter-full-screen", () => this.layout());
    this.win.on("leave-full-screen", () => this.layout());
    this.win.on("focus", () => this.startPolling());
    this.win.on("blur", () => this.stopPolling());
    this.win.on("close", () => this.onClose());
    this.win.on("closed", () => app.windowClosed(this));

    if (options.restore && this.store.session.tabs && this.store.session.tabs.length) this.restore(this.store.session);
    else this.newTab(options.url || this.homeUrl());

    this.idleTimer = setInterval(() => this.suspendIdleTabs(), 60000);
    this.previewTimer = setInterval(() => this.capture(this.activeTab), 30000);
    this.layout();
    this.win.show();
    this.startPolling();
  }

  homeUrl() {
    return this.store.settings.homePageUrl || url.NEW_TAB_URL;
  }

  palette() {
    const dark = this.store.settings.themeMode !== "Light";
    const base = dark ? DARK : LIGHT;
    const tint = this.isPrivate ? (dark ? PRIVATE_DARK : PRIVATE_LIGHT) : {};
    const accent = this.isPrivate ? "#8B5CF6" : this.store.settings.accentColorHex || "#4C8DFF";
    return { ...base, ...tint, accent, dark };
  }

  createShellView(file) {
    const view = new WebContentsView({
      webPreferences: { preload: SHELL_PRELOAD, contextIsolation: true, sandbox: true, spellcheck: false },
    });
    view.setBackgroundColor("#00000000");
    view.webContents.loadFile(path.join(SHELL_DIR, file));
    view.webContents.on("before-input-event", (event, input) => this.handleKey(event, input));
    view.webContents.on("did-finish-load", () => this.pushState(true));
    return view;
  }

  // =============================================================== tabs

  get activeTab() {
    return this.tabs.find((t) => t.id === this.activeId) || null;
  }

  tabById(id) {
    return this.tabs.find((t) => t.id === id) || null;
  }

  tabForWebContents(wc) {
    return this.tabs.find((t) => t.wc === wc) || null;
  }

  restore(saved) {
    for (const g of saved.groups || []) this.groups.push({ id: g.id, name: g.name, color: g.color || g.colorHex || GROUP_COLORS[0][1], collapsed: !!g.collapsed });
    for (const t of saved.tabs) {
      this.tabs.push(new Tab(this, { ...t, groupId: this.groups.some((g) => g.id === t.groupId) ? t.groupId : null }));
    }
    const active = this.tabs.find((t) => t.id === saved.activeTabId) || this.tabs[this.tabs.length - 1];
    this.selectTab(active);
  }

  newTab(address, options = {}) {
    const { select = true, parent = null, groupId = parent ? parent.groupId : null, webContents = null } = options;
    const tab = new Tab(this, { url: address || this.homeUrl(), parentId: parent ? parent.id : null, groupId });
    let index = this.tabs.length;
    if (parent) {
      index = this.tabs.indexOf(parent) + 1;
      while (index < this.tabs.length && this.tabs[index].parentId === parent.id) index++;
    }
    this.tabs.splice(index, 0, tab);
    if (webContents) tab.ensureView(webContents);
    if (select) this.selectTab(tab);
    else this.pushState();
    this.persist();
    return tab;
  }

  selectTab(tab) {
    if (!tab) return;
    const previous = this.activeTab;
    if (previous && previous !== tab) {
      this.capture(previous);
      if (previous.view) this.win.contentView.removeChildView(previous.view);
    }
    this.activeId = tab.id;
    tab.lastActive = Date.now();
    const view = tab.ensureView();
    this.win.contentView.addChildView(view, 0);
    const group = this.groups.find((g) => g.id === tab.groupId);
    if (group) group.collapsed = false;
    this.layout();
    if (tab.wc && !this.toolbarFocused) tab.wc.focus();
    if (previous && previous !== tab && this.tabs.includes(previous)) previous.leftForAnotherTab();
    tab.returnedTo();
    this.enforceLiveLimit();
    this.pushState();
    this.persist();
  }

  selectAdjacent(direction) {
    const index = this.tabs.indexOf(this.activeTab);
    const next = this.tabs[(index + direction + this.tabs.length) % this.tabs.length];
    if (next) this.selectTab(next);
  }

  closeTab(tab, remember = true) {
    const index = this.tabs.indexOf(tab);
    if (index < 0) return;
    if (remember && !url.isInternal(tab.url)) {
      this.closed.push({ url: tab.url, title: tab.title, index, groupId: tab.groupId, pinned: tab.pinned, history: tab.snapshotHistory() });
      if (this.closed.length > 25) this.closed.shift();
    }
    const wasActive = tab.id === this.activeId;
    if (tab.view) this.win.contentView.removeChildView(tab.view);
    tab.destroy();
    this.tabs.splice(index, 1);
    this.dropEmptyGroups();
    if (!this.tabs.length) {
      this.newTab(this.homeUrl());
      return;
    }
    if (wasActive) {
      this.activeId = null;
      const next = this.tabs.find((t) => t.id === tab.parentId) || this.tabs[Math.min(index, this.tabs.length - 1)];
      this.selectTab(next);
    } else {
      this.pushState();
    }
    this.persist();
  }

  reopenClosed() {
    const record = this.closed.pop();
    if (!record) return;
    const tab = new Tab(this, { url: record.url, title: record.title, groupId: this.groups.some((g) => g.id === record.groupId) ? record.groupId : null, pinned: record.pinned, history: record.history });
    this.tabs.splice(Math.min(record.index, this.tabs.length), 0, tab);
    this.selectTab(tab);
  }

  moveTab(id, targetId, position) {
    const tab = this.tabById(id);
    if (!tab) return;
    if (position === "group") {
      this.addToGroup(tab, targetId);
      return;
    }
    const target = this.tabById(targetId);
    if (!target || target === tab) return;
    this.tabs.splice(this.tabs.indexOf(tab), 1);
    const index = this.tabs.indexOf(target) + (position === "after" ? 1 : 0);
    this.tabs.splice(index, 0, tab);
    tab.groupId = target.groupId;
    tab.pinned = target.pinned;
    this.dropEmptyGroups();
    this.pushState();
    this.persist();
  }

  togglePin(tab) {
    tab.pinned = !tab.pinned;
    if (tab.pinned) tab.groupId = null;
    this.dropEmptyGroups();
    this.pushState();
    this.persist();
  }

  // ------------------------------------------------------------ groups

  createGroup(tab, name) {
    const color = GROUP_COLORS[this.groups.length % GROUP_COLORS.length][1];
    const group = { id: crypto.randomUUID(), name: name || `Groupe ${this.groups.length + 1}`, color, collapsed: false };
    this.groups.push(group);
    tab.pinned = false;
    tab.groupId = group.id;
    this.pushState();
    this.persist();
    return group;
  }

  addToGroup(tab, groupId) {
    if (!this.groups.some((g) => g.id === groupId)) return;
    tab.pinned = false;
    tab.groupId = groupId;
    // Keep a group's tabs side by side.
    const last = this.tabs.map((t, i) => (t.groupId === groupId && t !== tab ? i : -1)).filter((i) => i >= 0).pop();
    if (last !== undefined) {
      this.tabs.splice(this.tabs.indexOf(tab), 1);
      const after = this.tabs.map((t, i) => (t.groupId === groupId ? i : -1)).filter((i) => i >= 0).pop();
      this.tabs.splice(after + 1, 0, tab);
    }
    this.dropEmptyGroups();
    this.pushState();
    this.persist();
  }

  groupAction(groupId, action, value) {
    const group = this.groups.find((g) => g.id === groupId);
    if (!group) return;
    if (action === "toggle") group.collapsed = !group.collapsed;
    else if (action === "rename" && value && value.trim()) group.name = value.trim();
    else if (action === "color") group.color = value;
    else if (action === "ungroup") {
      this.tabs.forEach((t) => t.groupId === groupId && (t.groupId = null));
      this.groups = this.groups.filter((g) => g !== group);
    } else if (action === "close") {
      this.tabs.filter((t) => t.groupId === groupId).forEach((t) => this.closeTab(t));
      this.groups = this.groups.filter((g) => g !== group);
    } else if (action === "newtab") {
      const tab = this.newTab(this.homeUrl(), { groupId });
      tab.groupId = groupId;
    } else if (action === "move" && value) {
      // Drag a group header onto another: the whole group moves before/after it.
      const target = this.groups.find((g) => g.id === value.targetId);
      if (target && target !== group) {
        this.groups.splice(this.groups.indexOf(group), 1);
        this.groups.splice(this.groups.indexOf(target) + (value.after ? 1 : 0), 0, group);
      }
    }
    this.pushState();
    this.persist();
  }

  dropEmptyGroups() {
    this.groups = this.groups.filter((g) => this.tabs.some((t) => t.groupId === g.id));
  }

  // -------------------------------------------------------- live tabs

  enforceLiveLimit() {
    const limit = Math.max(1, this.store.settings.maxLiveTabs);
    const live = this.tabs.filter((t) => t.isLive && t !== this.activeTab && !t.pinned).sort((a, b) => a.lastActive - b.lastActive);
    let excess = live.length - (limit - 1);
    for (const tab of live) {
      if (excess <= 0) break;
      if (tab.audible || tab.inAutoPip) continue;
      tab.suspend();
      excess--;
    }
    this.pushState();
  }

  suspendIdleTabs() {
    const minutes = this.store.settings.tabSuspendMinutes;
    if (minutes <= 0) return;
    const cutoff = Date.now() - minutes * 60000;
    let changed = false;
    for (const tab of this.tabs) {
      if (tab.isLive && tab !== this.activeTab && !tab.pinned && !tab.audible && !tab.inAutoPip && tab.lastActive < cutoff) {
        tab.suspend();
        changed = true;
      }
    }
    if (changed) this.pushState();
  }

  tabChanged(tab) {
    if (tab === this.activeTab) this.pushStateSoon();
    else this.pushStateSoon();
    this.persistSoon();
  }

  /** A picture of the page for the tab column's hover card (only a page on screen can be pictured). */
  capture(tab) {
    if (!tab || !tab.wc || tab !== this.activeTab || !this.store.settings.interface.areTabPreviewsEnabled || this.win.isMinimized()) return;
    tab.wc.capturePage().then((image) => {
      if (image.isEmpty()) return;
      tab.thumbnail = image.resize({ width: 420 }).toDataURL();
      this.pushStateSoon();
    }).catch(() => {});
  }

  // ============================================================= layout

  get ui() {
    return this.store.settings.interface;
  }

  behavior(panel) {
    const ui = this.ui;
    if (ui.mode === "Classic") return "Docked";
    if (ui.mode === "Custom") return (panel === "toolbar" ? ui.toolbarAutoHide : ui.sidebarAutoHide) ? "AutoHide" : "Docked";
    return "AutoHide";
  }

  layout() {
    if (this.win.isDestroyed()) return;
    const { width, height } = this.win.getContentBounds();
    const ls = this.layoutState;
    const fullScreen = ls.pageFullScreen;
    const toolbarDocked = this.behavior("toolbar") === "Docked" && !fullScreen;
    const sidebarDocked = this.behavior("sidebar") === "Docked" && !fullScreen;
    const showToolbar = toolbarDocked || (ls.toolbarRevealed && !fullScreen);
    const showSidebar = sidebarDocked || (ls.sidebarRevealed && !fullScreen);
    const sidebarWidth = this.ui.sidebarWidth;
    const right = this.ui.sidebarSide === "Right";
    const findHeight = ls.findOpen ? 44 : 0;

    const pageTop = toolbarDocked ? TOOLBAR_HEIGHT + findHeight : 0;
    const pageLeft = sidebarDocked && !right ? sidebarWidth : 0;
    const pageWidth = width - (sidebarDocked ? sidebarWidth : 0);
    const page = { x: pageLeft, y: pageTop, width: Math.max(1, pageWidth), height: Math.max(1, height - pageTop) };
    const tab = this.activeTab;
    if (tab && tab.view) tab.view.setBounds(page);

    // Toolbar: over the page's column (the tab column keeps its full height when docked).
    const toolbarX = sidebarDocked && !right ? sidebarWidth : 0;
    const toolbarWidth = sidebarDocked ? width - sidebarWidth : width;
    this.toolbar.setVisible(showToolbar);
    if (showToolbar) this.toolbar.setBounds({ x: toolbarX, y: 0, width: toolbarWidth, height: TOOLBAR_HEIGHT + findHeight + ls.toolbarExtra });

    // Tab column: under the toolbar when it floats over the page, full height when docked.
    const sidebarTop = !sidebarDocked && showToolbar ? TOOLBAR_HEIGHT + findHeight : 0;
    const extra = ls.sidebarExtra;
    this.sidebar.setVisible(showSidebar);
    if (showSidebar) {
      this.sidebar.setBounds({
        x: right ? width - sidebarWidth - extra : 0,
        y: sidebarTop,
        width: sidebarWidth + extra,
        height: Math.max(1, height - sidebarTop),
      });
    }
    this.sendToShell("layout", { toolbarDocked, sidebarDocked, showToolbar, showSidebar, right, sidebarWidth, extra, findOpen: ls.findOpen });
  }

  revealToolbar(hold = false) {
    if (this.behavior("toolbar") === "Docked") return;
    this.layoutState.toolbarRevealed = true;
    if (hold) this.layoutState.toolbarHeld = true;
    this.edge.toolbarOutside = 0;
    this.layout();
  }

  hideToolbar() {
    const ls = this.layoutState;
    if (!ls.toolbarRevealed) return;
    ls.toolbarRevealed = false;
    ls.toolbarHeld = false;
    ls.toolbarExtra = 0;
    this.sendToToolbar("hidden");
    this.layout();
  }

  revealSidebar(hold = false) {
    if (this.behavior("sidebar") === "Docked") return;
    this.layoutState.sidebarRevealed = true;
    if (hold) this.layoutState.sidebarHeld = true;
    this.edge.sidebarOutside = 0;
    this.layout();
  }

  hideSidebar() {
    const ls = this.layoutState;
    if (!ls.sidebarRevealed) return;
    ls.sidebarRevealed = false;
    ls.sidebarHeld = false;
    ls.sidebarExtra = 0;
    this.layout();
  }

  startPolling() {
    if (this.pollTimer) return;
    this.pollTimer = setInterval(() => this.poll(), 40);
  }

  stopPolling() {
    clearInterval(this.pollTimer);
    this.pollTimer = null;
  }

  /** Edge reveal: the panels come out when the mouse rests at their edge, and go once it has left them (Windows: EdgeRevealStateMachine). */
  poll() {
    if (this.win.isDestroyed() || !this.win.isFocused()) return;
    const ui = this.ui;
    const ls = this.layoutState;
    if (ls.pageFullScreen && this.behavior("toolbar") === "Docked") return;
    const bounds = this.win.getContentBounds();
    const cursor = screen.getCursorScreenPoint();
    const x = cursor.x - bounds.x;
    const y = cursor.y - bounds.y;
    const inside = x >= 0 && y >= 0 && x < bounds.width && y < bounds.height;
    const now = Date.now();
    const right = ui.sidebarSide === "Right";

    if (this.behavior("toolbar") === "AutoHide" || ls.pageFullScreen) {
      if (!ls.toolbarRevealed) {
        if (ui.isEdgeRevealEnabled && inside && y < ui.edgeTriggerSize) {
          this.edge.toolbarSince ||= now;
          if (now - this.edge.toolbarSince >= ui.revealDelayMilliseconds) this.revealToolbar();
        } else this.edge.toolbarSince = 0;
      } else if (!ls.toolbarHeld) {
        const area = this.toolbar.getBounds();
        const over = x >= area.x - 8 && x <= area.x + area.width + 8 && y <= area.height + 12 && y >= -40;
        if (over) this.edge.toolbarOutside = 0;
        else {
          this.edge.toolbarOutside ||= now;
          if (now - this.edge.toolbarOutside >= ui.hideDelayMilliseconds) this.hideToolbar();
        }
      }
    }

    if (this.behavior("sidebar") === "AutoHide" && !ls.pageFullScreen) {
      const atEdge = right ? x > bounds.width - ui.sidebarEdgeTriggerSize : x < ui.sidebarEdgeTriggerSize;
      if (!ls.sidebarRevealed) {
        if (ui.isEdgeRevealEnabled && inside && atEdge && y > (ls.toolbarRevealed ? TOOLBAR_HEIGHT : ui.edgeTriggerSize)) {
          this.edge.sidebarSince ||= now;
          if (now - this.edge.sidebarSince >= ui.revealDelayMilliseconds) this.revealSidebar();
        } else this.edge.sidebarSince = 0;
      } else if (!ls.sidebarHeld) {
        const area = this.sidebar.getBounds();
        const panelWidth = ui.sidebarWidth;
        const over = right ? x >= bounds.width - panelWidth - 12 : x <= panelWidth + 12;
        if (over && y >= area.y - 12) this.edge.sidebarOutside = 0;
        else {
          this.edge.sidebarOutside ||= now;
          if (now - this.edge.sidebarOutside >= ui.hideDelayMilliseconds) this.hideSidebar();
        }
      }
    }
  }

  setPageFullScreen(tab, on) {
    if (tab !== this.activeTab) return;
    this.layoutState.pageFullScreen = on;
    this.layoutState.toolbarRevealed = false;
    this.layoutState.sidebarRevealed = false;
    this.win.setFullScreen(on);
    this.layout();
  }

  // ======================================================= shell (IPC)

  sendToShell(type, data) {
    for (const view of [this.toolbar, this.sidebar]) {
      if (!view.webContents.isDestroyed()) view.webContents.send("shell", type, data);
    }
  }

  sendToToolbar(type, data) {
    if (!this.toolbar.webContents.isDestroyed()) this.toolbar.webContents.send("shell", type, data);
  }

  pushStateSoon() {
    if (this.pushTimer) return;
    this.pushTimer = setTimeout(() => {
      this.pushTimer = null;
      this.pushState();
    }, 30);
  }

  pushState() {
    if (this.win.isDestroyed()) return;
    const tab = this.activeTab;
    const s = this.store.settings;
    const state = {
      isPrivate: this.isPrivate,
      palette: this.palette(),
      maximized: this.win.isMaximized(),
      tabs: this.tabs.map((t) => t.info()),
      groups: this.groups,
      activeId: this.activeId,
      canReopen: this.closed.length > 0,
      active: tab
        ? {
            id: tab.id,
            url: tab.url,
            display: url.searchTerms(tab.url) || url.toDisplayUrl(tab.url),
            loading: tab.loading,
            canGoBack: tab.canGoBack,
            canGoForward: tab.canGoForward,
            secure: /^https:/.test(tab.url) || url.isInternal(tab.url),
            internal: url.isInternal(tab.url),
            bookmarked: this.store.isBookmarked(tab.url),
            favicon: tab.favicon,
            zoom: tab.zoom,
            forceDark: tab.isForceDark,
          }
        : null,
      settings: {
        sidebarSide: s.interface.sidebarSide,
        sidebarWidth: s.interface.sidebarWidth,
        newTabButton: s.interface.newTabButtonPlacement,
        previews: s.interface.areTabPreviewsEnabled,
        animations: s.interface.areAnimationsEnabled,
        effects: s.interface.arePanelEffectsEnabled,
        sidebarDocked: this.behavior("sidebar") === "Docked",
        toolbarDocked: this.behavior("toolbar") === "Docked",
        chromeBackground: this.app.chromeBackgroundUrl(),
      },
      downloads: this.downloads.slice(0, 20),
      update: this.app.updateInfo(),
    };
    this.sendToShell("state", state);
    this.win.setTitle(tab ? `${tab.displayTitle} — Navigatueur${this.isPrivate ? " (privé)" : ""}` : "Navigatueur");
  }

  /** Messages from the toolbar and the tab column. */
  onShell(type, data = {}, sender) {
    const tab = this.activeTab;
    const target = data.id ? this.tabById(data.id) : tab;
    switch (type) {
      case "ready":
        this.pushState();
        this.layout();
        break;
      case "navigate":
        if (tab && data.input) this.navigate(tab, data.input, data.engine);
        break;
      case "command":
        if (data.command === "menu") {
          // Positions come from the toolbar's view; the menu wants window coordinates.
          const origin = this.toolbar.getBounds();
          data = { ...data, x: origin.x + (data.x | 0), y: origin.y + (data.y | 0) };
        }
        this.execute(data.command, data);
        break;
      case "mute":
        if (target) this.toggleMute(target);
        break;
      case "select":
        if (target) this.selectTab(target);
        break;
      case "close":
        if (target) this.closeTab(target);
        break;
      case "move":
        this.moveTab(data.id, data.targetId, data.position);
        break;
      case "group":
        this.groupAction(data.id, data.action, data.value);
        break;
      case "tab-menu":
        if (target) this.tabMenu(target);
        break;
      case "group-menu":
        this.groupMenu(data.id);
        break;
      case "hold":
        if (sender === this.toolbar.webContents) {
          this.layoutState.toolbarHeld = !!data.on;
          this.toolbarFocused = !!data.focused;
          if (data.on) this.revealToolbar();
        } else {
          this.layoutState.sidebarHeld = !!data.on;
        }
        this.edge.toolbarOutside = 0;
        this.edge.sidebarOutside = 0;
        break;
      case "expand":
        if (sender === this.toolbar.webContents) this.layoutState.toolbarExtra = Math.max(0, Math.min(700, data.height | 0));
        else this.layoutState.sidebarExtra = Math.max(0, Math.min(420, data.width | 0));
        this.layout();
        break;
      case "sidebar-width":
        this.store.updateSettings((s) => (s.interface.sidebarWidth = Math.min(440, Math.max(200, data.width | 0))));
        this.layout();
        break;
      case "sidebar-hover":
        this.capture(this.activeTab);
        break;
      case "find":
        if (tab && tab.wc) {
          if (data.text) tab.wc.findInPage(data.text, { forward: data.forward !== false, findNext: !!data.next });
          else tab.wc.stopFindInPage("clearSelection");
        }
        break;
      case "find-close":
        this.layoutState.findOpen = false;
        if (tab && tab.wc) tab.wc.stopFindInPage("clearSelection");
        this.layout();
        if (tab && tab.wc) tab.wc.focus();
        break;
      case "focus-page":
        this.toolbarFocused = false;
        if (tab && tab.wc) tab.wc.focus();
        break;
      case "download-open":
        this.openDownload(data.id, false);
        break;
      case "download-show":
        this.openDownload(data.id, true);
        break;
      case "download-cancel":
        this.app.cancelDownload(data.id);
        break;
      case "window":
        if (data.action === "minimize") this.win.minimize();
        else if (data.action === "maximize") this.win.isMaximized() ? this.win.unmaximize() : this.win.maximize();
        else if (data.action === "close") this.win.close();
        break;
    }
  }

  suggest(text) {
    const tabs = this.tabs.filter((t) => t !== this.activeTab && url.isWebUrl(t.url)).map((t) => ({ url: t.url, title: t.displayTitle, kind: "tab", id: t.id }));
    const sources = [
      ...tabs,
      ...this.store.bookmarks.map((b) => ({ url: b.url, title: b.title, kind: "bookmark" })),
      ...this.store.history.slice(-3000).reverse().map((h) => ({ url: h.url, title: h.title, kind: "history" })),
    ];
    return rank(text, sources, 7);
  }

  navigate(tab, input, engineId) {
    const prefix = url.engine(engineId || this.store.settings.searchEngine).prefix;
    const target = url.normalize(input, prefix);
    if (!target) return;
    tab.load(target);
    if (!tab.isLive) this.selectTab(tab);
    this.toolbarFocused = false;
    if (tab.wc) tab.wc.focus();
    this.pushState();
  }

  /** Links that open a new window: a tab, except real popups (sign-in windows) which keep their window. */
  openWindowRequest(tab, details) {
    if (details.disposition === "new-window" && details.features && /width|height|popup/.test(details.features)) {
      return {
        action: "allow",
        overrideBrowserWindowOptions: { width: 520, height: 680, autoHideMenuBar: true, webPreferences: { session: this.session, sandbox: true } },
      };
    }
    const background = details.disposition === "background-tab";
    setImmediate(() => this.newTab(details.url, { select: !background, parent: tab }));
    return { action: "deny" };
  }

  /** Before a page loads: phishing warning, and the warning page's own links. */
  guardNavigation(tab, event) {
    if (!event.isMainFrame) return;
    const target = event.url;
    if (/^https:\/\/navigatueur\.warning\/continue/i.test(target)) {
      event.preventDefault();
      const destination = new URL(target).searchParams.get("url");
      if (destination) {
        this.app.protection.allowOnce(url.displayHost(destination));
        tab.load(destination);
      }
      return;
    }
    if (/^https:\/\/navigatueur\.home\//i.test(target)) {
      event.preventDefault();
      tab.load(url.INTERNAL_ORIGIN + target.replace(/^https:\/\/navigatueur\.home\//i, ""));
      return;
    }
    let host = null;
    try {
      host = new URL(target).hostname;
    } catch {}
    if (/^https?:/i.test(target) && this.app.protection.isPhishing(host)) {
      event.preventDefault();
      tab.load(`navigatueur://warning/index.html?url=${encodeURIComponent(target)}`);
    }
  }

  // ============================================================ commands

  execute(command, data = {}) {
    const tab = this.activeTab;
    switch (command) {
      case "tab.new":
        this.newTab(this.homeUrl());
        this.focusAddress();
        break;
      case "tab.close":
        if (tab) this.closeTab(tab);
        break;
      case "tab.next":
        this.selectAdjacent(1);
        break;
      case "tab.previous":
        this.selectAdjacent(-1);
        break;
      case "tab.reopen":
        this.reopenClosed();
        break;
      case "tabs.show":
        this.revealSidebar(true);
        this.sendToShell("focus-tabs");
        break;
      case "address.focus":
        this.focusAddress();
        break;
      case "page.reload":
        if (tab) tab.crashed || !tab.wc ? (tab.destroy(), this.selectTab(tab)) : tab.wc.reload();
        break;
      case "page.stop":
        if (tab && tab.wc) tab.wc.stop();
        break;
      case "page.back":
        if (tab && tab.wc && tab.wc.navigationHistory.canGoBack()) tab.wc.navigationHistory.goBack();
        break;
      case "page.forward":
        if (tab && tab.wc && tab.wc.navigationHistory.canGoForward()) tab.wc.navigationHistory.goForward();
        break;
      case "page.home":
        if (tab) tab.load(this.homeUrl());
        break;
      case "bookmark.toggle":
        if (tab && url.isWebUrl(tab.url)) {
          this.store.toggleBookmark(tab.url, tab.displayTitle);
          this.pushState();
        }
        break;
      case "view.fullscreen":
        this.win.setFullScreen(!this.win.isFullScreen());
        break;
      case "window.new":
        this.app.openWindow({});
        break;
      case "window.private":
        this.app.openWindow({ isPrivate: true });
        break;
      case "history.open":
        this.newTab(url.INTERNAL_ORIGIN + url.PAGES.history);
        break;
      case "downloads.open":
        this.revealToolbar();
        this.sendToToolbar("open-downloads");
        break;
      case "settings.open":
        this.openInternal(url.PAGES.settings + (data.section ? "#" + data.section : ""));
        break;
      case "page.find":
        this.layoutState.findOpen = true;
        this.revealToolbar(true);
        this.sendToToolbar("open-find");
        break;
      case "zoom.in":
      case "zoom.out":
      case "zoom.reset":
        if (tab && tab.wc) {
          tab.zoom = command === "zoom.reset" ? 1 : Math.min(5, Math.max(0.25, Math.round((tab.zoom + (command === "zoom.in" ? 0.1 : -0.1)) * 10) / 10));
          tab.wc.setZoomFactor(tab.zoom);
          this.pushState();
        }
        break;
      case "page.pip":
        if (tab) tab.togglePictureInPicture();
        break;
      case "page.forceDark":
        if (tab) this.app.forceDark.setForSite(tab.url, !tab.isForceDark, this.isPrivate);
        break;
      case "page.translate":
        if (tab && url.isWebUrl(tab.url)) tab.load(`https://translate.yandex.com/translate?url=${encodeURIComponent(tab.url)}&lang=fr`);
        break;
      case "page.saveAs":
        this.savePage();
        break;
      case "page.print":
        if (tab && tab.wc) tab.wc.print();
        break;
      case "sidebar.toggleDock":
        this.store.updateSettings((s) => {
          const toolbar = this.behavior("toolbar");
          const sidebar = this.behavior("sidebar") === "Docked" ? "AutoHide" : "Docked";
          s.interface.toolbarAutoHide = toolbar === "AutoHide";
          s.interface.sidebarAutoHide = sidebar === "AutoHide";
          s.interface.mode = toolbar === "AutoHide" && sidebar === "AutoHide" ? "Immersive" : toolbar === "Docked" && sidebar === "Docked" ? "Classic" : "Custom";
        });
        break;
      case "update.install":
        this.app.installUpdate();
        break;
      case "menu":
        this.mainMenu(data);
        break;
      default:
        if (command && command.startsWith("tab.select.")) {
          const which = command.slice("tab.select.".length);
          const target = which === "last" ? this.tabs[this.tabs.length - 1] : this.tabs[Number(which) - 1];
          if (target) this.selectTab(target);
        }
    }
  }

  openInternal(page) {
    const target = url.INTERNAL_ORIGIN + page;
    const existing = this.tabs.find((t) => t.url.split("#")[0] === target.split("#")[0]);
    if (existing) {
      if (existing.url !== target) existing.load(target);
      this.selectTab(existing);
    } else this.newTab(target);
  }

  focusAddress() {
    this.revealToolbar(true);
    this.toolbarFocused = true;
    this.toolbar.webContents.focus();
    this.sendToToolbar("focus-address");
  }

  handleKey(event, input) {
    if (input.type !== "keyDown") return;
    if (this.app.capturingShortcut) return;
    if (input.key === "Escape" && this.layoutState.pageFullScreen) return;
    const command = shortcuts.commandFor(input, this.app.shortcutMap);
    if (!command) return;
    event.preventDefault();
    this.execute(command);
  }

  // =============================================================== menus

  mainMenu(position) {
    const tab = this.activeTab;
    const web = tab && url.isWebUrl(tab.url);
    const s = this.store.settings;
    const template = [
      { label: "Nouvel onglet", accelerator: "Ctrl+T", click: () => this.execute("tab.new") },
      { label: "Nouvelle fenêtre", accelerator: "Ctrl+N", click: () => this.execute("window.new") },
      { label: "Nouvelle fenêtre privée", accelerator: "Ctrl+Shift+N", click: () => this.execute("window.private") },
      { label: "Rouvrir l'onglet fermé", accelerator: "Ctrl+Shift+T", enabled: this.closed.length > 0, click: () => this.execute("tab.reopen") },
      { type: "separator" },
      { label: "Historique", accelerator: "Ctrl+H", click: () => this.execute("history.open") },
      { label: "Téléchargements", accelerator: "Ctrl+J", click: () => this.execute("downloads.open") },
      { label: "Favoris", submenu: this.bookmarksMenu() },
      { type: "separator" },
      {
        label: "Zoom",
        submenu: [
          { label: "Agrandir", accelerator: "Ctrl+Plus", click: () => this.execute("zoom.in") },
          { label: "Réduire", accelerator: "Ctrl+-", click: () => this.execute("zoom.out") },
          { label: "Taille réelle", accelerator: "Ctrl+0", click: () => this.execute("zoom.reset") },
        ],
      },
      { label: "Rechercher dans la page", accelerator: "Ctrl+F", enabled: !!(tab && tab.wc), click: () => this.execute("page.find") },
      { label: "Traduire la page", enabled: !!web, click: () => this.execute("page.translate") },
      { label: "Enregistrer sous…", enabled: !!web, click: () => this.execute("page.saveAs") },
      { label: "Imprimer…", enabled: !!(tab && tab.wc), click: () => this.execute("page.print") },
      { label: tab && tab.isForceDark ? "Désactiver le mode sombre sur ce site" : "Activer le mode sombre sur ce site", visible: !!web, click: () => this.execute("page.forceDark") },
      { label: "Picture-in-Picture", enabled: !!(tab && tab.wc), click: () => this.execute("page.pip") },
      { label: "Plein écran", accelerator: "F11", click: () => this.execute("view.fullscreen") },
      { type: "separator" },
      { label: "Paramètres", accelerator: "Ctrl+,", click: () => this.execute("settings.open") },
    ];
    const update = this.app.updateInfo();
    if (update.available) template.push({ label: `Installer la mise à jour ${update.latestVersion}`, click: () => this.execute("update.install") });
    if (s.isAdBlockEnabled) template.push({ type: "separator" }, { label: "Bloqueur de publicités actif", enabled: false });
    Menu.buildFromTemplate(template).popup({ window: this.win, x: position.x, y: position.y });
  }

  bookmarksMenu() {
    const tab = this.activeTab;
    const items = [];
    if (tab && url.isWebUrl(tab.url)) {
      items.push({ label: this.store.isBookmarked(tab.url) ? "Retirer cette page des favoris" : "Ajouter cette page aux favoris", accelerator: "Ctrl+D", click: () => this.execute("bookmark.toggle") });
      items.push({ type: "separator" });
    }
    const bookmarks = this.store.bookmarks.slice(-30).reverse();
    if (!bookmarks.length) items.push({ label: "Aucun favori", enabled: false });
    for (const b of bookmarks) items.push({ label: (b.title || b.url).slice(0, 60), click: () => this.activeTab && this.activeTab.load(b.url) });
    return items;
  }

  tabMenu(tab) {
    const template = [
      { label: tab.pinned ? "Détacher" : "Épingler", click: () => this.togglePin(tab) },
      { label: tab.muted ? "Réactiver le son" : "Couper le son", click: () => this.toggleMute(tab) },
      { label: "Dupliquer", click: () => this.newTab(tab.url, { parent: tab }) },
      { label: "Nouveau groupe…", click: () => this.createGroup(tab) },
      ...this.groups.filter((g) => g.id !== tab.groupId).map((g) => ({ label: `Ajouter à « ${g.name} »`, click: () => this.addToGroup(tab, g.id) })),
      ...(tab.groupId ? [{ label: "Retirer du groupe", click: () => { tab.groupId = null; this.dropEmptyGroups(); this.pushState(); this.persist(); } }] : []),
      { type: "separator" },
      { label: "Fermer les autres onglets", click: () => this.tabs.filter((t) => t !== tab && !t.pinned).forEach((t) => this.closeTab(t)) },
      { label: "Fermer", click: () => this.closeTab(tab) },
    ];
    this.layoutState.sidebarHeld = true;
    Menu.buildFromTemplate(template).popup({ window: this.win, callback: () => (this.layoutState.sidebarHeld = false) });
  }

  groupMenu(groupId) {
    const group = this.groups.find((g) => g.id === groupId);
    if (!group) return;
    const template = [
      { label: "Nouvel onglet dans le groupe", click: () => this.groupAction(groupId, "newtab") },
      { label: "Renommer…", click: () => this.sendToShell("rename-group", { id: groupId }) },
      { label: "Couleur", submenu: GROUP_COLORS.map(([name, hex]) => ({ label: name, type: "radio", checked: group.color === hex, click: () => this.groupAction(groupId, "color", hex) })) },
      { label: group.collapsed ? "Déplier" : "Replier", click: () => this.groupAction(groupId, "toggle") },
      { type: "separator" },
      { label: "Dégrouper", click: () => this.groupAction(groupId, "ungroup") },
      { label: "Fermer le groupe", click: () => this.groupAction(groupId, "close") },
    ];
    this.layoutState.sidebarHeld = true;
    Menu.buildFromTemplate(template).popup({ window: this.win, callback: () => (this.layoutState.sidebarHeld = false) });
  }

  toggleMute(tab) {
    tab.muted = !tab.muted;
    if (tab.wc) tab.wc.setAudioMuted(tab.muted);
    this.pushState();
  }

  pageContextMenu(tab, params) {
    const items = [];
    const wc = tab.wc;
    if (params.linkURL) {
      items.push(
        { label: "Ouvrir le lien dans un nouvel onglet", click: () => this.newTab(params.linkURL, { select: false, parent: tab }) },
        { label: "Ouvrir le lien dans une fenêtre privée", click: () => this.app.openWindow({ isPrivate: true, url: params.linkURL }) },
        { label: "Copier l'adresse du lien", click: () => clipboard.writeText(params.linkURL) },
        { label: "Enregistrer le lien sous…", click: () => wc.downloadURL(params.linkURL) },
        { type: "separator" },
      );
    }
    if (params.mediaType === "image" && params.srcURL) {
      items.push(
        { label: "Ouvrir l'image dans un nouvel onglet", click: () => this.newTab(params.srcURL, { parent: tab }) },
        { label: "Enregistrer l'image sous…", click: () => wc.downloadURL(params.srcURL) },
        { label: "Copier l'image", click: () => wc.copyImageAt(params.x, params.y) },
        { label: "Copier l'adresse de l'image", click: () => clipboard.writeText(params.srcURL) },
        { type: "separator" },
      );
    }
    if (params.mediaType === "video" && params.srcURL) {
      items.push({ label: "Picture-in-Picture", click: () => tab.togglePictureInPicture() }, { type: "separator" });
    }
    if (params.isEditable) {
      for (const suggestion of params.dictionarySuggestions || []) items.push({ label: suggestion, click: () => wc.replaceMisspelling(suggestion) });
      if (params.dictionarySuggestions && params.dictionarySuggestions.length) items.push({ type: "separator" });
      items.push(
        { label: "Annuler", role: "undo", enabled: params.editFlags.canUndo },
        { label: "Rétablir", role: "redo", enabled: params.editFlags.canRedo },
        { type: "separator" },
        { label: "Couper", role: "cut", enabled: params.editFlags.canCut },
        { label: "Copier", role: "copy", enabled: params.editFlags.canCopy },
        { label: "Coller", role: "paste", enabled: params.editFlags.canPaste },
        { label: "Tout sélectionner", role: "selectAll" },
      );
    } else if (params.selectionText) {
      const text = params.selectionText.trim();
      items.push(
        { label: "Copier", role: "copy" },
        { label: `Rechercher « ${text.length > 30 ? text.slice(0, 30) + "…" : text} »`, click: () => this.newTab(url.normalize(text, url.engine(this.store.settings.searchEngine).prefix), { parent: tab }) },
      );
    } else if (!params.linkURL && params.mediaType === "none") {
      items.push(
        { label: "Précédent", enabled: tab.canGoBack, click: () => this.execute("page.back") },
        { label: "Suivant", enabled: tab.canGoForward, click: () => this.execute("page.forward") },
        { label: "Actualiser", click: () => this.execute("page.reload") },
        { type: "separator" },
        { label: "Enregistrer sous…", enabled: url.isWebUrl(tab.url), click: () => this.execute("page.saveAs") },
        { label: "Imprimer…", click: () => this.execute("page.print") },
      );
    }
    items.push({ type: "separator" }, { label: "Inspecter", click: () => wc.inspectElement(params.x, params.y) });
    Menu.buildFromTemplate(items).popup({ window: this.win });
  }

  async savePage() {
    const tab = this.activeTab;
    if (!tab || !tab.wc) return;
    const name = (tab.displayTitle || "page").replace(/[\\/:*?"<>|]/g, "_").slice(0, 80);
    const result = await dialog.showSaveDialog(this.win, {
      defaultPath: path.join(require("electron").app.getPath("downloads"), name + ".html"),
      filters: [{ name: "Page web complète", extensions: ["html"] }, { name: "Archive web (MHTML)", extensions: ["mhtml"] }],
    });
    if (result.canceled || !result.filePath) return;
    const type = result.filePath.endsWith(".mhtml") ? "MHTML" : "HTMLComplete";
    tab.wc.savePage(result.filePath, type).catch(() => {});
  }

  openDownload(id, inFolder) {
    const item = this.downloads.find((d) => d.id === id);
    if (!item || !item.path) return;
    if (inFolder) shell.showItemInFolder(item.path);
    else shell.openPath(item.path);
  }

  notifyDownload(record) {
    if (Notification.isSupported() && record.state === "completed") {
      const n = new Notification({ title: "Téléchargement terminé", body: record.name, icon: path.join(this.app.resourcesDir, "icon.png") });
      n.on("click", () => shell.openPath(record.path));
      n.show();
    }
  }

  // ======================================================== persistence

  persistSoon() {
    if (!this.persists) return;
    clearTimeout(this.persistTimer);
    this.persistTimer = setTimeout(() => this.persist(), 800);
  }

  persist() {
    if (!this.persists) return;
    this.store.saveSession({
      tabs: this.tabs.map((t) => ({ id: t.id, url: t.url, title: t.title, favicon: t.favicon, groupId: t.groupId, pinned: t.pinned, history: t.snapshotHistory() })),
      groups: this.groups,
      activeTabId: this.activeId,
    });
  }

  onClose() {
    if (this.persists) {
      this.persist();
      const bounds = this.win.getNormalBounds();
      this.store.updateSettings((s) => (s.window = { width: bounds.width, height: bounds.height, x: bounds.x, y: bounds.y, maximized: this.win.isMaximized() }));
    }
    clearInterval(this.idleTimer);
    clearInterval(this.previewTimer);
    this.stopPolling();
    for (const tab of this.tabs) tab.destroy();
    for (const view of [this.toolbar, this.sidebar]) if (!view.webContents.isDestroyed()) view.webContents.close();
  }
}

module.exports = { NavWindow, GROUP_COLORS };
