package com.navigatueur.mobile.browser

import android.graphics.Bitmap
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.navigatueur.mobile.core.SearchEngines
import com.navigatueur.mobile.core.UrlHelper
import org.mozilla.geckoview.GeckoSession
import org.mozilla.geckoview.MediaSession
import org.mozilla.geckoview.WebExtension
import java.util.UUID

/**
 * One tab. Its engine session exists only while the tab is "live": a tab
 * that hasn't been opened since the app started, or that was put to sleep
 * to save memory, keeps its address, title, icon, picture and back/forward
 * history ([engineState]) and gets a fresh session when it's shown again.
 */
class BrowserTab(
    val id: String = UUID.randomUUID().toString(),
    initialUrl: String = "",
    val isPrivate: Boolean = false,
    /** The tab this one was opened from (a link opened in a new tab): Back on its first page returns there. */
    val parentId: String? = null,
) {
    var url by mutableStateOf(initialUrl)
    var title by mutableStateOf("")
    var progress by mutableIntStateOf(0)
    var isLoading by mutableStateOf(false)
    var canGoBack by mutableStateOf(false)
    var canGoForward by mutableStateOf(false)
    var isSecure by mutableStateOf(false)
    var favicon by mutableStateOf<Bitmap?>(null)
    var themeColor by mutableStateOf<Int?>(null)
    var thumbnail by mutableStateOf<Bitmap?>(null)
    var groupId by mutableStateOf<String?>(null)
    var isPinned by mutableStateOf(false)
    var isDesktopMode by mutableStateOf(false)
    var isMediaPlaying by mutableStateOf(false)
    var hasMedia by mutableStateOf(false)
    var isFullScreen by mutableStateOf(false)
    var isCrashed by mutableStateOf(false)
    var isLive by mutableStateOf(false)
    var lastActive by mutableLongStateOf(System.currentTimeMillis())

    /** Phishing warning shown instead of the page (the blocked address). */
    var blockedUrl by mutableStateOf<String?>(null)

    /** Error shown by the engine (no network, bad certificate…) — the page itself is an error page. */
    var loadErrorUrl: String? = null

    var session: GeckoSession? = null
    var engineState: String? = null
    var mediaSession: MediaSession? = null
    var mediaWidth: Long = 0
    var mediaHeight: Long = 0

    /** Connection to Navigatueur's helper script in the page (icons, fullscreen video). */
    var pagePort: WebExtension.Port? = null

    /** Shown when the tab has no web page: the Navigatueur home page. */
    val isHome: Boolean get() = blockedUrl == null && (url.isBlank() || url == "about:blank")

    val displayTitle: String
        get() = when {
            isHome -> "Nouvel onglet"
            title.isNotBlank() -> title
            url.startsWith("moz-extension://") -> "Extension"
            else -> UrlHelper.displayHost(url) ?: url
        }

    /** The address bar's resting text: the words of a search, the site otherwise. */
    val addressText: String
        get() = when {
            url.startsWith("moz-extension://") -> title.ifBlank { "Extension" }
            else -> SearchEngines.searchTermsOf(url) ?: UrlHelper.shortDisplay(url)
        }
}

class TabGroup(
    val id: String = UUID.randomUUID().toString(),
    name: String,
    colorHex: String,
    isCollapsed: Boolean = false,
) {
    var name by mutableStateOf(name)
    var colorHex by mutableStateOf(colorHex)
    var isCollapsed by mutableStateOf(isCollapsed)

    companion object {
        /** The Windows app's group colors (TabManagerService.GroupColorChoices), plus pink and grey. */
        val colors = listOf(
            "Bleu" to "#4C8DFF",
            "Vert" to "#4FE0A0",
            "Ambre" to "#E0A52A",
            "Rouge" to "#E04F4F",
            "Violet" to "#B14FE0",
            "Cyan" to "#4FD1E0",
            "Rose" to "#F472B6",
            "Gris" to "#8B8D98",
        )
    }
}

/** What Ctrl+Shift+T brings back. */
data class ClosedTab(
    val url: String,
    val title: String,
    val index: Int,
    val groupId: String?,
    val isPinned: Boolean,
    val isPrivate: Boolean,
    val engineState: String?,
)
