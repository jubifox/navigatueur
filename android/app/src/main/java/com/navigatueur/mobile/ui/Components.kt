package com.navigatueur.mobile.ui

import android.graphics.Bitmap
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.ArrowBack
import androidx.compose.material.icons.rounded.Public
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.FilterQuality
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.ui.theme.Nv
import com.navigatueur.mobile.ui.theme.WallpaperBox

@Composable
fun FaviconImage(bitmap: Bitmap?, size: Dp = 18.dp, modifier: Modifier = Modifier, tint: Color = Nv.colors.textTertiary) {
    if (bitmap != null) {
        Image(
            bitmap = bitmap.asImageBitmap(),
            contentDescription = null,
            filterQuality = FilterQuality.Medium,
            modifier = modifier.size(size).clip(RoundedCornerShape(size / 5)),
        )
    } else {
        Icon(Icons.Rounded.Public, contentDescription = null, tint = tint, modifier = modifier.size(size))
    }
}

/** A screen's title row, with a back arrow. */
@Composable
fun ScreenHeader(title: String, onBack: () -> Unit, actions: @Composable RowScope.() -> Unit = {}) {
    Row(
        Modifier
            .fillMaxWidth()
            .background(Nv.colors.panel)
            .statusBarsPadding()
            .height(56.dp)
            .padding(horizontal = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IconButton(onClick = onBack) {
            Icon(Icons.AutoMirrored.Rounded.ArrowBack, contentDescription = "Retour", tint = Nv.colors.text)
        }
        Text(
            title,
            color = Nv.colors.text,
            fontSize = 19.sp,
            fontWeight = FontWeight.SemiBold,
            modifier = Modifier.weight(1f).padding(start = 4.dp),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        actions()
    }
}

/** A titled group of rows on a card, like the Windows Settings page. */
@Composable
fun Section(title: String? = null, lead: String? = null, content: @Composable ColumnScope.() -> Unit) {
    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp)) {
        // Over the wallpaper, titles take its text color.
        val onWallpaper = Nv.look.wallpaperEverywhere
        if (title != null) {
            Text(title, color = if (onWallpaper) Nv.onWallpaper else Nv.colors.text, fontWeight = FontWeight.SemiBold, fontSize = 17.sp, modifier = Modifier.padding(start = 4.dp, top = 8.dp))
        }
        if (lead != null) {
            Text(lead, color = if (onWallpaper) Nv.onWallpaper.copy(alpha = 0.78f) else Nv.colors.textSecondary, fontSize = 13.sp, modifier = Modifier.padding(start = 4.dp, top = 2.dp))
        }
        Spacer(Modifier.height(8.dp))
        val shape = Nv.shape(14.dp)
        Column(
            Modifier
                .fillMaxWidth()
                .clip(shape)
                .background(Nv.colors.panel)
                .border(1.dp, Nv.colors.panelBorder, shape),
            content = content,
        )
    }
}

