"use strict";
// Where Chromium's auto dark mode is on: everywhere or nowhere by default,
// with per-site choices on top — the Windows app's ForceDarkRules. A choice
// covers the site's subdomains; only choices that differ from the default
// are kept.

const APP_HOSTS = new Set(["home", "warning", "userbg"]);

function siteKey(url) {
  try {
    const u = new URL(url);
    if (u.protocol !== "http:" && u.protocol !== "https:") return null;
    if (!u.hostname || APP_HOSTS.has(u.hostname)) return null;
    return u.hostname.toLowerCase().replace(/^www\./, "");
  } catch {
    return null;
  }
}

function resolve(host, enabledByDefault, sites) {
  for (let candidate = host; candidate; ) {
    if (Object.prototype.hasOwnProperty.call(sites, candidate)) return !!sites[candidate];
    const dot = candidate.indexOf(".");
    candidate = dot < 0 ? "" : candidate.slice(dot + 1);
  }
  return enabledByDefault;
}

function isEnabledFor(url, enabledByDefault, sites) {
  const key = siteKey(url);
  return key ? resolve(key, enabledByDefault, sites || {}) : false;
}

function setForSite(url, enabled, enabledByDefault, sites) {
  const key = siteKey(url);
  if (!key) return;
  delete sites[key];
  if (resolve(key, enabledByDefault, sites) !== enabled) sites[key] = enabled;
}

module.exports = { siteKey, isEnabledFor, setForSite };
