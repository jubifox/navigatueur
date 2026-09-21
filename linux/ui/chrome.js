// Browser chrome: renders the tab strip and toolbar, and drives the Rust side.
// Rust owns tab/group state and pushes it here on every change, so this file
// never keeps its own copy — it re-renders from whatever `state-changed` last
// delivered. That mirrors the Windows split, where TabManagerService owned the
// collections and the XAML merely bound to them.

const { invoke } = window.__TAURI__.core;
const { listen } = window.__TAURI__.event;

let state = { tabs: [], groups: [], savedGroups: [], active: null };
let settings = null;
let engines = [];
let groupColors = [];
let suggestion = null;      // pending inline address-bar completion
let addressDirty = false;   // true while the user is editing the address bar

const $ = (id) => document.getElementById(id);
const el = {
  tabs: $("tabs"), newtab: $("newtab"), savedgroups: $("savedgroups"),
  memory: $("memory"), address: $("address"), shield: $("shield"),
  back: $("back"), forward: $("forward"), reload: $("reload"), home: $("home"),
  zoomin: $("zoomin"), zoomout: $("zoomout"), zoomlevel: $("zoomlevel"),
  pip: $("pip"), translate: $("translate"), history: $("history"),
  settings: $("settings"), findbtn: $("findbtn"),
  findbar: $("findbar"), findinput: $("findinput"), findprev: $("findprev"),
  findnext: $("findnext"), findclose: $("findclose"), menu: $("menu"),
};

const activeTab = () => state.tabs.find((t) => t.id === state.active) || null;

/** The internal new-tab page is an implementation detail; show an empty bar. */
function displayUrl(url) {
  if (!url || url.startsWith("navigatueur://") || url.includes("newtab.html")) return "";
  return url;
}

function hostOf(url) {
  try { return new URL(url).host; } catch { return ""; }
}

function tabLabel(tab) {
  if (tab.title) return tab.title;
  const host = hostOf(tab.url);
  return host ? host.replace(/^www\./, "") : "Nouvel onglet";
}

// ------------------------------------------------------------- rendering

function render() {
  el.tabs.replaceChildren();

  const pinned = state.tabs.filter((t) => t.isPinned);
  const loose = state.tabs.filter((t) => !t.isPinned && !t.groupId);

  pinned.forEach((t) => el.tabs.appendChild(renderTab(t)));
  loose.forEach((t) => el.tabs.appendChild(renderTab(t)));

  for (const group of state.groups) {
    const members = state.tabs.filter((t) => !t.isPinned && t.groupId === group.id);
    // A group with no members left would render as a stray header.
    if (members.length === 0) continue;
    el.tabs.appendChild(renderGroupHeader(group, members.length));
    if (group.isCollapsed) continue;
    members.forEach((t) => el.tabs.appendChild(renderTab(t, group)));
  }

  renderToolbar();
}

function renderTab(tab, group) {
  const node = document.createElement("div");
  node.className = "tab";
  node.dataset.id = tab.id;
  if (tab.id === state.active) node.classList.add("active");
  if (tab.isPinned) node.classList.add("pinned");
  if (tab.isSuspended) node.classList.add("suspended");
  if (tab.isLoading) node.classList.add("loading");
  if (group) node.style.borderTopColor = group.colorHex;
  node.title = tab.url || "Nouvel onglet";

  const host = hostOf(tab.url);
  if (host) {
    // Fetched straight from the site being visited rather than through a
    // third-party favicon service, which would leak browsing to that service.
    const img = document.createElement("img");
    img.className = "favicon";
    img.src = `https://${host}/favicon.ico`;
    img.referrerPolicy = "no-referrer";
    img.onerror = () => img.remove();
    node.appendChild(img);
  }

  const title = document.createElement("span");
  title.className = "title";
  title.textContent = tabLabel(tab);
  node.appendChild(title);

  if (tab.isMuted) {
    const badge = document.createElement("span");
    badge.className = "badge";
    badge.textContent = "🔇";
    badge.title = "Son coupé";
    node.appendChild(badge);
  }

  const close = document.createElement("button");
  close.className = "icon-btn close";
  close.textContent = "×";
  close.title = "Fermer (Ctrl+W)";
  close.addEventListener("click", (e) => { e.stopPropagation(); invoke("tab_close", { id: tab.id }); });
  node.appendChild(close);

  node.addEventListener("click", () => invoke("tab_activate", { id: tab.id }));
  node.addEventListener("auxclick", (e) => {
    if (e.button === 1) invoke("tab_close", { id: tab.id });  // middle-click closes
  });
  node.addEventListener("contextmenu", (e) => { e.preventDefault(); openTabMenu(e, tab); });

  makeDraggable(node, tab);
  return node;
}

