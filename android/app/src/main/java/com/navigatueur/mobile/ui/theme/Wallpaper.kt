package com.navigatueur.mobile.ui.theme

import android.graphics.BitmapFactory
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.State
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.BlurredEdgeTreatment
import androidx.compose.ui.draw.blur
import androidx.compose.ui.draw.drawWithCache
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.dp
import com.navigatueur.mobile.core.GradientLine
import com.navigatueur.mobile.data.GradientSpec
import com.navigatueur.mobile.data.Wallpaper
import java.io.File
import kotlin.math.cos
import kotlin.math.max
import kotlin.math.sin

/**
 * The wallpaper chosen in Personnaliser (none, accent glow, color, gradient
 * or picture) filling its box, with [content] on top. Private tabs always
 * get the plain violet glow.
 */
@Composable
fun WallpaperBox(
    modifier: Modifier = Modifier,
    wallpaper: Wallpaper = Nv.look.appearance.wallpaper,
    private: Boolean = Nv.colors.isPrivate,
    content: @Composable BoxScope.() -> Unit = {},
) {
    val colors = Nv.colors
    Box(modifier.background(colors.window)) {
        when {
            private -> AccentGlow()
            wallpaper.kind == "None" -> Unit
            wallpaper.kind == "Color" -> Box(Modifier.fillMaxSize().background(parseHexColor(wallpaper.colorHex, colors.window)))
            wallpaper.kind == "Gradient" -> GradientFill(wallpaper.gradient, wallpaper.animated, Modifier.fillMaxSize())
            wallpaper.kind == "Image" -> {
                val picture = rememberWallpaperPicture(wallpaper.imagePath)
                if (picture != null) {
                    Image(
                        picture, null,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.fillMaxSize().then(
                            if (wallpaper.blur > 0) Modifier.blur(wallpaper.blur.dp, BlurredEdgeTreatment.Rectangle) else Modifier
                        ),
                    )
                    Box(Modifier.fillMaxSize().background(Color.Black.copy(alpha = wallpaper.dim.coerceIn(0, 80) / 100f)))
                } else {
                    AccentGlow()
                }
            }
            else -> AccentGlow()
        }
        content()
    }
}

/** The soft halo of the accent color: Navigatueur's default home page. */
@Composable
private fun AccentGlow() {
    val colors = Nv.colors
    Box(
        Modifier.fillMaxSize().background(
            Brush.radialGradient(listOf(colors.accent.copy(alpha = if (colors.isDark) 0.16f else 0.12f), Color.Transparent), radius = 1400f)
        )
    )
}

/** A gradient, optionally turning slowly (redrawn without recomposing). */
@Composable
fun GradientFill(spec: GradientSpec, animated: Boolean, modifier: Modifier = Modifier) {
    val colors = remember(spec.colors) { spec.colors.map { parseHexColor(it) }.let { if (it.size < 2) it + it else it } }
    val turn: State<Float> = if (animated) {
        rememberInfiniteTransition(label = "wallpaper").animateFloat(
            initialValue = 0f,
            targetValue = 360f,
            animationSpec = infiniteRepeatable(tween(durationMillis = 60_000, easing = LinearEasing)),
            label = "turn",
        )
    } else {
        remember { mutableFloatStateOf(0f) }
    }
    Spacer(
        modifier.drawWithCache {
            val degrees = turn.value
            val brush = if (spec.style == "Radial") {
                val radians = Math.toRadians(degrees.toDouble())
                val center = Offset(
                    size.width * (0.5f + 0.22f * cos(radians).toFloat()),
                    size.height * (0.38f + 0.16f * sin(radians).toFloat()),
                )
                Brush.radialGradient(colors, center = center, radius = max(size.width, size.height) * 0.85f)
            } else {
                val line = GradientLine.of(size.width, size.height, spec.angle + degrees)
                Brush.linearGradient(colors, start = Offset(line.startX, line.startY), end = Offset(line.endX, line.endY))
            }
            onDrawBehind { drawRect(brush) }
        }
    )
}

/** The picture is decoded once and shared by every screen that shows it. */
private object WallpaperPictures {
    var path: String? = null
    var modified: Long = 0
    var bitmap: ImageBitmap? = null

    @Synchronized
    fun load(path: String?): ImageBitmap? {
        if (path == null) return null
        val file = File(path)
        if (!file.isFile) return null
        if (path == this.path && file.lastModified() == modified && bitmap != null) return bitmap
        val decoded = runCatching {
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeFile(path, bounds)
            // About screen-sized: big photos are sampled down.
            var sample = 1
            while (bounds.outWidth / (sample * 2) >= 1080 && bounds.outHeight / (sample * 2) >= 1080) sample *= 2
            BitmapFactory.decodeFile(path, BitmapFactory.Options().apply { inSampleSize = sample })?.asImageBitmap()
        }.getOrNull()
        this.path = path
        modified = file.lastModified()
        bitmap = decoded
        return decoded
    }
}

@Composable
fun rememberWallpaperPicture(path: String?): ImageBitmap? = remember(path) { WallpaperPictures.load(path) }
