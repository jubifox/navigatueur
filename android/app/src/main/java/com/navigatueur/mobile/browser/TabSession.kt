package com.navigatueur.mobile.browser

import android.net.Uri
import android.os.Handler
import android.os.Looper
import com.navigatueur.mobile.core.UrlHelper
import com.navigatueur.mobile.data.HistoryEntry
import com.navigatueur.mobile.data.Stores
import org.mozilla.geckoview.AllowOrDeny
import org.mozilla.geckoview.GeckoResult
import org.mozilla.geckoview.GeckoSession
import org.mozilla.geckoview.GeckoSession.ContentDelegate
import org.mozilla.geckoview.GeckoSession.HistoryDelegate
import org.mozilla.geckoview.GeckoSession.NavigationDelegate
import org.mozilla.geckoview.GeckoSession.PermissionDelegate
import org.mozilla.geckoview.GeckoSession.PermissionDelegate.ContentPermission
import org.mozilla.geckoview.GeckoSession.ProgressDelegate
import org.mozilla.geckoview.GeckoSession.ScrollDelegate
import org.mozilla.geckoview.MediaSession
import org.mozilla.geckoview.WebRequestError
import org.mozilla.geckoview.WebResponse

/**
 * Everything one tab's engine session reports — address, title, loading,
 * security, history, downloads, permissions, media — turned into the tab's
 * state and the app's reactions.
 */
