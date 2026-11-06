"use strict";
// The toolbar: navigation buttons, the address bar with its suggestions,
// downloads, the "⋯" menu and the window buttons (the window has no system
// title bar, like on Windows).

const $ = (id) => document.getElementById(id);
const address = $("address");
const addressBox = $("addressBox");
const suggestionsBox = $("suggestions");
const downloadsBox = $("downloads");
let state = null;
let layoutInfo = null;
let suggestions = [];
let selected = 0;
let typedText = "";
let downloadsOpen = false;

const ENGINES = { Bing: "Bing", Google: "Google", DuckDuckGo: "DuckDuckGo", Qwant: "Qwant", Ecosia: "Ecosia" };

function setIcon(button, name, size = 17) {
  button.querySelector(".chip").innerHTML = icon(name, size);
}

setIcon($("backBtn"), "back");
setIcon($("forwardBtn"), "forward");
setIcon($("reloadBtn"), "refresh");
setIcon($("downloadsBtn"), "download");
setIcon($("menuBtn"), "more", 18);
setIcon($("minBtn"), "minimize", 13);
setIcon($("closeBtn"), "close", 13);
setIcon($("findPrev"), "chevronUp", 15);
setIcon($("findNext"), "chevronDown", 15);
setIcon($("findClose"), "close", 14);
$("star").innerHTML = icon("star", 15);

const send = (type, data) => window.nav.send(type, data);
const command = (name, data) => send("command", { command: name, ...data });

$("tabsBtn").onclick = () => command("tabs.show");
$("backBtn").onclick = () => command("page.back");
$("forwardBtn").onclick = () => command("page.forward");
$("reloadBtn").onclick = () => command(state && state.active && state.active.loading ? "page.stop" : "page.reload");
$("star").onclick = () => command("bookmark.toggle");
$("minBtn").onclick = () => send("window", { action: "minimize" });
$("maxBtn").onclick = () => send("window", { action: "maximize" });
$("closeBtn").onclick = () => send("window", { action: "close" });
$("menuBtn").onclick = () => {
  const r = $("menuBtn").getBoundingClientRect();
  command("menu", { x: Math.round(r.left), y: Math.round(r.bottom - 6) });
};
$("downloadsBtn").onclick = () => toggleDownloads(!downloadsOpen);
document.getElementById("bar").addEventListener("dblclick", (e) => {
  if (e.target.closest("button, .address")) return;
  send("window", { action: "maximize" });
});

// --------------------------------------------------------------- state

window.nav.on("state", (next) => {
  state = next;
  applyPalette(next.palette, next.settings);
  const a = next.active || {};
  $("backBtn").disabled = !a.canGoBack;
  $("forwardBtn").disabled = !a.canGoForward;
  setIcon($("reloadBtn"), a.loading ? "close" : "refresh", a.loading ? 15 : 17);
  $("reloadBtn").title = a.loading ? "Arrêter" : "Actualiser (Ctrl+R)";
  setIcon($("maxBtn"), next.maximized ? "restore" : "maximize", 13);
  $("star").classList.toggle("on", !!a.bookmarked);
  $("star").style.display = a.internal ? "none" : "";
  $("menuBtn").classList.toggle("update", !!(next.update && next.update.available));
  address.placeholder = next.isPrivate ? "Recherche privée ou adresse" : "Rechercher ou saisir une adresse";
  if (document.activeElement !== address) address.value = a.display || "";
  renderSecurity(a);
  renderDownloadsButton();
  if (downloadsOpen) renderDownloads();
  const wallpaper = next.settings && next.settings.chromeBackground;
  $("wallpaper").style.backgroundImage = wallpaper ? `url("${wallpaper}")` : "";
  $("wallpaper").style.background = wallpaper ? "" : `linear-gradient(90deg, ${hexAlpha(next.palette.accent, 0.10)}, transparent 60%)`;
  if (wallpaper) $("wallpaper").style.backgroundSize = "cover";
});

window.nav.on("layout", (info) => {
  layoutInfo = info;
  setIcon($("tabsBtn"), info.right ? "panelRight" : "panelLeft", 17);
  document.body.classList.toggle("find-open", !!info.findOpen);
  positionDropdowns();
});

function renderSecurity(a) {
  const sec = $("sec");
  if (!a.url || a.internal || !a.display) sec.innerHTML = icon("search", 15);
  else if (a.favicon && a.secure) {
    sec.innerHTML = `<img class="favicon" src="${escapeHtml(a.favicon)}" alt="">`;
    sec.querySelector("img").addEventListener("error", () => (sec.innerHTML = icon("lock", 14)));
  }
  else if (a.secure) sec.innerHTML = icon("lock", 14);
  else sec.innerHTML = `<span style="color:var(--danger)">${icon("globe", 15)}</span>`;
}

