"use strict";
// What Navigatueur blocks: ads and trackers (Ghostery's engine, which reads
// the same filter lists as uBlock Origin — EasyList, EasyPrivacy, uBlock's
// own lists, cookie notices), and phishing sites from the Windows app's list.
const fs = require("fs");
const path = require("path");
const readline = require("readline");
const { app } = require("electron");
const { DomainSet } = require("../core/domains");
const forceDark = require("../core/forcedark");

class Protection {
  constructor(store, resourcesDir) {
    this.store = store;
    this.resourcesDir = resourcesDir;
    this.blocker = null;
    this.sessions = new Set();
    this.disabledHosts = new Set();
    this.phishing = new DomainSet();
    this.bypassed = new Set();
  }

  async start() {
    this.loadPhishingList();
    try {
      const { ElectronBlocker } = require("@ghostery/adblocker-electron");
      const cache = path.join(app.getPath("userData"), "adblock-engine.bin");
      // Ads, trackers and annoyances (cookie notices…), prebuilt by Ghostery from the usual lists; cached on disk.
      this.blocker = await ElectronBlocker.fromPrebuiltFull(fetch, {
        path: cache,
        read: fs.promises.readFile,
        write: fs.promises.writeFile,
      });
      for (const session of this.sessions) this.apply(session);
    } catch (error) {
      console.error("Bloqueur de publicités indisponible", error);
    }
  }

  loadPhishingList() {
    // "phishing" in the packaged app, "Phishing" in the Windows app's sources (run from source).
    const file = ["phishing", "Phishing"].map((d) => path.join(this.resourcesDir, d, "phishing-domains.txt")).find((f) => fs.existsSync(f));
    if (!file) return;
    const reader = readline.createInterface({ input: fs.createReadStream(file), crlfDelay: Infinity });
    const lines = [];
    reader.on("line", (line) => lines.push(line));
    reader.on("close", () => {
      this.phishing = DomainSet.parse(lines);
    });
  }

  attach(session) {
    this.sessions.add(session);
    this.apply(session);
  }

  apply(session) {
    if (!this.blocker) return;
    const enabled = this.store.settings.isAdBlockEnabled;
    const active = this.blocker.isBlockingEnabled(session);
    if (enabled && !active) this.blocker.enableBlockingInSession(session);
    if (!enabled && active) this.blocker.disableBlockingInSession(session);
  }

  refresh() {
    for (const session of this.sessions) this.apply(session);
  }

  isPhishing(host) {
    if (!host || !this.store.settings.isPhishingProtectionEnabled) return false;
    return !this.bypassed.has(host.toLowerCase()) && this.phishing.has(host);
  }

  allowOnce(host) {
    if (host) this.bypassed.add(host.toLowerCase());
  }
}

/** The forced dark mode's switches, with per-site choices (private windows: this session only). */
class ForceDark {
  constructor(store) {
    this.store = store;
    this.privateSites = {};
  }

  isEnabledFor(url, isPrivate) {
    const s = this.store.settings;
    const saved = forceDark.isEnabledFor(url, s.isForceDarkEnabled, s.forceDarkSites);
    return isPrivate ? forceDark.isEnabledFor(url, saved, this.privateSites) : saved;
  }

  setForSite(url, enabled, isPrivate) {
    const s = this.store.settings;
    if (isPrivate) {
      const saved = forceDark.isEnabledFor(url, s.isForceDarkEnabled, s.forceDarkSites);
      forceDark.setForSite(url, enabled, saved, this.privateSites);
      this.store.emit("forcedark");
      return;
    }
    this.store.updateSettings((settings) => forceDark.setForSite(url, enabled, settings.isForceDarkEnabled, settings.forceDarkSites));
    this.store.emit("forcedark");
  }

  setEnabledByDefault(enabled) {
    this.store.updateSettings((s) => (s.isForceDarkEnabled = enabled));
    this.store.emit("forcedark");
  }

  clear() {
    this.privateSites = {};
    this.store.updateSettings((s) => (s.forceDarkSites = {}));
    this.store.emit("forcedark");
  }

  get siteCount() {
    return Object.keys(this.store.settings.forceDarkSites).length;
  }
}

module.exports = { Protection, ForceDark };
