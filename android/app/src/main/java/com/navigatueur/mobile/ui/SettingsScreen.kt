package com.navigatueur.mobile.ui

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.background
import androidx.compose.foundation.border
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
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.KeyboardArrowRight
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.Palette
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CheckboxDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedTextField
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
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.BuildConfig
import com.navigatueur.mobile.NavigatueurApp
import com.navigatueur.mobile.core.SearchEngines
import com.navigatueur.mobile.core.ThemePresets
import com.navigatueur.mobile.core.UrlHelper
import com.navigatueur.mobile.data.AppSettings
import com.navigatueur.mobile.engine.Engine
import com.navigatueur.mobile.ui.theme.AccentChoices
import com.navigatueur.mobile.ui.theme.Nv
import com.navigatueur.mobile.ui.theme.parseHexColor
import org.mozilla.geckoview.StorageController
import java.io.File

/** Settings, in the Windows Settings page's order: Apparence, Interface, Navigation, Confidentialité, Vidéo, Performances, À propos. */
@Composable
fun SettingsScreen(app: NavigatueurApp, state: AppUiState, actions: ActivityActions) {
    val colors = Nv.colors
    val stores = app.stores
    val settings by stores.settings.flow.collectAsState()
    var clearDialog by remember { mutableStateOf(false) }
    var homeDialog by remember { mutableStateOf(false) }

    fun update(change: (AppSettings) -> AppSettings) {
        stores.settings.update(change)
        Engine.apply(stores.settings.value)
    }

    ScreenColumn {
        ScreenHeader("Paramètres", onBack = { state.screen = Screen.Browser })
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).navigationBarsPadding().padding(bottom = 24.dp)) {

            Section("Apparence", "Couleurs, fond d'écran, barre d'outils, formes, page d'accueil.") {
                SettingRow(
                    "Personnaliser",
                    ThemePresets.byId(settings.appearance.themeId)?.let { "Thème « ${it.name} »" } ?: "Apparence sur mesure",
                    icon = Icons.Rounded.Palette,
                    onClick = { state.openCustomize(Screen.Settings) },
                ) {
                    Icon(Icons.AutoMirrored.Rounded.KeyboardArrowRight, null, tint = colors.textTertiary)
                }
                RowDivider()
                SettingRow("Thème") {
                    Segmented(listOf("Dark" to "Sombre", "Light" to "Clair", "System" to "Auto"), settings.themeMode, { mode -> update { it.copy(themeMode = mode) } })
                }
                RowDivider()
                SwitchRow(
                    "Mode sombre pour tous les sites",
                    "Assombrit les pages qui n'ont pas de thème sombre (avec Dark Reader). Réglable site par site depuis le bouton Dark Reader du menu.",
                    checked = settings.forceDarkSites,
                ) { app.extensions.setForceDark(it) }
            }

            Section("Interface", "Gestes et comportement de la barre d'outils.") {
                SwitchRow("Masquer la barre en lisant", "Elle s'efface quand on fait défiler la page vers le bas et revient vers le haut.", checked = settings.hideToolbarOnScroll) { v ->
                    update { it.copy(hideToolbarOnScroll = v) }
                }
                RowDivider()
                SwitchRow(
                    "Gestes sur la barre d'outils",
                    if (settings.toolbarPosition == "Top") "Glisser vers la gauche ou la droite : onglet voisin. Vers le bas : tous les onglets."
                    else "Glisser vers la gauche ou la droite : onglet voisin. Vers le haut : tous les onglets.",
                    checked = settings.toolbarGestures,
                ) { v -> update { it.copy(toolbarGestures = v) } }
                RowDivider()
                SwitchRow("Tirer pour actualiser", "En haut d'une page, tirez vers le bas pour la recharger.", checked = settings.pullToRefresh) { v ->
                    update { it.copy(pullToRefresh = v) }
                }
            }

            Section("Navigation", "Démarrage, page d'accueil et recherche.") {
                SettingRow("Au démarrage") {
                    Segmented(listOf("RestoreSession" to "Reprendre", "HomePage" to "Accueil"), settings.startupBehavior, { v -> update { it.copy(startupBehavior = v) } })
                }
                RowDivider()
                SettingRow("Page d'accueil", settings.homePageUrl.ifBlank { "Page Navigatueur" }, onClick = { homeDialog = true })
                RowDivider()
                SwitchRow(
                    "Suggestions de recherche",
                    "Le moteur choisi propose des recherches pendant la saisie (jamais dans les onglets privés).",
                    checked = settings.searchSuggestions,
                ) { v -> update { it.copy(searchSuggestions = v) } }
                RowDivider()
                Column(Modifier.padding(horizontal = 16.dp, vertical = 13.dp)) {
                    Text("Moteur de recherche", color = colors.text, fontSize = 15.sp)
                    Text("Pour la barre d'adresse et la page d'accueil.", color = colors.textSecondary, fontSize = 13.sp)
                    Spacer(Modifier.height(10.dp))
                    Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        SearchEngines.all.forEach { engine ->
                            val selected = engine.id == settings.searchEngine
                            Text(
                                engine.displayName,
                                color = if (selected) colors.accentForeground else colors.textSecondary,
                                fontSize = 14.sp,
                                modifier = Modifier
                                    .clip(androidx.compose.foundation.shape.RoundedCornerShape(20.dp))
                                    .background(if (selected) colors.accent else colors.surface)
                                    .clickable { update { it.copy(searchEngine = engine.id) } }
                                    .padding(horizontal = 14.dp, vertical = 8.dp),
                            )
                        }
                    }
                }
            }

            Section("Confidentialité et sécurité", "Ce que Navigatueur bloque, et ce qu'il retient.") {
                SettingRow(
                    "Bloqueur de publicités",
                    if (app.extensions.installed.any { it.id == "uBlock0@raymondhill.net" && it.enabled }) "uBlock Origin est actif. Désactivable site par site depuis son bouton dans le menu." else "uBlock Origin est désactivé ou pas encore installé.",
                    onClick = { state.screen = Screen.Extensions },
                )
                RowDivider()
                SwitchRow("Protection contre le pistage", "Bloque les traqueurs connus et isole les cookies de chaque site.", checked = settings.trackingProtection) { v -> update { it.copy(trackingProtection = v) } }
                RowDivider()
                SwitchRow("Protection contre l'hameçonnage", "Avertit avant d'ouvrir un site frauduleux connu.", checked = settings.phishingProtection) { v -> update { it.copy(phishingProtection = v) } }
                RowDivider()
                SwitchRow("HTTPS uniquement", "N'ouvre que des connexions chiffrées, et prévient quand un site n'en propose pas.", checked = settings.httpsOnly) { v -> update { it.copy(httpsOnly = v) } }
                RowDivider()
                SwitchRow("Demander de ne pas être suivi", "Envoie le signal Global Privacy Control aux sites.", checked = settings.globalPrivacyControl) { v -> update { it.copy(globalPrivacyControl = v) } }
                RowDivider()
                val remembered = stores.permissions.flow.collectAsState().value.size
                SettingRow("Autorisations des sites", if (remembered == 0) "Aucune décision mémorisée" else "$remembered décision${if (remembered > 1) "s" else ""} mémorisée${if (remembered > 1) "s" else ""}") {
                    if (remembered > 0) TextButton(onClick = { stores.permissions.set(emptyList()) }) { Text("Réinitialiser", color = colors.accent) }
                }
                RowDivider()
                SettingRow("Effacer les données de navigation", "Historique, cookies, cache…", onClick = { clearDialog = true })
            }

            Section("Vidéo et musique") {
                SwitchRow("Image dans l'image automatique", "Quand vous quittez l'application pendant une vidéo, elle continue dans une petite fenêtre.", checked = settings.autoPictureInPicture) { v ->
                    update { it.copy(autoPictureInPicture = v) }
                }
                RowDivider()
                SwitchRow("Lecture en arrière-plan", "La musique et le son des vidéos continuent écran éteint, avec les commandes dans les notifications.", checked = settings.backgroundPlayback) { v ->
                    update { it.copy(backgroundPlayback = v) }
                }
            }

            Section("Performances", "Les onglets en arrière-plan sont mis en veille pour libérer la mémoire ; ils se rechargent quand vous y revenez.") {
                ChoiceRow("Onglets actifs en même temps", "Au-delà, le moins récemment utilisé est mis en veille (sauf onglets épinglés ou qui jouent du son).",
                    listOf(2, 3, 4, 6, 8).map { it to it.toString() }, settings.maxLiveTabs) { v -> update { it.copy(maxLiveTabs = v) } }
                RowDivider()
                ChoiceRow("Mettre en veille les onglets inactifs après", null,
                    listOf(0 to "Jamais", 5 to "5 minutes", 15 to "15 minutes", 30 to "30 minutes", 60 to "1 heure"), settings.suspendMinutes) { v -> update { it.copy(suspendMinutes = v) } }
            }

            Section("Accessibilité") {
                ChoiceRow(
                    "Taille du texte des pages",
                    "« Système » suit la taille de police d'Android.",
                    listOf(0 to "Système", 80 to "80 %", 90 to "90 %", 100 to "100 %", 110 to "110 %", 125 to "125 %", 150 to "150 %", 175 to "175 %", 200 to "200 %"),
                    settings.textScale,
                ) { v -> update { it.copy(textScale = v) } }
                RowDivider()
                SwitchRow("Zoom sur tous les sites", "Permet d'agrandir avec deux doigts, même les sites qui l'interdisent.", checked = settings.forceZoom) { v ->
                    update { it.copy(forceZoom = v) }
                }
            }

            Section("Extensions") {
                SettingRow("Gérer les extensions", "${app.extensions.installed.count { it.enabled }} activée(s) sur ${app.extensions.installed.size}", onClick = { state.screen = Screen.Extensions })
            }

            Section("À propos") {
                SettingRow("Navigatueur ${BuildConfig.VERSION_NAME}", "Moteur Gecko ${org.mozilla.geckoview.BuildConfig.MOZILLA_VERSION} (Firefox)")
                RowDivider()
                val updater = app.updater
                val release = updater.available
                when {
                    updater.downloadProgress >= 0f -> Column(Modifier.padding(16.dp)) {
                        Text("Téléchargement de la mise à jour…", color = colors.text, fontSize = 15.sp)
                        Spacer(Modifier.height(8.dp))
                        LinearProgressIndicator(progress = { updater.downloadProgress }, modifier = Modifier.fillMaxWidth(), color = colors.accent, trackColor = colors.surfaceActive)
                    }
                    release != null -> SettingRow("Version ${release.version} disponible", "Touchez pour la télécharger et l'installer (${release.size / 1_000_000} Mo).", onClick = {
                        if (!updater.canInstall()) actions.startActivitySafely(updater.installPermissionIntent())
                        else updater.downloadAndInstall { intent -> actions.startActivitySafely(intent) }
                    })
                    else -> SettingRow(
                        if (updater.checking) "Recherche de mises à jour…" else "Rechercher une mise à jour",
                        updater.lastCheckMessage,
                        onClick = { updater.check() },
                    )
                }
                RowDivider()
                SettingRow("Code source et versions", "github.com/jubifox/navigatueur", onClick = {
                    actions.startActivitySafely(Intent(Intent.ACTION_VIEW, Uri.parse("https://github.com/jubifox/navigatueur")))
                })
            }
        }
    }

    if (homeDialog) {
        var text by remember { mutableStateOf(settings.homePageUrl) }
        AlertDialog(
            onDismissRequest = { homeDialog = false },
            title = { Text("Page d'accueil") },
            text = {
                Column {
                    Text("Vide : la page Navigatueur.", color = colors.textSecondary, fontSize = 14.sp)
                    Spacer(Modifier.height(10.dp))
                    OutlinedTextField(value = text, onValueChange = { text = it }, singleLine = true, placeholder = { Text("Page Navigatueur") },
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri))
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    val url = if (text.isBlank()) "" else UrlHelper.normalize(text, SearchEngines.byId(settings.searchEngine).queryPrefix)
                    update { it.copy(homePageUrl = url) }
                    homeDialog = false
                }) { Text("Enregistrer") }
            },
            dismissButton = { TextButton(onClick = { homeDialog = false }) { Text("Annuler") } },
        )
    }

    if (clearDialog) ClearDataDialog(app, onDone = { clearDialog = false })
}

