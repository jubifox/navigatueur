package com.navigatueur.mobile.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.core.AccentPalette
import com.navigatueur.mobile.core.ColorMath
import com.navigatueur.mobile.ui.theme.Nv
import com.navigatueur.mobile.ui.theme.parseHexColor

/** An 0xRRGGBB int as an opaque Compose color. */
fun rgbColor(rgb: Int): Color = Color(0xFF000000.toInt() or rgb)

/**
 * Any color: hue, saturation and brightness sliders, a hex code field and
 * quick swatches, with the result shown as it changes.
 */
@Composable
fun ColorPickerDialog(title: String, initialHex: String, onDismiss: () -> Unit, onPick: (String) -> Unit) {
    val colors = Nv.colors
    val start = remember { ColorMath.parseRgb(initialHex) ?: 0x4C8DFF }
    val startHsv = remember { ColorMath.rgbToHsv(start) }
    var hue by remember { mutableFloatStateOf(startHsv[0]) }
    var saturation by remember { mutableFloatStateOf(startHsv[1]) }
    var brightness by remember { mutableFloatStateOf(startHsv[2]) }
    var code by remember { mutableStateOf(ColorMath.toHex(start)) }
    val rgb = ColorMath.hsvToRgb(hue, saturation, brightness)

    fun fromSliders() {
        code = ColorMath.toHex(ColorMath.hsvToRgb(hue, saturation, brightness))
    }

    fun fromRgb(value: Int) {
        val hsv = ColorMath.rgbToHsv(value)
        // A grey has no hue: keep the slider where it was.
        if (hsv[1] > 0f) hue = hsv[0]
        saturation = hsv[1]
        brightness = hsv[2]
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(14.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(
                        Modifier
                            .size(52.dp)
                            .clip(CircleShape)
                            .background(rgbColor(rgb))
                            .border(1.dp, colors.panelBorder.copy(alpha = 1f), CircleShape)
                    )
                    Spacer(Modifier.width(14.dp))
                    OutlinedTextField(
                        value = code,
                        onValueChange = { text ->
                            code = text.take(7)
                            ColorMath.parseRgb(code)?.takeIf { code.removePrefix("#").length == 6 }?.let(::fromRgb)
                        },
                        singleLine = true,
                        label = { Text("Code") },
                        keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters, autoCorrectEnabled = false),
                        modifier = Modifier.weight(1f),
                    )
                }

                val rainbow = remember { listOf(0f, 60f, 120f, 180f, 240f, 300f, 360f).map { rgbColor(ColorMath.hsvToRgb(it, 1f, 1f)) } }
                HueSaturationSlider("Teinte", hue / 360f, Brush.horizontalGradient(rainbow), rgbColor(ColorMath.hsvToRgb(hue, 1f, 1f))) {
                    hue = it * 360f
                    fromSliders()
                }
                HueSaturationSlider(
                    "Intensité", saturation,
                    Brush.horizontalGradient(listOf(rgbColor(ColorMath.hsvToRgb(hue, 0f, brightness)), rgbColor(ColorMath.hsvToRgb(hue, 1f, brightness)))),
                    rgbColor(rgb),
                ) {
                    saturation = it
                    fromSliders()
                }
                HueSaturationSlider(
                    "Luminosité", brightness,
                    Brush.horizontalGradient(listOf(Color.Black, rgbColor(ColorMath.hsvToRgb(hue, saturation, 1f)))),
                    rgbColor(rgb),
                ) {
                    brightness = it
                    fromSliders()
                }

                // Quick picks.
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    AccentPalette.chunked(6).forEach { row ->
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                            row.forEach { hex ->
                                Box(
                                    Modifier
                                        .size(30.dp)
                                        .clip(CircleShape)
                                        .background(parseHexColor(hex))
                                        .border(1.dp, colors.panelBorder.copy(alpha = 1f), CircleShape)
                                        .clickable {
                                            code = hex
                                            ColorMath.parseRgb(hex)?.let(::fromRgb)
                                        }
                                )
                            }
                        }
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = { onPick(ColorMath.toHex(rgb)) }) { Text("Choisir") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Annuler") } },
    )
}

/** A track filled with [brush] and a round thumb showing [thumbColor]; tap or drag anywhere on it. */
@Composable
private fun HueSaturationSlider(label: String, fraction: Float, brush: Brush, thumbColor: Color, onChange: (Float) -> Unit) {
    val colors = Nv.colors
    val change by rememberUpdatedState(onChange)
    Column {
        Text(label, color = colors.textSecondary, fontSize = 13.sp)
        Spacer(Modifier.height(4.dp))
        BoxWithConstraints(
            Modifier
                .fillMaxWidth()
                .height(32.dp)
                .pointerInput(Unit) {
                    detectTapGestures { offset -> change((offset.x / size.width).coerceIn(0f, 1f)) }
                }
                .pointerInput(Unit) {
                    detectHorizontalDragGestures { pointer, _ ->
                        pointer.consume()
                        change((pointer.position.x / size.width).coerceIn(0f, 1f))
                    }
                },
        ) {
            Box(
                Modifier
                    .align(Alignment.Center)
                    .fillMaxWidth()
                    .height(14.dp)
                    .clip(RoundedCornerShape(7.dp))
                    .background(brush)
            )
            Box(
                Modifier
                    .align(Alignment.CenterStart)
                    .offset(x = (maxWidth - 26.dp) * fraction.coerceIn(0f, 1f))
                    .size(26.dp)
                    .shadow(3.dp, CircleShape)
                    .clip(CircleShape)
                    .background(Color.White)
                    .padding(3.dp)
                    .clip(CircleShape)
                    .background(thumbColor)
            )
        }
    }
}