function renderGroupHeader(group, count) {
  const node = document.createElement("div");
  node.className = "group-header";
  node.style.background = group.colorHex + "22";
  node.style.color = group.colorHex;

  const dot = document.createElement("span");
  dot.className = "dot";
  dot.style.background = group.colorHex;
  node.appendChild(dot);

  const name = document.createElement("span");
  name.textContent = group.isCollapsed ? `${group.name} (${count})` : group.name;
  node.appendChild(name);

  node.addEventListener("click", () =>
    invoke("group_action", { action: "toggleCollapsed", id: group.id, arg: null }));
  node.addEventListener("contextmenu", (e) => { e.preventDefault(); openGroupMenu(e, group); });
  return node;
}

function renderToolbar() {
  const tab = activeTab();
  const hasTab = !!tab;

  el.back.disabled = !hasTab;
  el.forward.disabled = !hasTab;
  el.reload.disabled = !hasTab;
  el.pip.disabled = !hasTab;
  el.translate.disabled = !hasTab;

  if (!addressDirty) el.address.value = hasTab ? displayUrl(tab.url) : "";
  el.zoomlevel.textContent = `${Math.round((tab?.zoom ?? 1) * 100)}%`;

  // The page's own theme colour washes into the chrome, as on Windows.
  const accent = tab?.siteAccentColorHex;
  document.documentElement.style.setProperty(
    "--chrome-bg-image",
    accent ? `linear-gradient(to bottom, ${accent}, transparent 70%)` : "none");
}

// -------------------------------------------------------------- menus

function openMenu(event, build) {
  el.menu.replaceChildren();
  build(el.menu);
  el.menu.hidden = false;
  // Keep the menu inside the chrome strip, which is only ~84px tall.
  const rect = el.menu.getBoundingClientRect();
  el.menu.style.left = `${Math.min(event.clientX, window.innerWidth - rect.width - 4)}px`;
  el.menu.style.top = `${Math.min(event.clientY, window.innerHeight - rect.height - 4)}px`;
}

function closeMenu() { el.menu.hidden = true; }

function menuItem(parent, label, onClick, swatch) {
  const button = document.createElement("button");
  if (swatch) {
    const dot = document.createElement("span");
    dot.className = "swatch";
    dot.style.background = swatch;
    button.appendChild(dot);
  }
  button.appendChild(document.createTextNode(label));
  button.addEventListener("click", () => { closeMenu(); onClick(); });
  parent.appendChild(button);
}

function openTabMenu(event, tab) {
  openMenu(event, (menu) => {
    const flag = (name, value) => invoke("tab_set_flag", { id: tab.id, flag: name, value });

    menuItem(menu, tab.isPinned ? "Détacher" : "Épingler", () => flag("isPinned", !tab.isPinned));
    menuItem(menu, tab.isMuted ? "Réactiver le son" : "Couper le son", () => flag("isMuted", !tab.isMuted));
    menuItem(menu,
      tab.isAdBlockDisabled ? "Réactiver le bloqueur sur ce site" : "Désactiver le bloqueur sur ce site",
      () => flag("isAdBlockDisabled", !tab.isAdBlockDisabled));

    menu.appendChild(document.createElement("hr"));
    menuItem(menu, "Nouveau groupe…", () =>
      invoke("group_action", { action: "createForTab", id: tab.id, arg: null }));

    if (tab.groupId) {
      menuItem(menu, "Retirer du groupe", () =>
        invoke("group_action", { action: "removeTab", id: tab.id, arg: null }));
    }
    for (const group of state.groups) {
      if (group.id === tab.groupId) continue;
      menuItem(menu, `Ajouter à « ${group.name} »`,
        () => invoke("group_action", { action: "assignTab", id: tab.id, arg: group.id }),
        group.colorHex);
    }

    menu.appendChild(document.createElement("hr"));
    menuItem(menu, "Fermer", () => invoke("tab_close", { id: tab.id }));
  });
}

