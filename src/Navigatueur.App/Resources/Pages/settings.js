// Settings page: every control writes through the bridge immediately
// ("nv:set" / "nv:action"); the page then re-renders from the "nv:state" the
// app sends back, so what's shown is always what's actually applied.
(function () {
  "use strict";

  var ACCENTS = [
    ["#4C8DFF", "Bleu"],
    ["#4FE0A0", "Vert"],
    ["#E0A52A", "Ambre"],
    ["#E04F4F", "Rouge"],
    ["#B14FE0", "Violet"],
    ["#4FD1E0", "Cyan"],
    ["#F472B6", "Rose"],
  ];

  // Setting key (what the app accepts) → where its current value lives in the state.
  var STATE_PATHS = {
    "theme.mode": "theme.mode",
    "appearance.cursorTrail": "theme.cursorTrail",
    "appearance.chromeFollowsNewTab": "theme.chromeFollowsNewTab",
    "appearance.forceDark": "theme.forceDark",
    "interface.tabPreviews": "interface.tabPreviews",
    "media.autoPip": "navigation.autoPip",
    "interface.toolbarAutoHide": "interface.toolbarAutoHide",
    "interface.sidebarAutoHide": "interface.sidebarAutoHide",
    "interface.sidebarSide": "interface.sidebarSide",
    "interface.newTabButton": "interface.newTabButton",
    "interface.sidebarWidth": "interface.sidebarWidth",
    "interface.edgeReveal": "interface.edgeReveal",
    "interface.revealDelay": "interface.revealDelay",
    "interface.hideDelay": "interface.hideDelay",
    "interface.triggerSize": "interface.triggerSize",
    "interface.sidebarTriggerSize": "interface.sidebarTriggerSize",
    "interface.animations": "interface.animations",
    "interface.panelEffects": "interface.panelEffects",
    "startup.behavior": "navigation.startup",
    "privacy.adBlock": "navigation.adBlock",
    "privacy.phishing": "navigation.phishing",
    "perf.suspendMinutes": "navigation.suspendMinutes",
    "perf.maxLiveTabs": "navigation.maxLiveTabs",
  };

  var state = null;
  var capture = null; // { id, row } while recording a shortcut

  function $(selector) {
    return document.querySelector(selector);
  }

  function $all(selector) {
    return Array.prototype.slice.call(document.querySelectorAll(selector));
  }

  function read(path) {
    return path.split(".").reduce(function (value, key) {
      return value == null ? undefined : value[key];
    }, state);
  }

  function set(key, value) {
    nv.send("nv:set", { key: key, value: value });
  }

  function toast(text) {
    var element = $("#toast");
    element.textContent = text;
    element.classList.add("visible");
    clearTimeout(toast.timer);
    toast.timer = setTimeout(function () {
      element.classList.remove("visible");
    }, 2200);
  }

  function press(group, value) {
    group.querySelectorAll("[aria-pressed]").forEach(function (button) {
      button.setAttribute("aria-pressed", String(button.getAttribute("data-value") === String(value)));
    });
  }

  function showRange(input) {
    input.parentElement.querySelector("output").textContent = input.value + (input.getAttribute("data-unit") || "");
  }

  // ------------------------------------------------------------ wiring

  $all("input[type=checkbox][data-key]").forEach(function (input) {
    input.addEventListener("change", function () {
      set(input.getAttribute("data-key"), input.checked);
    });
  });

  $all(".segmented[data-key]").forEach(function (group) {
    group.addEventListener("click", function (event) {
      var button = event.target.closest("button[data-value]");
      if (!button) return;
      press(group, button.getAttribute("data-value"));
      set(group.getAttribute("data-key"), button.getAttribute("data-value"));
    });
  });

  $all("input[type=range][data-key]").forEach(function (input) {
    input.addEventListener("input", function () {
      showRange(input);
    });
    input.addEventListener("change", function () {
      set(input.getAttribute("data-key"), Number(input.value));
    });
  });

  $all("select[data-key]").forEach(function (select) {
    select.addEventListener("change", function () {
      set(select.getAttribute("data-key"), Number(select.value));
    });
  });

  $all("[data-action]").forEach(function (button) {
    button.addEventListener("click", function () {
      var name = button.getAttribute("data-action");
      nv.send("nv:action", { name: name });
      if (name === "permissions.clear") toast("Autorisations réinitialisées");
      if (name === "shortcuts.resetAll") toast("Raccourcis par défaut rétablis");
      if (name === "update.check") toast("Recherche de mises à jour…");
      if (name === "codec.hevc.store") toast("Ouverture du Microsoft Store…");
    });
  });

  // What the engine can decode on this PC. Twitch's 2K lives (Enhanced
  // Broadcasting) are HEVC, which Windows only decodes with its HEVC Video
  // Extensions codec; without it Twitch falls back to 1080p.
  (function renderVideoSupport() {
    function supports(type) {
      return !!(window.MediaSource && MediaSource.isTypeSupported(type));
    }
    var vp9 = supports('video/webm; codecs="vp09.00.51.08"');
    var av1 = supports('video/mp4; codecs="av01.0.12M.08"');
    var hevc = supports('video/mp4; codecs="hvc1.1.6.L150.90"');
    $("#vp9Text").textContent = vp9 || av1
      ? "Pris en charge (" + [vp9 && "VP9", av1 && "AV1"].filter(Boolean).join(", ") + ") : YouTube et la plupart des sites."
      : "Non pris en charge par le moteur sur ce PC.";
    $("#hevcText").textContent = hevc
      ? "Pris en charge : les lives Twitch en 2K sont lisibles."
      : "Codec HEVC de Windows absent : Twitch s'arrête à 1080p. Installez le codec « Extensions vidéo HEVC » de Microsoft (un lecteur vidéo HEVC ne suffit pas), puis redémarrez Navigatueur.";
    $("#hevcStore").hidden = hevc;
  })();

  $all(".mode[data-mode]").forEach(function (button) {
    button.addEventListener("click", function () {
      set("interface.mode", button.getAttribute("data-mode"));
    });
  });

  var swatches = $("#swatches");
  ACCENTS.forEach(function (accent) {
    var swatch = document.createElement("button");
    swatch.className = "swatch";
    swatch.style.background = accent[0];
    swatch.title = accent[1];
    swatch.setAttribute("aria-label", accent[1]);
    swatch.setAttribute("aria-pressed", "false");
    swatch.setAttribute("data-value", accent[0]);
    swatch.addEventListener("click", function () {
      set("theme.accent", accent[0]);
    });
    swatches.insertBefore(swatch, $("#customAccent"));
  });

  $("#customAccent").addEventListener("change", function (event) {
    set("theme.accent", event.target.value.toUpperCase());
  });

  $("#homeForm").addEventListener("submit", function (event) {
    event.preventDefault();
    set("startup.homePage", $("#homePage").value.trim());
    toast("Page d'accueil enregistrée");
  });

  $("#clearHistory").addEventListener("click", function () {
    if (confirm("Effacer tout l'historique de navigation ?")) {
      nv.send("nv:action", { name: "history.clear" });
      toast("Historique effacé");
    }
  });

  // Highlights the section currently in view in the side navigation.
  var links = $all("nav.sections a");
  if ("IntersectionObserver" in window) {
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        links.forEach(function (link) {
          link.classList.toggle("current", link.getAttribute("href") === "#" + entry.target.id);
        });
      });
    }, { rootMargin: "-20% 0px -70% 0px" });
    $all("main section").forEach(function (section) {
      observer.observe(section);
    });
  }

  // ------------------------------------------------------------ render

  function render() {
    $all("input[type=checkbox][data-key]").forEach(function (input) {
      input.checked = !!read(STATE_PATHS[input.getAttribute("data-key")]);
    });

    $all(".segmented[data-key]").forEach(function (group) {
      press(group, read(STATE_PATHS[group.getAttribute("data-key")]));
    });

    $all("input[type=range][data-key]").forEach(function (input) {
      var value = read(STATE_PATHS[input.getAttribute("data-key")]);
      if (value != null) input.value = value;
      showRange(input);
    });

    $all("select[data-key]").forEach(function (select) {
      var value = String(read(STATE_PATHS[select.getAttribute("data-key")]));
      if (!$all("select[data-key='" + select.getAttribute("data-key") + "'] option").some(function (o) { return o.value === value; })) {
        var option = document.createElement("option");
        option.value = value;
        option.textContent = value;
        select.appendChild(option);
      }
      select.value = value;
    });

    var ui = state.interface || {};
    $all(".mode[data-mode]").forEach(function (button) {
      button.setAttribute("aria-pressed", String(button.getAttribute("data-mode") === ui.mode));
    });
    $all("[data-own-chrome]").forEach(function (row) {
      row.classList.toggle("disabled", !!(state.theme && state.theme.chromeFollowsNewTab));
    });
    $all("[data-reveal]").forEach(function (row) {
      row.classList.toggle("disabled", !ui.edgeReveal);
    });

    var accent = (state.theme.accent || "").toUpperCase();
    $all(".swatch").forEach(function (swatch) {
      swatch.setAttribute("aria-pressed", String(swatch.getAttribute("data-value") === accent));
    });
    $("#customAccent").value = /^#[0-9A-F]{6}$/.test(accent) ? accent.toLowerCase() : "#4c8dff";

    var navigation = state.navigation || {};
    if (document.activeElement !== $("#homePage")) {
      $("#homePage").value = navigation.homePage || "";
    }

    var engines = $("#engines");
    engines.innerHTML = "";
    (navigation.engines || []).forEach(function (engine) {
      var chip = document.createElement("button");
      chip.className = "chip";
      chip.textContent = engine.name;
      chip.setAttribute("aria-pressed", String(engine.id === navigation.searchEngine));
      chip.addEventListener("click", function () {
        set("search.engine", engine.id);
      });
      engines.appendChild(chip);
    });

    var darkSites = (state.theme && state.theme.forceDarkSites) || 0;
    $("#forceDarkSitesText").textContent = darkSites === 0
      ? "Aucun site réglé à part. Depuis le menu ⋯ d'une page : « Activer / Désactiver le mode sombre sur ce site »."
      : darkSites + " site" + (darkSites > 1 ? "s" : "") + " réglé" + (darkSites > 1 ? "s" : "") + " à part depuis le menu ⋯.";
    $("#forceDarkReset").disabled = darkSites === 0;

    var remembered = navigation.rememberedPermissions || 0;
    $("#permissionsText").textContent = remembered === 0
      ? "Aucune décision mémorisée : chaque site demande avant d'utiliser la caméra, le micro, la position…"
      : remembered + " décision" + (remembered > 1 ? "s" : "") + " mémorisée" + (remembered > 1 ? "s" : "") + " (caméra, micro, position, notifications…).";

    if (!capture) renderShortcuts();
    renderAbout();
  }

  function renderKeys(container, gestures) {
    container.innerHTML = "";
    if (!gestures.length) {
      container.innerHTML = '<span class="none">Aucun</span>';
      return;
    }
    gestures.forEach(function (gesture, index) {
      if (index > 0) container.appendChild(document.createTextNode(" "));
      var group = document.createElement("span");
      group.innerHTML = gesture.split(" + ").map(function (key) {
        return "<kbd>" + nv.escape(key) + "</kbd>";
      }).join(" + ");
      container.appendChild(group);
    });
  }

  function renderShortcuts() {
    var list = $("#shortcutList");
    list.innerHTML = "";
    var byCategory = {};
    (state.shortcuts || []).forEach(function (shortcut) {
      (byCategory[shortcut.category] = byCategory[shortcut.category] || []).push(shortcut);
    });

    Object.keys(byCategory).forEach(function (category) {
      var group = document.createElement("div");
      group.className = "shortcut-group";
      group.innerHTML = "<h3>" + nv.escape(category) + "</h3>";

      byCategory[category].forEach(function (shortcut) {
        var row = document.createElement("div");
        row.className = "row";
        row.setAttribute("data-shortcut", shortcut.id);
        row.innerHTML =
          '<div class="text"><strong>' + nv.escape(shortcut.label) + '</strong><small class="error" hidden></small></div>' +
          '<div class="keys"></div>' +
          '<button class="btn ghost edit">Modifier</button>' +
          (shortcut.isDefault ? "" : '<button class="btn ghost reset" title="Rétablir le raccourci par défaut">Rétablir</button>');
        renderKeys(row.querySelector(".keys"), shortcut.gestures);
        row.querySelector(".edit").addEventListener("click", function () {
          if (capture && capture.id === shortcut.id) {
            stopCapture();
          } else {
            startCapture(shortcut.id, row);
          }
        });
        var reset = row.querySelector(".reset");
        if (reset) {
          reset.addEventListener("click", function () {
            nv.send("nv:shortcut.reset", { id: shortcut.id });
          });
        }
        group.appendChild(row);
      });

      list.appendChild(group);
    });

    var fixed = $("#fixedShortcuts");
    fixed.innerHTML = '<div class="shortcut-group"><h3>Non modifiables</h3></div>';
    (state.fixedShortcuts || []).forEach(function (shortcut) {
      var row = document.createElement("div");
      row.className = "row";
      row.innerHTML = '<div class="text"><strong>' + nv.escape(shortcut.label) + '</strong></div><div class="keys"></div>';
      renderKeys(row.querySelector(".keys"), [shortcut.gesture]);
      fixed.firstChild.appendChild(row);
    });
  }

  function renderAbout() {
    var about = state.about || {};
    $("#version").textContent = about.version || "";
    $("#updateText").textContent = about.updateAvailable
      ? "La version " + about.latestVersion + " est disponible."
      : "À jour (vérification automatique au démarrage).";
    $("#installUpdate").hidden = !about.updateAvailable;
    $("#engineText").textContent = about.engineVersion
      ? "Chromium (WebView2) " + about.engineVersion + (about.engineBundled ? " — intégré à Navigatueur" : " — runtime installé sur le système")
      : "Introuvable";
  }

  // --------------------------------------------------- shortcut capture

  // Maps a key press to the app's key names (Core/Shortcuts/ShortcutGesture).
  // Letters come from event.key so AZERTY's "A" is "A"; digits and named
  // keys from event.code, which doesn't depend on Shift or the layout.
  function keyName(event) {
    var code = event.code || "";
    if (/^Digit\d$/.test(code)) return code.slice(5);
    if (/^Numpad\d$/.test(code)) return code.slice(6);
    if (/^F\d{1,2}$/.test(code)) return code;
    if (event.key && event.key.length === 1 && /[a-z]/i.test(event.key)) return event.key.toUpperCase();
    var named = {
      Tab: "Tab", Enter: "Enter", Space: "Space", Backspace: "Backspace", Delete: "Delete", Insert: "Insert",
      Home: "Home", End: "End", PageUp: "PageUp", PageDown: "PageDown",
      ArrowLeft: "Left", ArrowRight: "Right", ArrowUp: "Up", ArrowDown: "Down",
      Comma: "Comma", Period: "Period", Equal: "Plus", NumpadAdd: "Plus", Minus: "Minus", NumpadSubtract: "Minus",
    };
    return named[code] || null;
  }

  function startCapture(id, row) {
    stopCapture();
    capture = { id: id, row: row };
    row.classList.add("capturing");
    row.querySelector(".keys").innerHTML = '<span class="capture-hint">Appuyez sur la combinaison…</span>';
    row.querySelector(".error").hidden = true;
    row.querySelector(".edit").textContent = "Annuler";
    nv.send("nv:shortcut.capture", { active: true });
  }

  function stopCapture() {
    if (!capture) return;
    capture = null;
    nv.send("nv:shortcut.capture", { active: false });
    if (state) renderShortcuts();
  }

  window.addEventListener("keydown", function (event) {
    if (!capture) return;
    event.preventDefault();
    event.stopPropagation();

    if (["Control", "Shift", "Alt", "Meta", "AltGraph"].indexOf(event.key) >= 0) return;

    var noModifier = !event.ctrlKey && !event.altKey && !event.shiftKey;
    if (event.key === "Escape" && noModifier) {
      stopCapture();
      return;
    }
    if ((event.key === "Backspace" || event.key === "Delete") && noModifier) {
      var disabledId = capture.id;
      stopCapture();
      nv.send("nv:shortcut.disable", { id: disabledId });
      toast("Raccourci désactivé");
      return;
    }

    var key = keyName(event);
    if (!key) return;

    var parts = [];
    if (event.ctrlKey) parts.push("Ctrl");
    if (event.shiftKey) parts.push("Shift");
    if (event.altKey) parts.push("Alt");
    parts.push(key);
    nv.send("nv:shortcut.set", { id: capture.id, gesture: parts.join("+") });
  }, true);

  window.addEventListener("blur", stopCapture);
  document.addEventListener("visibilitychange", function () {
    if (document.hidden) stopCapture();
  });

  nv.on("nv:shortcut.result", function (result) {
    if (!capture || capture.id !== result.id) return;
    if (result.ok) {
      stopCapture();
      toast("Raccourci enregistré");
    } else {
      var error = capture.row.querySelector(".error");
      error.textContent = result.error;
      error.hidden = false;
    }
  });

  nv.on("nv:update.checked", function (result) {
    toast(result.available ? "Une mise à jour est disponible" : "Navigatueur est à jour");
  });

  nv.on("nv:state", function (next) {
    state = next;
    render();
  });
})();
