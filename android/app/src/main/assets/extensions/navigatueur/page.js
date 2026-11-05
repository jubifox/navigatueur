// Runs in every page (top frame only). Tells Navigatueur the site's icons and
// theme color, and puts the playing video in fullscreen when the app asks
// (automatic picture-in-picture when leaving the app).
(() => {
  "use strict";

  let port;
  try {
    port = browser.runtime.connectNative("navigatueur");
  } catch (e) {
    return;
  }

  function icons() {
    const found = [];
    for (const link of document.querySelectorAll("link[rel][href]")) {
      const rel = link.getAttribute("rel").toLowerCase();
      if (!/(^|\s)(icon|shortcut icon|apple-touch-icon|apple-touch-icon-precomposed|mask-icon)(\s|$)/.test(rel)) continue;
      if (rel.includes("mask-icon")) continue;
      let size = 0;
      const sizes = (link.getAttribute("sizes") || "").toLowerCase();
      if (sizes === "any") size = 512;
      else {
        const m = sizes.match(/(\d+)x(\d+)/);
        if (m) size = parseInt(m[1], 10);
      }
      if (!size && rel.includes("apple-touch-icon")) size = 180;
      found.push({ href: link.href, size, type: link.getAttribute("type") || "" });
    }
    if (!found.length) found.push({ href: new URL("/favicon.ico", location.href).href, size: 16, type: "" });
    return found;
  }

  function themeColor() {
    const meta = document.querySelector('meta[name="theme-color"]');
    return meta ? meta.getAttribute("content") : null;
  }

  let lastReport = "";
  function report() {
    const message = { type: "page", url: location.href, icons: icons(), themeColor: themeColor() };
    const key = JSON.stringify(message);
    if (key === lastReport) return;
    lastReport = key;
    try { port.postMessage(message); } catch (e) {}
  }

  // Single-page sites (YouTube, Twitch…) change their icon and color without loading a new page.
  let pending = 0;
  new MutationObserver(() => {
    if (pending) return;
    pending = setTimeout(() => { pending = 0; report(); }, 800);
  }).observe(document.head || document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ["href", "content"] });

  report();

  function playingVideo() {
    let best = null, bestArea = 0;
    for (const v of document.querySelectorAll("video")) {
      if (v.paused || v.ended || v.readyState < 2) continue;
      const r = v.getBoundingClientRect();
      const area = r.width * r.height;
      if (area > bestArea) { best = v; bestArea = area; }
    }
    return best;
  }

  port.onMessage.addListener((message) => {
    if (!message || typeof message !== "object") return;
    if (message.type === "fullscreenVideo") {
      const video = playingVideo();
      if (!video) { port.postMessage({ type: "fullscreenVideo", ok: false }); return; }
      if (document.fullscreenElement) { port.postMessage({ type: "fullscreenVideo", ok: true, width: video.videoWidth, height: video.videoHeight }); return; }
      // The site's own player container keeps its controls; the bare video is the fallback.
      const target = video.closest(".html5-video-player, .video-player, [data-a-target='video-player']") || video;
      Promise.resolve(target.requestFullscreen ? target.requestFullscreen() : video.requestFullscreen())
        .then(() => port.postMessage({ type: "fullscreenVideo", ok: true, width: video.videoWidth, height: video.videoHeight }))
        .catch(() => port.postMessage({ type: "fullscreenVideo", ok: false }));
    } else if (message.type === "exitFullscreen") {
      if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
    } else if (message.type === "refresh") {
      lastReport = "";
      report();
    }
  });
})();