@Composable
private fun <T> ChoiceRow(title: String, subtitle: String?, options: List<Pair<T, String>>, selected: T, onSelect: (T) -> Unit) {
    var open by remember { mutableStateOf(false) }
    val colors = Nv.colors
    Box {
        SettingRow(title, subtitle, onClick = { open = true }) {
            Text(options.firstOrNull { it.first == selected }?.second ?: selected.toString(), color = colors.accent, fontSize = 15.sp)
        }
        DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
            options.forEach { (value, label) ->
                DropdownMenuItem(text = { Text(label) }, onClick = { onSelect(value); open = false })
            }
        }
    }
}

@Composable
private fun ClearDataDialog(app: NavigatueurApp, onDone: () -> Unit) {
    var history by remember { mutableStateOf(true) }
    var cookies by remember { mutableStateOf(true) }
    var cache by remember { mutableStateOf(true) }
    var permissions by remember { mutableStateOf(false) }
    var downloads by remember { mutableStateOf(false) }
    val colors = Nv.colors

    @Composable
    fun option(label: String, checked: Boolean, onChange: (Boolean) -> Unit) {
        Row(Modifier.fillMaxWidth().clickable { onChange(!checked) }.padding(vertical = 2.dp), verticalAlignment = Alignment.CenterVertically) {
            Checkbox(checked, onChange, colors = CheckboxDefaults.colors(checkedColor = colors.accent))
            Text(label, color = colors.text, fontSize = 15.sp)
        }
    }

    AlertDialog(
        onDismissRequest = onDone,
        title = { Text("Effacer les données") },
        text = {
            Column {
                option("Historique de navigation", history) { history = it }
                option("Cookies et données des sites", cookies) { cookies = it }
                option("Images et fichiers en cache", cache) { cache = it }
                option("Autorisations des sites", permissions) { permissions = it }
                option("Liste des téléchargements", downloads) { downloads = it }
            }
        },
        confirmButton = {
            TextButton(onClick = {
                val storage = Engine.runtime.storageController
                var flags = 0L
                if (cookies) flags = flags or StorageController.ClearFlags.COOKIES or StorageController.ClearFlags.DOM_STORAGES or StorageController.ClearFlags.AUTH_SESSIONS
                if (cache) flags = flags or StorageController.ClearFlags.ALL_CACHES
                if (permissions) flags = flags or StorageController.ClearFlags.PERMISSIONS
                if (flags != 0L) storage.clearData(flags)
                if (history) app.stores.history.set(emptyList())
                if (permissions) app.stores.permissions.set(emptyList())
                if (downloads) app.downloads.clearFinished()
                if (cache) app.tabs.favicons.clear()
                app.tabs.emit(com.navigatueur.mobile.browser.UiEvent.Message("Données effacées"))
                onDone()
            }) { Text("Effacer", color = colors.danger) }
        },
        dismissButton = { TextButton(onClick = onDone) { Text("Annuler") } },
    )
}