function openGroupMenu(event, group) {
  openMenu(event, (menu) => {
    menuItem(menu, "Renommer…", () => startRename(group));
    for (const [name, hex] of groupColors) {
      if (hex.toLowerCase() === group.colorHex.toLowerCase()) continue;
      menuItem(menu, `Couleur : ${name}`,
        () => invoke("group_action", { action: "setColor", id: group.id, arg: hex }), hex);
    }
    menu.appendChild(document.createElement("hr"));
    menuItem(menu, "Enregistrer le groupe", () =>
      invoke("group_action", { action: "save", id: group.id, arg: null }));
    if (state.savedGroups.some((g) => g.id === group.id)) {
      menuItem(menu, "Supprimer la sauvegarde", () =>
        invoke("group_action", { action: "deleteSaved", id: group.id, arg: null }));
    }
    menuItem(menu, "Supprimer le groupe", () =>
      invoke("group_action", { action: "delete", id: group.id, arg: null }));
  });
}

/** Replaces the header's label with an input, committing on Enter or blur. */
function startRename(group) {
  const header = [...el.tabs.querySelectorAll(".group-header")]
    .find((n) => n.textContent.startsWith(group.name));
  if (!header) return;

  const input = document.createElement("input");
  input.value = group.name;
  header.replaceChildren(input);
  input.focus();
  input.select();

  let done = false;
  const commit = () => {
    if (done) return;
    done = true;
    const name = input.value.trim();
    if (name && name !== group.name) {
      invoke("group_action", { action: "rename", id: group.id, arg: name });
    } else {
      render();
    }
  };
  input.addEventListener("keydown", (e) => {
    if (e.key === "Enter") commit();
    if (e.key === "Escape") { done = true; render(); }
  });
  input.addEventListener("blur", commit);
}

el.savedgroups.addEventListener("click", (e) => {
  openMenu(e, (menu) => {
    if (state.savedGroups.length === 0) {
      const empty = document.createElement("button");
      empty.textContent = "Aucun groupe enregistré";
      empty.disabled = true;
      menu.appendChild(empty);
      return;
    }
    for (const saved of state.savedGroups) {
      menuItem(menu, `Ouvrir « ${saved.name} » (${saved.urls.length})`,
        () => invoke("group_action", { action: "openSaved", id: saved.id, arg: null }),
        saved.colorHex);
    }
    menu.appendChild(document.createElement("hr"));
    for (const saved of state.savedGroups) {
      menuItem(menu, `Supprimer « ${saved.name} »`,
        () => invoke("group_action", { action: "deleteSaved", id: saved.id, arg: null }));
    }
  });
});

window.addEventListener("click", (e) => { if (!el.menu.contains(e.target)) closeMenu(); });

// --------------------------------------------------------- drag and drop

let dragSource = null;

function makeDraggable(node, tab) {
  node.draggable = true;
  node.addEventListener("dragstart", (e) => {
    dragSource = tab.id;
    e.dataTransfer.effectAllowed = "move";
  });
  node.addEventListener("dragend", () => {
    dragSource = null;
    clearDropHints();
  });
  node.addEventListener("dragover", (e) => {
    if (!dragSource || dragSource === tab.id) return;
    e.preventDefault();
    clearDropHints();
    node.classList.add(dropMode(e, node));
  });
  node.addEventListener("drop", (e) => {
    if (!dragSource || dragSource === tab.id) return;
    e.preventDefault();
    const mode = dropMode(e, node).replace("dragover-", "");
    invoke("tab_reorder", { source: dragSource, target: tab.id, mode });
    clearDropHints();
  });
}