// -------------------------------------------------------- address bar

address.addEventListener("focus", () => {
  addressBox.classList.add("focused");
  const a = (state && state.active) || {};
  address.value = a.internal ? (a.display || "") : a.url || "";
  address.select();
  send("hold", { on: true, focused: true });
});

address.addEventListener("blur", () => {
  addressBox.classList.remove("focused");
  closeSuggestions();
  const a = (state && state.active) || {};
  address.value = a.display || "";
  send("hold", { on: false, focused: false });
});

let suggestTimer = 0;
address.addEventListener("input", () => {
  typedText = address.value;
  clearTimeout(suggestTimer);
  suggestTimer = setTimeout(refreshSuggestions, 50);
});

address.addEventListener("keydown", (e) => {
  if (e.key === "Enter") {
    const choice = suggestions[selected];
    go(choice && choice.url && selected > 0 ? choice : null);
    e.preventDefault();
  } else if (e.key === "Escape") {
    address.blur();
    send("focus-page");
  } else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
    if (!suggestions.length) return;
    selected = (selected + (e.key === "ArrowDown" ? 1 : -1) + suggestions.length) % suggestions.length;
    renderSuggestions();
    const choice = suggestions[selected];
    address.value = selected === 0 ? typedText : choice.url;
    e.preventDefault();
  }
});

function go(choice, engine) {
  const text = address.value.trim();
  closeSuggestions();
  if (choice && choice.kind === "tab" && choice.id) send("select", { id: choice.id });
  else if (choice) send("navigate", { input: choice.url });
  else if (text) send("navigate", { input: text, engine });
  address.blur();
}

async function refreshSuggestions() {
  const text = typedText.trim();
  if (!text) {
    closeSuggestions();
    return;
  }
  const found = (await window.nav.invoke("suggest", text)) || [];
  if (address.value.trim() !== text && selected === 0) return;
  const looksLikeUrl = /^[a-z][a-z0-9+.-]*:\/\//i.test(text) || (!text.includes(" ") && /\.[a-z]{2,}(\/|$)/i.test(text)) || /^localhost(:|$)/i.test(text);
  suggestions = [
    { primary: true, kind: looksLikeUrl ? "url" : "search", title: looksLikeUrl ? text : `Rechercher « ${text} »`, sub: looksLikeUrl ? "Aller à l'adresse" : "avec le moteur de recherche" },
    ...found,
  ];
  selected = 0;
  renderSuggestions();
}

function renderSuggestions() {
  if (!suggestions.length) return closeSuggestions();
  suggestionsBox.innerHTML = suggestions
    .map((s, i) => {
      const iconName = s.primary ? (s.kind === "url" ? "globe" : "search") : s.kind === "bookmark" ? "bookmark" : s.kind === "tab" ? "tab" : "history";
      const sub = s.primary ? s.sub : s.kind === "tab" ? `Basculer vers l'onglet · ${hostOf(s.url)}` : s.url.replace(/^https?:\/\/(www\.)?/, "");
      return `<div class="row ${i === selected ? "sel" : ""} ${s.primary ? "primary" : ""}" data-index="${i}">
        <span class="badge-icon">${icon(iconName, 15)}</span>
        <span class="texts"><div class="title">${escapeHtml(s.title || hostOf(s.url))}</div><div class="sub">${escapeHtml(sub)}</div></span>
      </div>`;
    })
    .join("");
  suggestionsBox.classList.add("open");
  positionDropdowns();
}

suggestionsBox.addEventListener("mousedown", (e) => {
  const row = e.target.closest(".row");
  if (!row) return;
  e.preventDefault();
  const choice = suggestions[Number(row.dataset.index)];
  if (choice.primary) go(null);
  else go(choice);
});

function closeSuggestions() {
  suggestions = [];
  suggestionsBox.classList.remove("open");
  suggestionsBox.innerHTML = "";
  positionDropdowns();
}

// ---------------------------------------------------------- downloads

function renderDownloadsButton() {
  const running = ((state && state.downloads) || []).filter((d) => d.state === "progressing");
  $("downloadsBtn").classList.toggle("active", running.length > 0);
  $("downloadsBadge").textContent = running.length ? String(running.length) : "";
}

