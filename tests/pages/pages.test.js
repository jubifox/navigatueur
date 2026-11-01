// End-to-end tests for Navigatueur's internal pages (new tab, settings,
// history), run in headless Chromium — the same engine WebView2 embeds.
//
// The app side of the bridge is replaced by a fake window.chrome.webview
// that records every message a page sends and lets the test push messages
// back, exactly like InternalPageBridge does. So these tests pin down the
// page half of the protocol; the C# half is covered by the build.
//
//   node tests/pages/pages.test.js [--screenshots <dir>]
"use strict";

const path = require("path");
const assert = require("assert");
const { chromium } = require("playwright");

const PAGES = path.resolve(__dirname, "../../src/Navigatueur.App/Resources/Pages");
const screenshotDir = process.argv.includes("--screenshots")
  ? process.argv[process.argv.indexOf("--screenshots") + 1]
  : null;

const FAKE_BRIDGE = `
  window.__sent = [];
  window.__listeners = [];
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    postMessage: (m) => window.__sent.push(JSON.parse(JSON.stringify(m))),
    addEventListener: (type, fn) => { if (type === "message") window.__listeners.push(fn); },
  };
  window.__deliver = (data) => window.__listeners.forEach((fn) => fn({ data }));
`;

function makeState(overrides = {}) {
  const base = {
    type: "nv:state",
    isPrivate: false,
    theme: { mode: "Dark", accent: "#4C8DFF", newTabBackground: null, hasChromeBackground: false, cursorTrail: true },
    interface: {
      mode: "Immersive", toolbarAutoHide: true, sidebarAutoHide: true, sidebarSide: "Left", sidebarWidth: 280,
      edgeReveal: true, revealDelay: 150, hideDelay: 600, triggerSize: 6, sidebarTriggerSize: 18, animations: true, panelEffects: true,
    },
    navigation: {
      startup: "RestoreSession", homePage: "", searchEngine: "DuckDuckGo", searchAction: "https://duckduckgo.com/",
      engines: [
        { id: "Bing", name: "Bing" }, { id: "Google", name: "Google" }, { id: "DuckDuckGo", name: "DuckDuckGo" },
        { id: "Qwant", name: "Qwant" }, { id: "Ecosia", name: "Ecosia" },
      ],
      adBlock: true, phishing: true, suspendMinutes: 15, maxLiveTabs: 3, rememberedPermissions: 2,
    },
    shortcuts: [
      { id: "tab.new", label: "Nouvel onglet", category: "Onglets", gestures: ["Ctrl + T"], isDefault: true },
      { id: "tab.close", label: "Fermer l'onglet", category: "Onglets", gestures: ["Ctrl + W", "Ctrl + F4"], isDefault: true },
      { id: "tab.reopen", label: "Rouvrir le dernier onglet fermé", category: "Onglets", gestures: ["Ctrl + Maj + T"], isDefault: false },
      { id: "address.focus", label: "Barre d'adresse", category: "Navigation", gestures: ["Ctrl + L", "Alt + D", "F6"], isDefault: true },
      { id: "view.fullscreen", label: "Plein écran", category: "Fenêtre et outils", gestures: ["F11"], isDefault: true },
    ],
    fixedShortcuts: [{ label: "Aller à l'onglet 1 à 8", gesture: "Ctrl + 1…8" }],
    bookmarks: [
      { url: "https://github.com/jubifox/navigatueur", title: "Navigatueur sur GitHub", host: "github.com" },
      { url: "https://fr.wikipedia.org/", title: "Wikipédia", host: "fr.wikipedia.org" },
      { url: "https://www.youtube.com/", title: "YouTube", host: "youtube.com" },
    ],
    about: { version: "0.18.0", updateAvailable: false, latestVersion: null, engineVersion: "130.0.2849.80", engineBundled: true },
  };
  return deepMerge(base, overrides);
}

function deepMerge(target, source) {
  for (const [key, value] of Object.entries(source)) {
    if (value && typeof value === "object" && !Array.isArray(value)) {
      target[key] = deepMerge(target[key] || {}, value);
    } else {
      target[key] = value;
    }
  }
  return target;
}

