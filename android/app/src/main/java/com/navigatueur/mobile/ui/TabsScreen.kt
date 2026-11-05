package com.navigatueur.mobile.ui

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyGridScope
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Bedtime
import androidx.compose.material.icons.rounded.Close
import androidx.compose.material.icons.rounded.ExpandLess
import androidx.compose.material.icons.rounded.ExpandMore
import androidx.compose.material.icons.rounded.MoreVert
import androidx.compose.material.icons.rounded.PushPin
import androidx.compose.material.icons.rounded.VisibilityOff
import androidx.compose.material.icons.rounded.VolumeUp
import androidx.compose.material.icons.rounded.Tab
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.browser.BrowserTab
import com.navigatueur.mobile.browser.TabGroup
import com.navigatueur.mobile.browser.TabsController
import com.navigatueur.mobile.ui.theme.Nv
import com.navigatueur.mobile.ui.theme.WordmarkStyle
import com.navigatueur.mobile.ui.theme.parseHexColor

/**
 * Every open tab as a card with a picture of its page (the Windows app's
 * hover previews, always on here), pinned tabs first, then groups — named,
 * colored and foldable like in the Windows tab column.
 */
@Composable
fun TabsScreen(controller: TabsController, state: AppUiState) {
    val colors = Nv.colors
    val private = state.tabsShowPrivate
    val visible = controller.tabs.filter { it.isPrivate == private }
    var renaming by remember { mutableStateOf<TabGroup?>(null) }
    var menu by remember { mutableStateOf(false) }

    fun open(tab: BrowserTab) {
        controller.selectTab(tab)
        state.screen = Screen.Browser
    }

    Box(Modifier.fillMaxSize().background(colors.window)) {
        Column(Modifier.fillMaxSize()) {
            Row(
                Modifier.fillMaxWidth().background(colors.panel).statusBarsPadding().height(60.dp).padding(start = 14.dp, end = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Segmented(
                    options = listOf(
                        "normal" to "Onglets · ${controller.tabs.count { !it.isPrivate }}",
                        "private" to "Privés · ${controller.tabs.count { it.isPrivate }}",
                    ),
                    selected = if (private) "private" else "normal",
                    onSelect = { state.tabsShowPrivate = it == "private" },
                )
                Spacer(Modifier.weight(1f))
                IconButton(onClick = { state.screen = Screen.Browser }) {
                    Icon(Icons.Rounded.Close, "Fermer la vue des onglets", tint = colors.text)
                }
                Box {
                    IconButton(onClick = { menu = true }) { Icon(Icons.Rounded.MoreVert, "Plus", tint = colors.text) }
                    DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                        if (controller.canReopenClosedTab) {
                            DropdownMenuItem(text = { Text("Rouvrir l'onglet fermé") }, onClick = {
                                menu = false
                                controller.reopenClosedTab()
                                state.screen = Screen.Browser
                            })
                        }
                        DropdownMenuItem(text = { Text(if (private) "Fermer les onglets privés" else "Fermer tous les onglets") }, onClick = {
                            menu = false
                            controller.closeAll(private)
                            if (!private) state.screen = Screen.Browser
                        })
                    }
                }
            }
            Box(Modifier.fillMaxWidth().height(1.dp).background(colors.panelBorder))

            if (visible.isEmpty()) {
                EmptyState(
                    if (private) Icons.Rounded.VisibilityOff else Icons.Rounded.Tab,
                    if (private) "Aucun onglet privé" else "Aucun onglet",
                    if (private) "Les onglets privés ne gardent ni historique, ni cookies, ni données des sites." else "Ouvrez-en un avec le bouton ci-dessous.",
                )
            } else {
                LazyVerticalGrid(
                    columns = GridCells.Adaptive(158.dp),
                    contentPadding = PaddingValues(start = 12.dp, end = 12.dp, top = 12.dp, bottom = 110.dp),
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                    modifier = Modifier.fillMaxSize(),
                ) {
                    val pinned = visible.filter { it.isPinned }
                    if (pinned.isNotEmpty()) {
                        header("pinned") { SectionLabel("Épinglés") }
                        tabCards(pinned, controller, ::open)
                    }
                    val loose = visible.filter { !it.isPinned && it.groupId == null }
                    if (loose.isNotEmpty() && (pinned.isNotEmpty() || controller.groups.isNotEmpty())) header("loose") { SectionLabel("Onglets") }
                    tabCards(loose, controller, ::open)

                    controller.groups.forEach { group ->
                        val members = visible.filter { it.groupId == group.id && !it.isPinned }
                        if (members.isEmpty()) return@forEach
                        header("group-${group.id}") { GroupHeader(group, members.size, controller, onRename = { renaming = group }, onNewTab = {
                            val tab = controller.newTab(private = private, groupId = group.id)
                            state.screen = Screen.Browser
                            state.openAddress(tab)
                        }) }
                        if (!group.isCollapsed) tabCards(members, controller, ::open)
                    }
                }
            }
        }

        ExtendedFloatingActionButton(
            onClick = {
                val tab = controller.newTab(private = private)
                state.screen = Screen.Browser
                state.openAddress(tab)
            },
            icon = { Icon(Icons.Rounded.Add, null) },
            text = { Text(if (private) "Onglet privé" else "Nouvel onglet") },
            containerColor = colors.accent,
            contentColor = colors.accentForeground,
            modifier = Modifier.align(Alignment.BottomCenter).navigationBarsPadding().padding(bottom = 22.dp),
        )
    }

    renaming?.let { group ->
        var name by remember(group) { mutableStateOf(group.name) }
        AlertDialog(
            onDismissRequest = { renaming = null },
            title = { Text("Renommer le groupe") },
            text = { OutlinedTextField(value = name, onValueChange = { name = it }, singleLine = true) },
            confirmButton = { TextButton(onClick = { controller.renameGroup(group, name); renaming = null }) { Text("Renommer") } },
            dismissButton = { TextButton(onClick = { renaming = null }) { Text("Annuler") } },
        )
    }
}