function size(bytes) {
  if (!bytes || bytes < 0) return "";
  if (bytes < 1024 * 1024) return Math.round(bytes / 1024) + " Ko";
  if (bytes < 1024 ** 3) return (bytes / 1024 / 1024).toFixed(1).replace(".", ",") + " Mo";
  return (bytes / 1024 ** 3).toFixed(2).replace(".", ",") + " Go";
}

function renderDownloads() {
  const list = (state && state.downloads) || [];
  downloadsBox.innerHTML = `<h3>Téléchargements</h3>` + (list.length
    ? list.map((d) => {
        const pct = d.total > 0 ? Math.round((d.received / d.total) * 100) : 0;
        const status = d.state === "progressing" ? `${size(d.received)}${d.total > 0 ? " sur " + size(d.total) : ""}` : d.state === "completed" ? size(d.total || d.received) : d.state === "cancelled" ? "Annulé" : "Échec";
        const actions = d.state === "progressing"
          ? `<button class="mini" data-act="download-cancel" data-id="${d.id}" title="Annuler">${icon("close", 13)}</button>`
          : d.state === "completed" ? `<button class="mini" data-act="download-show" data-id="${d.id}" title="Afficher dans le dossier">${icon("folderOpen", 14)}</button>` : "";
        return `<div class="row" data-open="${d.state === "completed" ? d.id : ""}">
          <span class="badge-icon">${icon("file", 15)}</span>
          <span class="texts"><div class="title">${escapeHtml(d.name)}</div><div class="sub">${escapeHtml(status)}</div>
          ${d.state === "progressing" ? `<div class="bar"><i style="width:${pct}%"></i></div>` : ""}</span>${actions}</div>`;
      }).join("")
    : `<div class="empty">Aucun téléchargement</div>`);
  positionDropdowns();
}

downloadsBox.addEventListener("click", (e) => {
  const act = e.target.closest("[data-act]");
  if (act) {
    send(act.dataset.act, { id: act.dataset.id });
    return;
  }
  const row = e.target.closest(".row");
  if (row && row.dataset.open) send("download-open", { id: row.dataset.open });
});

function toggleDownloads(open) {
  downloadsOpen = open;
  downloadsBox.classList.toggle("open", open);
  if (open) renderDownloads();
  send("hold", { on: open });
  positionDropdowns();
}

document.addEventListener("mousedown", (e) => {
  if (downloadsOpen && !e.target.closest("#downloads, #downloadsBtn")) toggleDownloads(false);
});

window.nav.on("open-downloads", () => toggleDownloads(true));
window.nav.on("download-started", () => {
  renderDownloadsButton();
});
window.nav.on("hidden", () => {
  if (downloadsOpen) toggleDownloads(false);
  closeSuggestions();
});

/** The toolbar's view grows while a dropdown is open, so the dropdown can hang over the page. */
function positionDropdowns() {
  const box = addressBox.getBoundingClientRect();
  suggestionsBox.style.left = box.left + "px";
  suggestionsBox.style.width = box.width + "px";
  const findHeight = document.body.classList.contains("find-open") ? 44 : 0;
  suggestionsBox.style.top = 46 + findHeight + "px";
  downloadsBox.style.top = 46 + findHeight + "px";
  let extra = 0;
  for (const el of [suggestionsBox, downloadsBox]) {
    if (el.classList.contains("open")) extra = Math.max(extra, el.offsetHeight + 16);
  }
  send("expand", { height: extra });
}

// --------------------------------------------------------------- find

const findInput = $("findInput");
window.nav.on("open-find", () => {
  document.body.classList.add("find-open");
  findInput.focus();
  findInput.select();
  send("hold", { on: true, focused: true });
});
findInput.addEventListener("input", () => send("find", { text: findInput.value }));
findInput.addEventListener("keydown", (e) => {
  if (e.key === "Enter") send("find", { text: findInput.value, forward: !e.shiftKey, next: true });
  if (e.key === "Escape") closeFind();
});
$("findNext").onclick = () => send("find", { text: findInput.value, forward: true, next: true });
$("findPrev").onclick = () => send("find", { text: findInput.value, forward: false, next: true });
$("findClose").onclick = closeFind;
function closeFind() {
  document.body.classList.remove("find-open");
  findInput.value = "";
  $("findCount").textContent = "";
  send("find-close");
  send("hold", { on: false });
}
window.nav.on("find-result", (r) => {
  $("findCount").textContent = r.matches ? `${r.activeMatchOrdinal}/${r.matches}` : findInput.value ? "0/0" : "";
});

window.nav.on("focus-address", () => {
  address.focus();
  address.select();
});

send("ready");
