package com.navigatueur.mobile.ui

import android.os.Build
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
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.automirrored.rounded.ArrowForward
import androidx.compose.material.icons.rounded.Add
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.Colorize
import androidx.compose.material.icons.rounded.Home
import androidx.compose.material.icons.rounded.Image
import androidx.compose.material.icons.rounded.MoreVert
import androidx.compose.material.icons.rounded.Remove
import androidx.compose.material.icons.rounded.Search
import androidx.compose.material.icons.rounded.Share
import androidx.compose.material.icons.rounded.StarBorder
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
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
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.NavigatueurApp
import com.navigatueur.mobile.core.AccentPalette
import com.navigatueur.mobile.core.Gradients
import com.navigatueur.mobile.core.ThemePreset
import com.navigatueur.mobile.core.ThemePresets
import com.navigatueur.mobile.core.WallpaperColorPalette
import com.navigatueur.mobile.core.withDefaultLook
import com.navigatueur.mobile.core.withTheme
import com.navigatueur.mobile.data.AppSettings
import com.navigatueur.mobile.data.Appearance
import com.navigatueur.mobile.data.GradientSpec
import com.navigatueur.mobile.data.ToolbarButtonIds
import com.navigatueur.mobile.data.Wallpaper
import com.navigatueur.mobile.engine.Engine
import com.navigatueur.mobile.ui.theme.GradientFill
import com.navigatueur.mobile.ui.theme.Nv
import com.navigatueur.mobile.ui.theme.WallpaperBox
import com.navigatueur.mobile.ui.theme.WordmarkStyle
import com.navigatueur.mobile.ui.theme.parseHexColor
import kotlin.math.roundToInt

/** What the color picker is choosing for. */
private sealed class ColorTarget {
    data object Accent : ColorTarget()
    data object WallpaperColor : ColorTarget()
    data class GradientStop(val index: Int) : ColorTarget()
}

/**
 * Everything about how Navigatueur looks: ready-made themes, colors, the
 * wallpaper (color, gradient, picture), the toolbar, shapes and the home
 * page — with a live preview at the top.
 */
