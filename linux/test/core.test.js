"use strict";
// Same matrix as the Windows app's UrlHelperTests / NavigationTests.
const test = require("node:test");
const assert = require("node:assert");
const url = require("../src/core/url");
const { rank } = require("../src/core/suggestions");
const { DomainSet } = require("../src/core/domains");
const { isNewer, pickAsset } = require("../src/core/versions");

test("addresses that already have a scheme pass through", () => {
  assert.strictEqual(url.normalize("https://example.com"), "https://example.com");
  assert.strictEqual(url.normalize("http://example.com/path"), "http://example.com/path");
  assert.strictEqual(url.normalize("about:blank"), "about:blank");
});

test("a bare host gets https://", () => {
  assert.strictEqual(url.normalize("example.com"), "https://example.com");
  assert.strictEqual(url.normalize("example.com/path"), "https://example.com/path");
  assert.strictEqual(url.normalize("localhost:5000"), "https://localhost:5000");
});

test("plain text becomes a search with the chosen engine", () => {
  assert.strictEqual(url.normalize("meilleur navigateur web"), "https://www.bing.com/search?q=meilleur%20navigateur%20web");
  assert.strictEqual(url.normalize("chat", url.engine("DuckDuckGo").prefix), "https://duckduckgo.com/?q=chat");
  assert.strictEqual(url.normalize("   "), "");
});

test("internal pages have short addresses", () => {
  assert.strictEqual(url.toDisplayUrl("navigatueur://home/index.html"), "");
  assert.strictEqual(url.toDisplayUrl("navigatueur://home/settings.html"), "navigatueur://settings");
  assert.strictEqual(url.toDisplayUrl("navigatueur://home/settings.html#interface"), "navigatueur://settings#interface");
  assert.strictEqual(url.toDisplayUrl("https://example.com/"), "https://example.com/");
  assert.strictEqual(url.normalize("navigatueur://settings"), "navigatueur://home/settings.html");
  assert.strictEqual(url.normalize("NAVIGATUEUR://History/"), "navigatueur://home/history.html");
  assert.strictEqual(url.normalize("navigatueur://inconnu"), "navigatueur://home/index.html");
});

test("display host and keys", () => {
  assert.strictEqual(url.displayHost("https://www.example.com/a"), "example.com");
  assert.strictEqual(url.displayHost("pas une adresse"), null);
  assert.strictEqual(url.normalizeKey("https://Example.com/"), "https://example.com");
  assert.strictEqual(url.normalizeKey("https://example.com/page#section"), "https://example.com/page");
  assert.strictEqual(url.searchTerms("https://www.bing.com/search?q=meilleur%20navigateur&form=x"), "meilleur navigateur");
});

test("suggestions: host prefix wins, pages appear once, bookmarks first", () => {
  const sources = [
    { url: "https://www.youtube.com/", title: "YouTube", kind: "history" },
    { url: "https://fr.wikipedia.org/wiki/Navigateur_web", title: "Navigateur web — Wikipédia", kind: "history" },
    { url: "https://github.com/jubifox/navigatueur", title: "Navigatueur sur GitHub", kind: "bookmark" },
    { url: "https://www.youtube.com", title: "YouTube (doublon)", kind: "bookmark" },
  ];
  const you = rank("you", sources);
  assert.strictEqual(you.length, 1);
  assert.strictEqual(you[0].kind, "bookmark");
  const nav = rank("navig", sources);
  assert.strictEqual(nav[0].url, "https://github.com/jubifox/navigatueur");
  assert.strictEqual(rank("  ", sources).length, 0);
});

test("phishing list: a domain and its subdomains", () => {
  const set = DomainSet.parse(["# liste", "evil.example", "www.phish.test", "0.0.0.0 hosts.example", "", "com"]);
  assert.ok(set.has("evil.example"));
  assert.ok(set.has("login.evil.example"));
  assert.ok(set.has("PHISH.TEST"));
  assert.ok(set.has("hosts.example"));
  assert.ok(!set.has("example"));
  assert.ok(!set.has("google.com"));
  assert.ok(!set.has(null));
});

test("updates: versions and the right file", () => {
  assert.ok(isNewer("0.22.0", "0.21.0"));
  assert.ok(isNewer("v0.10.0", "0.9.9"));
  assert.ok(!isNewer("0.21.0", "0.21.0"));
  assert.ok(!isNewer("0.21.0-rc1", "0.21.0"));
  const assets = ["NavigatueurSetup-0.22.0.exe", "Navigatueur-Linux-0.22.0-x86_64.AppImage", "Navigatueur-Android-0.22.0-arm64-v8a.apk"];
  assert.strictEqual(pickAsset(assets, "x64"), "Navigatueur-Linux-0.22.0-x86_64.AppImage");
  assert.strictEqual(pickAsset(assets, "arm64"), null);
});
