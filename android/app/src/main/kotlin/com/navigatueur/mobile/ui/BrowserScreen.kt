package com.navigatueur.mobile.ui

import android.webkit.WebView
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.Image
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.filled.Bookmark
import androidx.compose.material.icons.filled.BookmarkBorder
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.History
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Public
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Share
import androidx.compose.material.icons.filled.ZoomIn
import androidx.compose.material.icons.filled.ZoomOut
import androidx.compose.material3.Checkbox
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.material3.TextField
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.model.SearchEngine
import com.navigatueur.mobile.ui.theme.Accent
import com.navigatueur.mobile.ui.theme.ChromeBackground
import com.navigatueur.mobile.ui.theme.ChromeText
import com.navigatueur.mobile.ui.theme.Surface
import java.text.DateFormat
import java.util.Date

@Composable
fun BrowserScreen(viewModel: BrowserViewModel) {
    var activeWebView by remember { mutableStateOf<WebView?>(null) }
    val keyboardController = LocalSoftwareKeyboardController.current

    BackHandler(enabled = viewModel.overlay != OverlayScreen.NONE) {
        viewModel.overlay = OverlayScreen.NONE
    }
    BackHandler(enabled = viewModel.overlay == OverlayScreen.NONE && activeWebView?.canGoBack() == true) {
        activeWebView?.goBack()
    }

    Column(modifier = Modifier.fillMaxSize().background(ChromeBackground)) {
        TopBar(
            viewModel = viewModel,
            activeWebView = activeWebView,
            onSubmit = {
                viewModel.navigateFromAddressBar(viewModel.addressBarText)
                keyboardController?.hide()
            },
        )

        val progress = viewModel.activeTab?.progress ?: 100
        if (progress in 1..99) {
            LinearProgressIndicator(
                progress = { progress / 100f },
                modifier = Modifier.fillMaxWidth().height(2.dp),
                color = Accent,
                trackColor = ChromeBackground,
            )
        }

        if (viewModel.isFindInPageVisible) {
            FindInPageBar(viewModel = viewModel, activeWebView = activeWebView)
        }

        Box(modifier = Modifier.fillMaxSize()) {
            when (viewModel.overlay) {
                OverlayScreen.TAB_SWITCHER -> TabSwitcherScreen(viewModel)
                OverlayScreen.HISTORY -> HistoryScreen(viewModel)
                OverlayScreen.BOOKMARKS -> BookmarksScreen(viewModel)
                OverlayScreen.SETTINGS -> SettingsScreen(viewModel)
                OverlayScreen.NONE -> {
                    val tab = viewModel.activeTab
                    if (tab != null && tab.url != null) {
                        BrowserWebView(
                            tab = tab,
                            adBlock = viewModel.adBlock,
                            onProgressChanged = { tab.progress = it },
                            onPageNavigated = { url, title -> viewModel.onPageNavigated(tab.id, url, title) },
                            onWebViewReady = { activeWebView = it },
                        )
                    } else {
                        activeWebView = null
                        NewTabScreen(viewModel)
                    }
                }
            }
        }

        BottomBar(viewModel, activeWebView)
    }
}