@Composable
fun CustomizeScreen(app: NavigatueurApp, state: AppUiState, actions: ActivityActions) {
    val colors = Nv.colors
    val settings by app.stores.settings.flow.collectAsState()
    val look = settings.appearance
    val wallpaper = look.wallpaper
    var picking by remember { mutableStateOf<ColorTarget?>(null) }
    var confirmReset by remember { mutableStateOf(false) }

    /** A change of colors or wallpaper: the look is no longer one of the ready-made themes. */
    fun updateColors(change: (AppSettings) -> AppSettings) {
        app.stores.settings.update { s -> change(s).let { it.copy(appearance = it.appearance.copy(themeId = null)) } }
        Engine.apply(app.stores.settings.value)
    }

    fun updateWallpaper(change: (Wallpaper) -> Wallpaper) = updateColors { s -> s.copy(appearance = s.appearance.copy(wallpaper = change(s.appearance.wallpaper))) }

    /** Toolbar, shapes, home page: the theme stays selected. */
    fun updateLook(change: (Appearance) -> Appearance) {
        app.stores.settings.update { s -> s.copy(appearance = change(s.appearance)) }
    }

    ScreenColumn {
        ScreenHeader("Personnaliser", onBack = { state.screen = state.customizeReturn }) {
            TextButton(onClick = { confirmReset = true }) { Text("Réinitialiser", color = colors.textSecondary) }
        }
        Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).navigationBarsPadding().padding(bottom = 32.dp)) {
            LivePreview(settings)

            Section("Thèmes", "Un toucher applique les couleurs et le fond d'un thème.") {
                Row(
                    Modifier.horizontalScroll(rememberScrollState()).padding(12.dp),
                    horizontalArrangement = Arrangement.spacedBy(10.dp),
                ) {
                    ThemePresets.all.forEach { preset ->
                        ThemeCard(preset, selected = preset.id == look.themeId) {
                            app.stores.settings.update { it.withTheme(preset) }
                            Engine.apply(app.stores.settings.value)
                        }
                    }
                }
            }

            Section("Couleurs") {
                SettingRow("Thème") {
                    Segmented(listOf("Dark" to "Sombre", "Light" to "Clair", "System" to "Auto"), settings.themeMode, { mode -> updateColors { it.copy(themeMode = mode) } })
                }
                if (settings.themeMode != "Light") {
                    RowDivider()
                    SwitchRow("Noir pur", "Fond et panneaux noirs en thème sombre : parfait pour les écrans OLED.", checked = look.pureBlack) { v ->
                        updateColors { it.copy(appearance = it.appearance.copy(pureBlack = v)) }
                    }
                }
                RowDivider()
                Column(Modifier.padding(horizontal = 16.dp, vertical = 13.dp)) {
                    Text("Couleur d'accent", color = colors.text, fontSize = 15.sp, fontWeight = FontWeight.Medium)
                    Text("Boutons, liens actifs, indicateurs.", color = colors.textSecondary, fontSize = 13.sp)
                    Spacer(Modifier.height(10.dp))
                    SwatchRow(
                        swatches = AccentPalette,
                        selected = settings.accentColorHex,
                        onSelect = { hex -> updateColors { it.copy(accentColorHex = hex) } },
                        onCustom = { picking = ColorTarget.Accent },
                    )
                }
                RowDivider()
                SliderRow(
                    "Teinte de l'interface",
                    "Les panneaux prennent un peu de la couleur d'accent.",
                    value = look.tint.toFloat(), range = 0f..100f, label = "${look.tint} %",
                ) { v -> updateColors { it.copy(appearance = it.appearance.copy(tint = v.roundToInt())) } }
            }

            Section("Fond d'écran", "Sur la page d'accueil, et dans toute l'application si vous le voulez.") {
                Row(
                    Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 12.dp, vertical = 12.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    listOf("None" to "Aucun", "Glow" to "Lueur", "Color" to "Couleur", "Gradient" to "Dégradé", "Image" to "Image").forEach { (kind, label) ->
                        Chip(label, selected = wallpaper.kind == kind) {
                            if (kind == "Image" && wallpaper.imagePath == null) actions.pickBackgroundImage()
                            else updateWallpaper { it.copy(kind = kind) }
                        }
                    }
                }
                when (wallpaper.kind) {
                    "Color" -> {
                        RowDivider()
                        Column(Modifier.padding(horizontal = 16.dp, vertical = 13.dp)) {
                            SwatchRow(
                                swatches = WallpaperColorPalette,
                                selected = wallpaper.colorHex,
                                onSelect = { hex -> updateWallpaper { it.copy(colorHex = hex) } },
                                onCustom = { picking = ColorTarget.WallpaperColor },
                            )
                        }
                    }
                    "Gradient" -> {
                        RowDivider()
                        GradientChoices(wallpaper.gradient) { spec -> updateWallpaper { it.copy(gradient = spec) } }
                        RowDivider()
                        GradientEditor(
                            spec = wallpaper.gradient,
                            animated = wallpaper.animated,
                            onChange = { spec -> updateWallpaper { it.copy(gradient = spec) } },
                            onAnimated = { v -> updateWallpaper { it.copy(animated = v) } },
                            onPickStop = { index -> picking = ColorTarget.GradientStop(index) },
                        )
                    }
                    "Image" -> {
                        RowDivider()
                        SettingRow("Changer d'image", "Une photo de votre téléphone.", icon = Icons.Rounded.Image, onClick = { actions.pickBackgroundImage() })
                        RowDivider()
                        SliderRow("Assombrir", "Pour que le texte reste lisible.", wallpaper.dim.toFloat(), 0f..80f, "${wallpaper.dim} %") { v ->
                            updateWallpaper { it.copy(dim = v.roundToInt()) }
                        }
                        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                            RowDivider()
                            SliderRow("Flou", null, wallpaper.blur.toFloat(), 0f..24f, if (wallpaper.blur == 0) "Aucun" else "${wallpaper.blur}") { v ->
                                updateWallpaper { it.copy(blur = v.roundToInt()) }
                            }
                        }
                    }
                }
                if (wallpaper.kind != "None") {
                    RowDivider()
                    SwitchRow(
                        "Fond dans toute l'application",
                        "Aussi derrière la vue des onglets, les paramètres et les listes, avec des panneaux translucides.",
                        checked = look.wallpaperEverywhere,
                    ) { v -> updateLook { it.copy(wallpaperEverywhere = v) } }
                }
            }

            Section("Barre d'outils") {
                SettingRow("Position") {
                    Segmented(listOf("Bottom" to "En bas", "Top" to "En haut"), settings.toolbarPosition, { pos -> app.stores.settings.update { it.copy(toolbarPosition = pos) } })
                }
                RowDivider()
                SettingRow("Style") {
                    Segmented(listOf("Solid" to "Opaque", "Glass" to "Verre"), look.toolbarStyle, { v -> updateLook { it.copy(toolbarStyle = v) } })
                }
                RowDivider()
                SettingRow("Taille") {
                    Segmented(listOf("Compact" to "Compacte", "Normal" to "Normale", "Large" to "Grande"), look.toolbarSize, { v -> updateLook { it.copy(toolbarSize = v) } })
                }
                RowDivider()
                Column(Modifier.padding(horizontal = 16.dp, vertical = 13.dp)) {
                    Text("Boutons", color = colors.text, fontSize = 15.sp, fontWeight = FontWeight.Medium)
                    Text(
                        "Jusqu'à ${ToolbarButtonIds.MAX}, à côté de l'adresse. Appui long sur Retour ou Avancer : les pages de l'onglet.",
                        color = colors.textSecondary, fontSize = 13.sp, lineHeight = 17.sp,
                    )
                    Spacer(Modifier.height(10.dp))
                    val choices = listOf(
                        ToolbarButtonIds.BACK to "Retour",
                        ToolbarButtonIds.FORWARD to "Avancer",
                        ToolbarButtonIds.HOME to "Accueil",
                        ToolbarButtonIds.NEW_TAB to "Nouvel onglet",
                        ToolbarButtonIds.SHARE to "Partager",
                        ToolbarButtonIds.BOOKMARK to "Favori",
                    )
                    choices.chunked(2).forEach { row ->
                        Row(Modifier.fillMaxWidth().padding(vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            row.forEach { (id, label) ->
                                val on = id in look.toolbarButtons
                                val full = !on && look.toolbarButtons.size >= ToolbarButtonIds.MAX
                                Chip(label, selected = on, enabled = !full, modifier = Modifier.weight(1f)) {
                                    updateLook { a -> a.copy(toolbarButtons = if (on) a.toolbarButtons - id else a.toolbarButtons + id) }
                                }
                            }
                        }
                    }
                }
            }

            Section("Formes") {
                SettingRow("Coins") {
                    Segmented(listOf("Square" to "Carrés", "Rounded" to "Arrondis", "Round" to "Ronds"), look.corners, { v -> updateLook { it.copy(corners = v) } })
                }
            }

            Section("Page d'accueil") {
                Column(Modifier.padding(horizontal = 16.dp, vertical = 10.dp)) {
                    OutlinedTextField(
                        value = look.homeTitle,
                        onValueChange = { text -> updateLook { it.copy(homeTitle = text.take(24)) } },
                        singleLine = true,
                        label = { Text("Titre (vide : aucun)") },
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
                RowDivider()
                SwitchRow("Horloge et salutation", checked = look.homeClock) { v -> updateLook { it.copy(homeClock = v) } }
                RowDivider()
                SwitchRow("Favoris", checked = settings.showFavoritesOnHome) { v -> app.stores.settings.update { it.copy(showFavoritesOnHome = v) } }
                RowDivider()
                SwitchRow("Sites fréquents", "Les sites que vous visitez le plus, d'après l'historique.", checked = look.homeTopSites) { v ->
                    updateLook { it.copy(homeTopSites = v) }
                }
                if (settings.hiddenTopSites.isNotEmpty()) {
                    RowDivider()
                    SettingRow("Sites masqués", "${settings.hiddenTopSites.size} site(s) retiré(s) des sites fréquents") {
                        TextButton(onClick = { app.stores.settings.update { it.copy(hiddenTopSites = emptyList()) } }) { Text("Réafficher", color = colors.accent) }
                    }
                }
                RowDivider()
                SettingRow("Colonnes") {
                    Segmented(listOf("3" to "3", "4" to "4", "5" to "5"), look.homeColumns.toString(), { v -> updateLook { it.copy(homeColumns = v.toInt()) } })
                }
            }
        }
    }

    picking?.let { target ->
        val initial = when (target) {
            ColorTarget.Accent -> settings.accentColorHex
            ColorTarget.WallpaperColor -> wallpaper.colorHex
            is ColorTarget.GradientStop -> wallpaper.gradient.colors.getOrElse(target.index) { "#4C8DFF" }
        }
        ColorPickerDialog(
            title = when (target) {
                ColorTarget.Accent -> "Couleur d'accent"
                ColorTarget.WallpaperColor -> "Couleur du fond"
                is ColorTarget.GradientStop -> "Couleur ${target.index + 1} du dégradé"
            },
            initialHex = initial,
            onDismiss = { picking = null },
            onPick = { hex ->
                picking = null
                when (target) {
                    ColorTarget.Accent -> updateColors { it.copy(accentColorHex = hex) }
                    ColorTarget.WallpaperColor -> updateWallpaper { it.copy(colorHex = hex) }
                    is ColorTarget.GradientStop -> updateWallpaper { w ->
                        w.copy(gradient = w.gradient.copy(colors = w.gradient.colors.toMutableList().also { list -> if (target.index in list.indices) list[target.index] = hex }))
                    }
                }
            },
        )
    }

    if (confirmReset) {
        AlertDialog(
            onDismissRequest = { confirmReset = false },
            title = { Text("Réinitialiser l'apparence ?") },
            text = { Text("Couleurs, fond, barre d'outils, formes et page d'accueil reviennent à l'apparence d'origine de Navigatueur.") },
            confirmButton = {
                TextButton(onClick = {
                    confirmReset = false
                    app.stores.settings.update { it.withDefaultLook() }
                    Engine.apply(app.stores.settings.value)
                }) { Text("Réinitialiser") }
            },
            dismissButton = { TextButton(onClick = { confirmReset = false }) { Text("Annuler") } },
        )
    }
}

