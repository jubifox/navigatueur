"use strict";
// The page shown when a site can't be loaded, in Navigatueur's look and in French.
const { displayHost } = require("../core/url");

function describe(code, host) {
  switch (code) {
    case -105:
    case -137:
      return ["Site introuvable", `Impossible de trouver ${host}. Vérifiez l'adresse, ou votre connexion.`];
    case -106:
      return ["Pas de connexion", "Votre ordinateur n'est pas connecté à Internet."];
    case -102:
      return ["Connexion refusée", `${host} a refusé la connexion.`];
    case -118:
    case -7:
      return ["Le site met trop de temps à répondre", `${host} ne répond pas pour le moment. Réessayez dans un instant.`];
    case -101:
    case -100:
      return ["Connexion interrompue", `La connexion avec ${host} a été coupée pendant le chargement.`];
    case -310:
      return ["Redirections en boucle", `${host} renvoie sans fin vers lui-même. Effacer les cookies du site règle souvent le problème.`];
    default:
      if (code <= -200 && code > -300) {
        return ["Connexion non sécurisée", `Le certificat de ${host} n'est pas valable : quelqu'un pourrait se faire passer pour ce site. Navigatueur ne l'a pas ouvert.`];
      }
      return ["Impossible d'ouvrir la page", `${host} n'a pas pu être chargé (${code}).`];
  }
}

function escape(text) {
  return String(text).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

function errorPage(url, code) {
  const [title, text] = describe(code, displayHost(url) || url || "");
  const html = `<!doctype html><html lang="fr"><head><meta charset="utf-8">
<meta name="color-scheme" content="dark light"><title>${escape(title)}</title>
<style>
:root{--bg:#0C0D0F;--card:#16171A;--border:#26272C;--text:#ECECEF;--text2:#A1A1AA;--accent:#4C8DFF}
@media (prefers-color-scheme: light){:root{--bg:#E6E6EA;--card:#FBFBFC;--border:#E1E1E5;--text:#18181B;--text2:#5F5F68}}
html,body{margin:0;height:100%;background:var(--bg);color:var(--text);font:16px/1.5 system-ui,sans-serif}
main{min-height:100%;display:flex;align-items:center;justify-content:center;padding:24px;box-sizing:border-box}
.card{max-width:520px;width:100%;background:var(--card);border:1px solid var(--border);border-radius:16px;padding:30px 28px}
h1{font-size:22px;margin:0 0 10px}p{color:var(--text2);margin:0 0 22px;word-break:break-word}
button{background:var(--accent);color:#fff;border:0;border-radius:10px;padding:11px 18px;font:600 15px system-ui,sans-serif;cursor:pointer}
.brand{letter-spacing:.3em;font-size:11px;color:var(--text2);margin-bottom:18px}
</style></head><body><main><div class="card"><div class="brand">NAVIGATUEUR</div>
<h1>${escape(title)}</h1><p>${escape(text)}</p>
<button onclick="location.replace(${escape(JSON.stringify(url || ""))})">Réessayer</button>
</div></main></body></html>`;
  return "data:text/html;charset=utf-8," + encodeURIComponent(html);
}

module.exports = { errorPage };
