package com.navigatueur.mobile.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.Spring
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBars
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.automirrored.rounded.ArrowForward
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.Info
import androidx.compose.material.icons.rounded.KeyboardArrowDown
import androidx.compose.material.icons.rounded.KeyboardArrowUp
import androidx.compose.material.icons.rounded.Lock
import androidx.compose.material.icons.rounded.MoreVert
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.Restore
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.SentimentDissatisfied
import androidx.compose.material.icons.rounded.Share
import androidx.compose.material.icons.rounded.Shield
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material.icons.rounded.StarBorder
import androidx.compose.material.icons.rounded.VisibilityOff
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CheckboxDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import com.navigatueur.mobile.browser.BrowserTab
import com.navigatueur.mobile.browser.NvGeckoView
import com.navigatueur.mobile.browser.PermissionRequest
import com.navigatueur.mobile.browser.TabsController
import com.navigatueur.mobile.core.UrlHelper
import com.navigatueur.mobile.data.ToolbarButtonIds
import com.navigatueur.mobile.ui.theme.Nv
import kotlin.math.abs
import kotlin.math.roundToInt
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withTimeoutOrNull
import org.mozilla.geckoview.GeckoSession

/**
 * The page, and Navigatueur's toolbar over it. Like the Windows app's
 * immersive mode, the toolbar steps aside while reading (it slides away
 * when scrolling down and comes back when scrolling up).
 */
@Composable
fun BrowserScreen(
    controller: TabsController,
    state: AppUiState,
    inPictureInPicture: Boolean,
    actions: ActivityActions,
) {
    val tab = controller.activeTab ?: return
    val settings by com.navigatueur.mobile.NavigatueurApp.instance.stores.settings.flow.collectAsState()
    val bottom = settings.toolbarPosition != "Top"
    val fullScreen = tab.isFullScreen || inPictureInPicture
    controller.toolbar.enabled = settings.hideToolbarOnScroll && !tab.isHome
    val hidden = (controller.toolbar.hidden && !tab.isLoading && !state.findInPage) || fullScreen

    var toolbarHeightPx by remember { mutableIntStateOf(0) }
    val offsetFraction by animateFloatAsState(if (hidden) 1f else 0f, tween(180), label = "toolbar")
    val pull = remember { PullState() }

    // The spinner of a pull-to-refresh stays until the page has reloaded (or a few seconds).
    LaunchedEffect(pull.refreshing) {
        if (pull.refreshing) {
            delay(500)
            withTimeoutOrNull(8_000) { snapshotFlow { controller.activeTab?.isLoading == true }.first { !it } }
            pull.refreshing = false
        }
    }

    Box(Modifier.fillMaxSize().background(Nv.colors.window)) {
        val pageModifier = if (fullScreen) Modifier.fillMaxSize()
        else Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.statusBars).windowInsetsPadding(WindowInsets.navigationBars)

        GeckoViewHost(
            controller = controller,
            session = tab.session.takeIf { !tab.isHome && tab.blockedUrl == null },
            toolbarHeightPx = if (fullScreen) 0 else toolbarHeightPx,
            visibleToolbarPx = if (fullScreen) 0 else (toolbarHeightPx * (1 - offsetFraction)).roundToInt(),
            bottomToolbar = bottom,
            pullEnabled = settings.pullToRefresh && !fullScreen && !tab.isHome,
            pull = pull,
            modifier = pageModifier,
        )

        if (tab.isHome && !inPictureInPicture) {
            HomePage(controller, tab, state, Modifier.fillMaxSize(), toolbarAtBottom = bottom)
        }
        tab.blockedUrl?.let { blocked -> PhishingWarning(blocked, onBack = { controller.goBack(tab) }, onContinue = { controller.openBlockedAnyway(tab) }) }
        if (tab.isCrashed) CrashedPage(onReload = { controller.reload(tab) })

        if (!fullScreen && (pull.distance > 0f || pull.refreshing)) {
            val topInset = if (bottom) 0 else (toolbarHeightPx * (1 - offsetFraction)).roundToInt()
            PullIndicator(pull, Modifier.align(Alignment.TopCenter).statusBarsPadding().offset { IntOffset(0, topInset) })
        }

        if (!fullScreen) {
            val shift = (toolbarHeightPx * offsetFraction).roundToInt()
            Column(
                Modifier
                    .align(if (bottom) Alignment.BottomCenter else Alignment.TopCenter)
                    .offset { IntOffset(0, if (bottom) shift else -shift) }
                    .onSizeChanged { toolbarHeightPx = it.height }
                    .then(if (bottom) Modifier.navigationBarsPadding().imePadding() else Modifier.statusBarsPadding())
            ) {
                if (bottom) PermissionBars(controller, tab)
                if (state.findInPage && tab.session != null) {
                    FindBar(tab.session!!, onClose = { state.findInPage = false })
                } else {
                    Toolbar(controller, tab, state, bottom, actions, gestures = settings.toolbarGestures, buttons = settings.appearance.toolbarButtons)
                }
                if (!bottom) PermissionBars(controller, tab)
            }
        }
    }
}

