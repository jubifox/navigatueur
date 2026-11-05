package com.navigatueur.mobile.ui

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
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
import androidx.compose.material.icons.rounded.AddToHomeScreen
import androidx.compose.material.icons.rounded.Bookmark
import androidx.compose.material.icons.rounded.BookmarkBorder
import androidx.compose.material.icons.rounded.Bookmarks
import androidx.compose.material.icons.rounded.DarkMode
import androidx.compose.material.icons.rounded.DesktopWindows
import androidx.compose.material.icons.rounded.Download
import androidx.compose.material.icons.rounded.Extension
import androidx.compose.material.icons.rounded.FindInPage
import androidx.compose.material.icons.rounded.History
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.OpenInNew
import androidx.compose.material.icons.rounded.PictureInPictureAlt
import androidx.compose.material.icons.rounded.Refresh
import androidx.compose.material.icons.rounded.Settings
import androidx.compose.material.icons.rounded.Share
import androidx.compose.material.icons.rounded.SystemUpdate
import androidx.compose.material.icons.rounded.Translate
import androidx.compose.material.icons.rounded.VisibilityOff
import androidx.compose.material.icons.rounded.Add
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.NavigatueurApp
import com.navigatueur.mobile.browser.BrowserTab
import com.navigatueur.mobile.core.UrlHelper
import com.navigatueur.mobile.ui.theme.Nv

