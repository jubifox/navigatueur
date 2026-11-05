package com.navigatueur.mobile.browser

import android.graphics.Bitmap
import android.os.Handler
import android.os.Looper
import androidx.compose.runtime.derivedStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.graphics.scale
import com.navigatueur.mobile.core.SearchEngines
import com.navigatueur.mobile.core.UrlHelper
import com.navigatueur.mobile.data.SavedGroup
import com.navigatueur.mobile.data.SavedSession
import com.navigatueur.mobile.data.SavedTab
import com.navigatueur.mobile.data.Stores
import com.navigatueur.mobile.engine.Engine
import kotlinx.coroutines.flow.MutableSharedFlow
import org.mozilla.geckoview.GeckoSession
import org.mozilla.geckoview.GeckoSessionSettings
import org.mozilla.geckoview.GeckoView

/**
 * Every tab of the app and what happens to them: opening, switching,
 * closing (and reopening), groups, pinning, and putting background tabs to
 * sleep so only a few engine sessions run at once — the mobile counterpart
 * of the Windows app's TabManagerService.
 */
class TabsController(
    private val stores: Stores,
    val extensions: ExtensionsManager,
    val phishing: com.navigatueur.mobile.engine.PhishingGuard,
    val favicons: Favicons,
) {
    val tabs = mutableStateListOf<BrowserTab>()
    val groups = mutableStateListOf<TabGroup>()
    var activeTabId by mutableStateOf<String?>(null)
        private set

    val activeTab: BrowserTab? by derivedStateOf { tabs.firstOrNull { it.id == activeTabId } }

    /** Pages waiting for the user (alerts, select menus…), first one shown. */
    val prompts = mutableStateListOf<PromptRequest>()
    val permissionRequests = mutableStateListOf<PermissionRequest>()
    var contextMenu by mutableStateOf<ContextMenuRequest?>(null)
    val events = MutableSharedFlow<UiEvent>(extraBufferCapacity = 32)

    /** Set by the browser screen: the one view that shows the active tab's page (also used to picture it for the tab overview). */
    var geckoView: GeckoView? = null

    /** Set by the activity: lets sessions use the activity for text selection menus. */
    var selectionDelegateFactory: ((GeckoSession) -> GeckoSession.SelectionActionDelegate?)? = null

    var downloads: Downloads? = null
    var media: MediaController? = null

    /** Hide-on-scroll state of the toolbar. */
    val toolbar = ToolbarState()

    private val closed = ArrayDeque<ClosedTab>()
    var canReopenClosedTab by mutableStateOf(false)
        private set

    private val handler = Handler(Looper.getMainLooper())
    private val idleCheck = object : Runnable {
        override fun run() {
            suspendIdleTabs()
            handler.postDelayed(this, 60_000)
        }
    }

    val settings get() = stores.settings.value

    // ------------------------------------------------------------ startup

    fun restore() {
        val saved = stores.session.value
        val restoreSession = settings.startupBehavior != "HomePage" && saved.tabs.isNotEmpty()

        if (restoreSession) {
            saved.groups.forEach { groups += TabGroup(it.id, it.name, it.colorHex, it.isCollapsed) }
            saved.tabs.forEach { s ->
                tabs += BrowserTab(id = s.id, initialUrl = s.url).apply {
                    title = s.title
                    groupId = s.groupId?.takeIf { id -> groups.any { it.id == id } }
                    isPinned = s.isPinned
                    isDesktopMode = s.isDesktopMode
                    engineState = s.engineState
                    favicon = favicons.cached(s.url)
                }
            }
            val active = tabs.firstOrNull { it.id == saved.activeTabId } ?: tabs.last()
            selectTab(active)
        } else {
            newTab(settings.homePageUrl.ifBlank { null })
        }
        handler.postDelayed(idleCheck, 60_000)
    }

    // ------------------------------------------------------------- tabs

    fun newTab(
        url: String? = null,
        private: Boolean = activeTab?.isPrivate ?: false,
        select: Boolean = true,
        parent: BrowserTab? = null,
        groupId: String? = parent?.groupId,
    ): BrowserTab {
        val tab = BrowserTab(initialUrl = url ?: "", isPrivate = private, parentId = parent?.id)
        tab.groupId = groupId
        val index = parent?.let { p -> lastIndexOfFamily(p) + 1 } ?: tabs.size
        tabs.add(index.coerceIn(0, tabs.size), tab)
        if (select) selectTab(tab) else if (!url.isNullOrBlank()) tab.favicon = favicons.cached(url)
        persist()
        return tab
    }

    /** Right after the last tab opened from [parent] (or [parent] itself), so links opened in a row stay in order. */
    private fun lastIndexOfFamily(parent: BrowserTab): Int {
        var index = tabs.indexOf(parent)
        while (index + 1 < tabs.size && tabs[index + 1].parentId == parent.id) index++
        return index
    }

    fun selectTab(tab: BrowserTab) {
        val previous = activeTab
        if (previous != null && previous.id != tab.id) {
            captureThumbnail(previous)
            previous.session?.setActive(false)
            previous.session?.let { markExtensionTab(it, false) }
            previous.isFullScreen = false
        }
        activeTabId = tab.id
        toolbar.reset()
        tab.lastActive = System.currentTimeMillis()
        if (!tab.isHome && tab.session == null) openSession(tab)
        tab.session?.takeIf { it.isOpen }?.let {
            it.setActive(true)
            markExtensionTab(it, true)
        }
        groups.firstOrNull { it.id == tab.groupId }?.isCollapsed = false
        enforceLiveLimit()
        persist()
    }

    /** The previous/next tab of the same kind (normal or private), for the toolbar swipe. */
    fun selectAdjacent(direction: Int) {
        val current = activeTab ?: return
        val same = tabs.filter { it.isPrivate == current.isPrivate }
        val index = same.indexOf(current) + direction
        same.getOrNull(index)?.let(::selectTab)
    }

    fun closeTab(tab: BrowserTab, remember: Boolean = true) {
        val index = tabs.indexOf(tab)
        if (index < 0) return
        if (remember) {
            closed.addLast(ClosedTab(tab.url, tab.title, index, tab.groupId, tab.isPinned, tab.isPrivate, tab.engineState))
            while (closed.size > 25) closed.removeFirst()
            canReopenClosedTab = true
        }
        val wasActive = tab.id == activeTabId
        closeSession(tab)
        tabs.removeAt(index)
        dropEmptyGroups()

        if (wasActive) {
            // Back to the tab it was opened from, else its neighbor in the same mode (normal/private).
            val next = tabs.firstOrNull { it.id == tab.parentId && it.isPrivate == tab.isPrivate }
                ?: tabs.filter { it.isPrivate == tab.isPrivate }.let { same -> same.getOrNull(index.coerceAtMost(same.size - 1)) ?: same.lastOrNull() }
                ?: tabs.lastOrNull()
            if (next != null) selectTab(next) else newTab(private = false)
        }
        persist()
    }

    fun closeAll(private: Boolean) {
        tabs.filter { it.isPrivate == private }.forEach { closeTab(it, remember = !private) }
    }

    fun closeOthers(keep: BrowserTab) {
        tabs.filter { it.isPrivate == keep.isPrivate && it.id != keep.id && !it.isPinned }.forEach { closeTab(it) }
    }

    fun reopenClosedTab() {
        val record = closed.removeLastOrNull() ?: return
        canReopenClosedTab = closed.isNotEmpty()
        val tab = BrowserTab(initialUrl = record.url, isPrivate = record.isPrivate).apply {
            title = record.title
            groupId = record.groupId?.takeIf { id -> groups.any { it.id == id } }
            isPinned = record.isPinned
            engineState = record.engineState
            favicon = favicons.cached(record.url)
        }
        tabs.add(record.index.coerceIn(0, tabs.size), tab)
        selectTab(tab)
    }

    fun duplicate(tab: BrowserTab) {
        val copy = newTab(tab.url.ifBlank { null }, private = tab.isPrivate, select = true, parent = tab)
        copy.engineState = tab.engineState
    }

    fun moveTab(tab: BrowserTab, toIndex: Int) {
        val from = tabs.indexOf(tab)
        if (from < 0) return
        tabs.removeAt(from)
        tabs.add(toIndex.coerceIn(0, tabs.size), tab)
        persist()
    }

    fun togglePin(tab: BrowserTab) {
        tab.isPinned = !tab.isPinned
        if (tab.isPinned) tab.groupId = null
        dropEmptyGroups()
        persist()
    }

    // ------------------------------------------------------------ groups

    fun createGroup(tab: BrowserTab, name: String? = null): TabGroup {
        val color = TabGroup.colors[groups.size % TabGroup.colors.size].second
        val group = TabGroup(name = name?.takeIf { it.isNotBlank() } ?: "Groupe ${groups.size + 1}", colorHex = color)
        groups += group
        tab.isPinned = false
        tab.groupId = group.id
        persist()
        return group
    }

    fun addToGroup(tab: BrowserTab, group: TabGroup) {
        tab.isPinned = false
        tab.groupId = group.id
        // Keep a group's tabs side by side in the list.
        val lastOfGroup = tabs.indexOfLast { it.groupId == group.id && it.id != tab.id }
        if (lastOfGroup >= 0) moveTab(tab, if (tabs.indexOf(tab) > lastOfGroup) lastOfGroup + 1 else lastOfGroup)
        dropEmptyGroups()
        persist()
    }

    fun removeFromGroup(tab: BrowserTab) {
        tab.groupId = null
        dropEmptyGroups()
        persist()
    }

    fun ungroup(group: TabGroup) {
        tabs.filter { it.groupId == group.id }.forEach { it.groupId = null }
        groups.remove(group)
        persist()
    }

    fun closeGroup(group: TabGroup) {
        tabs.filter { it.groupId == group.id }.forEach { closeTab(it) }
        groups.remove(group)
        persist()
    }

    fun renameGroup(group: TabGroup, name: String) {
        if (name.isNotBlank()) group.name = name.trim()
        persist()
    }

    fun recolorGroup(group: TabGroup, colorHex: String) {
        group.colorHex = colorHex
        persist()
    }

    fun toggleGroupCollapsed(group: TabGroup) {
        group.isCollapsed = !group.isCollapsed
        persist()
    }

    private fun dropEmptyGroups() {
        groups.removeAll { group -> tabs.none { it.groupId == group.id } }
    }

    // -------------------------------------------------------- navigation

    /** Loads what was typed in the address bar: a URL, or a search with the chosen engine. */
    fun loadInput(tab: BrowserTab, input: String) {
        val url = UrlHelper.normalize(input, SearchEngines.byId(settings.searchEngine).queryPrefix)
        if (url.isEmpty()) return
        loadUrl(tab, url)
    }

    fun loadUrl(tab: BrowserTab, url: String) {
        tab.blockedUrl = null
        tab.url = url
        tab.favicon = favicons.cached(url) ?: tab.favicon.takeIf { UrlHelper.displayHost(url) == UrlHelper.displayHost(tab.url) }
        val session = tab.session
        if (session == null) {
            tab.engineState = null
            openSession(tab)
            if (tab.id == activeTabId) tab.session?.setActive(true)
        } else {
            session.loadUri(url)
        }
        persist()
    }

    fun goHome(tab: BrowserTab) {
        val home = settings.homePageUrl
        if (home.isNotBlank()) loadUrl(tab, home) else showHome(tab)
    }

    /** Shows the Navigatueur home page in [tab] (its page history stays for Back). */
    fun showHome(tab: BrowserTab) {
        tab.blockedUrl = null
        captureThumbnail(tab)
        closeSession(tab)
        tab.engineState = null
        tab.url = ""
        tab.title = ""
        tab.favicon = null
        tab.thumbnail = null
        persist()
    }

    fun goBack(tab: BrowserTab): Boolean {
        if (tab.blockedUrl != null) {
            tab.blockedUrl = null
            if (tab.session == null) showHome(tab)
            return true
        }
        if (tab.canGoBack) {
            tab.session?.goBack()
            return true
        }
        return false
    }

    fun goForward(tab: BrowserTab) = tab.session?.goForward()

    fun reload(tab: BrowserTab) {
        if (tab.isCrashed || tab.session == null) {
            tab.isCrashed = false
            closeSession(tab)
            if (!tab.isHome) openSession(tab)
            tab.session?.setActive(tab.id == activeTabId)
        } else {
            tab.session?.reload()
        }
    }

    fun stop(tab: BrowserTab) = tab.session?.stop()

    fun setDesktopMode(tab: BrowserTab, desktop: Boolean) {
        tab.isDesktopMode = desktop
        tab.session?.settings?.apply {
            setUserAgentMode(if (desktop) GeckoSessionSettings.USER_AGENT_MODE_DESKTOP else GeckoSessionSettings.USER_AGENT_MODE_MOBILE)
            setViewportMode(if (desktop) GeckoSessionSettings.VIEWPORT_MODE_DESKTOP else GeckoSessionSettings.VIEWPORT_MODE_MOBILE)
        }
        tab.session?.reload()
        persist()
    }

    /** The phishing warning's "open anyway". */
    fun openBlockedAnyway(tab: BrowserTab) {
        val url = tab.blockedUrl ?: return
        UrlHelper.displayHost(url)?.let(phishing::allowOnce)
        loadUrl(tab, url)
    }

    // ---------------------------------------------------------- sessions

    private fun openSession(tab: BrowserTab) {
        val session = createSession(tab)
        tab.session = session
        tab.isLive = true
        session.open(Engine.runtime)
        val state = tab.engineState?.let { runCatching { GeckoSession.SessionState.fromString(it) }.getOrNull() }
        if (state != null && state.size > 0) session.restoreState(state) else if (!tab.isHome) session.loadUri(tab.url)
        if (tab.id == activeTabId) markExtensionTab(session, true)
        sessionsChanged++
    }

    /**
     * A session the engine opens itself (window.open, target=_blank). It must
     * be handed back unopened; the engine opens it a moment later, and the
     * screen shows it once it is ([sessionsChanged]).
     */
    fun sessionForNewWindow(opener: BrowserTab, uri: String): GeckoSession {
        val tab = BrowserTab(initialUrl = uri, isPrivate = opener.isPrivate, parentId = opener.id)
        tab.groupId = opener.groupId
        tabs.add((lastIndexOfFamily(opener) + 1).coerceIn(0, tabs.size), tab)
        val session = createSession(tab)
        tab.session = session
        tab.isLive = true
        selectTab(tab)
        notifyWhenOpen(session)
        return session
    }

    /** An extension opening one of its pages (welcome, options): an opened session the engine loads the page into. */
    fun sessionForExtensionTab(url: String, select: Boolean): GeckoSession {
        val tab = BrowserTab(initialUrl = url, isPrivate = false)
        tabs += tab
        val session = createSession(tab)
        tab.session = session
        tab.isLive = true
        session.open(Engine.runtime)
        if (select) selectTab(tab)
        sessionsChanged++
        persist()
        return session
    }

    /** Bumped whenever a session the screen may need to show appears; the browser screen re-attaches its view. */
    var sessionsChanged by mutableStateOf(0)

    private fun notifyWhenOpen(session: GeckoSession, attempts: Int = 0) {
        if (session.isOpen) {
            sessionsChanged++
            if (tabFor(session)?.id == activeTabId) {
                session.setActive(true)
                markExtensionTab(session, true)
            }
        } else if (attempts < 60) {
            handler.postDelayed({ notifyWhenOpen(session, attempts + 1) }, 50)
        }
    }

    private fun createSession(tab: BrowserTab): GeckoSession {
        val sessionSettings = GeckoSessionSettings.Builder()
            .usePrivateMode(tab.isPrivate)
            .useTrackingProtection(settings.trackingProtection)
            .userAgentMode(if (tab.isDesktopMode) GeckoSessionSettings.USER_AGENT_MODE_DESKTOP else GeckoSessionSettings.USER_AGENT_MODE_MOBILE)
            .viewportMode(if (tab.isDesktopMode) GeckoSessionSettings.VIEWPORT_MODE_DESKTOP else GeckoSessionSettings.VIEWPORT_MODE_MOBILE)
            .suspendMediaWhenInactive(false)
            .build()
        val session = GeckoSession(sessionSettings)
        TabSession(this, tab, stores).attach(session)
        selectionDelegateFactory?.invoke(session)?.let(session::setSelectionActionDelegate)
        extensions.attachSession(session, tab)
        return session
    }

    private fun closeSession(tab: BrowserTab) {
        val session = tab.session ?: return
        if (geckoView?.session === session) geckoView?.releaseSession()
        media?.onTabSessionClosed(tab)
        tab.session = null
        tab.pagePort = null
        tab.mediaSession = null
        tab.isLive = false
        tab.isLoading = false
        tab.isMediaPlaying = false
        tab.hasMedia = false
        tab.isFullScreen = false
        runCatching { session.close() }
    }

    /** Extensions see which tab is in front (their buttons and popups act on it). */
    private fun markExtensionTab(session: GeckoSession, active: Boolean) {
        if (session.isOpen) runCatching { Engine.runtime.webExtensionController.setTabActive(session, active) }
    }

    /** Puts a background tab to sleep: its session goes, its address and history stay. */
    fun suspend(tab: BrowserTab) {
        if (tab.session == null || tab.id == activeTabId) return
        tab.session?.flushSessionState()
        closeSession(tab)
    }

    private fun enforceLiveLimit() {
        val limit = settings.maxLiveTabs.coerceIn(1, 20)
        val live = tabs.filter { it.session != null && it.id != activeTabId }
            .sortedBy { it.lastActive }
        val excess = live.size - (limit - 1)
        if (excess <= 0) return
        live.asSequence()
            .filter { !it.isPinned && !it.isMediaPlaying }
            .take(excess)
            .toList()
            .forEach(::suspend)
    }

    private fun suspendIdleTabs() {
        val minutes = settings.suspendMinutes
        if (minutes <= 0) return
        val cutoff = System.currentTimeMillis() - minutes * 60_000L
        tabs.filter { it.session != null && it.id != activeTabId && !it.isPinned && !it.isMediaPlaying && it.lastActive < cutoff }
            .forEach(::suspend)
    }

    /** The page's process died: drop the session (history stays), and bring it back right away if it's on screen. */
    fun suspendCrashed(tab: BrowserTab, reopenIfActive: Boolean = false) {
        if (tab.session == null) return
        closeSession(tab)
        if (reopenIfActive && tab.id == activeTabId && !tab.isHome) {
            tab.isCrashed = false
            openSession(tab)
            tab.session?.setActive(true)
        }
    }

    /** Low memory: everything but the visible tab and what's playing goes to sleep. */
    fun suspendBackgroundTabs() {
        tabs.filter { it.id != activeTabId && !it.isMediaPlaying }.forEach(::suspend)
    }

    // -------------------------------------------------------- thumbnails

    fun captureThumbnail(target: BrowserTab? = null) {
        val tab = target ?: activeTab ?: return
        val view = geckoView ?: return
        if (tab.id != activeTabId || tab.isHome || tab.session == null || view.session !== tab.session) return
        runCatching {
            view.capturePixels().accept({ bitmap ->
                if (bitmap != null && bitmap.width > 0) tab.thumbnail = shrink(bitmap)
            }, { })
        }
    }

    private fun shrink(bitmap: Bitmap): Bitmap {
        val width = 420
        if (bitmap.width <= width) return bitmap
        val height = (bitmap.height * width.toFloat() / bitmap.width).toInt().coerceAtLeast(1)
        return bitmap.scale(width, height).also { if (it !== bitmap) bitmap.recycle() }
    }

    // ------------------------------------------------------- persistence

    fun persist() {
        val session = SavedSession(
            tabs = tabs.filter { !it.isPrivate }.map {
                SavedTab(
                    id = it.id,
                    url = it.blockedUrl ?: it.url,
                    title = it.title,
                    groupId = it.groupId,
                    isPinned = it.isPinned,
                    isDesktopMode = it.isDesktopMode,
                    engineState = it.engineState,
                )
            },
            groups = groups.filter { g -> tabs.any { it.groupId == g.id && !it.isPrivate } }
                .map { SavedGroup(it.id, it.name, it.colorHex, it.isCollapsed) },
            activeTabId = activeTab?.takeIf { !it.isPrivate }?.id ?: tabs.lastOrNull { !it.isPrivate }?.id,
        )
        stores.session.set(session)
    }

    /** What the helper script in a page reports. */
    fun onPageMessage(tab: BrowserTab, message: org.json.JSONObject) {
        when (message.optString("type")) {
            "page" -> {
                if (UrlHelper.displayHost(message.optString("url")) != UrlHelper.displayHost(tab.url)) return
                tab.themeColor = parseCssColor(message.optString("themeColor", ""))
                favicons.load(tab, message.optJSONArray("icons"))
            }
            "fullscreenVideo" -> media?.onFullscreenVideoAnswer(tab, message.optBoolean("ok"), message.optLong("width"), message.optLong("height"))
        }
    }

    fun tabFor(session: GeckoSession?): BrowserTab? = session?.let { s -> tabs.firstOrNull { it.session === s } }

    fun tabById(id: String?): BrowserTab? = id?.let { tabs.firstOrNull { t -> t.id == it } }

    fun emit(event: UiEvent) {
        events.tryEmit(event)
    }
}