/** Dropping on the middle third groups the two tabs, as in Chrome/Edge;
    the outer thirds reorder before or after instead. */
function dropMode(event, node) {
  const rect = node.getBoundingClientRect();
  const ratio = (event.clientX - rect.left) / rect.width;
  if (ratio > 0.33 && ratio < 0.67) return "dragover-group";
  return ratio <= 0.33 ? "dragover-before" : "dragover-after";
}

function clearDropHints() {
  el.tabs.querySelectorAll(".tab").forEach((n) =>
    n.classList.remove("dragover-before", "dragover-after", "dragover-group"));
}

// ------------------------------------------------------------ toolbar

const withTab = (fn) => () => { const t = activeTab(); if (t) fn(t); };

el.newtab.addEventListener("click", () => invoke("tab_create", { url: null }));
el.back.addEventListener("click", withTab((t) => invoke("tab_history_go", { id: t.id, delta: -1 })));
el.forward.addEventListener("click", withTab((t) => invoke("tab_history_go", { id: t.id, delta: 1 })));
el.reload.addEventListener("click", withTab((t) => invoke("tab_reload", { id: t.id })));
el.pip.addEventListener("click", withTab((t) => invoke("tab_picture_in_picture", { id: t.id })));
el.translate.addEventListener("click", withTab((t) => invoke("tab_translate", { id: t.id })));

el.home.addEventListener("click", withTab((t) =>
  invoke("tab_navigate", { id: t.id, input: settings?.HomePageUrl ?? "" })));

el.history.addEventListener("click", () => invoke("tab_create", { url: "history.html" }));
el.settings.addEventListener("click", () => invoke("tab_create", { url: "settings.html" }));

const setZoom = (delta) => withTab((t) =>
  invoke("tab_set_zoom", { id: t.id, zoom: delta === 0 ? 1 : (t.zoom ?? 1) + delta }))();

el.zoomin.addEventListener("click", () => setZoom(0.1));
el.zoomout.addEventListener("click", () => setZoom(-0.1));
el.zoomlevel.addEventListener("click", () => setZoom(0));

// --------------------------------------------------------- address bar

el.address.addEventListener("focus", () => el.address.select());
el.address.addEventListener("blur", () => { addressDirty = false; renderToolbar(); });

el.address.addEventListener("input", async (e) => {
  addressDirty = true;
  suggestion = null;
  // Only complete while appending at the very end, never while deleting —
  // otherwise backspace immediately re-proposes what was just removed.
  if (e.inputType && e.inputType.startsWith("delete")) return;

  const typed = el.address.value;
  if (!typed) return;
  const match = await invoke("address_bar_suggest", { prefix: typed });
  if (!match || el.address.value !== typed) return;

  // Append the completion as selected text, so the next keystroke overwrites it
  // and Enter navigates to the whole thing if left alone.
  suggestion = match;
  el.address.value = match;
  el.address.setSelectionRange(typed.length, match.length);
});

el.address.addEventListener("keydown", async (e) => {
  if (e.key === "Escape") { addressDirty = false; renderToolbar(); el.address.blur(); return; }
  if (e.key !== "Enter") return;

  let tab = activeTab();
  if (!tab) {
    await invoke("tab_create", { url: null });
    tab = activeTab();
    if (!tab) return;
  }
  addressDirty = false;
  await invoke("tab_navigate", { id: tab.id, input: el.address.value });
});

// ------------------------------------------------------------- findbar

function openFind() {
  el.findbar.hidden = false;
  el.findinput.focus();
  el.findinput.select();
}

function closeFind() { el.findbar.hidden = true; }

const runFind = (forward) => withTab((t) =>
  invoke("tab_find", { id: t.id, query: el.findinput.value, forward }))();

el.findbtn.addEventListener("click", openFind);
el.findclose.addEventListener("click", closeFind);
el.findnext.addEventListener("click", () => runFind(true));
el.findprev.addEventListener("click", () => runFind(false));
el.findinput.addEventListener("keydown", (e) => {
  if (e.key === "Enter") runFind(!e.shiftKey);
  if (e.key === "Escape") closeFind();
});

