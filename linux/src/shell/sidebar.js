"use strict";
// The tab column: pinned tabs, loose tabs, groups (named, colored,
// foldable), drag and drop, hover cards with a picture of the page, and a
// grip to resize the column — the Windows app's TabsPanel.

const $ = (id) => document.getElementById(id);
const list = $("list");
const card = $("card");
let state = null;
let layoutInfo = { right: false, sidebarWidth: 280, extra: 0 };
let dragId = null;
let dragGroup = null;
let renaming = null;
let cardTimer = 0;
let cardHideTimer = 0;
let cardOpenedAt = 0;

const send = (type, data) => window.nav.send(type, data);
$("newBtn").querySelector(".chip").innerHTML = icon("plus", 17);
$("dockBtn").querySelector(".chip").innerHTML = icon("pin", 14);
$("newBtn").onclick = () => send("command", { command: "tab.new" });
$("dockBtn").onclick = () => send("command", { command: "sidebar.toggleDock" });

window.nav.on("state", (next) => {
  state = next;
  applyPalette(next.palette, next.settings);
  $("count").textContent = String(next.tabs.length);
  $("dockBtn").classList.toggle("on", !!next.settings.sidebarDocked);
  const wallpaper = next.settings.chromeBackground;
  $("wallpaper").style.backgroundImage = wallpaper ? `url("${wallpaper}")` : "";
  render();
});

window.nav.on("layout", (info) => {
  layoutInfo = info;
  document.body.classList.toggle("right", !!info.right);
  $("panel").style.width = info.sidebarWidth + "px";
});

function faviconHtml(tab) {
  if (tab.loading) return icon("spinner", 15, "spin");
  if (tab.favicon) return `<img src="${escapeHtml(tab.favicon)}" alt="" data-fallback="1">`;
  return `<span style="color:var(--text3)">${icon("globe", 15)}</span>`;
}

function tabRow(tab, group) {
  const classes = ["row", "tab"];
  if (tab.id === state.activeId) classes.push("active");
  if (tab.suspended) classes.push("suspended");
  if (tab.audible) classes.push("audible");
  if (tab.muted) classes.push("muted");
  if (group) classes.push("in-group");
  return `<div class="${classes.join(" ")}" draggable="true" data-id="${tab.id}" ${group ? `style="--group-color:${group.color}"` : ""}>
    <span class="marker"></span>
    <span class="fav">${faviconHtml(tab)}</span>
    <span class="title">${escapeHtml(tab.title)}</span>
    <span class="audio" data-act="mute" title="Couper / réactiver le son">${icon(tab.muted ? "mute" : "volume", 13)}</span>
    ${tab.pinned ? `<span class="pin">${icon("pin", 12)}</span>` : ""}
    <span class="close" data-act="close" title="Fermer l'onglet (Ctrl+W)">${icon("close", 11)}</span>
  </div>`;
}

function newTabButton() {
  return `<div class="newtab" data-act="new">${icon("plus", 14)}<span>Nouvel onglet</span><span class="kbd">Ctrl+T</span></div>`;
}

function render() {
  if (!state) return;
  const scroll = list.scrollTop;
  const tabs = state.tabs;
  const pinned = tabs.filter((t) => t.pinned);
  const loose = tabs.filter((t) => !t.pinned && !t.groupId);
  let html = "";
  for (const t of pinned) html += tabRow(t);
  for (const t of loose) html += tabRow(t);
  for (const g of state.groups) {
    const members = tabs.filter((t) => !t.pinned && t.groupId === g.id);
    if (!members.length) continue;
    const name = renaming === g.id
      ? `<input value="${escapeHtml(g.name)}" data-rename="${g.id}">`
      : `<span class="title">${escapeHtml(g.name)}</span>`;
    html += `<div class="row group-header" draggable="true" data-group="${g.id}" style="--group-color:${g.color}" title="Glisser pour déplacer le groupe">
      <span class="fav">${icon(g.collapsed ? "folder" : "folderOpen", 15)}</span>${name}
      <span class="count" style="margin:0 6px;color:var(--text3);font-weight:400">${members.length}</span>
      <span class="chev">${icon(g.collapsed ? "chevronRight" : "chevronDown", 13)}</span>
    </div>`;
    if (!g.collapsed) for (const t of members) html += tabRow(t, g);
  }
  const afterTabs = state.settings.newTabButton === "AfterTabs";
  if (afterTabs) html += newTabButton();
  list.innerHTML = html;
  list.scrollTop = scroll;
  $("footer").innerHTML = afterTabs ? "" : newTabButton();
  for (const img of list.querySelectorAll("img[data-fallback]")) {
    img.addEventListener("error", () => (img.outerHTML = `<span style="color:var(--text3)">${icon("globe", 15)}</span>`));
  }
  const input = list.querySelector("input[data-rename]");
  if (input) {
    input.focus();
    input.select();
    send("hold", { on: true });
  }
}