/** The "⋯" menu: the Windows toolbar's menu, as a sheet. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MenuSheet(app: NavigatueurApp, tab: BrowserTab, state: AppUiState, actions: ActivityActions) {
    val controller = app.tabs
    val colors = Nv.colors
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
        containerColor = colors.panel,
        dragHandle = null,
    ) {
        val maxHeight = (androidx.compose.ui.platform.LocalConfiguration.current.screenHeightDp - 64).dp
        Column(Modifier.fillMaxWidth().heightIn(max = maxHeight).verticalScroll(rememberScrollState()).navigationBarsPadding().padding(bottom = 8.dp)) {
            if (isWebPage) {
                Row(Modifier.fillMaxWidth().padding(start = 20.dp, end = 20.dp, top = 18.dp, bottom = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                    FaviconImage(tab.favicon, 22.dp)
                    Spacer(Modifier.width(12.dp))
                    Column(Modifier.weight(1f)) {
                        Text(tab.displayTitle, color = colors.text, fontWeight = FontWeight.SemiBold, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(UrlHelper.displayHost(tab.url) ?: tab.url, color = colors.textTertiary, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                }
            }

            Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 10.dp), horizontalArrangement = Arrangement.SpaceEvenly) {
                QuickAction(Icons.AutoMirrored.Rounded.ArrowBack, "Retour", tab.canGoBack) { controller.goBack(tab); close() }
                QuickAction(Icons.AutoMirrored.Rounded.ArrowForward, "Avancer", tab.canGoForward) { controller.goForward(tab); close() }
                QuickAction(Icons.Rounded.Refresh, "Recharger", isWebPage) { controller.reload(tab); close() }
                QuickAction(if (bookmarked) Icons.Rounded.Bookmark else Icons.Rounded.BookmarkBorder, if (bookmarked) "Favori" else "Ajouter", isWebPage, highlighted = bookmarked) {
                    val added = app.stores.toggleBookmark(tab.url, tab.displayTitle)
                    controller.emit(com.navigatueur.mobile.browser.UiEvent.Message(if (added) "Ajouté aux favoris" else "Retiré des favoris"))
                    close()
                }
                QuickAction(Icons.Rounded.Share, "Partager", isWebPage) { actions.share(tab.displayTitle, null, tab.url); close() }
            }

            // Extensions' own buttons (uBlock Origin's panel, Dark Reader's…).
            val extensionActions = app.extensions.actions.values.filter { action -> app.extensions.installed.any { it.id == action.extensionId && it.enabled } }
            if (extensionActions.isNotEmpty() && isWebPage) {
                Row(
                    Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 4.dp),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    extensionActions.forEach { action ->
                        Row(
                            Modifier
                                .clip(RoundedCornerShape(12.dp))
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
                                    modifier = Modifier.clip(RoundedCornerShape(6.dp)).background(colors.accent).padding(horizontal = 5.dp, vertical = 1.dp))
                            }
                        }
                    }
                }
            }

            Spacer(Modifier.height(6.dp))
            MenuItem(Icons.Rounded.Add, "Nouvel onglet") { close(); state.openAddress(null, newTab = true, private = false) }
            MenuItem(Icons.Rounded.VisibilityOff, "Nouvel onglet privé") { close(); state.openAddress(null, newTab = true, private = true) }
            MenuItem(Icons.Rounded.Home, "Page d'accueil") { close(); controller.goHome(tab) }
            Divider()
            MenuItem(Icons.Rounded.Bookmarks, "Favoris") { close(); state.screen = Screen.Bookmarks }
            MenuItem(Icons.Rounded.History, "Historique") { close(); state.screen = Screen.History }
            MenuItem(Icons.Rounded.Download, "Téléchargements") { close(); state.screen = Screen.Downloads }
            MenuItem(Icons.Rounded.Extension, "Extensions") { close(); state.screen = Screen.Extensions }
            Divider()
            if (isWebPage) {
                MenuItem(Icons.Rounded.FindInPage, "Rechercher dans la page") { close(); state.findInPage = true }
                MenuSwitch(Icons.Rounded.DesktopWindows, "Version ordinateur", tab.isDesktopMode) { controller.setDesktopMode(tab, it); close() }
            }
            MenuSwitch(Icons.Rounded.DarkMode, "Mode sombre des sites", settings.forceDarkSites) { app.extensions.setForceDark(it) }
            if (isWebPage) {
                if (tab.hasMedia) MenuItem(Icons.Rounded.PictureInPictureAlt, "Image dans l'image") { close(); actions.enterPictureInPicture() }
                MenuItem(Icons.Rounded.Translate, "Traduire la page") {
                    close()
                    controller.loadUrl(tab, "https://translate.yandex.com/translate?url=${Uri.encode(tab.url)}&lang=fr")
                }
                MenuItem(Icons.Rounded.AddToHomeScreen, "Ajouter à l'écran d'accueil") { close(); actions.addToHomeScreen(tab) }
                MenuItem(Icons.Rounded.OpenInNew, "Ouvrir dans une autre application") {
                    close()
                    actions.startActivitySafely(Intent.createChooser(Intent(Intent.ACTION_VIEW, Uri.parse(tab.url)), "Ouvrir avec"))
                }
            }
            Divider()
            app.updater.available?.let { release ->
                MenuItem(Icons.Rounded.SystemUpdate, "Installer la mise à jour ${release.version}", accent = true) { close(); state.screen = Screen.Settings }
            }
            MenuItem(Icons.Rounded.Settings, "Paramètres") { close(); state.screen = Screen.Settings }
        }
    }
}

@Composable
private fun QuickAction(icon: ImageVector, label: String, enabled: Boolean, highlighted: Boolean = false, onClick: () -> Unit) {
    val colors = Nv.colors
    val alpha = if (enabled) 1f else 0.35f
    Column(
        Modifier.clip(RoundedCornerShape(14.dp)).clickable(enabled = enabled, onClick = onClick).padding(horizontal = 6.dp, vertical = 6.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Box(Modifier.size(46.dp).clip(RoundedCornerShape(14.dp)).background(if (highlighted) colors.accentSoft else colors.surface), contentAlignment = Alignment.Center) {
            Icon(icon, label, tint = (if (highlighted) colors.accent else colors.text).copy(alpha = alpha), modifier = Modifier.size(22.dp))
        }
        Spacer(Modifier.height(4.dp))
        Text(label, color = colors.textSecondary.copy(alpha = alpha), fontSize = 11.sp)
    }
}

@Composable
private fun MenuItem(icon: ImageVector, title: String, accent: Boolean = false, onClick: () -> Unit) {
    val colors = Nv.colors
    Row(Modifier.fillMaxWidth().clickable(onClick = onClick).padding(horizontal = 22.dp, vertical = 13.dp), verticalAlignment = Alignment.CenterVertically) {
        Icon(icon, null, tint = if (accent) colors.accent else colors.textSecondary, modifier = Modifier.size(21.dp))
        Spacer(Modifier.width(18.dp))
        Text(title, color = if (accent) colors.accent else colors.text, fontSize = 15.sp, fontWeight = if (accent) FontWeight.SemiBold else FontWeight.Normal)
    }
}

@Composable
private fun MenuSwitch(icon: ImageVector, title: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    val colors = Nv.colors
    Row(Modifier.fillMaxWidth().clickable { onChange(!checked) }.padding(start = 22.dp, end = 16.dp, top = 6.dp, bottom = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        Icon(icon, null, tint = colors.textSecondary, modifier = Modifier.size(21.dp))
        Spacer(Modifier.width(18.dp))
        Text(title, color = colors.text, fontSize = 15.sp, modifier = Modifier.weight(1f))
        androidx.compose.material3.Switch(
            checked = checked,
            onCheckedChange = onChange,
            colors = androidx.compose.material3.SwitchDefaults.colors(checkedTrackColor = colors.accent, checkedThumbColor = colors.accentForeground, uncheckedTrackColor = colors.surfaceActive, uncheckedBorderColor = androidx.compose.ui.graphics.Color.Transparent),
        )
    }
}

@Composable
private fun Divider() {
    Box(Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 4.dp).height(1.dp).background(Nv.colors.panelBorder))
}
