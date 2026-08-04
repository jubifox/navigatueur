package com.navigatueur.mobile.model

import android.os.Bundle
import kotlinx.serialization.Serializable
import java.util.UUID

/**
 * One browser tab. [savedState] holds the WebView's saveState() bundle so the
 * page's back/forward history and scroll position survive switching away and
 * back — the actual WebView is only ever instantiated for the active tab (see
 * BrowserWebView), which keeps memory bounded the way a mobile browser should,
 * unlike the desktop app's "keep every tab's engine resident" approach.
 */
class BrowserTab(
    val id: String = UUID.randomUUID().toString(),
    var url: String? = null,
    var title: String = "Nouvel onglet",
    val isPrivate: Boolean = false,
) {
    var savedState: Bundle? = null
    var progress: Int = 0
}

@Serializable
data class HistoryEntry(
    val url: String,
    val title: String,
    val visitedAtEpochMillis: Long,
)

@Serializable
data class Bookmark(
    val url: String,
    val title: String,
)

enum class SearchEngine(val label: String, val queryUrl: String) {
    BING("Bing", "https://www.bing.com/search?q="),
    GOOGLE("Google", "https://www.google.com/search?q="),
    DUCKDUCKGO("DuckDuckGo", "https://duckduckgo.com/?q="),
}

@Serializable
data class BrowserSettings(
    val isAdBlockEnabled: Boolean = true,
    val searchEngineName: String = SearchEngine.BING.name,
) {
    val searchEngine: SearchEngine
        get() = SearchEngine.entries.firstOrNull { it.name == searchEngineName } ?: SearchEngine.BING
}