/** A small home page and toolbar drawn with the current choices. */
@Composable
private fun LivePreview(settings: AppSettings) {
    val colors = Nv.colors
    val look = Nv.look
    val shape = Nv.shape(20.dp)
    val bottom = settings.toolbarPosition != "Top"
    Box(
        Modifier
            .padding(horizontal = 16.dp, vertical = 12.dp)
            .fillMaxWidth()
            .height(230.dp)
            .clip(shape)
            .border(1.dp, colors.panelBorder, shape)
    ) {
        WallpaperBox(Modifier.fillMaxSize(), private = false) {
            Column(
                Modifier.fillMaxSize().padding(horizontal = 22.dp).padding(top = if (bottom) 0.dp else 40.dp, bottom = if (bottom) 40.dp else 0.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center,
            ) {
                if (look.appearance.homeClock) {
                    Text("12:34", color = Nv.onWallpaper, fontSize = 30.sp, fontWeight = FontWeight.Light)
                    Spacer(Modifier.height(4.dp))
                }
                if (look.appearance.homeTitle.isNotBlank()) {
                    Text(look.appearance.homeTitle, style = WordmarkStyle.copy(fontSize = 10.sp, letterSpacing = 4.sp), color = Nv.onWallpaper.copy(alpha = 0.85f), maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Spacer(Modifier.height(10.dp))
                }
                Row(
                    Modifier.fillMaxWidth().height(34.dp).clip(Nv.shape(12.dp)).background(colors.sheet.copy(alpha = 0.94f)).padding(horizontal = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Icon(Icons.Rounded.Search, null, tint = colors.textTertiary, modifier = Modifier.size(15.dp))
                    Spacer(Modifier.width(6.dp))
                    Text("Rechercher ou saisir une adresse", color = colors.textTertiary, fontSize = 11.sp, maxLines = 1)
                }
                Spacer(Modifier.height(10.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    repeat(4) { Box(Modifier.size(28.dp).clip(Nv.shape(8.dp)).background(colors.sheet.copy(alpha = 0.9f))) }
                }
            }
            // The toolbar, at its place and in its style.
            Row(
                Modifier
                    .align(if (bottom) Alignment.BottomCenter else Alignment.TopCenter)
                    .fillMaxWidth()
                    .height(40.dp)
                    .background(if (look.glassToolbar) colors.sheet.copy(alpha = 0.76f) else colors.sheet)
                    .padding(horizontal = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                look.appearance.toolbarButtons.forEach { id ->
                    Icon(toolbarButtonIcon(id), null, tint = colors.text, modifier = Modifier.padding(end = 8.dp).size(16.dp))
                }
                Box(Modifier.weight(1f).height(26.dp).clip(Nv.shape(9.dp)).background(colors.surface.copy(alpha = 1f)))
                Spacer(Modifier.width(8.dp))
                Box(Modifier.size(15.dp).border(1.5.dp, colors.text, Nv.shape(4.dp)))
                Spacer(Modifier.width(8.dp))
                Icon(Icons.Rounded.MoreVert, null, tint = colors.text, modifier = Modifier.size(16.dp))
            }
            Box(Modifier.align(Alignment.TopEnd).padding(10.dp).size(12.dp).clip(CircleShape).background(colors.accent))
        }
    }
}

private fun toolbarButtonIcon(id: String): ImageVector = when (id) {
    ToolbarButtonIds.BACK -> Icons.AutoMirrored.Rounded.ArrowBack
    ToolbarButtonIds.FORWARD -> Icons.AutoMirrored.Rounded.ArrowForward
    ToolbarButtonIds.HOME -> Icons.Rounded.Home
    ToolbarButtonIds.NEW_TAB -> Icons.Rounded.Add
    ToolbarButtonIds.SHARE -> Icons.Rounded.Share
    else -> Icons.Rounded.StarBorder
}

/** A ready-made theme: its wallpaper, accent dot and name. */
@Composable
private fun ThemeCard(preset: ThemePreset, selected: Boolean, onClick: () -> Unit) {
    val colors = Nv.colors
    val shape = Nv.shape(16.dp)
    val dark = preset.themeMode == "Dark"
    val base = when {
        preset.pureBlack -> Color.Black
        dark -> parseHexColor("#0C0D0F")
        else -> parseHexColor("#E6E6EA")
    }
    val accent = parseHexColor(preset.accent)
    Column(
        Modifier
            .width(92.dp)
            .clip(shape)
            .clickable(role = Role.RadioButton, onClickLabel = "Thème ${preset.name}", onClick = onClick),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Box(
            Modifier
                .fillMaxWidth()
                .height(110.dp)
                .clip(shape)
                .background(base)
                .border(if (selected) 2.5.dp else 1.dp, if (selected) colors.accent else colors.panelBorder, shape),
        ) {
            when (preset.wallpaper.kind) {
                "Gradient" -> GradientFill(preset.wallpaper.gradient, animated = false, modifier = Modifier.fillMaxSize())
                "Glow" -> Box(Modifier.fillMaxSize().background(Brush.radialGradient(listOf(accent.copy(alpha = 0.35f), Color.Transparent))))
                else -> Unit
            }
            // A miniature toolbar in the theme's colors.
            Box(
                Modifier.align(Alignment.BottomCenter).padding(8.dp).fillMaxWidth().height(16.dp).clip(Nv.shape(6.dp))
                    .background(if (dark) Color(0xCC16171A) else Color(0xCCFBFBFC))
            ) {
                Box(Modifier.align(Alignment.CenterEnd).padding(end = 5.dp).size(8.dp).clip(CircleShape).background(accent))
            }
            if (selected) {
                Box(
                    Modifier.align(Alignment.TopEnd).padding(6.dp).size(20.dp).clip(CircleShape).background(colors.accent),
                    contentAlignment = Alignment.Center,
                ) { Icon(Icons.Rounded.Check, "Thème choisi", tint = colors.accentForeground, modifier = Modifier.size(14.dp)) }
            }
        }
        Spacer(Modifier.height(6.dp))
        Text(preset.name, color = colors.text, fontSize = 12.sp, fontWeight = if (selected) FontWeight.SemiBold else FontWeight.Normal, maxLines = 1)
        Spacer(Modifier.height(4.dp))
    }
}

/** Color swatches, the current color marked, and a "+" that opens the color picker. */
@Composable
private fun SwatchRow(swatches: List<String>, selected: String, onSelect: (String) -> Unit, onCustom: () -> Unit) {
    val colors = Nv.colors
    val custom = swatches.none { it.equals(selected, ignoreCase = true) }
    Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        (if (custom) listOf(selected) + swatches else swatches).forEach { hex ->
            val isSelected = hex.equals(selected, ignoreCase = true)
            val color = parseHexColor(hex)
            Box(
                Modifier
                    .size(38.dp)
                    .clip(CircleShape)
                    .background(color)
                    .border(if (isSelected) 3.dp else 1.dp, if (isSelected) colors.text else colors.panelBorder, CircleShape)
                    .clickable(role = Role.RadioButton, onClickLabel = hex) { onSelect(hex) },
                contentAlignment = Alignment.Center,
            ) {
                if (isSelected) Icon(Icons.Rounded.Check, null, tint = if (com.navigatueur.mobile.core.ColorMath.isDark(com.navigatueur.mobile.core.ColorMath.parseRgb(hex) ?: 0)) Color.White else Color.Black, modifier = Modifier.size(18.dp))
            }
        }
        Box(
            Modifier
                .size(38.dp)
                .clip(CircleShape)
                .background(Brush.sweepGradient(listOf(Color.Red, Color.Yellow, Color.Green, Color.Cyan, Color.Blue, Color.Magenta, Color.Red)))
                .clickable(role = Role.Button, onClickLabel = "Autre couleur", onClick = onCustom),
            contentAlignment = Alignment.Center,
        ) {
            Box(Modifier.size(26.dp).clip(CircleShape).background(colors.sheet), contentAlignment = Alignment.Center) {
                Icon(Icons.Rounded.Colorize, "Autre couleur", tint = colors.text, modifier = Modifier.size(15.dp))
            }
        }
    }
}

@Composable
private fun GradientChoices(current: GradientSpec, onSelect: (GradientSpec) -> Unit) {
    val colors = Nv.colors
    Column(Modifier.padding(horizontal = 16.dp, vertical = 13.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        Text("Modèles", color = colors.text, fontSize = 15.sp, fontWeight = FontWeight.Medium)
        Gradients.presets.chunked(4).forEach { row ->
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                row.forEach { preset ->
                    val selected = preset.colors == current.colors && preset.style == current.style
                    val shape = Nv.shape(12.dp)
                    Column(Modifier.weight(1f), horizontalAlignment = Alignment.CenterHorizontally) {
                        Box(
                            Modifier
                                .fillMaxWidth()
                                .height(48.dp)
                                .clip(shape)
                                .border(if (selected) 2.5.dp else 1.dp, if (selected) colors.accent else colors.panelBorder, shape)
                                .clickable(role = Role.RadioButton, onClickLabel = "Dégradé ${preset.name}") { onSelect(preset.spec) },
                        ) {
                            GradientFill(preset.spec, animated = false, modifier = Modifier.fillMaxSize())
                        }
                        Text(preset.name, color = colors.textSecondary, fontSize = 11.sp, maxLines = 1, modifier = Modifier.padding(top = 3.dp))
                    }
                }
                repeat(4 - row.size) { Spacer(Modifier.weight(1f)) }
            }
        }
    }
}

/** A gradient made to measure: its 2 or 3 colors, its direction or a radial glow, and whether it turns. */
@Composable
private fun GradientEditor(
    spec: GradientSpec,
    animated: Boolean,
    onChange: (GradientSpec) -> Unit,
    onAnimated: (Boolean) -> Unit,
    onPickStop: (Int) -> Unit,
) {
    val colors = Nv.colors
    Column(Modifier.padding(horizontal = 16.dp, vertical = 13.dp)) {
        Text("Sur mesure", color = colors.text, fontSize = 15.sp, fontWeight = FontWeight.Medium)
        Text("Touchez une couleur pour la changer.", color = colors.textSecondary, fontSize = 13.sp)
        Spacer(Modifier.height(10.dp))
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            spec.colors.forEachIndexed { index, hex ->
                Box(
                    Modifier
                        .size(42.dp)
                        .clip(CircleShape)
                        .background(parseHexColor(hex))
                        .border(1.dp, colors.panelBorder, CircleShape)
                        .clickable(role = Role.Button, onClickLabel = "Couleur ${index + 1}") { onPickStop(index) }
                )
            }
            if (spec.colors.size < 3) {
                RoundAction(Icons.Rounded.Add, "Ajouter une couleur") { onChange(spec.copy(colors = spec.colors + spec.colors.last())) }
            } else {
                RoundAction(Icons.Rounded.Remove, "Retirer la dernière couleur") { onChange(spec.copy(colors = spec.colors.dropLast(1))) }
            }
        }
        Spacer(Modifier.height(12.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Forme", color = colors.text, fontSize = 15.sp, modifier = Modifier.weight(1f))
            Segmented(listOf("Linear" to "Linéaire", "Radial" to "Radial"), spec.style, { v -> onChange(spec.copy(style = v)) })
        }
    }
    if (spec.style != "Radial") {
        SliderRow("Direction", null, spec.angle.toFloat(), 0f..359f, "${spec.angle}°") { v -> onChange(spec.copy(angle = v.roundToInt())) }
    }
    SwitchRow("Dégradé animé", "Les couleurs tournent lentement.", checked = animated, onChange = onAnimated)
}

@Composable
private fun RoundAction(icon: ImageVector, description: String, onClick: () -> Unit) {
    val colors = Nv.colors
    Box(
        Modifier.size(42.dp).clip(CircleShape).background(colors.surface).clickable(role = Role.Button, onClickLabel = description, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { Icon(icon, description, tint = colors.text, modifier = Modifier.size(20.dp)) }
}

@Composable
private fun SliderRow(title: String, subtitle: String?, value: Float, range: ClosedFloatingPointRange<Float>, label: String, onChange: (Float) -> Unit) {
    val colors = Nv.colors
    Column(Modifier.padding(horizontal = 16.dp, vertical = 10.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(title, color = colors.text, fontSize = 15.sp, fontWeight = FontWeight.Medium)
                if (subtitle != null) Text(subtitle, color = colors.textSecondary, fontSize = 13.sp)
            }
            Text(label, color = colors.accent, fontSize = 14.sp, fontWeight = FontWeight.Medium)
        }
        Slider(
            value = value.coerceIn(range.start, range.endInclusive),
            onValueChange = onChange,
            valueRange = range,
            colors = SliderDefaults.colors(
                thumbColor = colors.accent,
                activeTrackColor = colors.accent,
                inactiveTrackColor = colors.surfaceActive,
            ),
        )
    }
}

@Composable
private fun Chip(label: String, selected: Boolean, enabled: Boolean = true, modifier: Modifier = Modifier, onClick: () -> Unit) {
    val colors = Nv.colors
    Box(
        modifier
            .clip(Nv.shape(20.dp))
            .background(if (selected) colors.accent else colors.surface)
            .clickable(enabled = enabled, role = Role.Checkbox, onClick = onClick)
            .padding(horizontal = 14.dp, vertical = 9.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            label,
            color = (if (selected) colors.accentForeground else colors.textSecondary).copy(alpha = if (enabled) 1f else 0.4f),
            fontSize = 14.sp,
            fontWeight = FontWeight.Medium,
            maxLines = 1,
        )
    }
}
