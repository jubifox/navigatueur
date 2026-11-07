package com.navigatueur.mobile.ui

import android.content.Intent
import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.scaleIn
import androidx.compose.animation.scaleOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Snackbar
import androidx.compose.material3.SnackbarDuration
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.SnackbarResult
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.navigatueur.mobile.NavigatueurApp
import com.navigatueur.mobile.browser.BrowserTab
import com.navigatueur.mobile.browser.UiEvent
import com.navigatueur.mobile.ui.theme.NavigatueurTheme
import com.navigatueur.mobile.ui.theme.Nv
import kotlinx.coroutines.launch

enum class Screen { Browser, Tabs, Settings, Customize, History, Bookmarks, Downloads, Extensions }

/** What the screen shows besides the page: which screen, the address being typed, the menu… */
class AppUiState {
    var screen by mutableStateOf(Screen.Browser)
    var addressTab by mutableStateOf<BrowserTab?>(null)
        private set
    var addressText by mutableStateOf("")
    var menuOpen by mutableStateOf(false)
    var findInPage by mutableStateOf(false)
    var tabsShowPrivate by mutableStateOf(false)

    /** The list of the tab's previous and next pages (long press on Retour). */
    var tabHistoryOpen by mutableStateOf(false)

    /** Where Personnaliser goes back to (Settings when opened from there, else the page). */
    var customizeReturn by mutableStateOf(Screen.Browser)

    fun openCustomize(from: Screen) {
        customizeReturn = if (from == Screen.Settings) Screen.Settings else Screen.Browser
        screen = Screen.Customize
        menuOpen = false
    }

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

    /** A speech recognizer is installed (Google, Samsung…). */
    val canVoiceSearch: Boolean

    /** Listens, then hands back what was said. */
    fun voiceSearch(onResult: (String) -> Unit)
}

@Composable
fun AppRoot(app: NavigatueurApp, state: AppUiState, inPictureInPicture: Boolean, actions: ActivityActions) {
    val controller = app.tabs
    val settings by app.stores.settings.flow.collectAsState()
    val tab = controller.activeTab
    val snackbar = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()

    LaunchedEffect(Unit) {
        controller.events.collect { event ->
            when (event) {
                is UiEvent.Message -> scope.launch {
                    snackbar.currentSnackbarData?.dismiss()
                    snackbar.showSnackbar(event.text)
                }
                is UiEvent.Undo -> scope.launch {
                    snackbar.currentSnackbarData?.dismiss()
                    val result = snackbar.showSnackbar(event.text, actionLabel = "Annuler", duration = SnackbarDuration.Long)
                    if (result == SnackbarResult.ActionPerformed) event.undo()
                }
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
    val appearance = settings.appearance
    val seeThrough = appearance.wallpaperEverywhere && appearance.wallpaper.kind != "None"

    NavigatueurTheme(settings.themeMode, settings.accentColorHex, private = privateLook, appearance = appearance, seeThrough = seeThrough) {
        Box(Modifier.fillMaxSize()) {
            BrowserScreen(controller, state, inPictureInPicture, actions)

            if (!inPictureInPicture) {
                AnimatedContent(
                    targetState = state.screen,
                    transitionSpec = {
                        when {
                            // The tab overview zooms out of the page, and back into it.
                            targetState == Screen.Tabs ->
                                (fadeIn(tween(180)) + scaleIn(tween(220), initialScale = 0.94f)) togetherWith fadeOut(tween(120))
                            initialState == Screen.Tabs ->
                                fadeIn(tween(120)) togetherWith (fadeOut(tween(180)) + scaleOut(tween(200), targetScale = 0.94f))
                            // Other screens slide in from the side and leave the same way.
                            targetState == Screen.Browser ->
                                fadeIn(tween(120)) togetherWith (fadeOut(tween(180)) + slideOutHorizontally(tween(220)) { it / 5 })
                            else ->
                                (fadeIn(tween(200)) + slideInHorizontally(tween(240)) { it / 5 }) togetherWith fadeOut(tween(140))
                        }
                    },
                    label = "screen",
                ) { screen ->
                    when (screen) {
                        Screen.Browser -> Unit
                        Screen.Tabs -> TabsScreen(controller, state)
                        Screen.Settings -> SettingsScreen(app, state, actions)
                        Screen.Customize -> CustomizeScreen(app, state, actions)
                        Screen.History -> HistoryScreen(app, state)
                        Screen.Bookmarks -> BookmarksScreen(app, state, actions)
                        Screen.Downloads -> DownloadsScreen(app, state, actions)
                        Screen.Extensions -> ExtensionsScreen(app, state)
                    }
                }

                AnimatedVisibility(state.isAddressOpen, enter = fadeIn() + slideInVertically { it / 12 }, exit = fadeOut()) {
                    AddressScreen(app, state, actions)
                }

                if (state.menuOpen && tab != null) MenuSheet(app, tab, state, actions)
                if (state.tabHistoryOpen && tab != null) TabHistorySheet(controller, tab, onDone = { state.tabHistoryOpen = false })

                PageDialogs(app, actions)

                val bottomToolbar = settings.toolbarPosition != "Top" && state.screen == Screen.Browser
                SnackbarHost(
                    snackbar,
                    Modifier
                        .align(Alignment.BottomCenter)
                        .navigationBarsPadding()
                        .padding(bottom = if (bottomToolbar) Nv.look.toolbarHeight + 12.dp else 16.dp),
                ) { data ->
                    val colors = Nv.colors
                    Snackbar(
                        data,
                        shape = Nv.shape(14.dp),
                        containerColor = colors.text,
                        contentColor = colors.window.copy(alpha = 1f),
                        actionColor = if (colors.isDark) colors.accent.copy(alpha = 1f) else colors.accent,
                        modifier = Modifier.padding(horizontal = 12.dp),
                    )
                }
            }
        }
    }

    BackHandler(enabled = !inPictureInPicture) {
        val current = controller.activeTab
        when {
            state.isAddressOpen -> state.closeAddress()
            state.menuOpen -> state.menuOpen = false
            state.tabHistoryOpen -> state.tabHistoryOpen = false
            state.screen == Screen.Customize -> state.screen = state.customizeReturn
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
