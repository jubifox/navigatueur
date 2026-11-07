package com.navigatueur.mobile.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.core.ColorMath
import com.navigatueur.mobile.data.Appearance

/**
 * The Windows app's palette (ThemeService: Dark, Light, and the violet
 * private variants), so both apps look like one product — then what
 * Personnaliser changes: pure black, panels tinted by the accent color,
 * see-through panels over the wallpaper.
 */
@Immutable
data class NvColors(
    val window: Color,
    val panel: Color,
    val panelBorder: Color,
    val surface: Color,
    val surfaceHover: Color,
    val surfaceActive: Color,
    val text: Color,
    val textSecondary: Color,
    val textTertiary: Color,
    val accent: Color,
    val accentForeground: Color,
    val accentSoft: Color,
    val danger: Color,
    val isDark: Boolean,
    val isPrivate: Boolean,
    /** Always opaque: sheets, dialogs and anything over a page ([panel] is see-through over the wallpaper). */
    val sheet: Color = panel,
)

private fun rgb(hex: Long) = Color(0xFF000000 or hex)

private val darkBase = NvColors(
    window = rgb(0x0C0D0F), panel = rgb(0x16171A), panelBorder = rgb(0x26272C), surface = rgb(0x1F2024),
    surfaceHover = rgb(0x25262B), surfaceActive = rgb(0x2C2D33), text = rgb(0xECECEF), textSecondary = rgb(0xA1A1AA),
    textTertiary = rgb(0x6E6E78), accent = rgb(0x4C8DFF), accentForeground = Color.White, accentSoft = Color(0x2E4C8DFF),
    danger = rgb(0xE5484D), isDark = true, isPrivate = false,
)

private val lightBase = NvColors(
    window = rgb(0xE6E6EA), panel = rgb(0xFBFBFC), panelBorder = rgb(0xE1E1E5), surface = rgb(0xF1F1F4),
    surfaceHover = rgb(0xEAEAEE), surfaceActive = rgb(0xE1E1E7), text = rgb(0x18181B), textSecondary = rgb(0x5F5F68),
    textTertiary = rgb(0x8E8E96), accent = rgb(0x4C8DFF), accentForeground = Color.White, accentSoft = Color(0x2E4C8DFF),
    danger = rgb(0xE5484D), isDark = false, isPrivate = false,
)

const val PRIVATE_ACCENT = "#8B5CF6"

fun parseHexColor(hex: String?, fallback: Color = rgb(0x4C8DFF)): Color =
    ColorMath.parseRgb(hex)?.let { rgb(it.toLong()) } ?: fallback

fun nvColors(dark: Boolean, private: Boolean, accentHex: String, appearance: Appearance = Appearance(), seeThrough: Boolean = false): NvColors {
    var colors = if (dark) darkBase else lightBase
    if (dark && appearance.pureBlack && !private) {
        colors = colors.copy(
            window = rgb(0x000000), panel = rgb(0x0A0A0B), panelBorder = rgb(0x1E1E22), surface = rgb(0x141416),
            surfaceHover = rgb(0x1A1A1D), surfaceActive = rgb(0x232327),
        )
    }
    if (private) {
        colors = if (dark) colors.copy(
            window = rgb(0x0E0B14), panel = rgb(0x1A1524), panelBorder = rgb(0x2E263D), surface = rgb(0x231C31),
            surfaceHover = rgb(0x2A223A), surfaceActive = rgb(0x332946),
        ) else colors.copy(
            window = rgb(0xE7E2F0), panel = rgb(0xF8F5FD), panelBorder = rgb(0xE0D8EE), surface = rgb(0xEFEAF8),
            surfaceHover = rgb(0xE8E1F4), surfaceActive = rgb(0xDFD6EF),
        )
    }
    val accent = parseHexColor(if (private) PRIVATE_ACCENT else accentHex)

    // The interface takes on some of the accent color (Personnaliser → Teinte).
    val tint = if (private) 0f else appearance.tint.coerceIn(0, 100) / 100f
    if (tint > 0f) {
        fun tinted(color: Color, strength: Float) = lerp(color, accent, strength * tint)
        colors = colors.copy(
            window = tinted(colors.window, 0.10f), panel = tinted(colors.panel, 0.12f), panelBorder = tinted(colors.panelBorder, 0.25f),
            surface = tinted(colors.surface, 0.16f), surfaceHover = tinted(colors.surfaceHover, 0.18f), surfaceActive = tinted(colors.surfaceActive, 0.22f),
        )
    }

    // White text on a light accent (yellow, cyan…) is unreadable: near-black past mid luminance, like the Windows app.
    val onAccent = if (accent.luminance() > 0.55f) rgb(0x111114) else Color.White
    colors = colors.copy(accent = accent, accentForeground = onAccent, accentSoft = accent.copy(alpha = 0.18f), isPrivate = private, sheet = colors.panel)
    if (seeThrough) {
        colors = colors.copy(
            panel = colors.panel.copy(alpha = 0.80f),
            surface = colors.surface.copy(alpha = 0.82f),
            panelBorder = colors.panelBorder.copy(alpha = 0.6f),
        )
    }
    return colors
}

