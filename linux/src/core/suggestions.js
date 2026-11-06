"use strict";
// Address-bar suggestions — the Windows app's AddressSuggestions: a match at
// the start of the host beats one at the start of a title word, which beats
// a match anywhere; bookmarks get a boost; each page appears once.
const { displayHost, normalizeKey, isWebUrl } = require("./url");

function stripScheme(url) {
  const i = url.indexOf("://");
  const rest = i < 0 ? url : url.slice(i + 3);
  return rest.replace(/^www\./i, "");
}

function startsAnyWord(text, needle) {
  return text.split(/[ \-|·:/]+/).some((w) => w && w.toLowerCase().startsWith(needle));
}

function score(needle, source) {
  const host = (displayHost(source.url) || "").toLowerCase();
  const url = stripScheme(source.url).toLowerCase();
  const title = (source.title || "").toLowerCase();
  let s;
  if (host.startsWith(needle)) s = 100;
  else if (url.startsWith(needle)) s = 90;
  else if (startsAnyWord(title, needle)) s = 70;
  else if (url.includes(needle)) s = 50;
  else if (title.includes(needle)) s = 40;
  else return 0;
  return source.kind === "bookmark" ? s + 15 : source.kind === "tab" ? s + 10 : s;
}

/** sources: iterable of { url, title, kind: "bookmark" | "history" | "tab" }. */
function rank(query, sources, limit = 6) {
  const needle = (query || "").trim().toLowerCase();
  if (!needle || limit <= 0) return [];
  const best = new Map();
  for (const source of sources) {
    if (!source.url || !isWebUrl(source.url)) continue;
    const s = score(needle, source);
    if (s <= 0) continue;
    const key = normalizeKey(source.url);
    const existing = best.get(key);
    if (!existing || existing.score < s) best.set(key, { ...source, score: s });
  }
  return [...best.values()].sort((a, b) => b.score - a.score || a.url.length - b.url.length).slice(0, limit);
}

module.exports = { rank };
