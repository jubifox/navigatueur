package com.navigatueur.mobile.core

import com.navigatueur.mobile.data.AppSettings
import com.navigatueur.mobile.data.GradientSpec
import com.navigatueur.mobile.data.Wallpaper

/** A ready-made gradient offered in Personnaliser. */
data class GradientPreset(val id: String, val name: String, val colors: List<String>, val angle: Int = 135, val style: String = "Linear") {
    val spec: GradientSpec get() = GradientSpec(colors, angle, style)
}

object Gradients {
    val presets = listOf(
        GradientPreset("aurore", "Aurore", listOf("#3A1C71", "#D76D77", "#FFAF7B")),
        GradientPreset("ocean", "Océan", listOf("#0F2027", "#203A43", "#2C5364"), angle = 160),
        GradientPreset("lagon", "Lagon", listOf("#1CB5E0", "#000851"), angle = 200),
        GradientPreset("crepuscule", "Crépuscule", listOf("#0B0F2B", "#4A1D6B", "#E0577B"), angle = 180),
        GradientPreset("foret", "Forêt", listOf("#0B3D2E", "#1E7B4F", "#9BD770"), angle = 160),
        GradientPreset("neon", "Néon", listOf("#12C2E9", "#C471ED", "#F64F59")),
        GradientPreset("nuit", "Nuit", listOf("#141E30", "#243B55"), angle = 180),
        GradientPreset("braise", "Braise", listOf("#200122", "#6F0000"), angle = 180),
        GradientPreset("lavande", "Lavande", listOf("#8E7CC3", "#C3A6E0", "#F5D0E8")),
        GradientPreset("agrumes", "Agrumes", listOf("#F7971E", "#FFD200")),
        GradientPreset("menthe", "Menthe", listOf("#43C6AC", "#F8FFAE")),
        GradientPreset("peche", "Pêche", listOf("#ED4264", "#FFEDBC")),
        GradientPreset("ardoise", "Ardoise", listOf("#232526", "#414345"), angle = 180),
        GradientPreset("halo", "Halo", listOf("#4C8DFF", "#0C0D0F"), style = "Radial"),
    )

    fun byId(id: String): GradientPreset? = presets.firstOrNull { it.id == id }
}

/** One tap in Personnaliser sets all of these at once. */
data class ThemePreset(
    val id: String,
    val name: String,
    /** "Dark" or "Light". */
    val themeMode: String,
    val accent: String,
    val wallpaper: Wallpaper,
    val tint: Int = 0,
    val pureBlack: Boolean = false,
)

object ThemePresets {
    private fun gradient(id: String, animated: Boolean = false) =
        Wallpaper(kind = "Gradient", gradient = Gradients.byId(id)!!.spec, animated = animated)

    val all = listOf(
        ThemePreset("navigatueur", "Navigatueur", "Dark", "#4C8DFF", Wallpaper(kind = "Glow")),
        ThemePreset("aurore", "Aurore", "Dark", "#FF9E80", gradient("aurore"), tint = 20),
        ThemePreset("ocean", "Océan", "Dark", "#4FD1E0", gradient("ocean"), tint = 30),
        ThemePreset("neon", "Néon", "Dark", "#C471ED", gradient("neon", animated = true), tint = 15),
        ThemePreset("minuit", "Minuit", "Dark", "#7C9CFF", gradient("nuit"), tint = 30),
        ThemePreset("foret", "Forêt", "Dark", "#4FE0A0", gradient("foret"), tint = 30),
        ThemePreset("crepuscule", "Crépuscule", "Dark", "#F472B6", gradient("crepuscule"), tint = 25),
        ThemePreset("braise", "Braise", "Dark", "#FF6B4A", gradient("braise"), tint = 25),
        ThemePreset("oled", "Noir pur", "Dark", "#E5E7EB", Wallpaper(kind = "None"), pureBlack = true),
        ThemePreset("lavande", "Lavande", "Light", "#8B5CF6", gradient("lavande"), tint = 25),
        ThemePreset("agrumes", "Agrumes", "Light", "#EA7A0C", gradient("agrumes"), tint = 15),
        ThemePreset("papier", "Papier", "Light", "#4C8DFF", Wallpaper(kind = "None")),
    )

    fun byId(id: String?): ThemePreset? = all.firstOrNull { it.id == id }
}

/** Accent colors offered as swatches (any other one through the color picker). */
val AccentPalette = listOf(
    "#4C8DFF", "#7C9CFF", "#4FD1E0", "#4FE0A0", "#A3E635", "#FACC15",
    "#E0A52A", "#FF9E80", "#E04F4F", "#F472B6", "#B14FE0", "#E5E7EB",
)

/** Solid wallpaper colors offered as swatches. */
val WallpaperColorPalette = listOf(
    "#0C0D0F", "#1B2333", "#0F2A2E", "#14261B", "#2A1631", "#331A1A",
    "#E8ECF4", "#F4ECE1", "#E3F1EA", "#EFE6F7", "#FBE4E4", "#FFFFFF",
)

/** Applies a ready-made theme; the picture already chosen is kept for when "Image" is picked again. */
fun AppSettings.withTheme(preset: ThemePreset): AppSettings = copy(
    themeMode = preset.themeMode,
    accentColorHex = preset.accent,
    appearance = appearance.copy(
        themeId = preset.id,
        tint = preset.tint,
        pureBlack = preset.pureBlack,
        wallpaper = preset.wallpaper.copy(imagePath = appearance.wallpaper.imagePath, dim = appearance.wallpaper.dim, blur = appearance.wallpaper.blur),
    ),
)

/** Back to how Navigatueur looks out of the box (the picture file is kept, unused). */
fun AppSettings.withDefaultLook(): AppSettings {
    val defaults = AppSettings()
    return copy(
        themeMode = defaults.themeMode,
        accentColorHex = defaults.accentColorHex,
        appearance = defaults.appearance.copy(wallpaper = defaults.appearance.wallpaper.copy(imagePath = appearance.wallpaper.imagePath)),
    )
}
