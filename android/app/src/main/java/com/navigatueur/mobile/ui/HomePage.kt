package com.navigatueur.mobile.ui

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.VisibilityOff
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shadow
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.NavigatueurApp
import com.navigatueur.mobile.browser.BrowserTab
import com.navigatueur.mobile.browser.TabsController
import com.navigatueur.mobile.core.SearchEngines
import com.navigatueur.mobile.core.TopSites
import com.navigatueur.mobile.core.UrlHelper
import com.navigatueur.mobile.ui.theme.Nv
import com.navigatueur.mobile.ui.theme.WallpaperBox
import com.navigatueur.mobile.ui.theme.WordmarkStyle
import kotlinx.coroutines.delay
import java.util.Calendar
import java.util.Locale

/**
 * The Navigatueur page of a new tab — the Windows app's new-tab page —
 * as set up in Personnaliser: wallpaper, title, clock, search box,
 * favorites and the most visited sites.
 */
@Composable
fun HomePage(controller: TabsController, tab: BrowserTab, state: AppUiState, modifier: Modifier, toolbarAtBottom: Boolean) {
    val app = NavigatueurApp.instance
    val settings by app.stores.settings.flow.collectAsState()
    val bookmarks by app.stores.bookmarks.flow.collectAsState()
    val history by app.stores.history.flow.collectAsState()
    val colors = Nv.colors
    val look = Nv.look
    val home = settings.appearance
    val onWallpaper = Nv.onWallpaper
    val textShadow = if (look.wallpaperIsDark) Shadow(Color.Black.copy(alpha = 0.35f), blurRadius = 8f) else null
    val columns = home.homeColumns.coerceIn(3, 6)

    val topSites = remember(history, settings.hiddenTopSites, home.homeTopSites, tab.isPrivate) {
        if (!home.homeTopSites || tab.isPrivate) emptyList()
        else TopSites.compute(history, settings.hiddenTopSites, System.currentTimeMillis(), limit = columns * 2)
            // A favorite is already on the page.
            .filter { site -> bookmarks.none { UrlHelper.displayHost(it.url) == site.host } }
            .take(columns)
    }

    WallpaperBox(modifier, private = tab.isPrivate) {
        Column(
            Modifier
                .fillMaxSize()
                .statusBarsPadding()
                .navigationBarsPadding()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 20.dp)
                .padding(top = if (toolbarAtBottom) 0.dp else 64.dp, bottom = if (toolbarAtBottom) look.toolbarHeight + 24.dp else 16.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Spacer(Modifier.height(if (home.homeClock) 56.dp else 96.dp))
            if (home.homeClock) {
                Clock(onWallpaper, textShadow)
                Spacer(Modifier.height(18.dp))
            }
            if (home.homeTitle.isNotBlank()) {
                Text(
                    home.homeTitle,
                    style = WordmarkStyle.copy(shadow = textShadow),
                    color = onWallpaper.copy(alpha = 0.85f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Spacer(Modifier.height(22.dp))
            }

            SearchBox(
                text = "Rechercher avec ${SearchEngines.byId(settings.searchEngine).displayName} ou saisir une adresse",
                onClick = { state.openAddress(tab) },
            )

            if (tab.isPrivate) {
                Spacer(Modifier.height(28.dp))
                Column(
                    Modifier.widthIn(max = 560.dp).fillMaxWidth().clip(Nv.shape(16.dp)).background(colors.sheet).padding(18.dp),
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Rounded.VisibilityOff, null, tint = colors.accent, modifier = Modifier.size(20.dp))
                        Spacer(Modifier.size(10.dp))
                        Text("Navigation privée", color = colors.text, fontWeight = FontWeight.SemiBold, fontSize = 16.sp)
                    }
                    Spacer(Modifier.height(8.dp))
                    Text(
                        "Ni l'historique, ni les cookies, ni les données des sites de ces onglets ne sont gardés : tout disparaît quand vous fermez le dernier onglet privé. Votre fournisseur d'accès et les sites visités peuvent toujours voir votre activité.",
                        color = colors.textSecondary, fontSize = 14.sp, lineHeight = 20.sp,
                    )
                }
            } else {
                if (settings.showFavoritesOnHome && bookmarks.isNotEmpty()) {
                    Spacer(Modifier.height(30.dp))
                    SiteGrid(
                        label = "Favoris",
                        sites = bookmarks.takeLast(columns * 3).reversed().map { HomeSite(it.url, it.title.ifBlank { UrlHelper.displayHost(it.url) ?: it.url }) },
                        columns = columns,
                        controller = controller,
                        tab = tab,
                        menuExtra = { site, close ->
                            DropdownMenuItem(text = { Text("Retirer des favoris") }, onClick = {
                                close()
                                app.stores.toggleBookmark(site.url, site.title)
                            })
                        },
                    )
                }
                if (topSites.isNotEmpty()) {
                    Spacer(Modifier.height(24.dp))
                    SiteGrid(
                        label = "Sites fréquents",
                        sites = topSites.map { HomeSite(it.url, it.label.replaceFirstChar { c -> c.titlecase(Locale.FRENCH) }, it.host) },
                        columns = columns,
                        controller = controller,
                        tab = tab,
                        menuExtra = { site, close ->
                            DropdownMenuItem(text = { Text("Ne plus afficher") }, onClick = {
                                close()
                                val host = site.host ?: return@DropdownMenuItem
                                app.stores.settings.update { it.copy(hiddenTopSites = (it.hiddenTopSites + host).distinct()) }
                            })
                        },
                    )
                }
            }
        }
    }
}

