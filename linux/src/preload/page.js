"use strict";
// Runs in every page. Navigatueur's own pages (navigatueur://home/…) get the
// same bridge object WebView2 gives them on Windows (window.chrome.webview),
// so they run unchanged. Every page gets the accent-colored cursor trail
// when it's switched on (Settings → Apparence), like on Windows.
const { contextBridge, ipcRenderer } = require("electron");

if (location.protocol === "navigatueur:" && location.hostname === "home") {
  const listeners = [];
  ipcRenderer.on("nv", (_event, data) => {
    for (const listener of listeners) listener({ data });
  });
  contextBridge.exposeInMainWorld("chrome", {
    webview: {
      postMessage: (message) => ipcRenderer.send("nv", JSON.parse(JSON.stringify(message))),
      addEventListener: (type, listener) => {
        if (type === "message") listeners.push(listener);
      },
    },
  });
}

// ---------------------------------------------------------- cursor trail

let enabled = false;
let accent = "#4C8DFF";
const LIFE = 260;
const GAP = 120;
let canvas = null;
let ctx = null;
let points = [];
let frame = 0;

function resize() {
  const ratio = window.devicePixelRatio || 1;
  canvas.width = innerWidth * ratio;
  canvas.height = innerHeight * ratio;
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
}

function ensureCanvas() {
  if (canvas || !document.documentElement) return !!canvas;
  canvas = document.createElement("canvas");
  canvas.setAttribute("aria-hidden", "true");
  canvas.style.cssText = "position:fixed;left:0;top:0;width:100vw;height:100vh;pointer-events:none;z-index:2147483647";
  document.documentElement.appendChild(canvas);
  ctx = canvas.getContext("2d");
  resize();
  addEventListener("resize", resize, { passive: true });
  return true;
}

function draw() {
  const now = performance.now();
  points = points.filter((p) => now - p.t < LIFE);
  ctx.clearRect(0, 0, innerWidth, innerHeight);
  ctx.lineCap = "round";
  ctx.lineWidth = 3.5;
  ctx.strokeStyle = accent;
  for (let i = 1; i < points.length; i++) {
    const a = points[i - 1];
    const b = points[i];
    if (b.t - a.t > GAP) continue;
    const life = 1 - (now - b.t) / LIFE;
    ctx.globalAlpha = 0.7 * life * life;
    ctx.beginPath();
    ctx.moveTo(a.x, a.y);
    ctx.lineTo(b.x, b.y);
    ctx.stroke();
  }
  frame = points.length ? requestAnimationFrame(draw) : 0;
}

addEventListener(
  "mousemove",
  (e) => {
    if (!enabled || !ensureCanvas()) return;
    points.push({ x: e.clientX, y: e.clientY, t: performance.now() });
    if (!frame) frame = requestAnimationFrame(draw);
  },
  { capture: true, passive: true },
);

function configure(config) {
  enabled = !!(config && config.trail);
  if (config && config.accent) accent = config.accent;
  if (!enabled && ctx) {
    points = [];
    ctx.clearRect(0, 0, innerWidth, innerHeight);
  }
}

ipcRenderer.invoke("page-config").then(configure).catch(() => {});
ipcRenderer.on("page-config", (_event, config) => configure(config));
