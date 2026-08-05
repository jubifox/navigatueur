package com.navigatueur.mobile.model

import android.os.Bundle
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.ImageBitmap
import kotlinx.serialization.Serializable
import java.util.UUID

/**
 * One browser tab. [savedState] holds the WebView's saveState() bundle so the
 * page's back/forward history and scroll position survive switching away and
 * back — the actual WebView is only ever instantiated for the active tab (see
 * BrowserWebView), which keeps memory bounded the way a mobile browser should,
 * unlike the desktop app's "keep every tab's engine resident" approach.
 *
 * The observable fields use Compose's mutableStateOf delegate (not plain
 * `var`) — a plain var mutated from a WebViewClient callback doesn't trigger
 * recomposition on its own, so without this the progress bar/title/favicon
 * would silently never update on screen despite the underlying data changing.
 */
class BrowserTab(
    val id: String = UUID.randomUUID().toString(),
    startUrl: String? = null,
    startTitle: String = "Nouvel onglet",
    val isPrivate: Boolean = false,
) {
    var url: String? by mutableStateOf(startUrl)
    var title: String by mutableStateOf(startTitle)
    var progress: Int by mutableIntStateOf(0)
    var favicon: ImageBitmap? by mutableStateOf(null)
    var isDesktopSite: Boolean by mutableStateOf(false)

    /** Not observed by Compose on purpose — only ever read/written imperatively around a tab switch, never during a recomposition. */
    var savedState: Bundle? = null
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

/** Chrome's own desktop UA string — sites that sniff for "Mobi"/Android serve their mobile layout otherwise, defeating the point of a "desktop site" toggle. */
const val DesktopUserAgent =
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
