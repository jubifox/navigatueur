package com.navigatueur.mobile.ui

import android.content.Intent
import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.navigatueur.mobile.NavigatueurApp
import com.navigatueur.mobile.browser.BrowserTab
import com.navigatueur.mobile.browser.UiEvent
import com.navigatueur.mobile.ui.theme.NavigatueurTheme

enum class Screen { Browser, Tabs, Settings, History, Bookmarks, Downloads, Extensions }

/** What the screen shows besides the page: which screen, the address being typed, the menu… */
class AppUiState {
    var screen by mutableStateOf(Screen.Browser)
    var addressTab by mutableStateOf<BrowserTab?>(null)
        private set
    var addressText by mutableStateOf("")
    var menuOpen by mutableStateOf(false)
    var findInPage by mutableStateOf(false)
    var tabsShowPrivate by mutableStateOf(false)

    /** The tab the next typed address goes to; null: a new tab. */
    var addressInNewTab by mutableStateOf(false)
    var addressPrivate by mutableStateOf(false)

    fun openAddress(tab: BrowserTab?, newTab: Boolean = false, private: Boolean = tab?.isPrivate ?: false) {
        addressTab = tab
        addressInNewTab = newTab
        addressPrivate = private
        addressText = if (newTab || tab == null || tab.isHome) "" else tab.url
        menuOpen = false
    }

    fun closeAddress() {
        addressTab = null
        addressInNewTab = false
    }

    val isAddressOpen: Boolean get() = addressTab != null || addressInNewTab

    fun showTabs(private: Boolean? = null) {
        tabsShowPrivate = private ?: (NavigatueurApp.instance.tabs.activeTab?.isPrivate == true)
        NavigatueurApp.instance.tabs.captureThumbnail()
        screen = Screen.Tabs
        menuOpen = false
    }
}

/** What only the activity can do (other apps, system pickers, picture-in-picture). */
interface ActivityActions {
    fun startActivitySafely(intent: Intent): Boolean
    fun openExternal(uri: String, fallbackUrl: String?)
    fun share(title: String?, text: String?, url: String?)
    fun handle(event: UiEvent.PickFiles)
    fun requestPermissions(event: UiEvent.AndroidPermissions)
    fun pickBackgroundImage()
    fun addToHomeScreen(tab: BrowserTab)
    fun enterPictureInPicture()
    fun moveToBackground()
}

@Composable
fun AppRoot(app: NavigatueurApp, state: AppUiState, inPictureInPicture: Boolean, actions: ActivityActions) {
    val controller = app.tabs
    val settings by app.stores.settings.flow.collectAsState()
    val tab = controller.activeTab
    val snackbar = remember { SnackbarHostState() }

    LaunchedEffect(Unit) {
        controller.events.collect { event ->
            when (event) {
                is UiEvent.Message -> snackbar.showSnackbar(event.text)
                is UiEvent.OpenExternal -> actions.openExternal(event.uri, event.fallbackUrl)
                is UiEvent.Share -> actions.share(event.title, event.text, event.url)
                is UiEvent.PickFiles -> actions.handle(event)
                is UiEvent.AndroidPermissions -> actions.requestPermissions(event)
                UiEvent.ShowToolbar -> controller.toolbar.show()
            }
        }
    }
    LaunchedEffect(app.extensions.lastError) {
        app.extensions.lastError?.let {
            snackbar.showSnackbar(it)
            app.extensions.lastError = null
        }
    }
    LaunchedEffect(app.updater.lastCheckMessage) {
        app.updater.lastCheckMessage?.takeIf { state.screen == Screen.Settings }?.let { snackbar.showSnackbar(it) }
    }

    val privateLook = when (state.screen) {
        Screen.Tabs -> state.tabsShowPrivate
        Screen.Browser -> tab?.isPrivate == true
        else -> false
    }

    NavigatueurTheme(settings.themeMode, settings.accentColorHex, private = privateLook) {
        Box(Modifier.fillMaxSize()) {
            BrowserScreen(controller, state, inPictureInPicture)

            if (!inPictureInPicture) {
                when (state.screen) {
                    Screen.Browser -> Unit
                    Screen.Tabs -> TabsScreen(controller, state)
                    Screen.Settings -> SettingsScreen(app, state, actions)
                    Screen.History -> HistoryScreen(app, state)
                    Screen.Bookmarks -> BookmarksScreen(app, state)
                    Screen.Downloads -> DownloadsScreen(app, state, actions)
                    Screen.Extensions -> ExtensionsScreen(app, state)
                }

                AnimatedVisibility(state.isAddressOpen, enter = fadeIn() + slideInVertically { it / 12 }, exit = fadeOut()) {
                    AddressScreen(app, state, actions)
                }

                if (state.menuOpen && tab != null) MenuSheet(app, tab, state, actions)

                PageDialogs(app, actions)

                SnackbarHost(snackbar, Modifier.align(Alignment.BottomCenter).navigationBarsPadding().padding(bottom = 64.dp))
            }
        }
    }

    BackHandler(enabled = !inPictureInPicture) {
        val current = controller.activeTab
        when {
            state.isAddressOpen -> state.closeAddress()
            state.menuOpen -> state.menuOpen = false
            state.screen != Screen.Browser -> state.screen = Screen.Browser
            state.findInPage -> state.findInPage = false
            current == null -> actions.moveToBackground()
            current.isFullScreen -> current.session?.exitFullScreen()
            controller.goBack(current) -> Unit
            current.parentId != null && controller.tabById(current.parentId) != null -> controller.closeTab(current, remember = true)
            !current.isHome && controller.settings.homePageUrl.isBlank() && current.session != null && !current.canGoBack -> controller.showHome(current)
            else -> actions.moveToBackground()
        }
    }
}
