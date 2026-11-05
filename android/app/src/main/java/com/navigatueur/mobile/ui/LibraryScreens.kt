package com.navigatueur.mobile.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Bookmarks
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.DeleteOutline
import androidx.compose.material.icons.rounded.Download
import androidx.compose.material.icons.rounded.History
import androidx.compose.material.icons.rounded.InsertDriveFile
import androidx.compose.material.icons.rounded.MoreVert
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.NavigatueurApp
import com.navigatueur.mobile.core.UrlHelper
import com.navigatueur.mobile.data.DownloadRecord
import com.navigatueur.mobile.ui.theme.Nv
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale

private fun dayLabel(time: Long): String {
    val day = Calendar.getInstance().apply { timeInMillis = time }
    val today = Calendar.getInstance()
    fun sameDay(a: Calendar, b: Calendar) = a.get(Calendar.YEAR) == b.get(Calendar.YEAR) && a.get(Calendar.DAY_OF_YEAR) == b.get(Calendar.DAY_OF_YEAR)
    val yesterday = Calendar.getInstance().apply { add(Calendar.DAY_OF_YEAR, -1) }
    return when {
        sameDay(day, today) -> "Aujourd'hui"
        sameDay(day, yesterday) -> "Hier"
        else -> SimpleDateFormat("EEEE d MMMM", Locale.FRENCH).format(Date(time)).replaceFirstChar { it.uppercase() }
    }
}

private val timeFormat = SimpleDateFormat("HH:mm", Locale.FRENCH)

@Composable
private fun SearchField(query: String, placeholder: String, onChange: (String) -> Unit) {
    val colors = Nv.colors
    Row(
        Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp).height(44.dp).clip(RoundedCornerShape(12.dp)).background(colors.surface).padding(horizontal = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Icon(Icons.Rounded.Search, null, tint = colors.textTertiary, modifier = Modifier.size(18.dp))
        Spacer(Modifier.width(8.dp))
        BasicTextField(
            value = query, onValueChange = onChange, singleLine = true,
            textStyle = TextStyle(color = colors.text, fontSize = 15.sp), cursorBrush = SolidColor(colors.accent),
            modifier = Modifier.weight(1f),
            decorationBox = { inner -> if (query.isEmpty()) Text(placeholder, color = colors.textTertiary, fontSize = 15.sp); inner() },
        )
        if (query.isNotEmpty()) IconButton(onClick = { onChange("") }) { Icon(Icons.Rounded.Close, "Effacer", tint = colors.textTertiary, modifier = Modifier.size(18.dp)) }
    }
}

