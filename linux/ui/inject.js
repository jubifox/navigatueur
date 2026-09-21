// Runs in every tab before page scripts.
//
// Everything here is deliberately self-contained: no Tauri IPC is exposed to
// remote pages, because a browser that hands every website a channel to its own
// settings, history and filesystem commands is not a browser worth shipping.
// The Windows build could afford a host round-trip for the cursor trail because
// WebView2's postMessage bridge is scoped to the app; the equivalent here would
// mean opening the whole command surface, so the effect is drawn page-side
// instead — which also removes the per-mousemove IPC hop it used to cost.
//
// Rust pushes appearance settings in after load by calling __nvSetConfig.
(function () {
  "use strict";

  var config = { trail: false, accent: "#4C8DFF", cursor: null };
  var trailLayer = null;

  // --------------------------------------------------------- cosmetics

  // Conservative on purpose: id/class tokens that are near-universally ad
  // furniture. Per-domain rules from EasyList arrive separately, evaluated by
  // Rust and injected only for hosts that actually have rules.
  var AD_SELECTORS = [
    '[id^="google_ads_"]', '[id^="div-gpt-ad"]', 'ins.adsbygoogle',
    'iframe[src*="doubleclick.net"]', 'iframe[src*="googlesyndication.com"]',
    'iframe[src*="amazon-adsystem.com"]', '[class*="sponsored-ad"]', '[data-ad-slot]',
  ];

  var CONSENT_SELECTORS = [
    '#onetrust-consent-sdk', '#didomi-host', '.fc-consent-root',
    '[id*="cookie-banner"]', '[class*="cookie-consent"]',
  ];

  var ALL = AD_SELECTORS.concat(CONSENT_SELECTORS);

  function sweep(root) {
    for (var i = 0; i < ALL.length; i++) {
      var nodes;
      // A malformed selector must not abort the whole sweep.
      try { nodes = root.querySelectorAll(ALL[i]); } catch (e) { continue; }
      for (var j = 0; j < nodes.length; j++) nodes[j].remove();
    }
  }

  function restoreScroll() {
    // Consent walls usually lock scrolling on <html>/<body>; removing the
    // overlay without this leaves the page frozen.
    [document.documentElement, document.body].forEach(function (node) {
      if (!node) return;
      if (getComputedStyle(node).overflow === "hidden") {
        node.style.setProperty("overflow", "auto", "important");
      }
      node.style.removeProperty("position");
    });
  }

  // ------------------------------------------------------------- mute

  // WebKitGTK's own mute flag is not reachable through Tauri, so muting sets
  // the media elements directly — and has to keep doing it, since a page can
  // add a fresh <video> at any time.
  function applyMute() {
    if (typeof window.__nvMuted !== "boolean") return;
    var media = document.querySelectorAll("video,audio");
    for (var i = 0; i < media.length; i++) media[i].muted = window.__nvMuted;
  }

  // ----------------------------------------------------- cursor trail

  function ensureTrailLayer() {
    if (trailLayer && trailLayer.isConnected) return trailLayer;
    trailLayer = document.createElement("div");
    trailLayer.style.cssText =
      "position:fixed;inset:0;pointer-events:none;z-index:2147483647;overflow:hidden";
    document.documentElement.appendChild(trailLayer);
    return trailLayer;
  }

  var lastSpawn = 0;

  function onMove(event) {
    if (!config.trail) return;
    var now = Date.now();
    if (now - lastSpawn < 20) return;   // same throttle the Windows tracker used
    lastSpawn = now;

    var dot = document.createElement("div");
    dot.style.cssText =
      "position:absolute;width:8px;height:8px;border-radius:50%;opacity:0.85;" +
      "background:" + config.accent + ";left:" + (event.clientX - 4) + "px;top:" +
      (event.clientY - 4) + "px;transition:opacity .45s linear,transform .45s linear";
    ensureTrailLayer().appendChild(dot);

    // Next frame, so the transition has an initial state to animate from.
    requestAnimationFrame(function () {
      dot.style.opacity = "0";
      dot.style.transform = "scale(0.3)";
    });
    setTimeout(function () { dot.remove(); }, 500);
  }

  function applyCursor() {
    if (!config.cursor) return;
    var style = document.createElement("style");
    style.textContent =
      '*,*::before,*::after{cursor:url("' + config.cursor + '") 2 2,auto !important}';
    (document.head || document.documentElement).appendChild(style);
  }

  // Called by Rust once per page load, after settings are known.
  window.__nvSetConfig = function (next) {
    config = Object.assign(config, next || {});
    applyCursor();
  };

  // --------------------------------------------------------------- run

  function run() {
    sweep(document);
    restoreScroll();
    applyMute();

    document.addEventListener("mousemove", onMove, { passive: true, capture: true });

    // Ad slots and media elements are usually created after load, so keep
    // watching rather than sweeping once.
    new MutationObserver(function (mutations) {
      var sawNodes = false;
      for (var i = 0; i < mutations.length; i++) {
        if (mutations[i].addedNodes.length) { sawNodes = true; break; }
      }
      if (!sawNodes) return;
      sweep(document);
      applyMute();
    }).observe(document.documentElement, { childList: true, subtree: true });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", run, { once: true });
  } else {
    run();
  }
})();