/** A pull-to-refresh in progress: how far, and whether the page is reloading. */
class PullState {
    var distance by mutableFloatStateOf(0f)
    var refreshing by mutableStateOf(false)
}

@Composable
private fun PullIndicator(pull: PullState, modifier: Modifier) {
    val colors = Nv.colors
    val density = LocalDensity.current
    val threshold = with(density) { PULL_THRESHOLD.toPx() }
    val shown by animateFloatAsState(if (pull.refreshing) threshold else pull.distance.coerceAtMost(threshold * 1.4f), label = "pull")
    Box(
        modifier
            .offset { IntOffset(0, (shown * 0.9f).roundToInt()) }
            .size(42.dp)
            .shadow(6.dp, CircleShape)
            .clip(CircleShape)
            .background(colors.sheet),
        contentAlignment = Alignment.Center,
    ) {
        if (pull.refreshing) {
            CircularProgressIndicator(Modifier.size(22.dp), color = colors.accent, strokeWidth = 2.5.dp)
        } else {
            val progress = (pull.distance / threshold).coerceIn(0f, 1f)
            CircularProgressIndicator(
                progress = { progress },
                modifier = Modifier.size(22.dp),
                color = if (progress >= 1f) colors.accent else colors.textSecondary,
                strokeWidth = 2.5.dp,
                trackColor = colors.surfaceActive,
            )
        }
    }
}

private val PULL_THRESHOLD = 72.dp

@Composable
private fun GeckoViewHost(
    controller: TabsController,
    session: GeckoSession?,
    toolbarHeightPx: Int,
    visibleToolbarPx: Int,
    bottomToolbar: Boolean,
    pullEnabled: Boolean,
    pull: PullState,
    modifier: Modifier,
) {
    val sessionsVersion = controller.sessionsChanged
    val background = Nv.colors.window.copy(alpha = 1f).toArgb()
    val threshold = with(LocalDensity.current) { PULL_THRESHOLD.toPx() }
    AndroidView(
        modifier = modifier,
        factory = { context ->
            NvGeckoView(context).apply {
                coverUntilFirstPaint(background)
                pullThreshold = threshold
                isScrolledToTop = { (controller.activeTab?.scrollY ?: 1) <= 1 }
                onPull = { distance -> pull.distance = distance }
                onPullEnd = { reload ->
                    pull.distance = 0f
                    if (reload) controller.activeTab?.let { active ->
                        pull.refreshing = true
                        controller.reload(active)
                    }
                }
                controller.geckoView = this
            }
        },
        update = { view ->
            sessionsVersion.hashCode()
            val target = session?.takeIf { it.isOpen }
            if (target == null) {
                if (view.session != null && session == null) view.releaseSession()
            } else if (view.session !== target) {
                view.releaseSession()
                view.setSession(target)
            }
            view.pullEnabled = pullEnabled && target != null
            view.setDynamicToolbarMaxHeight(toolbarHeightPx)
            view.setVerticalClipping(if (bottomToolbar) -visibleToolbarPx else visibleToolbarPx)
        },
        onRelease = { view ->
            if (controller.geckoView === view) controller.geckoView = null
            view.releaseSession()
        },
    )
}

