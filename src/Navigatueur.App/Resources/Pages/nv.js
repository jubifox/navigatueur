// Page side of the bridge to the app (Services/InternalPageBridge.cs).
//
// Pages call nv.send("nv:…", {...}) and subscribe with nv.on("nv:…", fn).
// The app answers "nv:hello" with "nv:state" and pushes a fresh "nv:state"
// whenever a setting changes anywhere, so every open internal page stays in
// sync. Theme tokens (dark/light, accent, private tint) are applied here.
(function () {
  "use strict";

  // These pages talk to the app: they must never run inside a frame some
  // website set up around them.
  if (window.top !== window.self) {
    window.stop();
    document.documentElement.innerHTML = "";
    throw new Error("Navigatueur pages cannot be framed.");
  }

  var bridge = window.chrome && window.chrome.webview;
  var listeners = {};

  var nv = {
    state: null,
    connected: !!bridge,

    send: function (type, payload) {
      if (!bridge) return;
      var message = { type: type };
      if (payload) {
        for (var key in payload) message[key] = payload[key];
      }
      bridge.postMessage(message);
    },

    on: function (type, handler) {
      (listeners[type] = listeners[type] || []).push(handler);
      if (type === "nv:state" && nv.state) handler(nv.state);
    },

    /** "#4c8dff" → "rgba(76, 141, 255, a)" */
    alpha: function (hex, a) {
      var m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(hex || "");
      if (!m) return "rgba(76, 141, 255, " + a + ")";
      return "rgba(" + parseInt(m[1], 16) + ", " + parseInt(m[2], 16) + ", " + parseInt(m[3], 16) + ", " + a + ")";
    },

    /** Stable, pleasant color for a letter avatar, derived from the host name. */
    hue: function (text) {
      var hash = 0;
      for (var i = 0; i < text.length; i++) hash = (hash * 31 + text.charCodeAt(i)) | 0;
      return "hsl(" + (Math.abs(hash) % 360) + ", 45%, 46%)";
    },

    escape: function (text) {
      var div = document.createElement("div");
      div.textContent = text == null ? "" : String(text);
      return div.innerHTML;
    },
  };

  function luminance(hex) {
    var m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(hex || "");
    if (!m) return 0;
    return (0.2126 * parseInt(m[1], 16) + 0.7152 * parseInt(m[2], 16) + 0.0722 * parseInt(m[3], 16)) / 255;
  }

  function applyTheme(state) {
    var root = document.documentElement;
    var theme = state.theme || {};
    root.setAttribute("data-theme", theme.mode === "Light" ? "light" : "dark");
    root.setAttribute("data-private", state.isPrivate ? "true" : "false");
    root.setAttribute("data-animations", state.interface && state.interface.animations === false ? "false" : "true");

    var accent = theme.accent || "#4c8dff";
    root.style.setProperty("--accent", accent);
    root.style.setProperty("--accent-soft", nv.alpha(accent, 0.18));
    root.style.setProperty("--accent-fg", luminance(accent) > 0.62 ? "#101012" : "#ffffff");
  }

  function dispatch(message) {
    if (!message || typeof message.type !== "string") return;
    if (message.type === "nv:state") {
      nv.state = message;
      applyTheme(message);
    }
    (listeners[message.type] || []).forEach(function (handler) {
      handler(message);
    });
  }

  if (bridge) {
    bridge.addEventListener("message", function (event) {
      dispatch(event.data);
    });
  }

  nv.dispatch = dispatch;
  window.nv = nv;

  document.addEventListener("DOMContentLoaded", function () {
    nv.send("nv:hello", { page: document.body.getAttribute("data-page") || "" });
  });
})();
