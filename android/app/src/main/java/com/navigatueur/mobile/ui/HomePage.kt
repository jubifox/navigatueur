package com.navigatueur.mobile.ui

import android.graphics.BitmapFactory
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.VisibilityOff
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.NavigatueurApp
import com.navigatueur.mobile.browser.BrowserTab
import com.navigatueur.mobile.browser.TabsController
import com.navigatueur.mobile.core.SearchEngines
import com.navigatueur.mobile.core.UrlHelper
import com.navigatueur.mobile.ui.theme.Nv
import com.navigatueur.mobile.ui.theme.WordmarkStyle
import java.io.File

/** The Navigatueur page of a new tab: the wordmark, the search box and the favorites — the Windows app's new-tab page. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun HomePage(controller: TabsController, tab: BrowserTab, state: AppUiState, modifier: Modifier, toolbarAtBottom: Boolean) {
    val app = NavigatueurApp.instance
    val settings by app.stores.settings.flow.collectAsState()
    val bookmarks by app.stores.bookmarks.flow.collectAsState()
    val colors = Nv.colors

    val background = remember(settings.newTabBackgroundPath) {
        settings.newTabBackgroundPath?.takeIf { File(it).isFile }?.let { path ->
            runCatching {
                val options = BitmapFactory.Options().apply { inSampleSize = 2 }
                BitmapFactory.decodeFile(path, options)?.asImageBitmap()
            }.getOrNull()
        }
    }

    Box(modifier.background(colors.window)) {
        if (background != null && !tab.isPrivate) {
            Image(background, null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
            Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Color.Black.copy(alpha = 0.25f), Color.Black.copy(alpha = 0.55f)))))
        } else {
            Box(
                Modifier.fillMaxSize().background(
                    Brush.radialGradient(listOf(colors.accent.copy(alpha = if (colors.isDark) 0.16f else 0.12f), Color.Transparent), radius = 1400f)
                )
            )
        }
        val onImage = background != null && !tab.isPrivate

        Column(
            Modifier
                .fillMaxSize()
                .statusBarsPadding()
                .navigationBarsPadding()
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 20.dp)
                .padding(top = if (toolbarAtBottom) 0.dp else 64.dp, bottom = if (toolbarAtBottom) 80.dp else 16.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Spacer(Modifier.height(96.dp))
            Text("NAVIGATUEUR", style = WordmarkStyle, color = if (onImage) Color.White.copy(alpha = 0.85f) else colors.textSecondary)
            Spacer(Modifier.height(22.dp))

            Row(
                Modifier
                    .widthIn(max = 560.dp)
                    .fillMaxWidth()
                    .height(52.dp)
                    .clip(RoundedCornerShape(16.dp))
                    .background(colors.panel.copy(alpha = if (onImage) 0.92f else 1f))
                    .border(1.dp, colors.panelBorder, RoundedCornerShape(16.dp))
                    .clickable { state.openAddress(tab) }
                    .padding(horizontal = 16.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Icon(Icons.Rounded.Search, null, tint = colors.textTertiary, modifier = Modifier.size(20.dp))
                Spacer(Modifier.size(10.dp))
                Text(
                    "Rechercher avec ${SearchEngines.byId(settings.searchEngine).displayName} ou saisir une adresse",
                    color = colors.textTertiary, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis,
                )
            }

            if (tab.isPrivate) {
                Spacer(Modifier.height(28.dp))
                Column(
                    Modifier.widthIn(max = 560.dp).fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(colors.panel).padding(18.dp),
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
            } else if (settings.showFavoritesOnHome && bookmarks.isNotEmpty()) {
                Spacer(Modifier.height(30.dp))
                val favorites = bookmarks.takeLast(12).reversed()
                Column(Modifier.widthIn(max = 560.dp).fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    favorites.chunked(4).forEach { row ->
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            row.forEach { bookmark ->
                                var menu by remember { mutableStateOf(false) }
                                Column(
                                    Modifier
                                        .weight(1f)
                                        .clip(RoundedCornerShape(14.dp))
                                        .combinedClickable(onClick = { controller.loadUrl(tab, bookmark.url) }, onLongClick = { menu = true })
                                        .padding(vertical = 8.dp),
                                    horizontalAlignment = Alignment.CenterHorizontally,
                                ) {
                                    Box(
                                        Modifier.size(52.dp).clip(RoundedCornerShape(14.dp)).background(colors.panel.copy(alpha = if (onImage) 0.9f else 1f)),
                                        contentAlignment = Alignment.Center,
                                    ) {
                                        FaviconImage(controller.favicons.cached(bookmark.url), 26.dp)
                                    }
                                    Spacer(Modifier.height(6.dp))
                                    Text(
                                        bookmark.title.ifBlank { UrlHelper.displayHost(bookmark.url) ?: bookmark.url },
                                        color = if (onImage) Color.White else colors.textSecondary,
                                        fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center,
                                    )
                                    DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                                        DropdownMenuItem(text = { Text("Ouvrir dans un nouvel onglet") }, onClick = { menu = false; controller.newTab(bookmark.url, select = false) })
                                        DropdownMenuItem(text = { Text("Retirer des favoris") }, onClick = { menu = false; app.stores.toggleBookmark(bookmark.url, bookmark.title) })
                                    }
                                }
                            }
                            repeat(4 - row.size) { Spacer(Modifier.weight(1f)) }
                        }
                    }
                }
            }
        }
    }
}
