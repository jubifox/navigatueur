package com.navigatueur.mobile.browser

import android.util.Base64
import com.navigatueur.mobile.core.UrlHelper
import org.mozilla.geckoview.WebRequestError

/** The page shown when a site can't be loaded, in Navigatueur's look and in French. */
object ErrorPages {
    fun dataUri(uri: String?, error: WebRequestError): String {
        val (title, text) = describe(error, UrlHelper.displayHost(uri) ?: uri ?: "")
        val html = page(title, text, uri)
        return "data:text/html;charset=utf-8;base64," + Base64.encodeToString(html.toByteArray(), Base64.NO_WRAP)
    }

    private fun describe(error: WebRequestError, host: String): Pair<String, String> = when (error.code) {
        WebRequestError.ERROR_UNKNOWN_HOST -> "Site introuvable" to "Impossible de trouver $host. Vérifiez l'adresse, ou votre connexion."
        WebRequestError.ERROR_OFFLINE -> "Pas de connexion" to "Votre appareil n'est pas connecté à Internet."
        WebRequestError.ERROR_CONNECTION_REFUSED -> "Connexion refusée" to "$host a refusé la connexion."
        WebRequestError.ERROR_NET_TIMEOUT -> "Le site met trop de temps à répondre" to "$host ne répond pas pour le moment. Réessayez dans un instant."
        WebRequestError.ERROR_NET_RESET, WebRequestError.ERROR_NET_INTERRUPT -> "Connexion interrompue" to "La connexion avec $host a été coupée pendant le chargement."
        WebRequestError.ERROR_SECURITY_BAD_CERT, WebRequestError.ERROR_SECURITY_SSL, WebRequestError.ERROR_BAD_HSTS_CERT ->
            "Connexion non sécurisée" to "Le certificat de $host n'est pas valable : quelqu'un pourrait se faire passer pour ce site. Navigatueur ne l'a pas ouvert."
        WebRequestError.ERROR_HTTPS_ONLY -> "Ce site n'a pas de version sécurisée" to "Le mode HTTPS uniquement est activé (Paramètres → Confidentialité) et $host ne propose pas de connexion chiffrée."
        WebRequestError.ERROR_REDIRECT_LOOP -> "Redirections en boucle" to "$host renvoie sans fin vers lui-même. Effacer les cookies du site règle souvent le problème."
        WebRequestError.ERROR_MALFORMED_URI -> "Adresse incorrecte" to "Cette adresse n'est pas valide."
        WebRequestError.ERROR_UNKNOWN_PROTOCOL -> "Adresse non prise en charge" to "Aucune application ne sait ouvrir cette adresse."
        WebRequestError.ERROR_FILE_NOT_FOUND -> "Fichier introuvable" to "Ce fichier n'existe pas ou a été déplacé."
        WebRequestError.ERROR_CONTENT_CRASHED -> "La page a planté" to "Rechargez-la pour réessayer."
        WebRequestError.ERROR_PORT_BLOCKED -> "Port bloqué" to "Ce port est bloqué pour votre sécurité."
        else -> "Impossible d'ouvrir la page" to "$host n'a pas pu être chargé (erreur ${error.code})."
    }

    private fun escape(text: String) = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\"", "&quot;")

    private fun page(title: String, text: String, uri: String?): String = """
        <!doctype html><html lang="fr"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="dark light">
        <title>${escape(title)}</title>
        <style>
          :root { --bg:#0C0D0F; --card:#16171A; --border:#26272C; --text:#ECECEF; --text2:#A1A1AA; --accent:#4C8DFF; }
          @media (prefers-color-scheme: light) { :root { --bg:#E6E6EA; --card:#FBFBFC; --border:#E1E1E5; --text:#18181B; --text2:#5F5F68; } }
          html,body { margin:0; height:100%; background:var(--bg); color:var(--text); font:16px/1.5 system-ui,sans-serif; }
          main { min-height:100%; display:flex; align-items:center; justify-content:center; padding:24px; box-sizing:border-box; }
          .card { max-width:460px; width:100%; background:var(--card); border:1px solid var(--border); border-radius:16px; padding:28px 24px; }
          h1 { font-size:21px; margin:0 0 10px; }
          p { color:var(--text2); margin:0 0 22px; word-break:break-word; }
          button { background:var(--accent); color:#fff; border:0; border-radius:10px; padding:11px 18px; font:600 15px system-ui,sans-serif; }
          .brand { letter-spacing:.3em; font-size:11px; color:var(--text2); margin-bottom:18px; }
        </style></head>
        <body><main><div class="card">
          <div class="brand">NAVIGATUEUR</div>
          <h1>${escape(title)}</h1>
          <p>${escape(text)}</p>
          ${if (uri != null) "<button onclick=\"location.reload()\">Réessayer</button>" else ""}
        </div></main></body></html>
    """.trimIndent()
}