@Composable
private fun Toolbar(
    controller: TabsController,
    tab: BrowserTab,
    state: AppUiState,
    bottom: Boolean,
    actions: ActivityActions,
    gestures: Boolean,
    buttons: List<String>,
) {
    val colors = Nv.colors
    val look = Nv.look
    val haptics = LocalHapticFeedback.current
    val density = LocalDensity.current
    val switchDistance = with(density) { 72.dp.toPx() }
    val openDistance = with(density) { 36.dp.toPx() }
    var dragX by remember { mutableFloatStateOf(0f) }
    var dragY by remember { mutableFloatStateOf(0f) }
    // The address follows the finger a little while swiping to the next tab.
    val pillShift by animateFloatAsState(dragX * 0.35f, spring(stiffness = Spring.StiffnessMediumLow), label = "pill")
    val background = if (look.glassToolbar) colors.sheet.copy(alpha = 0.76f) else colors.sheet
    val pillHeight = look.toolbarHeight - 16.dp

    Column(Modifier.fillMaxWidth().background(background)) {
        if (bottom) Box(Modifier.fillMaxWidth().height(1.dp).background(colors.panelBorder))
        Box(Modifier.fillMaxWidth().height(2.dp)) {
            if (tab.isLoading) {
                Box(
                    Modifier
                        .fillMaxWidth((tab.progress.coerceIn(5, 100)) / 100f)
                        .height(2.dp)
                        .background(colors.accent)
                )
            }
        }
        Row(
            Modifier
                .fillMaxWidth()
                .height(look.toolbarHeight)
                .padding(start = if (buttons.isEmpty()) 10.dp else 4.dp, end = 4.dp)
                .then(
                    if (!gestures) Modifier
                    // Sideways: previous / next tab. Up (down for a top toolbar): the tab overview.
                    else Modifier.pointerInput(tab.id, bottom) {
                        detectDragGestures(
                            onDragStart = { dragX = 0f; dragY = 0f },
                            onDragCancel = { dragX = 0f; dragY = 0f },
                            onDragEnd = {
                                if (abs(dragX) > abs(dragY)) {
                                    if (dragX > switchDistance) controller.selectAdjacent(-1).also { haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove) }
                                    else if (dragX < -switchDistance) controller.selectAdjacent(1).also { haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove) }
                                } else if ((bottom && dragY < -openDistance) || (!bottom && dragY > openDistance)) {
                                    haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                                    state.showTabs()
                                }
                                dragX = 0f
                                dragY = 0f
                            },
                        ) { change, amount ->
                            change.consume()
                            dragX += amount.x
                            dragY += amount.y
                        }
                    }
                ),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            buttons.forEach { id -> ToolbarButton(id, controller, tab, state, actions) }
            AddressPill(
                tab,
                Modifier.weight(1f).height(pillHeight).offset { IntOffset(pillShift.roundToInt(), 0) },
                onClick = { state.openAddress(tab) },
                onReload = { if (tab.isLoading) controller.stop(tab) else controller.reload(tab) },
            )
            Spacer(Modifier.width(4.dp))
            TabsButton(controller, tab, state)
            IconButton(onClick = { state.menuOpen = true }) {
                Icon(Icons.Rounded.MoreVert, contentDescription = "Menu du navigateur", tint = colors.text)
            }
        }
        if (!bottom) Box(Modifier.fillMaxWidth().height(1.dp).background(colors.panelBorder))
    }
}

