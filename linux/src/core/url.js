"use strict";
// Address-bar rules shared by every Navigatueur: the Windows app's
// Navigatueur.Core.UrlHelper and InternalPages, with the same test matrix
// (test/core.test.js).

const SEARCH_ENGINES = [
  { id: "Bing", name: "Bing", prefix: "https://www.bing.com/search?q=", action: "https://www.bing.com/search" },
  { id: "Google", name: "Google", prefix: "https://www.google.com/search?q=", action: "https://www.google.com/search" },
  { id: "DuckDuckGo", name: "DuckDuckGo", prefix: "https://duckduckgo.com/?q=", action: "https://duckduckgo.com/" },
  { id: "Qwant", name: "Qwant", prefix: "https://www.qwant.com/?q=", action: "https://www.qwant.com/" },
  { id: "Ecosia", name: "Ecosia", prefix: "https://www.ecosia.org/search?q=", action: "https://www.ecosia.org/search" },
];

function engine(id) {
  return SEARCH_ENGINES.find((e) => e.id === id) || SEARCH_ENGINES[0];
}

// Navigatueur's own pages, served from the navigatueur:// protocol.
const INTERNAL_ORIGIN = "navigatueur://home/";
const PAGES = { newtab: "index.html", settings: "settings.html", history: "history.html" };
const NEW_TAB_URL = INTERNAL_ORIGIN + PAGES.newtab;

function isInternal(url) {
  return typeof url === "string" && url.toLowerCase().startsWith(INTERNAL_ORIGIN);
}

/** What the address bar shows: nothing for the new tab page, "navigatueur://settings" for the others. */
function toDisplayUrl(url) {
  if (!isInternal(url)) return url || "";
  const rest = url.slice(INTERNAL_ORIGIN.length);
  const cut = rest.search(/[?#]/);
  const file = (cut < 0 ? rest : rest.slice(0, cut)).toLowerCase();
  const suffix = cut < 0 ? "" : rest.slice(cut);
  for (const [name, page] of Object.entries(PAGES)) {
    if (file === page) return name === "newtab" && !suffix ? "" : "navigatueur://" + name + suffix;
  }
  return url;
}

/** "navigatueur://settings" (typed) → the page's real address; null when it isn't one. */
function fromDisplayUrl(input) {
  const trimmed = input.trim();
  if (!/^navigatueur:\/\//i.test(trimmed) || isInternal(trimmed)) return null;
  const rest = trimmed.slice("navigatueur://".length);
  const cut = rest.search(/[?#]/);
  const name = (cut < 0 ? rest : rest.slice(0, cut)).replace(/\/+$/, "").toLowerCase();
  const suffix = cut < 0 ? "" : rest.slice(cut);
  return INTERNAL_ORIGIN + (PAGES[name] || PAGES.newtab) + suffix;
}

function looksLikeHost(text) {
  if (text.includes(" ")) return false;
  if (/^localhost(:|$)/i.test(text)) return true;
  const host = text.split("/")[0].split(":")[0];
  return host.includes(".") && !host.endsWith(".") && !host.startsWith(".");
}

function absoluteScheme(text) {
  const m = /^([a-z][a-z0-9+.-]*):(.*)$/i.exec(text);
  if (!m) return null;
  // "localhost:8080", "example.com:8443/x": a host with a port, not a scheme.
  if (/^\d/.test(m[2])) return null;
  return m[1].toLowerCase();
}

/** Free-form address-bar text → a URL to load: kept as is when it already is one, https:// for a bare host, a search otherwise. */
function normalize(input, searchPrefix = SEARCH_ENGINES[0].prefix) {
  const trimmed = (input || "").trim();
  if (!trimmed) return "";
  const internal = fromDisplayUrl(trimmed);
  if (internal) return internal;
  const scheme = absoluteScheme(trimmed);
  if (scheme && ["http", "https", "file", "about", "data", "navigatueur", "view-source", "chrome-extension"].includes(scheme)) return trimmed;
  if (looksLikeHost(trimmed)) return "https://" + trimmed;
  return searchPrefix + encodeURIComponent(trimmed);
}

/** The host without "www.", or null. */
function displayHost(url) {
  try {
    const u = new URL(url);
    if (!u.hostname) return null;
    return u.hostname.replace(/^www\./i, "");
  } catch {
    return null;
  }
}

function isWebUrl(url) {
  return /^https?:\/\//i.test(url || "");
}

/** Same page for bookmarks and suggestions: lower-case scheme/host, no default port, no "/" path, no fragment. */
function normalizeKey(url) {
  try {
    const u = new URL(url.trim());
    const path = u.pathname === "/" ? "" : u.pathname;
    return `${u.protocol}//${u.host.toLowerCase()}${path}${u.search}`;
  } catch {
    return (url || "").trim();
  }
}

/** The words of a search, when [url] is an engine's results page. */
function searchTerms(url) {
  if (!url) return null;
  const e = SEARCH_ENGINES.find((x) => url.startsWith(x.prefix));
  if (!e) return null;
  const raw = url.slice(e.prefix.length).split("&")[0].split("#")[0];
  try {
    return decodeURIComponent(raw.replace(/\+/g, " ")) || null;
  } catch {
    return null;
  }
}

module.exports = {
  SEARCH_ENGINES,
  engine,
  INTERNAL_ORIGIN,
  NEW_TAB_URL,
  PAGES,
  isInternal,
  toDisplayUrl,
  fromDisplayUrl,
  normalize,
  displayHost,
  isWebUrl,
  normalizeKey,
  searchTerms,
  looksLikeHost,
};