async function openPage(browser, file, { width = 1280, height = 820 } = {}) {
  const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1 });
  await context.addInitScript(FAKE_BRIDGE);
  const page = await context.newPage();
  const errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await page.goto("file://" + path.join(PAGES, file));
  return { page, context, errors };
}

const sent = (page) => page.evaluate(() => window.__sent);
const deliver = (page, message) => page.evaluate((m) => window.__deliver(m), message);
const lastOfType = async (page, type) => (await sent(page)).filter((m) => m.type === type).pop();

const tests = [];
const test = (name, fn) => tests.push({ name, fn });

// ---------------------------------------------------------------- new tab

test("new tab: says hello, then renders favorites, engine and hint from state", async (browser) => {
  const { page, errors } = await openPage(browser, "index.html");
  assert.deepStrictEqual((await sent(page))[0], { type: "nv:hello", page: "newtab" });

  await deliver(page, makeState());
  assert.strictEqual(await page.locator(".tile").count(), 3);
  assert.strictEqual(await page.getAttribute("#searchForm", "action"), "https://duckduckgo.com/");
  assert.match(await page.textContent("#hint"), /bord supérieur : barre d'outils · bord gauche : onglets/);
  assert.strictEqual(await page.isVisible(".private-note"), false);
  if (screenshotDir) {
    await page.waitForTimeout(1200); // the edge hint fades in
    await page.screenshot({ path: path.join(screenshotDir, "nouvel-onglet.png") });
  }
  assert.deepStrictEqual(errors, []);
});

test("new tab: search goes through the app (address-or-search logic)", async (browser) => {
  const { page } = await openPage(browser, "index.html");
  await deliver(page, makeState());
  await page.fill("#query", "chat noir");
  await page.press("#query", "Enter");
  assert.deepStrictEqual(await lastOfType(page, "nv:navigate"), { type: "nv:navigate", input: "chat noir" });
  assert.ok(page.url().startsWith("file://"), "the page must not have submitted the form itself");
});

test("new tab: removing a favorite asks the app", async (browser) => {
  const { page } = await openPage(browser, "index.html");
  await deliver(page, makeState());
  await page.hover(".tile >> nth=1");
  await page.click(".tile >> nth=1 >> .remove");
  assert.deepStrictEqual(await lastOfType(page, "nv:bookmark.remove"), { type: "nv:bookmark.remove", url: "https://fr.wikipedia.org/" });
});

test("new tab: classic mode hides the edge hint; right side is named", async (browser) => {
  const { page } = await openPage(browser, "index.html");
  await deliver(page, makeState({ interface: { toolbarAutoHide: false, sidebarAutoHide: false } }));
  assert.strictEqual(await page.$eval("#hint", (e) => e.classList.contains("visible")), false);
  await deliver(page, makeState({ interface: { toolbarAutoHide: false, sidebarAutoHide: true, sidebarSide: "Right" } }));
  assert.match(await page.textContent("#hint"), /bord droit : onglets/);
});

test("new tab: private window gets the violet tint and the private note", async (browser) => {
  const { page } = await openPage(browser, "index.html");
  await deliver(page, makeState({ isPrivate: true, theme: { accent: "#8B5CF6" } }));
  assert.strictEqual(await page.getAttribute("html", "data-private"), "true");
  assert.strictEqual(await page.isVisible(".private-note"), true);
  // The token, not the painted color: the body cross-fades between palettes.
  const bg = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue("--bg").trim());
  assert.strictEqual(bg, "#0e0b14");
  if (screenshotDir) await page.screenshot({ path: path.join(screenshotDir, "nouvel-onglet-prive.png") });
});

// --------------------------------------------------------------- settings