private data class HomeSite(val url: String, val title: String, val host: String? = null)

@Composable
private fun SearchBox(text: String, onClick: () -> Unit) {
    val colors = Nv.colors
    val shape = Nv.shape(16.dp)
    Row(
        Modifier
            .widthIn(max = 560.dp)
            .fillMaxWidth()
            .height(52.dp)
            .clip(shape)
            .background(colors.sheet.copy(alpha = 0.94f))
            .border(1.dp, colors.panelBorder, shape)
            .clickable(onClick = onClick, onClickLabel = "Saisir une adresse")
            .padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(Icons.Rounded.Search, null, tint = colors.textTertiary, modifier = Modifier.size(20.dp))
        Spacer(Modifier.size(10.dp))
        Text(text, color = colors.textTertiary, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
    }
}

/** The time, big, with a greeting under it; updated each minute. */
@Composable
private fun Clock(color: Color, shadow: Shadow?) {
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(Unit) {
        while (true) {
            now = System.currentTimeMillis()
            delay(60_000 - now % 60_000)
        }
    }
    val calendar = remember(now) { Calendar.getInstance().apply { timeInMillis = now } }
    val hour = calendar.get(Calendar.HOUR_OF_DAY)
    val time = String.format(Locale.FRENCH, "%02d:%02d", hour, calendar.get(Calendar.MINUTE))
    val greeting = when (hour) {
        in 5..11 -> "Bonjour"
        in 12..17 -> "Bon après-midi"
        in 18..22 -> "Bonsoir"
        else -> "Bonne nuit"
    }
    val day = remember(now) {
        java.text.SimpleDateFormat("EEEE d MMMM", Locale.FRENCH).format(java.util.Date(now)).replaceFirstChar { it.titlecase(Locale.FRENCH) }
    }
    Text(time, color = color, style = TextStyle(fontSize = 64.sp, fontWeight = FontWeight.Light, letterSpacing = 2.sp, shadow = shadow))
    Text("$greeting · $day", color = color.copy(alpha = 0.8f), style = TextStyle(fontSize = 15.sp, shadow = shadow), textAlign = TextAlign.Center)
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun SiteGrid(
    label: String,
    sites: List<HomeSite>,
    columns: Int,
    controller: TabsController,
    tab: BrowserTab,
    menuExtra: @Composable (HomeSite, () -> Unit) -> Unit,
) {
    val colors = Nv.colors
    val onWallpaper = Nv.onWallpaper
    val haptics = LocalHapticFeedback.current
    val tile: Dp = if (columns >= 5) 46.dp else 52.dp
    Column(Modifier.widthIn(max = 560.dp).fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(
            label.uppercase(),
            color = onWallpaper.copy(alpha = 0.7f), fontSize = 11.sp, letterSpacing = 1.2.sp, fontWeight = FontWeight.SemiBold,
            modifier = Modifier.padding(start = 6.dp, bottom = 2.dp),
        )
        sites.chunked(columns).forEach { row ->
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                row.forEach { site ->
                    var menu by remember { mutableStateOf(false) }
                    Column(
                        Modifier
                            .weight(1f)
                            .clip(Nv.shape(14.dp))
                            .combinedClickable(
                                onClick = { controller.loadUrl(tab, site.url) },
                                onLongClick = {
                                    haptics.performHapticFeedback(HapticFeedbackType.LongPress)
                                    menu = true
                                },
                                onClickLabel = site.title,
                            )
                            .padding(vertical = 8.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                    ) {
                        Box(
                            Modifier.size(tile).clip(Nv.shape(14.dp)).background(colors.sheet.copy(alpha = 0.92f)),
                            contentAlignment = Alignment.Center,
                        ) {
                            FaviconImage(controller.favicons.cached(site.url), tile / 2)
                        }
                        Spacer(Modifier.height(6.dp))
                        Text(
                            site.title,
                            color = onWallpaper.copy(alpha = 0.9f),
                            fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center,
                        )
                        DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                            DropdownMenuItem(text = { Text("Ouvrir dans un nouvel onglet") }, onClick = { menu = false; controller.newTab(site.url, select = false) })
                            DropdownMenuItem(text = { Text("Ouvrir dans un onglet privé") }, onClick = { menu = false; controller.newTab(site.url, private = true) })
                            menuExtra(site) { menu = false }
                        }
                    }
                }
                repeat(columns - row.size) { Spacer(Modifier.weight(1f)) }
            }
        }
    }
}
