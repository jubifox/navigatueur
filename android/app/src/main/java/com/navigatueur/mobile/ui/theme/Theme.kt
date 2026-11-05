package com.navigatueur.mobile.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

/**
 * The Windows app's palette (ThemeService: Dark, Light, and the violet
 * private variants), so both apps look like one product.
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

fun parseHexColor(hex: String?, fallback: Color = rgb(0x4C8DFF)): Color = runCatching {
    val clean = hex!!.trim().removePrefix("#")
    if (clean.length != 6) fallback else rgb(clean.toLong(16))
}.getOrDefault(fallback)

fun nvColors(dark: Boolean, private: Boolean, accentHex: String): NvColors {
    var colors = if (dark) darkBase else lightBase
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
    // White text on a light accent (yellow, cyan…) is unreadable: near-black past mid luminance, like the Windows app.
    val onAccent = if (accent.luminance() > 0.55f) rgb(0x111114) else Color.White
    return colors.copy(accent = accent, accentForeground = onAccent, accentSoft = accent.copy(alpha = 0.18f), isPrivate = private)
}

val LocalNv = staticCompositionLocalOf { darkBase }

object Nv {
    val colors: NvColors @Composable get() = LocalNv.current
}

/** Accent swatches offered in Settings — the Windows app's seven. */
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

@Composable
fun NavigatueurTheme(themeMode: String, accentHex: String, private: Boolean, content: @Composable () -> Unit) {
    val dark = isDarkTheme(themeMode)
    val nv = nvColors(dark, private, accentHex)
    val scheme = if (dark) darkColorScheme(
        primary = nv.accent, onPrimary = nv.accentForeground, primaryContainer = nv.accentSoft, onPrimaryContainer = nv.text,
        secondary = nv.accent, onSecondary = nv.accentForeground,
        background = nv.window, onBackground = nv.text, surface = nv.panel, onSurface = nv.text,
        surfaceVariant = nv.surface, onSurfaceVariant = nv.textSecondary, surfaceContainer = nv.panel,
        surfaceContainerHigh = nv.surface, surfaceContainerHighest = nv.surfaceActive, surfaceContainerLow = nv.panel,
        surfaceContainerLowest = nv.window, outline = nv.panelBorder, outlineVariant = nv.panelBorder, error = nv.danger,
    ) else lightColorScheme(
        primary = nv.accent, onPrimary = nv.accentForeground, primaryContainer = nv.accentSoft, onPrimaryContainer = nv.text,
        secondary = nv.accent, onSecondary = nv.accentForeground,
        background = nv.window, onBackground = nv.text, surface = nv.panel, onSurface = nv.text,
        surfaceVariant = nv.surface, onSurfaceVariant = nv.textSecondary, surfaceContainer = nv.panel,
        surfaceContainerHigh = nv.surface, surfaceContainerHighest = nv.surfaceActive, surfaceContainerLow = nv.panel,
        surfaceContainerLowest = nv.window, outline = nv.panelBorder, outlineVariant = nv.panelBorder, error = nv.danger,
    )
    CompositionLocalProvider(LocalNv provides nv) {
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