/** One of the buttons chosen in Personnaliser → Barre d'outils. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun ToolbarButton(id: String, controller: TabsController, tab: BrowserTab, state: AppUiState, actions: ActivityActions) {
    val app = com.navigatueur.mobile.NavigatueurApp.instance
    val haptics = LocalHapticFeedback.current
    val isWebPage = UrlHelper.isWebUrl(tab.url) && !tab.isHome
    val bookmarks by app.stores.bookmarks.flow.collectAsState()
    val bookmarked = remember(bookmarks, tab.url) { app.stores.isBookmarked(tab.url) }
    val showHistory = {
        if (tab.history.size > 1) {
            haptics.performHapticFeedback(HapticFeedbackType.LongPress)
            state.tabHistoryOpen = true
        }
    }
    when (id) {
        ToolbarButtonIds.BACK -> ToolbarIcon(Icons.AutoMirrored.Rounded.ArrowBack, "Page précédente", tab.canGoBack || tab.blockedUrl != null, onLongClick = showHistory) { controller.goBack(tab) }
        ToolbarButtonIds.FORWARD -> ToolbarIcon(Icons.AutoMirrored.Rounded.ArrowForward, "Page suivante", tab.canGoForward, onLongClick = showHistory) { controller.goForward(tab) }
        ToolbarButtonIds.HOME -> ToolbarIcon(Icons.Rounded.Home, "Page d'accueil", true) { controller.goHome(tab) }
        ToolbarButtonIds.NEW_TAB -> ToolbarIcon(Icons.Rounded.Add, "Nouvel onglet", true) { state.openAddress(null, newTab = true, private = tab.isPrivate) }
        ToolbarButtonIds.SHARE -> ToolbarIcon(Icons.Rounded.Share, "Partager", isWebPage) { actions.share(tab.displayTitle, null, tab.url) }
        ToolbarButtonIds.BOOKMARK -> ToolbarIcon(if (bookmarked) Icons.Rounded.Star else Icons.Rounded.StarBorder, if (bookmarked) "Retirer des favoris" else "Ajouter aux favoris", isWebPage, highlighted = bookmarked) {
            val added = app.stores.toggleBookmark(tab.url, tab.displayTitle)
            controller.emit(com.navigatueur.mobile.browser.UiEvent.Message(if (added) "Ajouté aux favoris" else "Retiré des favoris"))
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun ToolbarIcon(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    description: String,
    enabled: Boolean,
    highlighted: Boolean = false,
    onLongClick: (() -> Unit)? = null,
    onClick: () -> Unit,
) {
    val colors = Nv.colors
    Box(
        Modifier
            .size(42.dp)
            .clip(CircleShape)
            .combinedClickable(enabled = enabled, onClick = onClick, onLongClick = onLongClick, role = Role.Button, onClickLabel = description),
        contentAlignment = Alignment.Center,
    ) {
        Icon(
            icon, description,
            tint = (if (highlighted) colors.accent else colors.text).copy(alpha = if (enabled) 1f else 0.32f),
            modifier = Modifier.size(22.dp),
        )
    }
}

@Composable
private fun AddressPill(tab: BrowserTab, modifier: Modifier, onClick: () -> Unit, onReload: () -> Unit) {
    val colors = Nv.colors
    Row(
        modifier
            .clip(Nv.shape(12.dp))
            .background(colors.surface.copy(alpha = if (Nv.look.glassToolbar) 0.7f else 1f))
            .clickable(onClick = onClick, onClickLabel = "Saisir une adresse")
            .padding(start = 12.dp, end = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        val text = tab.addressText
        when {
            tab.isHome || text.isEmpty() -> Icon(Icons.Rounded.Search, null, tint = colors.textTertiary, modifier = Modifier.size(18.dp))
            UrlHelper.isWebUrl(tab.url) && tab.isSecure -> Icon(Icons.Rounded.Lock, "Connexion sécurisée", tint = colors.textSecondary, modifier = Modifier.size(16.dp))
            UrlHelper.isWebUrl(tab.url) && tab.securityKnown -> Icon(Icons.Rounded.Info, "Connexion non sécurisée", tint = colors.danger, modifier = Modifier.size(17.dp))
            else -> FaviconImage(tab.favicon, 16.dp)
        }
        Spacer(Modifier.width(9.dp))
        Text(
            text.ifEmpty { if (tab.isPrivate) "Recherche privée ou adresse" else "Rechercher ou saisir une adresse" },
            color = if (text.isEmpty()) colors.textTertiary else colors.text,
            fontSize = 15.sp,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        if (!tab.isHome) {
            IconButton(onClick = onReload, modifier = Modifier.size(36.dp)) {
                Icon(if (tab.isLoading) Icons.Rounded.Close else Icons.Rounded.Refresh, if (tab.isLoading) "Arrêter" else "Recharger", tint = colors.textSecondary, modifier = Modifier.size(19.dp))
            }
        }
    }
}

/** The tab counter: a tap shows every tab, a long press offers new tab, private tab, close. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun TabsButton(controller: TabsController, tab: BrowserTab, state: AppUiState) {
    val colors = Nv.colors
    val haptics = LocalHapticFeedback.current
    val count = controller.tabs.count { it.isPrivate == tab.isPrivate }
    var menu by remember { mutableStateOf(false) }
    Box {
        Box(
            Modifier
                .size(44.dp)
                .clip(Nv.shape(10.dp))
                .combinedClickable(
                    onClick = { state.showTabs() },
                    onLongClick = {
                        haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                        menu = true
                    },
                    onClickLabel = "Vue des onglets",
                )
                .semantics { contentDescription = "Vue des onglets" },
            contentAlignment = Alignment.Center,
        ) {
            Box(
                Modifier
                    .size(23.dp)
                    .border(1.8.dp, if (colors.isPrivate) colors.accent else colors.text, Nv.shape(6.dp)),
                contentAlignment = Alignment.Center,
            ) {
                Text(if (count > 99) ":D" else count.toString(), color = colors.text, fontSize = 11.sp, fontWeight = FontWeight.Bold)
            }
        }
        DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
            DropdownMenuItem(
                text = { Text("Nouvel onglet") },
                leadingIcon = { Icon(Icons.Rounded.Add, null) },
                onClick = { menu = false; state.openAddress(null, newTab = true, private = false) },
            )
            DropdownMenuItem(
                text = { Text("Nouvel onglet privé") },
                leadingIcon = { Icon(Icons.Rounded.VisibilityOff, null) },
                onClick = { menu = false; state.openAddress(null, newTab = true, private = true) },
            )
            if (controller.canReopenClosedTab) {
                DropdownMenuItem(
                    text = { Text("Rouvrir l'onglet fermé") },
                    leadingIcon = { Icon(Icons.Rounded.Restore, null) },
                    onClick = { menu = false; controller.reopenClosedTab() },
                )
            }
            DropdownMenuItem(
                text = { Text("Fermer cet onglet") },
                leadingIcon = { Icon(Icons.Rounded.Close, null) },
                onClick = { menu = false; controller.closeTabByUser(tab) },
            )
        }
    }
}

@Composable
private fun FindBar(session: GeckoSession, onClose: () -> Unit) {
    val colors = Nv.colors
    var query by remember { mutableStateOf("") }
    var status by remember { mutableStateOf("") }
    val focus = remember { FocusRequester() }

    fun find(forward: Boolean, newQuery: Boolean = false) {
        if (query.isEmpty()) {
            session.finder.clear()
            status = ""
            return
        }
        val flags = if (forward || newQuery) 0 else GeckoSession.FINDER_FIND_BACKWARDS
        session.finder.find(if (newQuery) query else null, flags).accept({ result ->
            status = if (result == null || !result.found) "0/0" else "${result.current}/${result.total}"
        }, { })
    }

    LaunchedEffect(Unit) { focus.requestFocus() }

    Row(
        Modifier.fillMaxWidth().background(colors.sheet).height(58.dp).padding(horizontal = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Row(
            Modifier.weight(1f).height(40.dp).clip(Nv.shape(12.dp)).background(colors.surface.copy(alpha = 1f)).padding(horizontal = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Icon(Icons.Rounded.Search, null, tint = colors.textTertiary, modifier = Modifier.size(18.dp))
            Spacer(Modifier.width(8.dp))
            BasicTextField(
                value = query,
                onValueChange = { query = it; find(true, newQuery = true) },
                singleLine = true,
                textStyle = TextStyle(color = colors.text, fontSize = 15.sp),
                cursorBrush = SolidColor(colors.accent),
                keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                keyboardActions = KeyboardActions(onSearch = { find(true) }),
                modifier = Modifier.weight(1f).focusRequester(focus),
                decorationBox = { inner ->
                    if (query.isEmpty()) Text("Rechercher dans la page", color = colors.textTertiary, fontSize = 15.sp)
                    inner()
                },
            )
            Text(status, color = colors.textSecondary, fontSize = 13.sp)
        }
        IconButton(onClick = { find(false) }) { Icon(Icons.Rounded.KeyboardArrowUp, "Précédent", tint = colors.text) }
        IconButton(onClick = { find(true) }) { Icon(Icons.Rounded.KeyboardArrowDown, "Suivant", tint = colors.text) }
        IconButton(onClick = { session.finder.clear(); onClose() }) { Icon(Icons.Rounded.Close, "Fermer", tint = colors.text) }
    }
}

@Composable
private fun PermissionBars(controller: TabsController, tab: BrowserTab) {
    val request = controller.permissionRequests.firstOrNull { it.tabId == tab.id } ?: return
    PermissionCard(request, onDone = { controller.permissionRequests.remove(request) })
}

@Composable
private fun PermissionCard(request: PermissionRequest, onDone: () -> Unit) {
    val colors = Nv.colors
    var remember by remember(request) { mutableStateOf(true) }
    Column(
        Modifier
            .fillMaxWidth()
            .padding(10.dp)
            .clip(Nv.shape(14.dp))
            .background(colors.sheet)
            .border(1.dp, colors.panelBorder, Nv.shape(14.dp))
            .padding(16.dp),
    ) {
        Text("${request.host} demande à ${request.description}", color = colors.text, fontSize = 15.sp, fontWeight = FontWeight.Medium)
        Spacer(Modifier.height(10.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            Checkbox(checked = remember, onCheckedChange = { remember = it }, colors = CheckboxDefaults.colors(checkedColor = colors.accent))
            Text("Se souvenir", color = colors.textSecondary, fontSize = 14.sp, modifier = Modifier.weight(1f))
            TextButton(onClick = { request.onDecision(false, remember); onDone() }) { Text("Bloquer", color = colors.textSecondary) }
            Button(
                onClick = { request.onDecision(true, remember); onDone() },
                colors = ButtonDefaults.buttonColors(containerColor = colors.accent, contentColor = colors.accentForeground),
            ) { Text("Autoriser") }
        }
    }
}

@Composable
private fun BoxScope.PhishingWarning(url: String, onBack: () -> Unit, onContinue: () -> Unit) {
    val colors = Nv.colors
    Column(
        Modifier
            .fillMaxSize()
            .background(colors.window)
            .statusBarsPadding()
            .navigationBarsPadding()
            .padding(28.dp),
        verticalArrangement = Arrangement.Center,
    ) {
        Icon(Icons.Rounded.Shield, null, tint = colors.danger, modifier = Modifier.size(52.dp))
        Spacer(Modifier.height(18.dp))
        Text("Site frauduleux signalé", color = colors.text, fontSize = 24.sp, fontWeight = FontWeight.SemiBold)
        Spacer(Modifier.height(10.dp))
        Text(
            "${UrlHelper.displayHost(url) ?: url} figure sur la liste des sites d'hameçonnage : il peut se faire passer pour un site connu afin de voler vos mots de passe ou vos coordonnées bancaires.",
            color = colors.textSecondary, fontSize = 15.sp, lineHeight = 21.sp,
        )
        Spacer(Modifier.height(26.dp))
        Button(onClick = onBack, colors = ButtonDefaults.buttonColors(containerColor = colors.accent, contentColor = colors.accentForeground)) {
            Text("Revenir en lieu sûr")
        }
        Spacer(Modifier.height(6.dp))
        TextButton(onClick = onContinue) { Text("Continuer quand même", color = colors.textTertiary) }
    }
}

@Composable
private fun BoxScope.CrashedPage(onReload: () -> Unit) {
    val colors = Nv.colors
    Column(
        Modifier.fillMaxSize().background(colors.window).padding(28.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Icon(Icons.Rounded.SentimentDissatisfied, null, tint = colors.textTertiary, modifier = Modifier.size(48.dp))
        Spacer(Modifier.height(14.dp))
        Text("Cette page a planté", color = colors.text, fontSize = 20.sp, fontWeight = FontWeight.SemiBold)
        Spacer(Modifier.height(18.dp))
        Button(onClick = onReload, colors = ButtonDefaults.buttonColors(containerColor = colors.accent, contentColor = colors.accentForeground)) {
            Text("Recharger")
        }
    }
}