/** The toolbar slides away while reading down a page and comes back when scrolling up, at the top, or when a page loads. */
class ToolbarState {
    var hidden by mutableStateOf(false)
        private set
    var enabled = true
    private var lastY = 0

    fun onPageScrolled(y: Int) {
        val dy = y - lastY
        lastY = y
        hidden = when {
            !enabled || y <= 0 -> false
            dy > 24 -> true
            dy < -24 -> false
            else -> hidden
        }
    }

    fun show() {
        hidden = false
    }

    fun reset() {
        lastY = 0
        hidden = false
    }
}

/** "#RRGGBB", "#RGB" or "rgb(r, g, b)" → ARGB int, anything else → null. */
fun parseCssColor(text: String?): Int? {
    val value = text?.trim()?.lowercase() ?: return null
    if (value.isEmpty()) return null
    runCatching {
        if (value.startsWith("#")) {
            val hex = value.substring(1)
            val full = when (hex.length) {
                3 -> hex.map { "$it$it" }.joinToString("")
                6 -> hex
                8 -> hex.substring(0, 6)
                else -> return null
            }
            return (0xFF000000L or full.toLong(16)).toInt()
        }
        val match = Regex("""rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)""").find(value) ?: return null
        val (r, g, b) = match.destructured
        return (0xFF shl 24) or (r.toInt().coerceIn(0, 255) shl 16) or (g.toInt().coerceIn(0, 255) shl 8) or b.toInt().coerceIn(0, 255)
    }
    return null
}
