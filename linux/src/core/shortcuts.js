"use strict";
// Keyboard shortcuts: the Windows app's ShortcutCatalog (same command ids,
// labels and defaults, so the Settings page is the same) and gesture
// parsing/matching for Electron's input events.

const CATEGORY_TABS = "Onglets";
const CATEGORY_NAV = "Navigation";
const CATEGORY_WINDOW = "Fenêtre et outils";

const CATALOG = [
  ["tab.new", "Nouvel onglet", CATEGORY_TABS, ["Ctrl+T"]],
  ["tab.close", "Fermer l'onglet", CATEGORY_TABS, ["Ctrl+W", "Ctrl+F4"]],
  ["tab.next", "Onglet suivant", CATEGORY_TABS, ["Ctrl+Tab", "Ctrl+PageDown"]],
  ["tab.previous", "Onglet précédent", CATEGORY_TABS, ["Ctrl+Shift+Tab", "Ctrl+PageUp"]],
  ["tab.reopen", "Rouvrir le dernier onglet fermé", CATEGORY_TABS, ["Ctrl+Shift+T"]],
  ["tabs.show", "Afficher la colonne d'onglets", CATEGORY_TABS, ["Ctrl+Shift+A"]],
  ["address.focus", "Barre d'adresse", CATEGORY_NAV, ["Ctrl+L", "Alt+D", "F6"]],
  ["page.reload", "Actualiser", CATEGORY_NAV, ["Ctrl+R", "F5"]],
  ["page.back", "Page précédente", CATEGORY_NAV, ["Alt+Left"]],
  ["page.forward", "Page suivante", CATEGORY_NAV, ["Alt+Right"]],
  ["bookmark.toggle", "Ajouter ou retirer des favoris", CATEGORY_NAV, ["Ctrl+D"]],
  ["view.fullscreen", "Plein écran", CATEGORY_WINDOW, ["F11"]],
  ["window.new", "Nouvelle fenêtre", CATEGORY_WINDOW, ["Ctrl+N"]],
  ["window.private", "Nouvelle fenêtre privée", CATEGORY_WINDOW, ["Ctrl+Shift+N"]],
  ["history.open", "Historique", CATEGORY_WINDOW, ["Ctrl+H"]],
  ["downloads.open", "Téléchargements", CATEGORY_WINDOW, ["Ctrl+J"]],
  ["settings.open", "Paramètres", CATEGORY_WINDOW, ["Ctrl+Comma"]],
].map(([id, label, category, defaults]) => ({ id, label, category, defaults, customizable: true }));

// Not customizable: Ctrl+1…8, Ctrl+9, find, zoom, Escape.
const FIXED = [
  ...Array.from({ length: 8 }, (_, i) => [`Ctrl+${i + 1}`, `tab.select.${i + 1}`]),
  ["Ctrl+9", "tab.select.last"],
  ["Ctrl+F", "page.find"],
  ["Ctrl+Plus", "zoom.in"],
  ["Ctrl+Shift+Plus", "zoom.in"],
  ["Ctrl+Minus", "zoom.out"],
  ["Ctrl+0", "zoom.reset"],
];

const ALIASES = { control: "ctrl", maj: "shift", pgdn: "pagedown", pgup: "pageup", esc: "escape", del: "delete", "=": "plus", ",": "comma", ".": "period", "-": "minus" };

/** "Ctrl+Shift+T" → { ctrl, shift, alt, key: "T" }, or null. */
function parse(text) {
  if (!text || !String(text).trim()) return null;
  const tokens = String(text).replace(/\+\+$/, "+Plus").split("+").map((t) => t.trim()).filter(Boolean);
  const g = { ctrl: false, shift: false, alt: false, key: null };
  for (const token of tokens) {
    const lower = ALIASES[token.toLowerCase()] || token.toLowerCase();
    if (lower === "ctrl") g.ctrl = true;
    else if (lower === "shift") g.shift = true;
    else if (lower === "alt") g.alt = true;
    else if (g.key) return null;
    else g.key = normalizeKey(lower);
  }
  return g.key ? g : null;
}