test("settings: renders every control from state", async (browser) => {
  const { page, errors } = await openPage(browser, "settings.html");
  assert.deepStrictEqual((await sent(page))[0], { type: "nv:hello", page: "settings" });
  await deliver(page, makeState());

  assert.strictEqual(await page.getAttribute('.mode[data-mode="Immersive"]', "aria-pressed"), "true");
  assert.strictEqual(await page.isChecked('[data-key="interface.edgeReveal"]'), true);
  assert.strictEqual(await page.getAttribute('[data-key="interface.sidebarSide"] [data-value="Left"]', "aria-pressed"), "true");
  assert.strictEqual(await page.inputValue('[data-key="interface.revealDelay"]'), "150");
  assert.strictEqual(await page.textContent('[data-key="interface.revealDelay"] + output'), "150 ms");
  assert.strictEqual(await page.inputValue('[data-key="interface.sidebarTriggerSize"]'), "18");
  assert.strictEqual(await page.textContent('[data-key="interface.sidebarTriggerSize"] + output'), "18 px");
  assert.strictEqual(await page.inputValue('[data-key="perf.suspendMinutes"]'), "15");
  assert.strictEqual(await page.getAttribute('#engines .chip[aria-pressed="true"]', "aria-pressed"), "true");
  assert.strictEqual(await page.textContent('#engines .chip[aria-pressed="true"]'), "DuckDuckGo");
  assert.strictEqual(await page.locator("#shortcutList [data-shortcut]").count(), 5);
  assert.match(await page.textContent("#engineText"), /intégré à Navigatueur/);
  assert.match(await page.textContent("#permissionsText"), /2 décisions mémorisées/);
  if (screenshotDir) await page.screenshot({ path: path.join(screenshotDir, "parametres.png") });
  assert.deepStrictEqual(errors, []);
});

test("settings: controls send the right setting keys and values", async (browser) => {
  const { page } = await openPage(browser, "settings.html");
  await deliver(page, makeState());

  await page.click('.mode[data-mode="Classic"]');
  assert.deepStrictEqual(await lastOfType(page, "nv:set"), { type: "nv:set", key: "interface.mode", value: "Classic" });

  await page.click('[data-key="interface.sidebarSide"] [data-value="Right"]');
  assert.deepStrictEqual(await lastOfType(page, "nv:set"), { type: "nv:set", key: "interface.sidebarSide", value: "Right" });

  await page.click('label.switch:has([data-key="interface.animations"])');
  assert.deepStrictEqual(await lastOfType(page, "nv:set"), { type: "nv:set", key: "interface.animations", value: false });

  await page.$eval('[data-key="interface.hideDelay"]', (input) => {
    input.value = "1200";
    input.dispatchEvent(new Event("input"));
    input.dispatchEvent(new Event("change"));
  });
  assert.deepStrictEqual(await lastOfType(page, "nv:set"), { type: "nv:set", key: "interface.hideDelay", value: 1200 });
  assert.strictEqual(await page.textContent('[data-key="interface.hideDelay"] + output'), "1200 ms");

  await page.$eval('[data-key="interface.sidebarTriggerSize"]', (input) => {
    input.value = "30";
    input.dispatchEvent(new Event("input"));
    input.dispatchEvent(new Event("change"));
  });
  assert.deepStrictEqual(await lastOfType(page, "nv:set"), { type: "nv:set", key: "interface.sidebarTriggerSize", value: 30 });

  await page.selectOption('[data-key="perf.maxLiveTabs"]', "6");
  assert.deepStrictEqual(await lastOfType(page, "nv:set"), { type: "nv:set", key: "perf.maxLiveTabs", value: 6 });

  await page.click('#swatches .swatch[title="Vert"]');
  assert.deepStrictEqual(await lastOfType(page, "nv:set"), { type: "nv:set", key: "theme.accent", value: "#4FE0A0" });

  await page.click('#engines .chip:has-text("Qwant")');
  assert.deepStrictEqual(await lastOfType(page, "nv:set"), { type: "nv:set", key: "search.engine", value: "Qwant" });

  await page.fill("#homePage", "example.com");
  await page.click('#homeForm button[type="submit"]');
  assert.deepStrictEqual(await lastOfType(page, "nv:set"), { type: "nv:set", key: "startup.homePage", value: "example.com" });

  await page.click('[data-action="extensions.open"]');
  assert.deepStrictEqual(await lastOfType(page, "nv:action"), { type: "nv:action", name: "extensions.open" });
});

