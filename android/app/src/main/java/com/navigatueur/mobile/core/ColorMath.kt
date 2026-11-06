package com.navigatueur.mobile.core

import java.util.Locale
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow
import kotlin.math.roundToInt
import kotlin.math.sin

/**
 * Colors as 0xRRGGBB ints, without Android's Color class (so the unit
 * tests run on the JVM): hex text, hue/saturation/brightness for the color
 * picker, mixing for the tinted interface, luminance for readable text.
 */
object ColorMath {
    /** "#RRGGBB", "RRGGBB" or "#RGB" → 0xRRGGBB; anything else → null. */
    fun parseRgb(text: String?): Int? {
        val clean = text?.trim()?.removePrefix("#") ?: return null
        val full = when (clean.length) {
            3 -> clean.map { "$it$it" }.joinToString("")
            6 -> clean
            else -> return null
        }
        if (!full.all { it.isDigit() || it.lowercaseChar() in 'a'..'f' }) return null
        return full.toInt(16)
    }

    fun toHex(rgb: Int): String = String.format(Locale.ROOT, "#%06X", rgb and 0xFFFFFF)

    /** Hue 0–360, saturation and brightness 0–1 → 0xRRGGBB. */
    fun hsvToRgb(hue: Float, saturation: Float, value: Float): Int {
        val h = ((hue % 360f) + 360f) % 360f
        val s = saturation.coerceIn(0f, 1f)
        val v = value.coerceIn(0f, 1f)
        val c = v * s
        val x = c * (1 - abs((h / 60f) % 2 - 1))
        val m = v - c
        val (r, g, b) = when ((h / 60f).toInt()) {
            0 -> Triple(c, x, 0f)
            1 -> Triple(x, c, 0f)
            2 -> Triple(0f, c, x)
            3 -> Triple(0f, x, c)
            4 -> Triple(x, 0f, c)
            else -> Triple(c, 0f, x)
        }
        fun channel(f: Float) = ((f + m) * 255f).roundToInt().coerceIn(0, 255)
        return (channel(r) shl 16) or (channel(g) shl 8) or channel(b)
    }

    /** 0xRRGGBB → [hue 0–360, saturation 0–1, brightness 0–1]. */
    fun rgbToHsv(rgb: Int): FloatArray {
        val r = (rgb shr 16 and 0xFF) / 255f
        val g = (rgb shr 8 and 0xFF) / 255f
        val b = (rgb and 0xFF) / 255f
        val maxC = max(r, max(g, b))
        val minC = min(r, min(g, b))
        val delta = maxC - minC
        val hue = when {
            delta == 0f -> 0f
            maxC == r -> 60f * (((g - b) / delta) % 6f)
            maxC == g -> 60f * (((b - r) / delta) + 2f)
            else -> 60f * (((r - g) / delta) + 4f)
        }.let { if (it < 0) it + 360f else it }
        val saturation = if (maxC == 0f) 0f else delta / maxC
        return floatArrayOf(hue, saturation, maxC)
    }

    /** [a] moved toward [b] by [t] (0: a, 1: b). */
    fun mix(a: Int, b: Int, t: Float): Int {
        val k = t.coerceIn(0f, 1f)
        fun lerp(shift: Int): Int {
            val x = a shr shift and 0xFF
            val y = b shr shift and 0xFF
            return (x + (y - x) * k).roundToInt().coerceIn(0, 255)
        }
        return (lerp(16) shl 16) or (lerp(8) shl 8) or lerp(0)
    }

    /** Relative luminance (WCAG), 0 for black to 1 for white. */
    fun luminance(rgb: Int): Double {
        fun linear(channel: Int): Double {
            val c = channel / 255.0
            return if (c <= 0.03928) c / 12.92 else ((c + 0.055) / 1.055).pow(2.4)
        }
        return 0.2126 * linear(rgb shr 16 and 0xFF) + 0.7152 * linear(rgb shr 8 and 0xFF) + 0.0722 * linear(rgb and 0xFF)
    }

    /** Light text reads better on it. */
    fun isDark(rgb: Int): Boolean = luminance(rgb) < 0.36

    /** Average of several colors (a gradient's overall look). */
    fun average(colors: List<Int>): Int {
        if (colors.isEmpty()) return 0
        var r = 0
        var g = 0
        var b = 0
        colors.forEach { r += it shr 16 and 0xFF; g += it shr 8 and 0xFF; b += it and 0xFF }
        return ((r / colors.size) shl 16) or ((g / colors.size) shl 8) or (b / colors.size)
    }
}

/**
 * Where a linear gradient starts and ends on a [width] × [height] area, for
 * a CSS-like [angleDegrees] (0 toward the top, 90 toward the right): the
 * line goes through the center and is long enough for the corners to get
 * the first and last colors, as in CSS.
 */
data class GradientLine(val startX: Float, val startY: Float, val endX: Float, val endY: Float) {
    companion object {
        fun of(width: Float, height: Float, angleDegrees: Float): GradientLine {
            val radians = Math.toRadians(angleDegrees.toDouble())
            val dx = sin(radians).toFloat()
            val dy = -cos(radians).toFloat()
            val half = (abs(width * dx) + abs(height * dy)) / 2f
            val cx = width / 2f
            val cy = height / 2f
            return GradientLine(cx - dx * half, cy - dy * half, cx + dx * half, cy + dy * half)
        }
    }
}