/** What Personnaliser sets besides colors: shapes, toolbar, the wallpaper. */
@Immutable
data class NvLook(
    val appearance: Appearance = Appearance(),
    /** Corner radii are multiplied by this (square, rounded, round). */
    val cornerScale: Float = 1f,
    val toolbarHeight: Dp = 56.dp,
    val glassToolbar: Boolean = false,
    /** The wallpaper is drawn behind every screen (tabs, settings, lists…), not only the home page. */
    val wallpaperEverywhere: Boolean = false,
    /** The wallpaper is dark overall: light text over it. */
    val wallpaperIsDark: Boolean = true,
) {
    companion object {
        fun of(appearance: Appearance, colors: NvColors): NvLook = NvLook(
            appearance = appearance,
            cornerScale = when (appearance.corners) {
                "Square" -> 0.3f
                "Round" -> 1.6f
                else -> 1f
            },
            toolbarHeight = when (appearance.toolbarSize) {
                "Compact" -> 48.dp
                "Large" -> 64.dp
                else -> 56.dp
            },
            glassToolbar = appearance.toolbarStyle == "Glass",
            wallpaperEverywhere = appearance.wallpaperEverywhere && appearance.wallpaper.kind != "None",
            wallpaperIsDark = wallpaperIsDark(appearance, colors),
        )

        private fun wallpaperIsDark(appearance: Appearance, colors: NvColors): Boolean {
            if (colors.isPrivate) return colors.isDark
            val wallpaper = appearance.wallpaper
            return when (wallpaper.kind) {
                "Color" -> ColorMath.parseRgb(wallpaper.colorHex)?.let(ColorMath::isDark) ?: colors.isDark
                "Gradient" -> ColorMath.isDark(ColorMath.average(wallpaper.gradient.colors.mapNotNull(ColorMath::parseRgb)))
                "Image" -> true
                else -> colors.isDark
            }
        }
    }
}

val LocalNv = staticCompositionLocalOf { darkBase }
val LocalNvLook = staticCompositionLocalOf { NvLook() }

object Nv {
    val colors: NvColors @Composable get() = LocalNv.current
    val look: NvLook @Composable get() = LocalNvLook.current

    /** A rounded shape whose radius follows the corner style chosen in Personnaliser. */
    @Composable
    fun shape(radius: Dp): RoundedCornerShape = RoundedCornerShape(radius * LocalNvLook.current.cornerScale)

    /** Text over the wallpaper (home page, screens drawn over it). */
    val onWallpaper: Color @Composable get() = if (LocalNvLook.current.wallpaperIsDark) Color.White else rgb(0x16161A)
}

/** Accent swatches of the Windows app's Settings (its seven), kept for reference by the Windows-parity code. */
val AccentChoices = listOf(
    "#4C8DFF" to "Bleu",
    "#4FE0A0" to "Vert",
    "#E0A52A" to "Ambre",
    "#E04F4F" to "Rouge",
    "#B14FE0" to "Violet",
    "#4FD1E0" to "Cyan",
    "#F472B6" to "Rose",
)

@Composable
fun isDarkTheme(mode: String): Boolean = when (mode) {
    "Light" -> false
    "System" -> isSystemInDarkTheme()
    else -> true
}

/**
 * [seeThrough]: the screen shows the wallpaper behind its panels (only
 * where Personnaliser → "Fond dans toute l'application" asks for it).
 */
@Composable
fun NavigatueurTheme(
    themeMode: String,
    accentHex: String,
    private: Boolean,
    appearance: Appearance = Appearance(),
    seeThrough: Boolean = false,
    content: @Composable () -> Unit,
) {
    val dark = isDarkTheme(themeMode)
    val nv = nvColors(dark, private, accentHex, appearance, seeThrough)
    val look = NvLook.of(appearance, nv)
    // Material components (dialogs, menus, sheets) get the opaque colors.
    val scheme = if (dark) darkColorScheme(
        primary = nv.accent, onPrimary = nv.accentForeground, primaryContainer = nv.accentSoft, onPrimaryContainer = nv.text,
        secondary = nv.accent, onSecondary = nv.accentForeground,
        background = nv.window, onBackground = nv.text, surface = nv.sheet, onSurface = nv.text,
        surfaceVariant = nv.surface.copy(alpha = 1f), onSurfaceVariant = nv.textSecondary, surfaceContainer = nv.sheet,
        surfaceContainerHigh = nv.surface.copy(alpha = 1f), surfaceContainerHighest = nv.surfaceActive, surfaceContainerLow = nv.sheet,
        surfaceContainerLowest = nv.window, outline = nv.panelBorder.copy(alpha = 1f), outlineVariant = nv.panelBorder.copy(alpha = 1f), error = nv.danger,
    ) else lightColorScheme(
        primary = nv.accent, onPrimary = nv.accentForeground, primaryContainer = nv.accentSoft, onPrimaryContainer = nv.text,
        secondary = nv.accent, onSecondary = nv.accentForeground,
        background = nv.window, onBackground = nv.text, surface = nv.sheet, onSurface = nv.text,
        surfaceVariant = nv.surface.copy(alpha = 1f), onSurfaceVariant = nv.textSecondary, surfaceContainer = nv.sheet,
        surfaceContainerHigh = nv.surface.copy(alpha = 1f), surfaceContainerHighest = nv.surfaceActive, surfaceContainerLow = nv.sheet,
        surfaceContainerLowest = nv.window, outline = nv.panelBorder.copy(alpha = 1f), outlineVariant = nv.panelBorder.copy(alpha = 1f), error = nv.danger,
    )
    CompositionLocalProvider(LocalNv provides nv, LocalNvLook provides look) {
        MaterialTheme(colorScheme = scheme, typography = typography, content = content)
    }
}

private val typography = androidx.compose.material3.Typography().let { base ->
    base.copy(
        titleLarge = base.titleLarge.copy(fontWeight = FontWeight.SemiBold, fontSize = 22.sp),
        titleMedium = base.titleMedium.copy(fontWeight = FontWeight.SemiBold),
        labelLarge = base.labelLarge.copy(fontWeight = FontWeight.SemiBold),
    )
}

val WordmarkStyle = TextStyle(fontSize = 13.sp, letterSpacing = 6.sp, fontWeight = FontWeight.SemiBold)