@Composable
private fun FindInPageBar(viewModel: BrowserViewModel, activeWebView: WebView?) {
    Row(
        modifier = Modifier.fillMaxWidth().background(Surface).padding(horizontal = 8.dp, vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        TextField(
            value = viewModel.findInPageQuery,
            onValueChange = {
                viewModel.findInPageQuery = it
                activeWebView?.findAllAsync(it)
            },
            modifier = Modifier.weight(1f),
            singleLine = true,
            placeholder = { Text("Rechercher dans la page") },
            keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = androidx.compose.foundation.text.KeyboardActions(
                onSearch = { activeWebView?.findNext(true) },
            ),
        )
        IconButton(onClick = { activeWebView?.findNext(true) }) {
            Icon(Icons.Filled.Search, contentDescription = "Occurrence suivante", tint = ChromeText)
        }
        IconButton(onClick = {
            viewModel.isFindInPageVisible = false
            viewModel.findInPageQuery = ""
            activeWebView?.clearMatches()
        }) {
            Icon(Icons.Filled.Close, contentDescription = "Fermer la recherche", tint = ChromeText)
        }
    }
}

@Composable
private fun TopBar(viewModel: BrowserViewModel, activeWebView: WebView?, onSubmit: () -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth().background(Surface).padding(horizontal = 4.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IconButton(onClick = { activeWebView?.goBack() }, enabled = activeWebView?.canGoBack() == true) {
            Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Précédent", tint = ChromeText)
        }
        IconButton(onClick = { activeWebView?.goForward() }, enabled = activeWebView?.canGoForward() == true) {
            Icon(Icons.AutoMirrored.Filled.ArrowForward, contentDescription = "Suivant", tint = ChromeText)
        }
        TextField(
            value = viewModel.addressBarText,
            onValueChange = { viewModel.addressBarText = it },
            modifier = Modifier.weight(1f),
            singleLine = true,
            shape = RoundedCornerShape(20.dp),
            placeholder = { Text("Rechercher ou saisir une adresse") },
            keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(imeAction = ImeAction.Go),
            keyboardActions = androidx.compose.foundation.text.KeyboardActions(onGo = { onSubmit() }),
        )
        IconButton(onClick = { activeWebView?.reload() }) {
            Icon(Icons.Filled.Refresh, contentDescription = "Actualiser", tint = ChromeText)
        }
    }
}

@Composable
private fun BottomBar(viewModel: BrowserViewModel, activeWebView: WebView?) {
    var menuOpen by remember { mutableStateOf(false) }
    val activeTab = viewModel.activeTab
    val context = androidx.compose.ui.platform.LocalContext.current

    Row(
        modifier = Modifier.fillMaxWidth().background(Surface).padding(vertical = 4.dp),
        horizontalArrangement = Arrangement.SpaceEvenly,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IconButton(onClick = { viewModel.overlay = OverlayScreen.NONE; viewModel.newTab() }) {
            Icon(Icons.Filled.Home, contentDescription = "Nouvel onglet", tint = ChromeText)
        }

        IconButton(onClick = {
            if (activeTab?.url != null) {
                viewModel.toggleBookmark(activeTab.url!!, activeTab.title)
            }
        }) {
            val bookmarked = viewModel.isBookmarked(activeTab?.url)
            Icon(
                if (bookmarked) Icons.Filled.Bookmark else Icons.Filled.BookmarkBorder,
                contentDescription = "Marque-page",
                tint = if (bookmarked) Accent else ChromeText,
            )
        }

        Box(
            modifier = Modifier
                .size(32.dp)
                .clickable {
                    viewModel.overlay =
                        if (viewModel.overlay == OverlayScreen.TAB_SWITCHER) OverlayScreen.NONE else OverlayScreen.TAB_SWITCHER
                },
            contentAlignment = Alignment.Center,
        ) {
            Box(
                modifier = Modifier
                    .size(22.dp)
                    .background(Color.Transparent, RoundedCornerShape(4.dp))
                    .border(1.5.dp, ChromeText, RoundedCornerShape(4.dp)),
                contentAlignment = Alignment.Center,
            ) {
                Text("${viewModel.tabs.size}", color = ChromeText, fontSize = 12.sp)
            }
        }

        Box {
            IconButton(onClick = { menuOpen = true }) {
                Icon(Icons.Filled.MoreVert, contentDescription = "Menu", tint = ChromeText)
            }
            DropdownMenu(expanded = menuOpen, onDismissRequest = { menuOpen = false }) {
                DropdownMenuItem(
                    text = { Text("Nouvel onglet privé") },
                    onClick = { menuOpen = false; viewModel.newTab(private = true) },
                )
                DropdownMenuItem(
                    leadingIcon = { Icon(Icons.Filled.History, contentDescription = null) },
                    text = { Text("Historique") },
                    onClick = { menuOpen = false; viewModel.overlay = OverlayScreen.HISTORY },
                )
                DropdownMenuItem(
                    leadingIcon = { Icon(Icons.Filled.Bookmark, contentDescription = null) },
                    text = { Text("Marque-pages") },
                    onClick = { menuOpen = false; viewModel.overlay = OverlayScreen.BOOKMARKS },
                )
                DropdownMenuItem(
                    leadingIcon = { Icon(Icons.Filled.Settings, contentDescription = null) },
                    text = { Text("Paramètres") },
                    onClick = { menuOpen = false; viewModel.overlay = OverlayScreen.SETTINGS },
                )

                if (activeTab?.url != null) {
                    androidx.compose.material3.HorizontalDivider()
                    DropdownMenuItem(
                        leadingIcon = { Icon(Icons.Filled.Search, contentDescription = null) },
                        text = { Text("Rechercher dans la page") },
                        onClick = { menuOpen = false; viewModel.isFindInPageVisible = true },
                    )
                    DropdownMenuItem(
                        leadingIcon = { Icon(Icons.Filled.ZoomIn, contentDescription = null) },
                        text = { Text("Zoom +") },
                        onClick = { activeWebView?.zoomIn() },
                    )
                    DropdownMenuItem(
                        leadingIcon = { Icon(Icons.Filled.ZoomOut, contentDescription = null) },
                        text = { Text("Zoom -") },
                        onClick = { activeWebView?.zoomOut() },
                    )
                    DropdownMenuItem(
                        leadingIcon = { Icon(Icons.Filled.Public, contentDescription = null) },
                        text = { Text(if (activeTab.isDesktopSite) "Site mobile" else "Site pour ordinateur") },
                        onClick = {
                            menuOpen = false
                            viewModel.toggleDesktopSite()
                            activeWebView?.settings?.userAgentString =
                                if (activeTab.isDesktopSite) com.navigatueur.mobile.model.DesktopUserAgent else null
                            activeWebView?.reload()
                        },
                    )
                    DropdownMenuItem(
                        leadingIcon = { Icon(Icons.Filled.Share, contentDescription = null) },
                        text = { Text("Partager") },
                        onClick = {
                            menuOpen = false
                            val intent = android.content.Intent(android.content.Intent.ACTION_SEND).apply {
                                type = "text/plain"
                                putExtra(android.content.Intent.EXTRA_TEXT, activeTab.url)
                            }
                            context.startActivity(android.content.Intent.createChooser(intent, "Partager le lien"))
                        },
                    )
                }
            }
        }
    }
}

@Composable
private fun NewTabScreen(viewModel: BrowserViewModel) {
    Column(
        modifier = Modifier.fillMaxSize().padding(24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Text("🧭", fontSize = 48.sp)
        androidx.compose.foundation.layout.Spacer(modifier = Modifier.height(12.dp))
        Text("Navigatueur", color = ChromeText, fontSize = 20.sp, fontWeight = FontWeight.SemiBold)
        androidx.compose.foundation.layout.Spacer(modifier = Modifier.height(24.dp))

        if (viewModel.bookmarks.isNotEmpty()) {
            Text("Marque-pages", color = ChromeText.copy(alpha = 0.6f), modifier = Modifier.fillMaxWidth())
            androidx.compose.foundation.layout.Spacer(modifier = Modifier.height(8.dp))
            LazyColumn(modifier = Modifier.fillMaxWidth()) {
                items(viewModel.bookmarks.take(6)) { bookmark ->
                    Text(
                        bookmark.title.ifBlank { bookmark.url },
                        color = Accent,
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { viewModel.navigateFromAddressBar(bookmark.url) }
                            .padding(vertical = 8.dp),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
        }
    }
}

@Composable
private fun TabSwitcherScreen(viewModel: BrowserViewModel) {
    OverlayHeader(title = "Onglets (${viewModel.tabs.size})", onClose = { viewModel.overlay = OverlayScreen.NONE })
    LazyColumn(modifier = Modifier.fillMaxSize().padding(top = 48.dp)) {
        items(viewModel.tabs, key = { it.id }) { tab ->
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable { viewModel.activateTab(tab.id) }
                    .background(if (tab.id == viewModel.activeTab?.id) Surface else Color.Transparent)
                    .padding(16.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                val favicon = tab.favicon
                if (favicon != null) {
                    Image(
                        bitmap = favicon,
                        contentDescription = null,
                        modifier = Modifier.size(20.dp).padding(end = 10.dp),
                    )
                } else {
                    Box(modifier = Modifier.size(20.dp).padding(end = 10.dp))
                }
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        (if (tab.isPrivate) "🕶 " else "") + tab.title,
                        color = ChromeText,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    tab.url?.let {
                        Text(it, color = ChromeText.copy(alpha = 0.5f), maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                }
                IconButton(onClick = { viewModel.closeTab(tab.id) }) {
                    Icon(Icons.Filled.Close, contentDescription = "Fermer", tint = ChromeText)
                }
            }
        }
    }
}

@Composable
private fun HistoryScreen(viewModel: BrowserViewModel) {
    OverlayHeader(
        title = "Historique",
        onClose = { viewModel.overlay = OverlayScreen.NONE },
        trailing = {
            IconButton(onClick = { viewModel.clearHistory() }) {
                Icon(Icons.Filled.Delete, contentDescription = "Tout effacer", tint = ChromeText)
            }
        },
    )
    val formatter = remember { DateFormat.getDateTimeInstance(DateFormat.SHORT, DateFormat.SHORT) }
    LazyColumn(modifier = Modifier.fillMaxSize().padding(top = 48.dp)) {
        items(viewModel.history) { entry ->
            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable {
                        viewModel.overlay = OverlayScreen.NONE
                        viewModel.navigateFromAddressBar(entry.url)
                    }
                    .padding(horizontal = 16.dp, vertical = 10.dp),
            ) {
                Text(entry.title, color = ChromeText, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(
                    "${entry.url} · ${formatter.format(Date(entry.visitedAtEpochMillis))}",
                    color = ChromeText.copy(alpha = 0.5f),
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
    }
}

@Composable
private fun BookmarksScreen(viewModel: BrowserViewModel) {
    OverlayHeader(title = "Marque-pages", onClose = { viewModel.overlay = OverlayScreen.NONE })
    LazyColumn(modifier = Modifier.fillMaxSize().padding(top = 48.dp)) {
        items(viewModel.bookmarks, key = { it.url }) { bookmark ->
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable {
                        viewModel.overlay = OverlayScreen.NONE
                        viewModel.navigateFromAddressBar(bookmark.url)
                    }
                    .padding(horizontal = 16.dp, vertical = 10.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(bookmark.title.ifBlank { bookmark.url }, color = ChromeText, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(bookmark.url, color = ChromeText.copy(alpha = 0.5f), maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                IconButton(onClick = { viewModel.removeBookmark(bookmark) }) {
                    Icon(Icons.Filled.Delete, contentDescription = "Supprimer", tint = ChromeText)
                }
            }
        }
    }
}

@Composable
private fun SettingsScreen(viewModel: BrowserViewModel) {
    OverlayHeader(title = "Paramètres", onClose = { viewModel.overlay = OverlayScreen.NONE })
    Column(modifier = Modifier.fillMaxSize().padding(top = 60.dp, start = 16.dp, end = 16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
            Checkbox(
                checked = viewModel.settings.isAdBlockEnabled,
                onCheckedChange = { viewModel.setAdBlockEnabled(it) },
            )
            Text("Bloqueur de publicités", color = ChromeText)
        }

        androidx.compose.foundation.layout.Spacer(modifier = Modifier.height(16.dp))
        Text("Moteur de recherche", color = ChromeText.copy(alpha = 0.7f))
        SearchEngine.entries.forEach { engine ->
            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier.fillMaxWidth().clickable { viewModel.setSearchEngine(engine) }.padding(vertical = 8.dp),
            ) {
                androidx.compose.material3.RadioButton(
                    selected = viewModel.settings.searchEngine == engine,
                    onClick = { viewModel.setSearchEngine(engine) },
                )
                Text(engine.label, color = ChromeText)
            }
        }
    }
}

@Composable
private fun OverlayHeader(title: String, onClose: () -> Unit, trailing: @Composable (() -> Unit)? = null) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(ChromeBackground)
            .padding(horizontal = 4.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IconButton(onClick = onClose) {
            Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Retour", tint = ChromeText)
        }
        Text(title, color = ChromeText, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
        trailing?.invoke()
    }
}