// ------------------------------------------------------------- clicks

document.addEventListener("click", (e) => {
  const act = e.target.closest("[data-act]");
  const row = e.target.closest(".row");
  if (act && act.dataset.act === "new") return send("command", { command: "tab.new" });
  if (act && row && row.dataset.id) {
    e.stopPropagation();
    return send(act.dataset.act, { id: row.dataset.id });
  }
  if (row && row.dataset.id) {
    hideCard(true);
    send("select", { id: row.dataset.id });
    send("focus-page");
  } else if (row && row.dataset.group && !e.target.closest("input")) {
    send("group", { id: row.dataset.group, action: "toggle" });
  }
});

document.addEventListener("auxclick", (e) => {
  const row = e.target.closest(".row");
  if (e.button === 1 && row && row.dataset.id) send("close", { id: row.dataset.id });
});

document.addEventListener("dblclick", (e) => {
  const row = e.target.closest(".group-header");
  if (row) startRename(row.dataset.group);
});

document.addEventListener("contextmenu", (e) => {
  const row = e.target.closest(".row");
  if (!row) return;
  e.preventDefault();
  hideCard(true);
  if (row.dataset.id) send("tab-menu", { id: row.dataset.id });
  else if (row.dataset.group) send("group-menu", { id: row.dataset.group });
});

function startRename(id) {
  renaming = id;
  render();
}

list.addEventListener("keydown", (e) => {
  const input = e.target.closest("input[data-rename]");
  if (input) {
    if (e.key === "Enter") finishRename(input, true);
    if (e.key === "Escape") finishRename(input, false);
    return;
  }
  const rows = [...list.querySelectorAll(".row.tab")];
  const current = rows.indexOf(document.activeElement);
  if (e.key === "ArrowDown" || e.key === "ArrowUp") {
    const next = rows[Math.max(0, Math.min(rows.length - 1, current + (e.key === "ArrowDown" ? 1 : -1)))];
    if (next) next.focus();
    e.preventDefault();
  } else if (e.key === "Enter" && current >= 0) {
    send("select", { id: rows[current].dataset.id });
    send("focus-page");
    send("hold", { on: false });
  } else if (e.key === "Delete" && current >= 0) {
    send("close", { id: rows[current].dataset.id });
  } else if (e.key === "Escape") {
    send("focus-page");
    send("hold", { on: false });
  }
});

list.addEventListener("focusout", (e) => {
  const input = e.target.closest && e.target.closest("input[data-rename]");
  if (input) finishRename(input, true);
});

function finishRename(input, save) {
  const id = input.dataset.rename;
  renaming = null;
  if (save) send("group", { id, action: "rename", value: input.value });
  send("hold", { on: false });
  render();
}

window.nav.on("rename-group", ({ id }) => startRename(id));
window.nav.on("focus-tabs", () => {
  const active = list.querySelector(".row.active");
  for (const row of list.querySelectorAll(".row.tab")) row.tabIndex = -1;
  if (active) {
    active.tabIndex = 0;
    active.focus();
  }
});

// -------------------------------------------------------- drag & drop

document.addEventListener("dragstart", (e) => {
  const row = e.target.closest(".row");
  if (!row) return;
  hideCard(true);
  dragId = row.dataset.id || null;
  dragGroup = row.dataset.group || null;
  e.dataTransfer.effectAllowed = "move";
  e.dataTransfer.setData("text/plain", dragId || dragGroup);
  send("hold", { on: true });
});

function clearDrop() {
  for (const el of list.querySelectorAll(".drop-before, .drop-after, .drop-into")) el.classList.remove("drop-before", "drop-after", "drop-into");
}

document.addEventListener("dragover", (e) => {
  const row = e.target.closest(".row");
  if (!row || (!dragId && !dragGroup)) return;
  e.preventDefault();
  clearDrop();
  const r = row.getBoundingClientRect();
  const y = (e.clientY - r.top) / r.height;
  if (dragId && row.dataset.group) row.classList.add("drop-into");
  else row.classList.add(y < 0.5 ? "drop-before" : "drop-after");
});