test("settings: disabling edge reveal greys out its timing rows", async (browser) => {
  const { page } = await openPage(browser, "settings.html");
  await deliver(page, makeState({ interface: { edgeReveal: false } }));
  // Reveal delay, and both trigger zones (top bar, tab column).
  assert.strictEqual(await page.locator("[data-reveal].disabled").count(), 3);
});

test("settings: light theme and accent are applied to the page", async (browser) => {
  const { page } = await openPage(browser, "settings.html");
  await deliver(page, makeState({ theme: { mode: "Light", accent: "#E0A52A" } }));
  assert.strictEqual(await page.getAttribute("html", "data-theme"), "light");
  const accent = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue("--accent").trim());
  assert.strictEqual(accent, "#E0A52A");
  // Amber is light: text on it must switch to dark for contrast.
  const fg = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue("--accent-fg").trim());
  assert.strictEqual(fg, "#101012");
  if (screenshotDir) {
    await page.waitForTimeout(400); // let the palette cross-fade finish
    await page.screenshot({ path: path.join(screenshotDir, "parametres-clair.png") });
  }
});

test("settings: recording a shortcut captures keys and reports success", async (browser) => {
  const { page } = await openPage(browser, "settings.html");
  await deliver(page, makeState());

  await page.click('[data-shortcut="tab.new"] .edit');
  assert.deepStrictEqual(await lastOfType(page, "nv:shortcut.capture"), { type: "nv:shortcut.capture", active: true });
  assert.match(await page.textContent('[data-shortcut="tab.new"] .keys'), /Appuyez sur la combinaison/);

  await page.keyboard.press("Control+Shift+K");
  assert.deepStrictEqual(await lastOfType(page, "nv:shortcut.set"), { type: "nv:shortcut.set", id: "tab.new", gesture: "Ctrl+Shift+K" });

  await deliver(page, { type: "nv:shortcut.result", id: "tab.new", ok: false, error: "Combinaison invalide" });
  assert.strictEqual(await page.isVisible('[data-shortcut="tab.new"] .error'), true);

  await page.keyboard.press("Control+Digit5");
  assert.deepStrictEqual(await lastOfType(page, "nv:shortcut.set"), { type: "nv:shortcut.set", id: "tab.new", gesture: "Ctrl+5" });
  await deliver(page, { type: "nv:shortcut.result", id: "tab.new", ok: true, error: null });
  assert.deepStrictEqual(await lastOfType(page, "nv:shortcut.capture"), { type: "nv:shortcut.capture", active: false });
});

test("settings: Escape cancels recording, Delete disables the shortcut", async (browser) => {
  const { page } = await openPage(browser, "settings.html");
  await deliver(page, makeState());

  await page.click('[data-shortcut="address.focus"] .edit');
  await page.keyboard.press("Escape");
  assert.deepStrictEqual(await lastOfType(page, "nv:shortcut.capture"), { type: "nv:shortcut.capture", active: false });
  assert.strictEqual(await lastOfType(page, "nv:shortcut.set"), undefined);

  await page.click('[data-shortcut="view.fullscreen"] .edit');
  await page.keyboard.press("Delete");
  assert.deepStrictEqual(await lastOfType(page, "nv:shortcut.disable"), { type: "nv:shortcut.disable", id: "view.fullscreen" });
});

test("settings: a non-default shortcut can be reset", async (browser) => {
  const { page } = await openPage(browser, "settings.html");
  await deliver(page, makeState());
  await page.click('[data-shortcut="tab.reopen"] .reset');
  assert.deepStrictEqual(await lastOfType(page, "nv:shortcut.reset"), { type: "nv:shortcut.reset", id: "tab.reopen" });
  assert.strictEqual(await page.locator('[data-shortcut="tab.new"] .reset').count(), 0);
});

// ---------------------------------------------------------------- history