@Composable
private fun PageRow(title: String, url: String, meta: String?, onClick: () -> Unit, onLongClick: (() -> Unit)? = null, trailing: @Composable (() -> Unit)? = null) {
    val colors = Nv.colors
    val controller = NavigatueurApp.instance.tabs
    Row(
        Modifier.fillMaxWidth().clickable(onClick = onClick).padding(start = 18.dp, end = 4.dp, top = 9.dp, bottom = 9.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(34.dp).clip(RoundedCornerShape(10.dp)).background(colors.surface), contentAlignment = Alignment.Center) {
            FaviconImage(controller.favicons.cached(url), 18.dp)
        }
        Spacer(Modifier.width(14.dp))
        Column(Modifier.weight(1f)) {
            Text(title.ifBlank { UrlHelper.displayHost(url) ?: url }, color = colors.text, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(listOfNotNull(UrlHelper.displayHost(url) ?: url, meta).joinToString(" · "), color = colors.textTertiary, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        trailing?.invoke()
    }
}

@Composable
private fun DayHeader(text: String) {
    Text(text, color = Nv.colors.textSecondary, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 18.dp, top = 16.dp, bottom = 4.dp))
}

private fun openInCurrentTab(app: NavigatueurApp, state: AppUiState, url: String) {
    val controller = app.tabs
    val tab = controller.activeTab
    if (tab == null || tab.isPrivate) controller.newTab(url, private = false) else controller.loadUrl(tab, url)
    state.screen = Screen.Browser
}

@Composable
fun HistoryScreen(app: NavigatueurApp, state: AppUiState) {
    val colors = Nv.colors
    val history by app.stores.history.flow.collectAsState()
    var query by remember { mutableStateOf("") }
    var confirmClear by remember { mutableStateOf(false) }

    val entries = remember(history, query) {
        val q = query.trim()
        history.asReversed().filter { q.isEmpty() || it.title.contains(q, true) || it.url.contains(q, true) }.take(1500)
    }

    Column(Modifier.fillMaxSize().background(colors.window)) {
        ScreenHeader("Historique", onBack = { state.screen = Screen.Browser }) {
            if (history.isNotEmpty()) TextButton(onClick = { confirmClear = true }) { Text("Effacer…", color = colors.danger) }
        }
        SearchField(query, "Rechercher dans l'historique") { query = it }
        if (entries.isEmpty()) {
            EmptyState(Icons.Rounded.History, if (query.isBlank()) "Aucun historique" else "Aucun résultat", "Les onglets privés n'enregistrent jamais d'historique.")
        } else {
            LazyColumn(Modifier.fillMaxSize().navigationBarsPadding(), contentPadding = ListPadding) {
                var lastDay = ""
                entries.forEach { entry ->
                    val day = dayLabel(entry.visitedAt)
                    if (day != lastDay) {
                        lastDay = day
                        item(key = "day-$day-${entry.visitedAt}") { DayHeader(day) }
                    }
                    item(key = "${entry.url}-${entry.visitedAt}") {
                        PageRow(entry.title, entry.url, timeFormat.format(Date(entry.visitedAt)), onClick = { openInCurrentTab(app, state, entry.url) }) {
                            IconButton(onClick = { app.stores.history.update { list -> list.filterNot { it == entry } } }) {
                                Icon(Icons.Rounded.Close, "Supprimer", tint = colors.textTertiary, modifier = Modifier.size(18.dp))
                            }
                        }
                    }
                }
            }
        }
    }

    if (confirmClear) {
        AlertDialog(
            onDismissRequest = { confirmClear = false },
            title = { Text("Effacer l'historique ?") },
            text = { Text("Toutes les pages visitées seront oubliées.") },
            confirmButton = { TextButton(onClick = { app.stores.history.set(emptyList()); confirmClear = false }) { Text("Effacer", color = colors.danger) } },
            dismissButton = { TextButton(onClick = { confirmClear = false }) { Text("Annuler") } },
        )
    }
}

@Composable
fun BookmarksScreen(app: NavigatueurApp, state: AppUiState) {
    val colors = Nv.colors
    val bookmarks by app.stores.bookmarks.flow.collectAsState()
    var query by remember { mutableStateOf("") }
    val entries = remember(bookmarks, query) {
        val q = query.trim()
        bookmarks.asReversed().filter { q.isEmpty() || it.title.contains(q, true) || it.url.contains(q, true) }
    }

    Column(Modifier.fillMaxSize().background(colors.window)) {
        ScreenHeader("Favoris", onBack = { state.screen = Screen.Browser })
        if (bookmarks.isNotEmpty()) SearchField(query, "Rechercher dans les favoris") { query = it }
        if (entries.isEmpty()) {
            EmptyState(Icons.Rounded.Bookmarks, "Aucun favori", "Ajoutez la page ouverte depuis le menu ⋯, bouton « Ajouter ». Les favoris apparaissent aussi sur la page d'accueil.")
        } else {
            LazyColumn(Modifier.fillMaxSize().navigationBarsPadding(), contentPadding = ListPadding) {
                items(entries, key = { it.url }) { bookmark ->
                    var menu by remember { mutableStateOf(false) }
                    PageRow(bookmark.title, bookmark.url, null, onClick = { openInCurrentTab(app, state, bookmark.url) }) {
                        Box {
                            IconButton(onClick = { menu = true }) { Icon(Icons.Rounded.MoreVert, "Options", tint = colors.textTertiary) }
                            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                                DropdownMenuItem(text = { Text("Ouvrir dans un nouvel onglet") }, onClick = { menu = false; app.tabs.newTab(bookmark.url, private = false, select = false) })
                                DropdownMenuItem(text = { Text("Ouvrir dans un onglet privé") }, onClick = { menu = false; app.tabs.newTab(bookmark.url, private = true); state.screen = Screen.Browser })
                                DropdownMenuItem(text = { Text("Supprimer") }, onClick = { menu = false; app.stores.toggleBookmark(bookmark.url, bookmark.title) })
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun DownloadsScreen(app: NavigatueurApp, state: AppUiState, actions: ActivityActions) {
    val colors = Nv.colors
    val downloads by app.stores.downloads.flow.collectAsState()

    Column(Modifier.fillMaxSize().background(colors.window)) {
        ScreenHeader("Téléchargements", onBack = { state.screen = Screen.Browser }) {
            if (downloads.any { it.state != "Running" }) TextButton(onClick = { app.downloads.clearFinished() }) { Text("Vider la liste", color = colors.textSecondary) }
        }
        if (downloads.isEmpty()) {
            EmptyState(Icons.Rounded.Download, "Aucun téléchargement", "Les fichiers téléchargés vont dans le dossier Téléchargements du téléphone.")
        } else {
            LazyColumn(Modifier.fillMaxSize().navigationBarsPadding(), contentPadding = ListPadding) {
                items(downloads, key = { it.id }) { record -> DownloadRow(app, record, actions) }
            }
        }
    }
}

@Composable
private fun DownloadRow(app: NavigatueurApp, record: DownloadRecord, actions: ActivityActions) {
    val colors = Nv.colors
    var menu by remember { mutableStateOf(false) }
    Row(
        Modifier
            .fillMaxWidth()
            .clickable(enabled = record.state == "Done") { app.downloads.openIntent(record)?.let(actions::startActivitySafely) }
            .padding(start = 18.dp, end = 4.dp, top = 10.dp, bottom = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(38.dp).clip(RoundedCornerShape(10.dp)).background(colors.surface), contentAlignment = Alignment.Center) {
            Icon(Icons.Rounded.InsertDriveFile, null, tint = colors.textSecondary, modifier = Modifier.size(20.dp))
        }
        Spacer(Modifier.width(14.dp))
        Column(Modifier.weight(1f)) {
            Text(record.fileName, color = colors.text, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            val status = when (record.state) {
                "Running" -> if (record.totalBytes > 0) "${formatSize(record.receivedBytes)} sur ${formatSize(record.totalBytes)}" else formatSize(record.receivedBytes)
                "Done" -> "${formatSize(record.totalBytes)} · ${UrlHelper.displayHost(record.url) ?: ""}"
                "Cancelled" -> "Annulé"
                else -> "Échec"
            }
            Text(status, color = if (record.state == "Failed") colors.danger else colors.textTertiary, fontSize = 13.sp, maxLines = 1)
            if (record.state == "Running") {
                Spacer(Modifier.height(6.dp))
                if (record.totalBytes > 0) LinearProgressIndicator(progress = { record.receivedBytes.toFloat() / record.totalBytes }, modifier = Modifier.fillMaxWidth(), color = colors.accent, trackColor = colors.surfaceActive)
                else LinearProgressIndicator(modifier = Modifier.fillMaxWidth(), color = colors.accent, trackColor = colors.surfaceActive)
            }
        }
        Box {
            IconButton(onClick = { menu = true }) { Icon(Icons.Rounded.MoreVert, "Options", tint = colors.textTertiary) }
            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                if (record.state == "Running") DropdownMenuItem(text = { Text("Annuler") }, onClick = { menu = false; app.downloads.cancel(record) })
                if (record.state == "Done") {
                    DropdownMenuItem(text = { Text("Partager") }, onClick = {
                        menu = false
                        record.contentUri?.let { uri ->
                            actions.startActivitySafely(
                                android.content.Intent.createChooser(
                                    android.content.Intent(android.content.Intent.ACTION_SEND)
                                        .setType(record.mimeType ?: "*/*")
                                        .putExtra(android.content.Intent.EXTRA_STREAM, android.net.Uri.parse(uri))
                                        .addFlags(android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION),
                                    record.fileName,
                                )
                            )
                        }
                    })
                    DropdownMenuItem(text = { Text("Supprimer le fichier") }, leadingIcon = { Icon(Icons.Rounded.DeleteOutline, null) }, onClick = { menu = false; app.downloads.remove(record, deleteFile = true) })
                }
                DropdownMenuItem(text = { Text("Retirer de la liste") }, onClick = { menu = false; app.downloads.remove(record, deleteFile = false) })
            }
        }
    }
}

fun formatSize(bytes: Long): String = when {
    bytes < 0 -> "?"
    bytes < 1024 -> "$bytes o"
    bytes < 1024 * 1024 -> "${bytes / 1024} Ko"
    bytes < 1024L * 1024 * 1024 -> String.format(Locale.FRENCH, "%.1f Mo", bytes / 1048576.0)
    else -> String.format(Locale.FRENCH, "%.2f Go", bytes / 1073741824.0)
}
