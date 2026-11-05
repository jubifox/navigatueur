package com.navigatueur.mobile.core

/** The five engines of the Windows app (SearchEngineService), with the same ids, so the choice reads the same everywhere. */
data class SearchEngine(val id: String, val displayName: String, val queryPrefix: String, val homeUrl: String)

object SearchEngines {
    val all = listOf(
        SearchEngine("Bing", "Bing", "https://www.bing.com/search?q=", "https://www.bing.com/"),
        SearchEngine("Google", "Google", "https://www.google.com/search?q=", "https://www.google.com/"),
        SearchEngine("DuckDuckGo", "DuckDuckGo", "https://duckduckgo.com/?q=", "https://duckduckgo.com/"),
        SearchEngine("Qwant", "Qwant", "https://www.qwant.com/?q=", "https://www.qwant.com/"),
        SearchEngine("Ecosia", "Ecosia", "https://www.ecosia.org/search?q=", "https://www.ecosia.org/"),
    )

    val default: SearchEngine = all.first()

    fun byId(id: String?): SearchEngine = all.firstOrNull { it.id == id } ?: default

    /** The words searched for, when [url] is a results page of one of the engines (shown in the address bar instead of the long URL). */
    fun searchTermsOf(url: String?): String? {
        if (url == null) return null
        val engine = all.firstOrNull { url.startsWith(it.queryPrefix) } ?: return null
        val raw = url.substring(engine.queryPrefix.length).substringBefore('&').substringBefore('#')
        return runCatching { java.net.URLDecoder.decode(raw, "UTF-8") }.getOrNull()?.takeIf { it.isNotBlank() }
    }
}