document.addEventListener("drop", (e) => {
  const row = e.target.closest(".row");
  e.preventDefault();
  if (row) {
    const position = row.classList.contains("drop-into") ? "group" : row.classList.contains("drop-before") ? "before" : "after";
    if (dragId && row.dataset.id && row.dataset.id !== dragId) send("move", { id: dragId, targetId: row.dataset.id, position });
    else if (dragId && row.dataset.group) send("move", { id: dragId, targetId: row.dataset.group, position: "group" });
    else if (dragGroup && row.dataset.group && row.dataset.group !== dragGroup) send("group", { id: dragGroup, action: "move", value: { targetId: row.dataset.group, after: position === "after" } });
  }
  clearDrop();
});

document.addEventListener("dragend", () => {
  dragId = null;
  dragGroup = null;
  clearDrop();
  send("hold", { on: false });
});

// ---------------------------------------------------------- hover card

list.addEventListener("mouseover", (e) => {
  const row = e.target.closest(".row.tab");
  if (!row || !state || !state.settings.previews || dragId) return;
  clearTimeout(cardHideTimer);
  const quick = card.classList.contains("open") || Date.now() - cardOpenedAt < 350;
  clearTimeout(cardTimer);
  cardTimer = setTimeout(() => showCard(row), quick ? 0 : 450);
});

list.addEventListener("mouseout", (e) => {
  const row = e.target.closest(".row.tab");
  if (!row || (e.relatedTarget && row.contains(e.relatedTarget))) return;
  clearTimeout(cardTimer);
  cardHideTimer = setTimeout(() => hideCard(), 140);
});

document.addEventListener("mouseenter", () => send("sidebar-hover"));
document.addEventListener("mouseleave", () => hideCard());
list.addEventListener("scroll", () => hideCard(true), { passive: true });

function showCard(row) {
  const tab = state.tabs.find((t) => t.id === row.dataset.id);
  if (!tab) return;
  const status = tab.inAutoPip ? "Vidéo en image dans l'image" : tab.suspended ? "En veille : se recharge à l'ouverture" : tab.muted ? "Son coupé" : tab.audible ? "Lecture du son" : "";
  const showShot = tab.thumbnail && tab.id !== state.activeId;
  const host = tab.internal ? "Page Navigatueur" : hostOf(tab.url);
  card.innerHTML = `${showShot ? `<div class="shot" style="background-image:url('${tab.thumbnail}')"></div>` : ""}
    <div class="body"><div class="t">${escapeHtml(tab.title)}</div>
    <div class="h">${tab.favicon ? `<img src="${escapeHtml(tab.favicon)}" alt="">` : icon("globe", 13)}<span>${escapeHtml(host)}</span></div>
    ${status ? `<div class="s">${escapeHtml(status)}</div>` : ""}</div>`;
  const rowRect = row.getBoundingClientRect();
  const width = layoutInfo.sidebarWidth;
  const gap = 10;
  card.style.left = layoutInfo.right ? "0px" : width + gap + "px";
  card.classList.add("open");
  const top = Math.max(6, Math.min(rowRect.top - 4, window.innerHeight - card.offsetHeight - 8));
  card.style.top = top + "px";
  cardOpenedAt = Date.now();
  if (layoutInfo.extra < 300) send("expand", { width: 276 + gap + 6 });
  if (layoutInfo.right) document.body.style.paddingLeft = "0";
}

function hideCard(now) {
  clearTimeout(cardTimer);
  if (!card.classList.contains("open")) return;
  card.classList.remove("open");
  cardOpenedAt = now ? 0 : Date.now();
  send("expand", { width: 0 });
}

// --------------------------------------------------------------- grip

const grip = $("grip");
grip.addEventListener("pointerdown", (e) => {
  e.preventDefault();
  grip.setPointerCapture(e.pointerId);
  const startX = e.screenX;
  const startWidth = layoutInfo.sidebarWidth;
  let pending = 0;
  send("hold", { on: true });
  const widthFor = (ev) => Math.min(440, Math.max(200, startWidth + (ev.screenX - startX) * (layoutInfo.right ? -1 : 1)));
  const move = (ev) => {
    const width = widthFor(ev);
    $("panel").style.width = width + "px";
    if (!pending) pending = requestAnimationFrame(() => {
      pending = 0;
      send("sidebar-width", { width });
    });
  };
  const up = (ev) => {
    grip.removeEventListener("pointermove", move);
    grip.removeEventListener("pointerup", up);
    send("sidebar-width", { width: widthFor(ev) });
    send("hold", { on: false });
  };
  grip.addEventListener("pointermove", move);
  grip.addEventListener("pointerup", up);
});

send("ready");
