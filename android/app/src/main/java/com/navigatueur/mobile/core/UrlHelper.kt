package com.navigatueur.mobile.core

import java.net.URI
import java.net.URLEncoder

/**
 * Address-bar logic, ported from the Windows app's Navigatueur.Core.UrlHelper
 * (same rules, same test matrix in UrlHelperTest): text that already looks
 * like a URL is loaded as is, a bare host gets https://, everything else is
 * searched with the chosen engine.
 */
object UrlHelper {
    private val schemesKeptAsIs = setOf("http", "https", "file", "about", "data", "view-source", "moz-extension", "resource")

    fun normalize(input: String, searchUrlPrefix: String = SearchEngines.default.queryPrefix): String {
        val trimmed = input.trim()
        if (trimmed.isEmpty()) return ""

        absoluteScheme(trimmed)?.let { scheme ->
            if (scheme in schemesKeptAsIs) return trimmed
        }

        if (looksLikeHost(trimmed)) return "https://$trimmed"

        return searchUrlPrefix + encode(trimmed)
    }

    /** URLEncoder writes spaces as "+", which every engine accepts; "%20" is what .NET's EscapeDataString writes. */
    private fun encode(text: String): String = URLEncoder.encode(text, "UTF-8").replace("+", "%20")

    private fun absoluteScheme(text: String): String? {
        val colon = text.indexOf(':')
        if (colon <= 0) return null
        val scheme = text.substring(0, colon)
        if (!scheme.first().isLetter() || !scheme.all { it.isLetterOrDigit() || it == '+' || it == '-' || it == '.' }) return null
        // "localhost:8080" or "example.com:8443/x" are hosts with a port, not schemes.
        val rest = text.substring(colon + 1)
        if (rest.firstOrNull()?.isDigit() == true) return null
        return scheme.lowercase()
    }

    fun looksLikeHost(text: String): Boolean {
        if (text.contains(' ')) return false
        if (text.equals("localhost", ignoreCase = true) || text.startsWith("localhost:", ignoreCase = true)) return true
        val hostPart = text.split('/', limit = 2)[0].split(':', limit = 2)[0]
        return hostPart.contains('.') && !hostPart.endsWith(".") && !hostPart.startsWith(".")
    }

    /** The host without a leading "www.", or null if [url] isn't an absolute URL with a host. */
    fun displayHost(url: String?): String? {
        if (url.isNullOrBlank()) return null
        val host = runCatching { URI(url.trim()).host }.getOrNull()?.takeIf { it.isNotEmpty() } ?: return null
        return if (host.startsWith("www.", ignoreCase = true)) host.substring(4) else host
    }

    fun isWebUrl(url: String?): Boolean {
        val scheme = url?.let { absoluteScheme(it.trim()) } ?: return false
        return scheme == "http" || scheme == "https"
    }

    /** The key two URLs are compared by (bookmarks, suggestions): lower-case scheme and host, no default port, no "/" path, no fragment. */
    fun normalizeKey(url: String): String {
        val trimmed = url.trim()
        val uri = runCatching { URI(trimmed) }.getOrNull()
        if (uri == null || uri.scheme == null || uri.host == null) return trimmed
        val scheme = uri.scheme.lowercase()
        val defaultPort = (scheme == "https" && uri.port == 443) || (scheme == "http" && uri.port == 80) || uri.port == -1
        val port = if (defaultPort) "" else ":${uri.port}"
        val path = if (uri.rawPath.isNullOrEmpty() || uri.rawPath == "/") "" else uri.rawPath
        val query = uri.rawQuery?.let { "?$it" } ?: ""
        return "$scheme://${uri.host.lowercase()}$port$path$query"
    }

    /** What the address bar shows while not editing: the host for web pages, nothing for the home page. */
    fun shortDisplay(url: String?): String = when {
        url.isNullOrBlank() || url == "about:blank" -> ""
        else -> displayHost(url) ?: url
    }
}
