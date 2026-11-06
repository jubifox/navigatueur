"use strict";

function parse(text) {
  const core = String(text || "").trim().replace(/^v/i, "").split("-")[0];
  if (!core) return null;
  const parts = core.split(".").map((p) => (/^\d+$/.test(p) ? Number(p) : NaN));
  return parts.some(Number.isNaN) ? null : parts;
}

/** "0.22.0" newer than "0.21.0"? Anything after "-" (rc1…) is ignored, like the Windows updater. */
function isNewer(candidate, current) {
  const a = parse(candidate);
  if (!a) return false;
  const b = parse(current);
  if (!b) return true;
  for (let i = 0; i < Math.max(a.length, b.length); i++) {
    const x = a[i] || 0;
    const y = b[i] || 0;
    if (x !== y) return x > y;
  }
  return false;
}

/** The AppImage of a release for this processor ("x64" → x86_64, "arm64" → arm64). */
function pickAsset(names, arch) {
  const suffix = arch === "arm64" ? "-arm64.AppImage" : "-x86_64.AppImage";
  return names.find((n) => n.startsWith("Navigatueur-Linux-") && n.endsWith(suffix)) || null;
}

module.exports = { isNewer, pickAsset, parse };