@Composable
fun SettingRow(
    title: String,
    subtitle: String? = null,
    icon: ImageVector? = null,
    enabled: Boolean = true,
    onClick: (() -> Unit)? = null,
    trailing: @Composable (() -> Unit)? = null,
) {
    val alpha = if (enabled) 1f else 0.45f
    Row(
        Modifier
            .fillMaxWidth()
            .then(if (onClick != null && enabled) Modifier.clickable(onClick = onClick) else Modifier)
            .padding(horizontal = 16.dp, vertical = 13.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (icon != null) {
            Icon(icon, contentDescription = null, tint = Nv.colors.textSecondary.copy(alpha = alpha), modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(14.dp))
        }
        Column(Modifier.weight(1f)) {
            Text(title, color = Nv.colors.text.copy(alpha = alpha), fontSize = 15.sp, fontWeight = FontWeight.Medium)
            if (subtitle != null) {
                Text(subtitle, color = Nv.colors.textSecondary.copy(alpha = alpha), fontSize = 13.sp, lineHeight = 17.sp, modifier = Modifier.padding(top = 2.dp))
            }
        }
        if (trailing != null) {
            Spacer(Modifier.width(12.dp))
            trailing()
        }
    }
}

@Composable
fun SwitchRow(title: String, subtitle: String? = null, checked: Boolean, icon: ImageVector? = null, enabled: Boolean = true, onChange: (Boolean) -> Unit) {
    SettingRow(title, subtitle, icon, enabled, onClick = { onChange(!checked) }) {
        Switch(
            checked = checked,
            onCheckedChange = if (enabled) onChange else null,
            colors = SwitchDefaults.colors(
                checkedTrackColor = Nv.colors.accent,
                checkedThumbColor = Nv.colors.accentForeground,
                uncheckedTrackColor = Nv.colors.surfaceActive,
                uncheckedThumbColor = Nv.colors.textSecondary,
                uncheckedBorderColor = Color.Transparent,
            ),
        )
    }
}

@Composable
fun RowDivider() {
    Box(Modifier.fillMaxWidth().padding(start = 16.dp).height(1.dp).background(Nv.colors.panelBorder))
}

/** Segmented choice, like the Windows Settings page's ("Sombre | Clair"). */
@Composable
fun Segmented(options: List<Pair<String, String>>, selected: String, onSelect: (String) -> Unit, modifier: Modifier = Modifier) {
    Row(
        modifier
            .clip(Nv.shape(10.dp))
            .background(Nv.colors.surface)
            .padding(3.dp),
        horizontalArrangement = Arrangement.spacedBy(3.dp),
    ) {
        options.forEach { (value, label) ->
            val active = value == selected
            Box(
                Modifier
                    .clip(Nv.shape(8.dp))
                    .background(if (active) Nv.colors.accent else Color.Transparent)
                    .clickable(role = Role.RadioButton) { onSelect(value) }
                    .padding(horizontal = 12.dp, vertical = 7.dp),
                contentAlignment = Alignment.Center,
            ) {
                Text(label, color = if (active) Nv.colors.accentForeground else Nv.colors.textSecondary, fontSize = 13.sp, fontWeight = FontWeight.Medium)
            }
        }
    }
}

@Composable
fun ColorDot(color: Color, size: Dp = 12.dp, modifier: Modifier = Modifier) {
    Box(modifier.size(size).clip(CircleShape).background(color))
}

@Composable
fun EmptyState(icon: ImageVector, title: String, text: String) {
    Column(
        Modifier.fillMaxSize().padding(32.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Icon(icon, contentDescription = null, tint = Nv.colors.textTertiary, modifier = Modifier.size(44.dp))
        Spacer(Modifier.height(14.dp))
        Text(title, color = Nv.colors.text, fontWeight = FontWeight.SemiBold, fontSize = 17.sp)
        Spacer(Modifier.height(6.dp))
        Text(text, color = Nv.colors.textSecondary, fontSize = 14.sp, textAlign = androidx.compose.ui.text.style.TextAlign.Center)
    }
}

val ListPadding = PaddingValues(bottom = 24.dp)

/**
 * A full screen's background: the window color, or — when Personnaliser →
 * "Fond dans toute l'application" is on — the wallpaper under a veil of the
 * window color, so text stays readable on any picture or gradient.
 */
@Composable
fun ScreenBackground(modifier: Modifier = Modifier, content: @Composable BoxScope.() -> Unit) {
    if (Nv.look.wallpaperEverywhere) {
        WallpaperBox(modifier) {
            Box(Modifier.fillMaxSize().background(Nv.colors.window.copy(alpha = 0.55f)))
            content()
        }
    } else {
        Box(modifier.background(Nv.colors.window), content = content)
    }
}

/** A full screen laid out as a column, on [ScreenBackground]. */
@Composable
fun ScreenColumn(content: @Composable ColumnScope.() -> Unit) {
    ScreenBackground(Modifier.fillMaxSize()) {
        Column(Modifier.fillMaxSize(), content = content)
    }
}
