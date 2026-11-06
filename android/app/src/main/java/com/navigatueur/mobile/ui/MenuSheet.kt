package com.navigatueur.mobile.ui

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.automirrored.rounded.ArrowForward
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.AddToHomeScreen
import androidx.compose.material.icons.rounded.Bookmarks
import androidx.compose.material.icons.rounded.ContentCopy
import androidx.compose.material.icons.rounded.DarkMode
import androidx.compose.material.icons.rounded.DesktopWindows
import androidx.compose.material.icons.rounded.Download
import androidx.compose.material.icons.rounded.Extension
import androidx.compose.material.icons.rounded.FindInPage
import androidx.compose.material.icons.rounded.History
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.OpenInNew
import androidx.compose.material.icons.rounded.Palette
import androidx.compose.material.icons.rounded.PictureInPictureAlt
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.Settings
import androidx.compose.material.icons.rounded.Share
import androidx.compose.material.icons.rounded.Star
import androidx.compose.material.icons.rounded.StarBorder
import androidx.compose.material.icons.rounded.SystemUpdate
import androidx.compose.material.icons.rounded.Translate
import androidx.compose.material.icons.rounded.VisibilityOff
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.NavigatueurApp
import com.navigatueur.mobile.browser.BrowserTab
import com.navigatueur.mobile.browser.UiEvent
import com.navigatueur.mobile.core.UrlHelper
import com.navigatueur.mobile.ui.theme.Nv

/** One tile of the menu's grid. [active]: a switch that is on (shown in the accent color). */
private class MenuTile(
    val icon: ImageVector,
    val label: String,
    val active: Boolean = false,
    val enabled: Boolean = true,
    val onClick: () -> Unit,
)