// ---------------------------------------------------------- shortcuts

window.addEventListener("keydown", (e) => {
  if (e.key === "F5") { e.preventDefault(); withTab((t) => invoke("tab_reload", { id: t.id }))(); return; }
  if (e.altKey && e.key === "ArrowLeft")  { e.preventDefault(); withTab((t) => invoke("tab_history_go", { id: t.id, delta: -1 }))(); return; }
  if (e.altKey && e.key === "ArrowRight") { e.preventDefault(); withTab((t) => invoke("tab_history_go", { id: t.id, delta: 1 }))(); return; }
  if (!e.ctrlKey) return;

  const tab = activeTab();
  switch (e.key.toLowerCase()) {
    case "t": e.preventDefault(); invoke("tab_create", { url: null }); break;
    case "w": e.preventDefault(); if (tab) invoke("tab_close", { id: tab.id }); break;
    case "l": e.preventDefault(); el.address.focus(); break;
    case "r": e.preventDefault(); if (tab) invoke("tab_reload", { id: tab.id }); break;
    case "f": e.preventDefault(); openFind(); break;
    case "h": e.preventDefault(); invoke("tab_create", { url: "history.html" }); break;
    case "+": case "=": e.preventDefault(); setZoom(0.1); break;
    case "-": e.preventDefault(); setZoom(-0.1); break;
    case "0": e.preventDefault(); setZoom(0); break;
    case "tab": {
      e.preventDefault();
      const order = state.tabs.map((t) => t.id);
      if (order.length === 0) break;
      const at = order.indexOf(state.active);
      const next = ((at + (e.shiftKey ? -1 : 1)) % order.length + order.length) % order.length;
      invoke("tab_activate", { id: order[next] });
      break;
    }
    default: {
      // Ctrl+1..8 jump to a position, Ctrl+9 to the last tab.
      if (!/^[1-9]$/.test(e.key)) break;
      e.preventDefault();
      const index = e.key === "9" ? state.tabs.length - 1 : Number(e.key) - 1;
      const target = state.tabs[index];
      if (target) invoke("tab_activate", { id: target.id });
    }
  }
});

// -------------------------------------------------------------- events

listen("state-changed", ({ payload }) => { state = payload; render(); });

listen("navigation-blocked", ({ payload }) => {
  // Rust refused the navigation; send that tab to the warning page with the
  // blocked target so it can be shown and optionally overridden.
  const page = `warning.html?url=${encodeURIComponent(payload.url)}&reason=${payload.reason}`;
  invoke("tab_navigate", { id: payload.tab, input: page });
});

listen("settings-changed", async () => {
  settings = await invoke("get_settings");
  applySettings();
});

function applySettings() {
  const root = document.documentElement;
  root.dataset.theme = (settings.ThemeMode || "Dark").toLowerCase();
  root.dataset.addrsize = settings.AddressBarSize || "Normal";
  root.style.setProperty("--accent", settings.AccentColorHex || "#4C8DFF");

  const guards = [];
  if (settings.IsAdBlockEnabled) guards.push("pubs");
  if (settings.IsPhishingProtectionEnabled) guards.push("hameçonnage");
  el.shield.textContent = guards.length ? `⛨ ${guards.join(" · ")}` : "";
}

// --------------------------------------------------------------- boot

(async function init() {
  [settings, engines, groupColors, state] = await Promise.all([
    invoke("get_settings"),
    invoke("get_search_engines"),
    invoke("get_group_colors"),
    invoke("get_state"),
  ]);

  applySettings();
  render();

  // Rust restores the session before the chrome exists, so there is normally
  // already a tab; only a genuinely empty state needs one opened.
  if (state.tabs.length === 0) await invoke("tab_create", { url: null });

  const refreshMemory = async () => {
    el.memory.textContent = `${await invoke("get_memory_usage")} Mo`;
  };
  refreshMemory();
  setInterval(refreshMemory, 5000);
})();