test("history: queries once state arrives, groups by day, opens and deletes", async (browser) => {
  const { page, errors } = await openPage(browser, "history.html");
  await deliver(page, makeState());
  assert.deepStrictEqual(await lastOfType(page, "nv:history.query"), { type: "nv:history.query", query: "", limit: 400 });

  const now = new Date();
  const yesterday = new Date(now.getTime() - 86400000);
  await deliver(page, {
    type: "nv:history",
    query: "",
    entries: [
      { url: "https://example.com/a", title: "Page A", host: "example.com", visitedAt: now.toISOString() },
      { url: "https://example.org/b", title: "<b>Page B</b>", host: "example.org", visitedAt: yesterday.toISOString() },
    ],
  });

  const days = await page.locator(".day h2").allTextContents();
  assert.deepStrictEqual(days, ["Aujourd'hui", "Hier"]);
  // Titles are text, never markup.
  assert.strictEqual(await page.locator(".entry .title b").count(), 0);
  assert.strictEqual(await page.textContent(".entry >> nth=1 >> .title"), "<b>Page B</b>");

  await page.click(".entry >> nth=0");
  assert.deepStrictEqual(await lastOfType(page, "nv:open"), { type: "nv:open", url: "https://example.com/a", background: false });

  await page.click(".entry >> nth=0", { modifiers: ["Control"] });
  assert.deepStrictEqual(await lastOfType(page, "nv:open"), { type: "nv:open", url: "https://example.com/a", background: true });

  await page.hover(".entry >> nth=1");
  await page.click(".entry >> nth=1 >> .remove");
  assert.strictEqual((await lastOfType(page, "nv:history.delete")).url, "https://example.org/b");
  assert.strictEqual(await page.locator(".entry").count(), 1);
  assert.deepStrictEqual(await page.locator(".day h2").allTextContents(), ["Aujourd'hui"], "an emptied day disappears");
  if (screenshotDir) await page.screenshot({ path: path.join(screenshotDir, "historique.png") });
  assert.deepStrictEqual(errors, []);
});

test("history: search is debounced and stale results are ignored", async (browser) => {
  const { page } = await openPage(browser, "history.html");
  await deliver(page, makeState());
  await page.fill("#query", "wiki");
  await page.waitForTimeout(250);
  assert.deepStrictEqual(await lastOfType(page, "nv:history.query"), { type: "nv:history.query", query: "wiki", limit: 400 });

  await deliver(page, { type: "nv:history", query: "", entries: [{ url: "https://x/", title: "stale", host: "x", visitedAt: new Date().toISOString() }] });
  assert.strictEqual(await page.locator(".entry").count(), 0);
  await deliver(page, { type: "nv:history", query: "wiki", entries: [] });
  assert.match(await page.textContent("#list"), /Aucun résultat/);
});

// --------------------------------------------------------------- security

test("pages refuse to run inside a frame (no bridge access for embedding sites)", async (browser) => {
  const fs = require("fs");
  const os = require("os");
  const wrapper = path.join(fs.mkdtempSync(path.join(os.tmpdir(), "nv-frame-")), "embed.html");
  fs.writeFileSync(wrapper, `<iframe id="f" src="file://${path.join(PAGES, "settings.html")}"></iframe>`);

  const context = await browser.newContext();
  await context.addInitScript(FAKE_BRIDGE);
  const page = await context.newPage();
  await page.goto("file://" + wrapper);
  const frame = await (await page.waitForSelector("#f")).contentFrame();
  // window.stop() aborts the framed page's load, so there's no "load" event to wait for.
  await page.waitForTimeout(600);
  assert.strictEqual(await frame.evaluate(() => document.body.innerHTML.trim()), "");
  assert.strictEqual(await frame.evaluate(() => typeof window.nv), "undefined");
  assert.strictEqual(await frame.evaluate(() => window.__sent.length), 0);
});

// ------------------------------------------------------------------- run

(async () => {
  const browser = await chromium.launch();
  let failed = 0;
  for (const { name, fn } of tests) {
    try {
      await Promise.race([
        fn(browser),
        new Promise((_, reject) => setTimeout(() => reject(new Error("timed out after 20s")), 20000)),
      ]);
      console.log("  ✓ " + name);
    } catch (error) {
      failed++;
      console.log("  ✗ " + name + "\n    " + String(error && error.stack || error).split("\n").slice(0, 6).join("\n    "));
    }
  }
  await browser.close();
  console.log(`\n${tests.length - failed}/${tests.length} tests passed`);
  process.exit(failed ? 1 : 0);
})();
