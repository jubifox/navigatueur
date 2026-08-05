package com.navigatueur.mobile.ui

import android.webkit.CookieManager
import android.webkit.WebStorage
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.navigatueur.mobile.data.AdBlockManager
import com.navigatueur.mobile.data.BrowserRepository
import com.navigatueur.mobile.model.Bookmark
import com.navigatueur.mobile.model.BrowserSettings
import com.navigatueur.mobile.model.BrowserTab
import com.navigatueur.mobile.model.HistoryEntry
import com.navigatueur.mobile.model.SearchEngine
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

/** Screen the main content area is currently showing, layered over/instead of the active tab's WebView. */
enum class OverlayScreen { NONE, TAB_SWITCHER, HISTORY, BOOKMARKS, SETTINGS }

class BrowserViewModel(
    private val repository: BrowserRepository,
    val adBlock: AdBlockManager,
) : ViewModel() {

    val tabs = mutableStateListOf<BrowserTab>()
    var activeTabId by mutableStateOf<String?>(null)
        private set

    var addressBarText by mutableStateOf("")
    var overlay by mutableStateOf(OverlayScreen.NONE)
    var isFindInPageVisible by mutableStateOf(false)
    var findInPageQuery by mutableStateOf("")

    val history = mutableStateListOf<HistoryEntry>()
    val bookmarks = mutableStateListOf<Bookmark>()
    var settings by mutableStateOf(BrowserSettings())
        private set

    /** Bumped on every navigation/title change so BrowserWebView's caller knows to re-sync the address bar text with the active tab. */
    var addressBarSyncTick by mutableStateOf(0)
        private set

    val activeTab: BrowserTab?
        get() = tabs.firstOrNull { it.id == activeTabId }

    init {
        history.addAll(repository.loadHistory())
        bookmarks.addAll(repository.loadBookmarks())
        settings = repository.loadSettings()
        adBlock.isEnabled = settings.isAdBlockEnabled
        newTab()
    }

    fun newTab(private: Boolean = false) {
        val tab = BrowserTab(isPrivate = private)
        tabs.add(tab)
        activateTab(tab.id)
        overlay = OverlayScreen.NONE
    }

    fun activateTab(id: String) {
        activeTabId = id
        addressBarText = tabs.firstOrNull { it.id == id }?.url.orEmpty()
        overlay = OverlayScreen.NONE
    }

    fun closeTab(id: String) {
        val closedTab = tabs.firstOrNull { it.id == id }
        val index = tabs.indexOfFirst { it.id == id }
        if (index < 0) return
        tabs.removeAt(index)

        if (closedTab?.isPrivate == true) {
            clearPrivateData()
        }

        if (tabs.isEmpty()) {
            newTab()
            return
        }

        if (activeTabId == id) {
            val next = tabs.getOrNull(index) ?: tabs.getOrNull(index - 1) ?: tabs.first()
            activateTab(next.id)
        }
    }

    /** Resolves what was typed: a bare host/URL navigates directly, anything else becomes a search query. */
    fun navigateFromAddressBar(input: String) {
        val trimmed = input.trim()
        if (trimmed.isEmpty()) return

        val url = when {
            trimmed.startsWith("http://") || trimmed.startsWith("https://") -> trimmed
            looksLikeBareHost(trimmed) -> "https://$trimmed"
            else -> settings.searchEngine.queryUrl + java.net.URLEncoder.encode(trimmed, "UTF-8")
        }

        activeTab?.url = url
        addressBarText = url
        addressBarSyncTick++
    }

    private fun looksLikeBareHost(text: String): Boolean =
        !text.contains(" ") && text.contains(".") && !text.contains("\n")

    fun onPageNavigated(tabId: String, url: String, title: String) {
        val tab = tabs.firstOrNull { it.id == tabId } ?: return
        tab.url = url
        tab.title = title.ifBlank { url }
        if (tabId == activeTabId) {
            addressBarText = url
        }
        addressBarSyncTick++

        if (!tab.isPrivate) {
            recordHistory(url, tab.title)
        }
    }

    private fun recordHistory(url: String, title: String) {
        history.add(0, HistoryEntry(url, title, System.currentTimeMillis()))
        viewModelScope.launch(Dispatchers.IO) {
            repository.saveHistory(history.toList())
        }
    }

    fun clearHistory() {
        history.clear()
        viewModelScope.launch(Dispatchers.IO) { repository.saveHistory(emptyList()) }
    }

    fun toggleBookmark(url: String, title: String) {
        val existing = bookmarks.firstOrNull { it.url == url }
        if (existing != null) {
            bookmarks.remove(existing)
        } else {
            bookmarks.add(0, Bookmark(url, title))
        }
        viewModelScope.launch(Dispatchers.IO) { repository.saveBookmarks(bookmarks.toList()) }
    }

    fun isBookmarked(url: String?): Boolean = url != null && bookmarks.any { it.url == url }

    /** Flips the tab's own flag; applying the new user-agent + reloading the live WebView is the caller's job (BrowserScreen), since that needs a WebView reference the ViewModel deliberately doesn't hold onto (leak risk across config changes). */
    fun toggleDesktopSite() {
        activeTab?.let { it.isDesktopSite = !it.isDesktopSite }
    }

    fun removeBookmark(bookmark: Bookmark) {
        bookmarks.remove(bookmark)
        viewModelScope.launch(Dispatchers.IO) { repository.saveBookmarks(bookmarks.toList()) }
    }

    fun setAdBlockEnabled(enabled: Boolean) {
        adBlock.isEnabled = enabled
        settings = settings.copy(isAdBlockEnabled = enabled)
        persistSettings()
    }

    fun setSearchEngine(engine: SearchEngine) {
        settings = settings.copy(searchEngineName = engine.name)
        persistSettings()
    }

    private fun persistSettings() {
        viewModelScope.launch(Dispatchers.IO) { repository.saveSettings(settings) }
    }

    /**
     * Plain WebView has one process-wide CookieManager/WebStorage — there's
     * no real per-tab isolation available the way separate WebView2 profiles
     * give the desktop app genuine private-browsing separation. This clears
     * everything on a private tab's close as the closest honest equivalent:
     * nothing from it lingers, but a private and a normal tab open at the
     * same time still technically share a cookie jar while both are open.
     */
    private fun clearPrivateData() {
        CookieManager.getInstance().removeAllCookies(null)
        WebStorage.getInstance().deleteAllData()
    }
}
