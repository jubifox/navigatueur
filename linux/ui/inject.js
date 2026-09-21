// Runs in every tab before page scripts.
//
// Scope note: request-level ad blocking happens in Rust, but WebKitGTK only
// hands Tauri *top-level* navigations, so subresource requests (the ad iframes
// and trackers embedded in an otherwise-wanted page) never reach that hook.
// This script covers what is reachable from the page side: cosmetic removal of
// ad containers and consent walls, which is the annoyance half of what the
// Windows build gets from WebView2's WebResourceRequested filter.
(function () {
  "use strict";

  // Deliberately conservative: id/class tokens that are near-universally ad
  // furniture. Anything broader starts breaking real content.
  const AD_SELECTORS = [
    '[id^="google_ads_"]',
    '[id^="div-gpt-ad"]',
    'ins.adsbygoogle',
    'iframe[src*="doubleclick.net"]',
    'iframe[src*="googlesyndication.com"]',
    'iframe[src*="amazon-adsystem.com"]',
    '[class*="sponsored-ad"]',
    '[data-ad-slot]',
  ];

  const CONSENT_SELECTORS = [
    '#onetrust-consent-sdk',
    '#didomi-host',
    '.fc-consent-root',
    '[id*="cookie-banner"]',
    '[class*="cookie-consent"]',
  ];

  const ALL = AD_SELECTORS.concat(CONSENT_SELECTORS);

  function sweep(root) {
    for (const selector of ALL) {
      let nodes;
      // A malformed selector must not abort the whole sweep.
      try { nodes = root.querySelectorAll(selector); } catch { continue; }
      for (const node of nodes) node.remove();
    }
  }

  function restoreScroll() {
    // Consent walls usually lock scrolling on <html>/<body>; removing the
    // overlay without this leaves the page frozen.
    for (const el of [document.documentElement, document.body]) {
      if (!el) continue;
      if (getComputedStyle(el).overflow === "hidden") el.style.setProperty("overflow", "auto", "important");
      el.style.removeProperty("position");
    }
  }

  function run() {
    sweep(document);
    restoreScroll();

    // Ad slots are usually filled after load, so keep watching rather than
    // sweeping once.
    new MutationObserver((mutations) => {
      for (const m of mutations) {
        for (const node of m.addedNodes) {
          if (node.nodeType === 1) sweep(node.parentNode || document);
        }
      }
    }).observe(document.documentElement, { childList: true, subtree: true });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", run, { once: true });
  } else {
    run();
  }
})();
