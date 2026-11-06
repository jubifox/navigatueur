package com.navigatueur.mobile.ui

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.rounded.NorthWest
import androidx.compose.material.icons.rounded.Bookmark
import androidx.compose.material.icons.rounded.Cancel
import androidx.compose.material.icons.rounded.ContentCopy
import androidx.compose.material.icons.rounded.ContentPaste
import androidx.compose.material.icons.rounded.History
import androidx.compose.material.icons.rounded.Mic
import androidx.compose.material.icons.rounded.Public
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.Share
import androidx.compose.material.icons.rounded.Tab
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.NavigatueurApp
import com.navigatueur.mobile.core.AddressSuggestions
import com.navigatueur.mobile.core.SearchEngines
import com.navigatueur.mobile.core.SearchSuggestions
import com.navigatueur.mobile.core.SuggestionKind
import com.navigatueur.mobile.core.SuggestionSource
import com.navigatueur.mobile.core.UrlHelper
import com.navigatueur.mobile.ui.theme.Nv
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext

/** Typing an address or a search: suggestions from open tabs, favorites and history, like the Windows address bar's dropdown. */
@Composable
fun AddressScreen(app: NavigatueurApp, state: AppUiState, actions: ActivityActions) {
    val controller = app.tabs
    val colors = Nv.colors
    val context = LocalContext.current
    val settings by app.stores.settings.flow.collectAsState()
    val engine = SearchEngines.byId(settings.searchEngine)
    val tab = state.addressTab
    val initial = remember(state.addressTab, state.addressInNewTab) { state.addressText }
    var field by remember(state.addressTab, state.addressInNewTab) { mutableStateOf(TextFieldValue(initial, TextRange(0, initial.length))) }
    val focus = remember { FocusRequester() }
    LaunchedEffect(state.addressTab, state.addressInNewTab) { focus.requestFocus() }

    val query = field.text.trim()
    val suggestions = remember(query) {
        val tabsSources = controller.tabs.asSequence().filter { !it.isPrivate && it !== tab && UrlHelper.isWebUrl(it.url) }
            .map { SuggestionSource(it.url, it.displayTitle, SuggestionKind.Tab) }
        if (query.isEmpty()) emptyList() else AddressSuggestions.rank(query, tabsSources + app.stores.suggestionSources(), limit = 6)
    }
    val looksLikeAddress = query.isNotEmpty() && !query.contains(' ') && (query.contains('.') || query.contains(':') || query.contains('/'))

    // What the search engine suggests, asked a moment after typing stops (never for private tabs).
    var engineSuggestions by remember { mutableStateOf<List<String>>(emptyList()) }
    val askEngine = settings.searchSuggestions && !state.addressPrivate
    LaunchedEffect(query, engine.id, askEngine) {
        if (!askEngine || query.length < 2 || looksLikeAddress) {
            engineSuggestions = emptyList()
            return@LaunchedEffect
        }
        delay(180)
        engineSuggestions = withContext(Dispatchers.IO) { SearchSuggestions.fetch(engine.id, query, limit = 5) }
    }

    /** Puts [text] where the cursor is (the keys above the keyboard). */
    fun insert(text: String) {
        val start = field.selection.min
        val end = field.selection.max
        val updated = field.text.replaceRange(start, end, text)
        field = TextFieldValue(updated, TextRange(start + text.length))
    }

    fun go(input: String, engineOverride: String? = null) {
        val prefix = SearchEngines.byId(engineOverride ?: settings.searchEngine).queryPrefix
        val url = UrlHelper.normalize(input, prefix)
        if (url.isEmpty()) return
        val target = if (state.addressInNewTab || tab == null) controller.newTab(private = state.addressPrivate) else tab
        controller.loadUrl(target, url)
        state.closeAddress()
        state.screen = Screen.Browser
    }

    ScreenBackground(Modifier.fillMaxSize()) {
        Column(
            Modifier
                .fillMaxSize()
                .statusBarsPadding()
                .imePadding()
        ) {
            Row(
                Modifier.fillMaxWidth().padding(horizontal = 6.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                IconButton(onClick = { state.closeAddress() }) {
                    Icon(Icons.AutoMirrored.Rounded.ArrowBack, "Retour", tint = colors.text)
                }
                Row(
                    Modifier.weight(1f).height(46.dp).clip(Nv.shape(14.dp)).background(colors.surface).padding(start = 14.dp, end = 4.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    BasicTextField(
                        value = field,
                        onValueChange = { field = it },
                        singleLine = true,
                        textStyle = TextStyle(color = colors.text, fontSize = 16.sp),
                        cursorBrush = SolidColor(colors.accent),
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Go, autoCorrectEnabled = false),
                        keyboardActions = KeyboardActions(onGo = { go(field.text) }),
                        modifier = Modifier.weight(1f).focusRequester(focus),
                        decorationBox = { inner ->
                            Box(contentAlignment = Alignment.CenterStart) {
                                if (field.text.isEmpty()) {
                                    Text(if (state.addressPrivate) "Recherche privée ou adresse" else "Rechercher ou saisir une adresse", color = colors.textTertiary, fontSize = 16.sp)
                                }
                                inner()
                            }
                        },
                    )
                    if (field.text.isNotEmpty()) {
                        IconButton(onClick = { field = TextFieldValue("") }) {
                            Icon(Icons.Rounded.Cancel, "Effacer", tint = colors.textTertiary, modifier = Modifier.size(20.dp))
                        }
                    } else if (actions.canVoiceSearch) {
                        IconButton(onClick = { actions.voiceSearch { spoken -> go(spoken) } }) {
                            Icon(Icons.Rounded.Mic, "Recherche vocale", tint = colors.textSecondary, modifier = Modifier.size(21.dp))
                        }
                    }
                }
                Spacer(Modifier.width(8.dp))
            }

            // Search once with another engine.
            if (query.isNotEmpty()) {
                Row(
                    Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 14.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    SearchEngines.all.forEach { candidate ->
                        val selected = candidate.id == engine.id
                        Text(
                            candidate.displayName,
                            color = if (selected) colors.accentForeground else colors.textSecondary,
                            fontSize = 13.sp,
                            fontWeight = FontWeight.Medium,
                            modifier = Modifier
                                .clip(Nv.shape(20.dp))
                                .background(if (selected) colors.accent else colors.surface)
                                .clickable { go(field.text, candidate.id) }
                                .padding(horizontal = 14.dp, vertical = 7.dp),
                        )
                    }
                }
                Spacer(Modifier.height(8.dp))
            }

            LazyColumn(Modifier.fillMaxWidth().weight(1f)) {
                if (query.isEmpty()) {
                    val current = tab?.takeIf { !it.isHome && !state.addressInNewTab }
                    if (current != null) {
                        item {
                            QuickRow(Icons.Rounded.ContentCopy, "Copier l'adresse", current.url) {
                                (context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("URL", current.url))
                                state.closeAddress()
                            }
                        }
                        item { QuickRow(Icons.Rounded.Share, "Partager", null) { actions.share(current.displayTitle, null, current.url); state.closeAddress() } }
                    }
                    val clip = (context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).primaryClip
                        ?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.coerceToText(context)?.toString()?.trim()?.take(500)
                    if (!clip.isNullOrBlank() && clip != current?.url) {
                        item { QuickRow(Icons.Rounded.ContentPaste, "Coller et ouvrir", clip) { go(clip) } }
                    }
                } else {
                    val isUrl = UrlHelper.normalize(query, engine.queryPrefix).let { !it.startsWith(engine.queryPrefix) }
                    item {
                        SuggestionRow(
                            icon = if (isUrl) Icons.Rounded.Public else Icons.Rounded.Search,
                            title = if (isUrl) query else "Rechercher « $query »",
                            subtitle = if (isUrl) "Aller à l'adresse" else "avec ${engine.displayName}",
                            accent = true,
                            onClick = { go(field.text) },
                            onFill = null,
                        )
                    }
                    items(suggestions, key = { it.url + it.kind }) { suggestion ->
                        SuggestionRow(
                            icon = when (suggestion.kind) {
                                SuggestionKind.Bookmark -> Icons.Rounded.Bookmark
                                SuggestionKind.History -> Icons.Rounded.History
                                SuggestionKind.Tab -> Icons.Rounded.Tab
                            },
                            title = suggestion.title.ifBlank { UrlHelper.displayHost(suggestion.url) ?: suggestion.url },
                            subtitle = if (suggestion.kind == SuggestionKind.Tab) "Basculer vers l'onglet · ${UrlHelper.displayHost(suggestion.url)}" else suggestion.url.removePrefix("https://").removePrefix("www."),
                            accent = false,
                            onClick = {
                                val open = if (suggestion.kind == SuggestionKind.Tab) controller.tabs.firstOrNull { it.url == suggestion.url && !it.isPrivate } else null
                                if (open != null) {
                                    controller.selectTab(open)
                                    state.closeAddress()
                                    state.screen = Screen.Browser
                                } else {
                                    go(suggestion.url)
                                }
                            },
                            onFill = { field = TextFieldValue(suggestion.url, TextRange(suggestion.url.length)) },
                        )
                    }
                    if (engineSuggestions.isNotEmpty()) {
                        item(key = "engine-header") {
                            Text(
                                "SUGGESTIONS DE ${engine.displayName.uppercase()}",
                                color = colors.textTertiary, fontSize = 11.sp, letterSpacing = 1.2.sp, fontWeight = FontWeight.SemiBold,
                                modifier = Modifier.padding(start = 22.dp, top = 12.dp, bottom = 2.dp),
                            )
                        }
                        items(engineSuggestions, key = { "engine-$it" }) { text ->
                            SuggestionRow(
                                icon = Icons.Rounded.Search,
                                title = text,
                                subtitle = null,
                                accent = false,
                                onClick = { go(text) },
                                onFill = { field = TextFieldValue("$text ", TextRange(text.length + 1)) },
                            )
                        }
                    }
                }
            }

            // Keys that are a pain to type on a phone keyboard.
            Row(
                Modifier
                    .fillMaxWidth()
                    .background(colors.sheet)
                    .horizontalScroll(rememberScrollState())
                    .padding(horizontal = 10.dp, vertical = 6.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                listOf("www.", ".com", ".fr", ".org", "/", "https://").forEach { key ->
                    Text(
                        key,
                        color = colors.text,
                        fontSize = 15.sp,
                        fontWeight = FontWeight.Medium,
                        modifier = Modifier
                            .clip(Nv.shape(10.dp))
                            .background(colors.surface.copy(alpha = 1f))
                            .clickable(onClickLabel = "Insérer $key") { insert(key) }
                            .padding(horizontal = 14.dp, vertical = 8.dp),
                    )
                }
            }
        }
    }
}

@Composable
private fun QuickRow(icon: androidx.compose.ui.graphics.vector.ImageVector, title: String, subtitle: String?, onClick: () -> Unit) {
    SuggestionRow(icon, title, subtitle, accent = false, onClick = onClick, onFill = null)
}

@Composable
private fun SuggestionRow(
    icon: androidx.compose.ui.graphics.vector.ImageVector,
    title: String,
    subtitle: String?,
    accent: Boolean,
    onClick: () -> Unit,
    onFill: (() -> Unit)?,
) {
    val colors = Nv.colors
    Row(
        Modifier.fillMaxWidth().clickable(onClick = onClick).padding(start = 20.dp, end = 6.dp, top = 10.dp, bottom = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(
            Modifier.size(34.dp).clip(Nv.shape(10.dp)).background(if (accent) colors.accentSoft else colors.surface),
            contentAlignment = Alignment.Center,
        ) {
            Icon(icon, null, tint = if (accent) colors.accent else colors.textSecondary, modifier = Modifier.size(18.dp))
        }
        Spacer(Modifier.width(14.dp))
        Column(Modifier.weight(1f)) {
            Text(title, color = colors.text, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            if (subtitle != null) Text(subtitle, color = colors.textTertiary, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        if (onFill != null) {
            IconButton(onClick = onFill) {
                Icon(Icons.Rounded.NorthWest, "Compléter", tint = colors.textTertiary, modifier = Modifier.size(18.dp))
            }
        }
    }
}
