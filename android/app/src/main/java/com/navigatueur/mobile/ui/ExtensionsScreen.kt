package com.navigatueur.mobile.ui

import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Extension
import androidx.compose.material.icons.rounded.MoreVert
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.NavigatueurApp
import com.navigatueur.mobile.browser.ExtensionsManager
import com.navigatueur.mobile.browser.InstalledExtension
import com.navigatueur.mobile.ui.theme.Nv

/** Installed Firefox extensions: the catalog Navigatueur installs, plus any the user added from addons.mozilla.org. */
@Composable
fun ExtensionsScreen(app: NavigatueurApp, state: AppUiState) {
    val colors = Nv.colors
    val manager = app.extensions
    val missing = ExtensionsManager.catalog.filter { entry -> manager.installed.none { it.id == entry.id } }

    ScreenColumn {
        ScreenHeader("Extensions", onBack = { state.screen = Screen.Browser })
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).navigationBarsPadding().padding(bottom = 24.dp)) {
            Text(
                "De vraies extensions Firefox, installées depuis addons.mozilla.org et mises à jour automatiquement. Pour en ajouter une autre, ouvrez sa page sur addons.mozilla.org et touchez « Ajouter à Firefox ».",
                color = colors.textSecondary, fontSize = 14.sp, lineHeight = 20.sp, modifier = Modifier.padding(horizontal = 20.dp, vertical = 14.dp),
            )

            if (manager.installed.isNotEmpty()) {
                Section("Installées") {
                    manager.installed.forEachIndexed { index, item ->
                        if (index > 0) RowDivider()
                        ExtensionRow(app, item, state)
                    }
                }
            }

            if (missing.isNotEmpty()) {
                Section("Disponibles", "Fournies avec Navigatueur, pas encore installées.") {
                    missing.forEachIndexed { index, entry ->
                        if (index > 0) RowDivider()
                        val busy = entry.id in manager.installing
                        SettingRow(entry.name, entry.description, icon = Icons.Rounded.Extension) {
                            if (busy) CircularProgressIndicator(Modifier.size(22.dp), color = colors.accent, strokeWidth = 2.dp)
                            else TextButton(onClick = { manager.reinstall(entry) }) { Text("Installer", color = colors.accent) }
                        }
                    }
                }
            }

            TextButton(
                onClick = {
                    val tab = app.tabs.newTab("https://addons.mozilla.org/android/", private = false)
                    app.tabs.selectTab(tab)
                    state.screen = Screen.Browser
                },
                modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
            ) { Text("Parcourir addons.mozilla.org", color = colors.accent) }
        }
    }
}

@Composable
private fun ExtensionRow(app: NavigatueurApp, item: InstalledExtension, state: AppUiState) {
    val colors = Nv.colors
    val manager = app.extensions
    var menu by remember { mutableStateOf(false) }
    val action = manager.actions[item.id]
    Row(Modifier.fillMaxWidth().padding(start = 16.dp, end = 4.dp, top = 12.dp, bottom = 12.dp), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(38.dp).clip(Nv.shape(10.dp)).background(colors.surface), contentAlignment = Alignment.Center) {
            val icon = action?.icon
            if (icon != null) Image(icon.asImageBitmap(), null, Modifier.size(24.dp)) else Icon(Icons.Rounded.Extension, null, tint = colors.textSecondary)
        }
        Spacer(Modifier.width(14.dp))
        Column(Modifier.weight(1f)) {
            Text(item.name, color = colors.text, fontSize = 15.sp, fontWeight = FontWeight.Medium)
            Text(item.description, color = colors.textSecondary, fontSize = 13.sp, lineHeight = 17.sp, maxLines = 3)
            item.catalog?.note?.let { Text(it, color = colors.textTertiary, fontSize = 12.sp, lineHeight = 16.sp, modifier = Modifier.padding(top = 2.dp)) }
            Text("Version ${item.version}", color = colors.textTertiary, fontSize = 12.sp, modifier = Modifier.padding(top = 2.dp))
        }
        Switch(
            checked = item.enabled,
            onCheckedChange = { enabled ->
                if (item.id == ExtensionsManager.DARK_READER_ID) manager.setForceDark(enabled) else manager.setEnabled(item, enabled)
            },
            colors = SwitchDefaults.colors(checkedTrackColor = colors.accent, checkedThumbColor = colors.accentForeground, uncheckedTrackColor = colors.surfaceActive, uncheckedBorderColor = Color.Transparent),
        )
        Box {
            IconButton(onClick = { menu = true }) { Icon(Icons.Rounded.MoreVert, "Options", tint = colors.textTertiary) }
            DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
                item.optionsUrl?.let { url ->
                    DropdownMenuItem(text = { Text("Réglages de l'extension") }, onClick = {
                        menu = false
                        app.tabs.newTab(url, private = false)
                        state.screen = Screen.Browser
                    })
                }
                DropdownMenuItem(text = { Text("Supprimer") }, onClick = { menu = false; manager.uninstall(item) })
            }
        }
    }
}
