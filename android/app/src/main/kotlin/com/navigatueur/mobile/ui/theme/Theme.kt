package com.navigatueur.mobile.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// Same palette as the desktop app's Theme.xaml — one product, one identity.
val ChromeBackground = Color(0xFF1E1F22)
val ChromeBorder = Color(0xFF3A3B3F)
val ChromeText = Color(0xFFE4E4E7)
val Surface = Color(0xFF2B2D31)
val Accent = Color(0xFF4C8DFF)

private val DarkColors = darkColorScheme(
    primary = Accent,
    background = ChromeBackground,
    surface = Surface,
    onPrimary = Color.White,
    onBackground = ChromeText,
    onSurface = ChromeText,
    outline = ChromeBorder,
)

// Navigatueur is dark-themed by default on desktop too — no separate light
// palette has been designed yet, so light mode just reuses the dark one
// rather than falling back to Compose's generic Material default (which
// would clash badly with the rest of the app's look).
private val LightColors = DarkColors

@Composable
fun NavigatueurTheme(content: @Composable () -> Unit) {
    val colors = if (isSystemInDarkTheme()) DarkColors else LightColors
    MaterialTheme(colorScheme = colors, content = content)
}