/**
 * The "⋯" menu: the page and its navigation at the top, then everything
 * else as a grid of tiles — all of it visible at once, switches lit up when
 * they are on.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MenuSheet(app: NavigatueurApp, tab: BrowserTab, state: AppUiState, actions: ActivityActions) {
    val controller = app.tabs
    val colors = Nv.colors
    val context = LocalContext.current
    val haptics = LocalHapticFeedback.current
    val settings by app.stores.settings.flow.collectAsState()
    val bookmarks by app.stores.bookmarks.flow.collectAsState()
    val isWebPage = UrlHelper.isWebUrl(tab.url) && !tab.isHome
    val bookmarked = remember(bookmarks, tab.url) { app.stores.isBookmarked(tab.url) }

    fun close() {
        state.menuOpen = false
    }

    ModalBottomSheet(
        onDismissRequest = ::close,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        containerColor = colors.sheet,
        shape = RoundedCornerShape(topStart = 24.dp * Nv.look.cornerScale, topEnd = 24.dp * Nv.look.cornerScale),
    ) {
        val maxHeight = (LocalConfiguration.current.screenHeightDp - 64).dp
        Column(Modifier.fillMaxWidth().heightIn(max = maxHeight).verticalScroll(rememberScrollState()).navigationBarsPadding().padding(bottom = 10.dp)) {
            // The page: title, site, star and share.
            if (isWebPage) {
                Row(Modifier.fillMaxWidth().padding(start = 20.dp, end = 8.dp, bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                    FaviconImage(tab.favicon, 22.dp)
                    Spacer(Modifier.width(12.dp))
                    Column(Modifier.weight(1f)) {
                        Text(tab.displayTitle, color = colors.text, fontWeight = FontWeight.SemiBold, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(UrlHelper.displayHost(tab.url) ?: tab.url, color = colors.textTertiary, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                    ToolbarIcon(if (bookmarked) Icons.Rounded.Star else Icons.Rounded.StarBorder, if (bookmarked) "Retirer des favoris" else "Ajouter aux favoris", true, highlighted = bookmarked) {
                        val added = app.stores.toggleBookmark(tab.url, tab.displayTitle)
                        controller.emit(UiEvent.Message(if (added) "Ajouté aux favoris" else "Retiré des favoris"))
                    }
                    ToolbarIcon(Icons.Rounded.Share, "Partager", true) { actions.share(tab.displayTitle, null, tab.url); close() }
                }
            }

            // Navigation: a long press on Retour or Avancer lists the tab's pages.
            Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                val showHistory = {
                    if (tab.history.size > 1) {
                        haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                        close()
                        state.tabHistoryOpen = true
                    }
                }
                NavButton(Icons.AutoMirrored.Rounded.ArrowBack, "Retour", tab.canGoBack, Modifier.weight(1f), onLongClick = showHistory) { controller.goBack(tab); close() }
                NavButton(Icons.AutoMirrored.Rounded.ArrowForward, "Avancer", tab.canGoForward, Modifier.weight(1f), onLongClick = showHistory) { controller.goForward(tab); close() }
                NavButton(Icons.Rounded.Refresh, "Recharger", isWebPage, Modifier.weight(1f)) { controller.reload(tab); close() }
                NavButton(Icons.Rounded.Home, "Accueil", true, Modifier.weight(1f)) { controller.goHome(tab); close() }
            }

            // Extensions' own buttons (uBlock Origin's panel, Dark Reader's…).
            val extensionActions = app.extensions.actions.values.filter { action -> app.extensions.installed.any { it.id == action.extensionId && it.enabled } }
            if (extensionActions.isNotEmpty() && isWebPage) {
                Row(
                    Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 6.dp),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    extensionActions.forEach { action ->
                        Row(
                            Modifier
                                .clip(Nv.shape(12.dp))
                                .background(colors.surface)
                                .clickable { action.action.click(); close() }
                                .padding(horizontal = 12.dp, vertical = 8.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            val icon = action.icon
                            if (icon != null) Image(icon.asImageBitmap(), null, Modifier.size(20.dp)) else Icon(Icons.Rounded.Extension, null, tint = colors.textSecondary, modifier = Modifier.size(20.dp))
                            Spacer(Modifier.width(8.dp))
                            Text(action.title, color = colors.text, fontSize = 13.sp, maxLines = 1)
                            if (!action.badge.isNullOrBlank()) {
                                Spacer(Modifier.width(6.dp))
                                Text(action.badge, color = colors.accentForeground, fontSize = 11.sp, fontWeight = FontWeight.Bold,
                                    modifier = Modifier.clip(Nv.shape(6.dp)).background(colors.accent).padding(horizontal = 5.dp, vertical = 1.dp))
                            }
                        }
                    }
                }
            }

            val tiles = buildList {
                add(MenuTile(Icons.Rounded.Add, "Nouvel onglet") { close(); state.openAddress(null, newTab = true, private = false) })
                add(MenuTile(Icons.Rounded.VisibilityOff, "Onglet privé") { close(); state.openAddress(null, newTab = true, private = true) })
                add(MenuTile(Icons.Rounded.Bookmarks, "Favoris") { close(); state.screen = Screen.Bookmarks })
                add(MenuTile(Icons.Rounded.History, "Historique") { close(); state.screen = Screen.History })
                add(MenuTile(Icons.Rounded.Download, "Téléchargements") { close(); state.screen = Screen.Downloads })
                add(MenuTile(Icons.Rounded.Extension, "Extensions") { close(); state.screen = Screen.Extensions })
                add(MenuTile(Icons.Rounded.FindInPage, "Chercher dans la page", enabled = isWebPage) { close(); state.findInPage = true })
                add(MenuTile(Icons.Rounded.DesktopWindows, "Version ordinateur", active = tab.isDesktopMode, enabled = isWebPage) { controller.setDesktopMode(tab, !tab.isDesktopMode); close() })
                add(MenuTile(Icons.Rounded.DarkMode, "Sites en sombre", active = settings.forceDarkSites) { app.extensions.setForceDark(!settings.forceDarkSites) })
                add(MenuTile(Icons.Rounded.Translate, "Traduire", enabled = isWebPage) {
                    close()
                    controller.loadUrl(tab, "https://translate.yandex.com/translate?url=${Uri.encode(tab.url)}&lang=fr")
                })
                add(MenuTile(Icons.Rounded.ContentCopy, "Copier le lien", enabled = isWebPage) {
                    (context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("URL", tab.url))
                    controller.emit(UiEvent.Message("Lien copié"))
                    close()
                })
                if (tab.hasMedia) add(MenuTile(Icons.Rounded.PictureInPictureAlt, "Image dans l'image") { close(); actions.enterPictureInPicture() })
                add(MenuTile(Icons.Rounded.AddToHomeScreen, "Écran d'accueil", enabled = isWebPage) { close(); actions.addToHomeScreen(tab) })
                add(MenuTile(Icons.Rounded.OpenInNew, "Ouvrir avec…", enabled = isWebPage) {
                    close()
                    actions.startActivitySafely(Intent.createChooser(Intent(Intent.ACTION_VIEW, Uri.parse(tab.url)), "Ouvrir avec"))
                })
                add(MenuTile(Icons.Rounded.Palette, "Personnaliser") { state.openCustomize(Screen.Browser) })
                add(MenuTile(Icons.Rounded.Settings, "Paramètres") { close(); state.screen = Screen.Settings })
            }
            Column(Modifier.fillMaxWidth().padding(horizontal = 10.dp, vertical = 6.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                tiles.chunked(4).forEach { row ->
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                        row.forEach { tile -> Tile(tile, Modifier.weight(1f)) }
                        repeat(4 - row.size) { Spacer(Modifier.weight(1f)) }
                    }
                }
            }

            app.updater.available?.let { release ->
                Row(
                    Modifier
                        .padding(horizontal = 16.dp, vertical = 8.dp)
                        .fillMaxWidth()
                        .clip(Nv.shape(14.dp))
                        .background(colors.accentSoft)
                        .clickable { close(); state.screen = Screen.Settings }
                        .padding(horizontal = 16.dp, vertical = 14.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Icon(Icons.Rounded.SystemUpdate, null, tint = colors.accent, modifier = Modifier.size(21.dp))
                    Spacer(Modifier.width(14.dp))
                    Text("Installer la mise à jour ${release.version}", color = colors.accent, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
                }
            }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun NavButton(icon: ImageVector, label: String, enabled: Boolean, modifier: Modifier, onLongClick: (() -> Unit)? = null, onClick: () -> Unit) {
    val colors = Nv.colors
    val alpha = if (enabled) 1f else 0.35f
    Box(
        modifier
            .height(48.dp)
            .clip(Nv.shape(14.dp))
            .background(colors.surface)
            .combinedClickable(enabled = enabled, onClick = onClick, onLongClick = onLongClick, role = Role.Button, onClickLabel = label),
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, label, tint = colors.text.copy(alpha = alpha), modifier = Modifier.size(22.dp))
    }
}

@Composable
private fun Tile(tile: MenuTile, modifier: Modifier) {
    val colors = Nv.colors
    val alpha = if (tile.enabled) 1f else 0.35f
    Column(
        modifier
            .clip(Nv.shape(14.dp))
            .clickable(enabled = tile.enabled, role = Role.Button, onClick = tile.onClick)
            .padding(vertical = 8.dp, horizontal = 2.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Box(
            Modifier
                .size(48.dp)
                .clip(Nv.shape(14.dp))
                .background(if (tile.active) colors.accent else colors.surface),
            contentAlignment = Alignment.Center,
        ) {
            Icon(tile.icon, null, tint = (if (tile.active) colors.accentForeground else colors.text).copy(alpha = alpha), modifier = Modifier.size(22.dp))
        }
        Spacer(Modifier.height(6.dp))
        Text(
            tile.label,
            color = colors.textSecondary.copy(alpha = alpha),
            fontSize = 11.sp,
            lineHeight = 14.sp,
            maxLines = 2,
            textAlign = TextAlign.Center,
            overflow = TextOverflow.Ellipsis,
        )
    }
}