private fun LazyGridScope.header(key: String, content: @Composable () -> Unit) {
    item(key = key, span = { GridItemSpan(maxLineSpan) }) { content() }
}

private fun LazyGridScope.tabCards(tabs: List<BrowserTab>, controller: TabsController, open: (BrowserTab) -> Unit) {
    items(tabs, key = { it.id }) { tab -> TabCard(tab, controller, open) }
}

@Composable
private fun SectionLabel(text: String) {
    Text(text.uppercase(), color = Nv.colors.textTertiary, fontSize = 11.sp, letterSpacing = 1.2.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(start = 4.dp, top = 4.dp))
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun TabCard(tab: BrowserTab, controller: TabsController, open: (BrowserTab) -> Unit) {
    val colors = Nv.colors
    val active = tab.id == controller.activeTabId
    val group = controller.groups.firstOrNull { it.id == tab.groupId }
    val groupColor = group?.let { parseHexColor(it.colorHex) }
    var menu by remember { mutableStateOf(false) }

    Box {
        Column(
            Modifier
                .clip(RoundedCornerShape(16.dp))
                .background(colors.panel)
                .border(
                    width = if (active) 2.dp else 1.dp,
                    color = when {
                        active -> colors.accent
                        groupColor != null -> groupColor.copy(alpha = 0.7f)
                        else -> colors.panelBorder
                    },
                    shape = RoundedCornerShape(16.dp),
                )
                .combinedClickable(onClick = { open(tab) }, onLongClick = { menu = true }),
        ) {
            Row(Modifier.fillMaxWidth().padding(start = 10.dp, top = 6.dp, bottom = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                FaviconImage(tab.favicon, 16.dp)
                Spacer(Modifier.width(8.dp))
                Text(tab.displayTitle, color = colors.text, fontSize = 13.sp, fontWeight = FontWeight.Medium, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                Box(Modifier.size(32.dp).clip(CircleShape).clickable { controller.closeTab(tab) }, contentAlignment = Alignment.Center) {
                    Icon(Icons.Rounded.Close, "Fermer l'onglet", tint = colors.textSecondary, modifier = Modifier.size(16.dp))
                }
            }
            Box(Modifier.fillMaxWidth().aspectRatio(0.78f).background(colors.surface)) {
                val thumbnail = tab.thumbnail
                when {
                    tab.isHome -> Text("NAVIGATUEUR", style = WordmarkStyle.copy(fontSize = 9.sp, letterSpacing = 3.sp), color = colors.textTertiary, modifier = Modifier.align(Alignment.Center))
                    thumbnail != null -> Image(thumbnail.asImageBitmap(), null, contentScale = ContentScale.Crop, alignment = Alignment.TopCenter, modifier = Modifier.fillMaxSize())
                    else -> FaviconImage(tab.favicon, 40.dp, Modifier.align(Alignment.Center))
                }
                Row(Modifier.align(Alignment.BottomStart).padding(8.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    if (tab.isPinned) Badge(Icons.Rounded.PushPin)
                    if (tab.isMediaPlaying) Badge(Icons.Rounded.VolumeUp)
                    if (!tab.isLive && !tab.isHome && !active) Badge(Icons.Rounded.Bedtime)
                }
            }
        }

        DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
            DropdownMenuItem(text = { Text(if (tab.isPinned) "Détacher" else "Épingler") }, onClick = { menu = false; controller.togglePin(tab) })
            DropdownMenuItem(text = { Text("Nouveau groupe avec cet onglet") }, onClick = { menu = false; controller.createGroup(tab) })
            controller.groups.filter { it.id != tab.groupId }.forEach { g ->
                DropdownMenuItem(
                    text = { Text("Ajouter à « ${g.name} »") },
                    leadingIcon = { ColorDot(parseHexColor(g.colorHex)) },
                    onClick = { menu = false; controller.addToGroup(tab, g) },
                )
            }
            if (tab.groupId != null) DropdownMenuItem(text = { Text("Retirer du groupe") }, onClick = { menu = false; controller.removeFromGroup(tab) })
            HorizontalDivider()
            DropdownMenuItem(text = { Text("Dupliquer") }, onClick = { menu = false; controller.duplicate(tab) })
            DropdownMenuItem(text = { Text("Fermer les autres onglets") }, onClick = { menu = false; controller.closeOthers(tab) })
            DropdownMenuItem(text = { Text("Fermer") }, onClick = { menu = false; controller.closeTab(tab) })
        }
    }
}

@Composable
private fun Badge(icon: androidx.compose.ui.graphics.vector.ImageVector) {
    Box(
        Modifier.size(24.dp).clip(CircleShape).background(Color.Black.copy(alpha = 0.55f)),
        contentAlignment = Alignment.Center,
    ) {
        Icon(icon, null, tint = Color.White, modifier = Modifier.size(14.dp))
    }
}

@Composable
private fun GroupHeader(group: TabGroup, count: Int, controller: TabsController, onRename: () -> Unit, onNewTab: () -> Unit) {
    val colors = Nv.colors
    val color = parseHexColor(group.colorHex)
    var menu by remember { mutableStateOf(false) }
    Row(
        Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(color.copy(alpha = 0.14f))
            .clickable { controller.toggleGroupCollapsed(group) }
            .padding(start = 12.dp, end = 2.dp, top = 2.dp, bottom = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        ColorDot(color, 12.dp)
        Spacer(Modifier.width(10.dp))
        Text(group.name, color = colors.text, fontWeight = FontWeight.SemiBold, fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
        Spacer(Modifier.width(8.dp))
        Text("$count", color = colors.textSecondary, fontSize = 13.sp)
        Spacer(Modifier.weight(1f))
        Icon(if (group.isCollapsed) Icons.Rounded.ExpandMore else Icons.Rounded.ExpandLess, if (group.isCollapsed) "Déplier" else "Replier", tint = colors.textSecondary)
        Box {
            IconButton(onClick = { menu = true }) { Icon(Icons.Rounded.MoreVert, "Options du groupe", tint = colors.textSecondary) }
            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                DropdownMenuItem(text = { Text("Nouvel onglet dans le groupe") }, onClick = { menu = false; onNewTab() })
                DropdownMenuItem(text = { Text("Renommer") }, onClick = { menu = false; onRename() })
                Row(Modifier.padding(horizontal = 12.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    TabGroup.colors.forEach { (name, hex) ->
                        Box(
                            Modifier
                                .size(26.dp)
                                .clip(CircleShape)
                                .background(parseHexColor(hex))
                                .border(if (hex == group.colorHex) 2.dp else 0.dp, colors.text, CircleShape)
                                .clickable { controller.recolorGroup(group, hex); menu = false },
                        )
                    }
                }
                DropdownMenuItem(text = { Text("Dégrouper") }, onClick = { menu = false; controller.ungroup(group) })
                DropdownMenuItem(text = { Text("Fermer le groupe") }, onClick = { menu = false; controller.closeGroup(group) })
            }
        }
    }
}