function normalizeKey(lower) {
  const named = {
    left: "Left", right: "Right", up: "Up", down: "Down", pageup: "PageUp", pagedown: "PageDown", tab: "Tab",
    enter: "Enter", space: "Space", backspace: "Backspace", delete: "Delete", insert: "Insert", home: "Home", end: "End",
    escape: "Escape", comma: "Comma", period: "Period", plus: "Plus", minus: "Minus",
  };
  if (named[lower]) return named[lower];
  if (/^f\d{1,2}$/.test(lower)) return lower.toUpperCase();
  return lower.length === 1 ? lower.toUpperCase() : lower;
}

function toText(g) {
  return [g.ctrl && "Ctrl", g.shift && "Shift", g.alt && "Alt", g.key].filter(Boolean).join("+");
}

const DISPLAY = { Left: "←", Right: "→", Up: "↑", Down: "↓", PageUp: "Page préc.", PageDown: "Page suiv.", Escape: "Échap", Enter: "Entrée", Space: "Espace", Delete: "Suppr", Backspace: "Retour arrière", Home: "Début", End: "Fin", Comma: ",", Period: ".", Plus: "+", Minus: "-" };

function toDisplay(g) {
  return [g.ctrl && "Ctrl", g.shift && "Maj", g.alt && "Alt", DISPLAY[g.key] || g.key].filter(Boolean).join(" + ");
}

/** Function keys may stand alone; anything else needs Ctrl or Alt (a bare letter would hijack typing). */
function isValid(g) {
  return !!g && !!g.key && (g.ctrl || g.alt || /^F\d{1,2}$/.test(g.key));
}

/** Electron's before-input-event input → gesture. */
function fromInput(input) {
  const code = input.code || "";
  let key = null;
  if (/^Digit\d$/.test(code)) key = code.slice(5);
  else if (/^Numpad\d$/.test(code)) key = code.slice(6);
  else if (/^F\d{1,2}$/.test(code)) key = code;
  else if (/^Key[A-Z]$/.test(code)) key = code.slice(3);
  else {
    const named = {
      Tab: "Tab", Enter: "Enter", Space: "Space", Backspace: "Backspace", Delete: "Delete", Insert: "Insert", Home: "Home", End: "End",
      PageUp: "PageUp", PageDown: "PageDown", ArrowLeft: "Left", ArrowRight: "Right", ArrowUp: "Up", ArrowDown: "Down", Escape: "Escape",
      Comma: "Comma", Period: "Period", Equal: "Plus", NumpadAdd: "Plus", Minus: "Minus", NumpadSubtract: "Minus",
    };
    key = named[code] || null;
  }
  if (!key) return null;
  return { ctrl: !!(input.control || input.meta), shift: !!input.shift, alt: !!input.alt, key };
}

function same(a, b) {
  return a && b && a.ctrl === b.ctrl && a.shift === b.shift && a.alt === b.alt && a.key === b.key;
}

/** The gestures in force per command: defaults, replaced by the user's overrides ("" disables). */
function buildMap(overrides = {}) {
  const map = new Map();
  for (const def of CATALOG) {
    const override = overrides[def.id];
    const gestures = override === undefined ? def.defaults.map(parse) : override === "" ? [] : [parse(override)].filter(Boolean);
    map.set(def.id, gestures);
  }
  return map;
}

/** The command for a key press, or null. */
function commandFor(input, map) {
  const g = fromInput(input);
  if (!g) return null;
  for (const [text, id] of FIXED) if (same(parse(text), g)) return id;
  for (const [id, gestures] of map) if (gestures.some((x) => same(x, g))) return id;
  return null;
}

/** Why [gesture] can't be bound to [id], or null when it can. */
function conflict(id, gesture, map) {
  if (!isValid(gesture)) return "Utilisez Ctrl ou Alt avec une touche (ou une touche F1 à F12 seule).";
  for (const [text, fixedId] of FIXED) if (same(parse(text), gesture)) return `Réservé (${fixedId.startsWith("tab.select") ? "aller à un onglet" : "fonction de la page"}).`;
  for (const [other, gestures] of map) {
    if (other !== id && gestures.some((x) => same(x, gesture))) {
      return `Déjà utilisé par « ${CATALOG.find((d) => d.id === other).label} ».`;
    }
  }
  return null;
}

module.exports = { CATALOG, parse, toText, toDisplay, isValid, fromInput, buildMap, commandFor, conflict };