class TabSession(
    private val controller: TabsController,
    private val tab: BrowserTab,
    private val stores: Stores,
) : NavigationDelegate, ProgressDelegate, ContentDelegate, HistoryDelegate, ScrollDelegate, PermissionDelegate, MediaSession.Delegate {

    private val handler = Handler(Looper.getMainLooper())

    fun attach(session: GeckoSession) {
        session.navigationDelegate = this
        session.progressDelegate = this
        session.contentDelegate = this
        session.historyDelegate = this
        session.scrollDelegate = this
        session.permissionDelegate = this
        session.mediaSessionDelegate = this
        session.promptDelegate = PromptHandler(controller, tab)
    }

    private val isActive get() = controller.activeTabId == tab.id

    // -------------------------------------------------------- navigation

    override fun onLocationChange(session: GeckoSession, url: String?, perms: MutableList<ContentPermission>, hasUserGesture: Boolean) {
        val newUrl = url ?: ""
        if (newUrl == "about:blank" && tab.url.isNotBlank() && tab.isLoading) return
        val hostChanged = UrlHelper.displayHost(newUrl) != UrlHelper.displayHost(tab.url)
        tab.url = newUrl
        if (hostChanged) {
            tab.favicon = controller.favicons.cached(newUrl)
            tab.themeColor = null
        }
        if (newUrl != tab.loadErrorUrl) tab.loadErrorUrl = null
        controller.extensions.nameFor(newUrl)?.let { if (tab.title.isBlank()) tab.title = it }
    }

    override fun onCanGoBack(session: GeckoSession, canGoBack: Boolean) {
        tab.canGoBack = canGoBack
    }

    override fun onCanGoForward(session: GeckoSession, canGoForward: Boolean) {
        tab.canGoForward = canGoForward
    }

    override fun onLoadRequest(session: GeckoSession, request: NavigationDelegate.LoadRequest): GeckoResult<AllowOrDeny>? {
        val uri = request.uri
        val scheme = uri.substringBefore(':', "").lowercase()

        if (scheme !in engineSchemes) {
            // tel:, mailto:, intent:, market:, an app's own link… — handed to Android.
            controller.emit(UiEvent.OpenExternal(uri, fallbackUrl(uri)))
            return GeckoResult.deny()
        }

        if ((scheme == "http" || scheme == "https") && controller.settings.phishingProtection) {
            val host = runCatching { Uri.parse(uri).host }.getOrNull()
            if (controller.phishing.isPhishing(host)) {
                tab.blockedUrl = uri
                tab.isLoading = false
                tab.progress = 0
                return GeckoResult.deny()
            }
        }
        return GeckoResult.allow()
    }

    override fun onNewSession(session: GeckoSession, uri: String): GeckoResult<GeckoSession>? =
        GeckoResult.fromValue(controller.sessionForNewWindow(tab, uri))

    override fun onLoadError(session: GeckoSession, uri: String?, error: WebRequestError): GeckoResult<String>? {
        tab.loadErrorUrl = uri
        return GeckoResult.fromValue(ErrorPages.dataUri(uri, error))
    }

    // ---------------------------------------------------------- progress

    override fun onPageStart(session: GeckoSession, url: String) {
        tab.isLoading = true
        tab.securityKnown = false
        tab.progress = 5
        tab.isCrashed = false
        if (isActive) controller.emit(UiEvent.ShowToolbar)
    }

    override fun onPageStop(session: GeckoSession, success: Boolean) {
        tab.isLoading = false
        tab.progress = 100
        if (isActive) handler.postDelayed({ controller.captureThumbnail(tab) }, 900)
    }

    override fun onProgressChange(session: GeckoSession, progress: Int) {
        tab.progress = progress
    }

    override fun onSecurityChange(session: GeckoSession, securityInfo: ProgressDelegate.SecurityInformation) {
        tab.isSecure = securityInfo.isSecure
        tab.securityKnown = true
    }

    override fun onSessionStateChange(session: GeckoSession, sessionState: GeckoSession.SessionState) {
        tab.engineState = sessionState.toString()
        if (!tab.isPrivate) controller.persist()
    }

    // ----------------------------------------------------------- content

    override fun onTitleChange(session: GeckoSession, title: String?) {
        tab.title = title ?: ""
        if (!tab.isPrivate && !title.isNullOrBlank()) stores.updateLastTitle(tab.url, title)
    }

    override fun onFullScreen(session: GeckoSession, fullScreen: Boolean) {
        tab.isFullScreen = fullScreen
    }

    override fun onContextMenu(session: GeckoSession, screenX: Int, screenY: Int, element: ContentDelegate.ContextElement) {
        if (element.linkUri == null && element.srcUri == null) return
        controller.contextMenu = ContextMenuRequest(
            tabId = tab.id,
            linkUrl = element.linkUri,
            linkText = element.linkText ?: element.title,
            mediaUrl = element.srcUri,
            mediaType = element.type,
            title = element.title ?: element.altText,
            isPrivate = tab.isPrivate,
        )
    }

    override fun onExternalResponse(session: GeckoSession, response: WebResponse) {
        controller.downloads?.start(response, tab)
    }

    override fun onCloseRequest(session: GeckoSession) {
        controller.closeTab(tab, remember = false)
    }

    override fun onCrash(session: GeckoSession) {
        tab.isCrashed = true
        tab.isLoading = false
        handler.post { controller.suspendCrashed(tab) }
    }

    override fun onKill(session: GeckoSession) {
        // The system reclaimed the page's process while in the background: reload it quietly when shown.
        handler.post { controller.suspendCrashed(tab, reopenIfActive = true) }
    }

    override fun onFirstContentfulPaint(session: GeckoSession) {
        if (isActive) handler.postDelayed({ controller.captureThumbnail(tab) }, 700)
    }

    override fun onShowDynamicToolbar(session: GeckoSession) {
        if (isActive) controller.emit(UiEvent.ShowToolbar)
    }

    // ----------------------------------------------------------- history

    override fun onVisited(session: GeckoSession, url: String, lastVisitedURL: String?, flags: Int): GeckoResult<Boolean>? {
        val topLevel = flags and HistoryDelegate.VISIT_TOP_LEVEL != 0
        val error = flags and HistoryDelegate.VISIT_UNRECOVERABLE_ERROR != 0
        val redirectSource = flags and (HistoryDelegate.VISIT_REDIRECT_SOURCE or HistoryDelegate.VISIT_REDIRECT_SOURCE_PERMANENT) != 0
        if (tab.isPrivate || !topLevel || error || redirectSource) return GeckoResult.fromValue(false)
        stores.recordVisit(url, tab.title)
        return GeckoResult.fromValue(true)
    }

    override fun onHistoryStateChange(session: GeckoSession, historyList: HistoryDelegate.HistoryList) {
        tab.history = historyList.map { TabHistoryItem(it.uri, it.title) }
        tab.historyIndex = historyList.currentIndex
    }

    override fun getVisited(session: GeckoSession, urls: Array<out String>): GeckoResult<BooleanArray>? {
        val visited = visitedUrls(stores.history.value)
        return GeckoResult.fromValue(BooleanArray(urls.size) { urls[it] in visited })
    }

    // ------------------------------------------------------------ scroll

    override fun onScrollChanged(session: GeckoSession, scrollX: Int, scrollY: Int) {
        if (isActive) controller.toolbar.onPageScrolled(scrollY)
    }

    // ------------------------------------------------------- permissions

    override fun onContentPermissionRequest(session: GeckoSession, perm: ContentPermission): GeckoResult<Int>? {
        val kind = when (perm.permission) {
            PermissionDelegate.PERMISSION_GEOLOCATION -> "geolocation"
            PermissionDelegate.PERMISSION_DESKTOP_NOTIFICATION -> "notifications"
            PermissionDelegate.PERMISSION_STORAGE_ACCESS -> "storage-access"
            PermissionDelegate.PERMISSION_LOCAL_NETWORK_ACCESS, PermissionDelegate.PERMISSION_LOCAL_DEVICE_ACCESS -> "local-network"
            PermissionDelegate.PERMISSION_PERSISTENT_STORAGE,
            PermissionDelegate.PERMISSION_AUTOPLAY_INAUDIBLE,
            PermissionDelegate.PERMISSION_MEDIA_KEY_SYSTEM_ACCESS -> return GeckoResult.fromValue(ContentPermission.VALUE_ALLOW)
            // Pages don't start making noise on their own; a tap on a player still plays.
            PermissionDelegate.PERMISSION_AUTOPLAY_AUDIBLE -> return GeckoResult.fromValue(ContentPermission.VALUE_DENY)
            else -> return GeckoResult.fromValue(ContentPermission.VALUE_DENY)
        }
        val result = GeckoResult<Int>()
        ask(perm.uri, kind) { allow -> result.complete(if (allow) ContentPermission.VALUE_ALLOW else ContentPermission.VALUE_DENY) }
        return result
    }

    override fun onAndroidPermissionsRequest(session: GeckoSession, permissions: Array<out String>?, callback: PermissionDelegate.Callback) {
        if (permissions.isNullOrEmpty()) {
            callback.grant()
            return
        }
        controller.emit(UiEvent.AndroidPermissions(arrayOf(*permissions)) { granted -> if (granted) callback.grant() else callback.reject() })
    }

    override fun onMediaPermissionRequest(
        session: GeckoSession,
        uri: String,
        video: Array<out PermissionDelegate.MediaSource>?,
        audio: Array<out PermissionDelegate.MediaSource>?,
        callback: PermissionDelegate.MediaCallback,
    ) {
        val camera = video?.firstOrNull { it.source == PermissionDelegate.MediaSource.SOURCE_CAMERA } ?: video?.firstOrNull()
        val microphone = audio?.firstOrNull()
        val kind = when {
            camera != null && microphone != null -> "camera+microphone"
            camera != null -> "camera"
            microphone != null -> "microphone"
            else -> {
                callback.reject()
                return
            }
        }
        ask(uri, kind) { allow -> if (allow) callback.grant(camera, microphone) else callback.reject() }
    }

    private fun ask(uri: String, kind: String, decide: (Boolean) -> Unit) {
        val host = UrlHelper.displayHost(uri) ?: uri
        if (!tab.isPrivate) {
            stores.rememberedPermission(host, kind)?.let {
                decide(it)
                return
            }
        }
        handler.post {
            controller.permissionRequests += PermissionRequest(tab.id, host, kind) { allow, remember ->
                if (remember && !tab.isPrivate) stores.rememberPermission(host, kind, allow)
                decide(allow)
            }
        }
    }

    // ------------------------------------------------------------- media

    override fun onActivated(session: GeckoSession, mediaSession: MediaSession) {
        tab.mediaSession = mediaSession
        tab.hasMedia = true
        controller.media?.onActivated(tab)
    }

    override fun onDeactivated(session: GeckoSession, mediaSession: MediaSession) {
        tab.hasMedia = false
        tab.isMediaPlaying = false
        controller.media?.onDeactivated(tab)
    }

    override fun onMetadata(session: GeckoSession, mediaSession: MediaSession, meta: MediaSession.Metadata) {
        controller.media?.onMetadata(tab, meta)
    }

    override fun onPlay(session: GeckoSession, mediaSession: MediaSession) {
        tab.isMediaPlaying = true
        controller.media?.onPlaybackChanged(tab)
    }

    override fun onPause(session: GeckoSession, mediaSession: MediaSession) {
        tab.isMediaPlaying = false
        controller.media?.onPlaybackChanged(tab)
    }

    override fun onStop(session: GeckoSession, mediaSession: MediaSession) {
        tab.isMediaPlaying = false
        controller.media?.onPlaybackChanged(tab)
    }

    override fun onPositionState(session: GeckoSession, mediaSession: MediaSession, state: MediaSession.PositionState) {
        controller.media?.onPosition(tab, state)
    }

    override fun onFullscreen(session: GeckoSession, mediaSession: MediaSession, enabled: Boolean, meta: MediaSession.ElementMetadata?) {
        if (meta != null) {
            tab.mediaWidth = meta.width
            tab.mediaHeight = meta.height
        }
        controller.media?.onVideoFullscreen(tab, enabled)
    }

    companion object {
        /** Schemes the engine loads itself; anything else is another app's business. */
        private val engineSchemes = setOf("http", "https", "about", "data", "blob", "file", "moz-extension", "resource", "javascript", "view-source")

        /** An intent: link's own web fallback ("S.browser_fallback_url=…"). */
        private fun fallbackUrl(uri: String): String? {
            if (!uri.startsWith("intent:")) return null
            val marker = "S.browser_fallback_url="
            val start = uri.indexOf(marker).takeIf { it >= 0 } ?: return null
            val raw = uri.substring(start + marker.length).substringBefore(';')
            return runCatching { Uri.decode(raw) }.getOrNull()?.takeIf { UrlHelper.isWebUrl(it) }
        }

        private var visitedCacheSource: List<HistoryEntry>? = null
        private var visitedCache: Set<String> = emptySet()

        @Synchronized
        private fun visitedUrls(history: List<HistoryEntry>): Set<String> {
            if (visitedCacheSource !== history) {
                visitedCache = history.mapTo(HashSet(history.size)) { it.url }
                visitedCacheSource = history
            }
            return visitedCache
        }
    }
}
