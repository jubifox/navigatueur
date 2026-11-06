package com.navigatueur.mobile.core

import com.navigatueur.mobile.data.AppSettings
import com.navigatueur.mobile.data.Appearance
import com.navigatueur.mobile.data.GradientSpec
import com.navigatueur.mobile.data.HistoryEntry
import com.navigatueur.mobile.data.ToolbarButtonIds
import com.navigatueur.mobile.data.migrated
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.math.abs

class ColorMathTest {
    @Test fun hex_parsesAndFormats() {
        assertEquals(0x4C8DFF, ColorMath.parseRgb("#4C8DFF"))
        assertEquals(0x4C8DFF, ColorMath.parseRgb("4c8dff"))
        assertEquals(0xFFAA00, ColorMath.parseRgb("#FA0"))
        assertNull(ColorMath.parseRgb("#12345"))
        assertNull(ColorMath.parseRgb("#GGGGGG"))
        assertNull(ColorMath.parseRgb(null))
        assertEquals("#4C8DFF", ColorMath.toHex(0x4C8DFF))
        assertEquals("#000000", ColorMath.toHex(0))
    }

    @Test fun hsv_knownColors() {
        assertEquals(0xFF0000, ColorMath.hsvToRgb(0f, 1f, 1f))
        assertEquals(0x00FF00, ColorMath.hsvToRgb(120f, 1f, 1f))
        assertEquals(0x0000FF, ColorMath.hsvToRgb(240f, 1f, 1f))
        assertEquals(0xFFFFFF, ColorMath.hsvToRgb(77f, 0f, 1f))
        assertEquals(0x000000, ColorMath.hsvToRgb(200f, 1f, 0f))
        assertEquals(0xFF0000, ColorMath.hsvToRgb(360f, 1f, 1f))
    }

    @Test fun hsv_roundTripsEveryPaletteColor() {
        (AccentPalette + WallpaperColorPalette + Gradients.presets.flatMap { it.colors }).forEach { hex ->
            val rgb = ColorMath.parseRgb(hex)!!
            val (h, s, v) = ColorMath.rgbToHsv(rgb).toList()
            val back = ColorMath.hsvToRgb(h, s, v)
            for (shift in listOf(0, 8, 16)) {
                assertTrue("$hex → ${ColorMath.toHex(back)}", abs((rgb shr shift and 0xFF) - (back shr shift and 0xFF)) <= 1)
            }
        }
    }

    @Test fun mix_andLuminance() {
        assertEquals(0x000000, ColorMath.mix(0x000000, 0xFFFFFF, 0f))
        assertEquals(0xFFFFFF, ColorMath.mix(0x000000, 0xFFFFFF, 1f))
        assertEquals(0x808080, ColorMath.mix(0x000000, 0xFFFFFF, 0.5f))
        assertTrue(ColorMath.isDark(0x0C0D0F))
        assertFalse(ColorMath.isDark(0xFFFFFF))
        assertFalse(ColorMath.isDark(0xFFD200))
        assertEquals(0x7F7F7F, ColorMath.average(listOf(0x000000, 0xFFFFFF)))
    }
}

class GradientLineTest {
    private fun assertNear(expected: Float, actual: Float) = assertEquals(expected, actual, 0.01f)

    @Test fun toTheRight_spansTheWidth() {
        val line = GradientLine.of(200f, 100f, 90f)
        assertNear(0f, line.startX); assertNear(50f, line.startY)
        assertNear(200f, line.endX); assertNear(50f, line.endY)
    }

    @Test fun toTheBottom_spansTheHeight() {
        val line = GradientLine.of(200f, 100f, 180f)
        assertNear(100f, line.startX); assertNear(0f, line.startY)
        assertNear(100f, line.endX); assertNear(100f, line.endY)
    }

    @Test fun diagonal_reachesBothCornersLikeCss() {
        // A square at 135° goes from the top-left corner to the bottom-right one.
        val line = GradientLine.of(100f, 100f, 135f)
        assertNear(0f, line.startX); assertNear(0f, line.startY)
        assertNear(100f, line.endX); assertNear(100f, line.endY)
    }
}

class LooksTest {
    private val hex = Regex("^#[0-9A-F]{6}$")

    @Test fun presets_haveUniqueIdsAndValidColors() {
        assertEquals(ThemePresets.all.size, ThemePresets.all.map { it.id }.toSet().size)
        assertEquals(Gradients.presets.size, Gradients.presets.map { it.id }.toSet().size)
        ThemePresets.all.forEach { preset ->
            assertTrue(preset.accent, hex.matches(preset.accent))
            assertTrue(preset.themeMode in setOf("Dark", "Light"))
            assertTrue(preset.wallpaper.kind in setOf("None", "Glow", "Color", "Gradient", "Image"))
            preset.wallpaper.gradient.colors.forEach { assertTrue(it, hex.matches(it)) }
        }
        Gradients.presets.forEach { preset ->
            assertTrue(preset.colors.size in 2..3)
            preset.colors.forEach { assertTrue(it, hex.matches(it)) }
        }
        (AccentPalette + WallpaperColorPalette).forEach { assertTrue(it, hex.matches(it)) }
    }

    @Test fun firstThemeIsTheDefaultLook() {
        val defaults = AppSettings()
        val first = ThemePresets.all.first()
        assertEquals(defaults.appearance.themeId, first.id)
        assertEquals(defaults.accentColorHex, first.accent)
        assertEquals(defaults.themeMode, first.themeMode)
    }

    @Test fun withTheme_setsColorsAndKeepsThePicture() {
        val start = AppSettings().let { it.copy(appearance = it.appearance.copy(wallpaper = it.appearance.wallpaper.copy(kind = "Image", imagePath = "/x.jpg"))) }
        val neon = ThemePresets.byId("neon")!!
        val themed = start.withTheme(neon)
        assertEquals("neon", themed.appearance.themeId)
        assertEquals(neon.accent, themed.accentColorHex)
        assertEquals("Gradient", themed.appearance.wallpaper.kind)
        assertTrue(themed.appearance.wallpaper.animated)
        assertEquals("/x.jpg", themed.appearance.wallpaper.imagePath)

        val reset = themed.copy(appearance = themed.appearance.copy(homeColumns = 5)).withDefaultLook()
        assertEquals(AppSettings().accentColorHex, reset.accentColorHex)
        assertEquals(4, reset.appearance.homeColumns)
        assertEquals("/x.jpg", reset.appearance.wallpaper.imagePath)
    }
}

class SettingsMigrationTest {
    private val json = Json { ignoreUnknownKeys = true; encodeDefaults = true; coerceInputValues = true }

    @Test fun v0220File_readsWithDefaultsAndKeepsItsPicture() {
        val old = """{"themeMode":"Light","accentColorHex":"#E04F4F","newTabBackgroundPath":"/data/bg.jpg","maxLiveTabs":6,"unknownFutureField":1}"""
        val settings = json.decodeFromString(AppSettings.serializer(), old).migrated()
        assertEquals("Light", settings.themeMode)
        assertEquals(6, settings.maxLiveTabs)
        assertNull(settings.newTabBackgroundPath)
        assertEquals("Image", settings.appearance.wallpaper.kind)
        assertEquals("/data/bg.jpg", settings.appearance.wallpaper.imagePath)
        assertTrue(settings.searchSuggestions)
        assertTrue(settings.pullToRefresh)
        assertEquals(0, settings.textScale)
    }

    @Test fun outOfRangeValues_arePulledBack() {
        val messy = AppSettings(
            textScale = 1000,
            appearance = Appearance(
                tint = 250,
                homeColumns = 12,
                toolbarButtons = listOf("back", "back", "teleport", "home", "share", "newTab"),
            ).let { it.copy(wallpaper = it.wallpaper.copy(dim = 99, blur = -3, colorHex = "rouge", gradient = GradientSpec(listOf("#123456", "nope"), angle = -90, style = "Spiral"))) },
        ).migrated()
        assertEquals(300, messy.textScale)
        assertEquals(100, messy.appearance.tint)
        assertEquals(6, messy.appearance.homeColumns)
        assertEquals(listOf("back", "home", "share"), messy.appearance.toolbarButtons)
        assertTrue(messy.appearance.toolbarButtons.size <= ToolbarButtonIds.MAX)
        assertEquals(80, messy.appearance.wallpaper.dim)
        assertEquals(0, messy.appearance.wallpaper.blur)
        assertEquals("#1B2333", messy.appearance.wallpaper.colorHex)
        assertEquals(listOf("#123456", "#123456"), messy.appearance.wallpaper.gradient.colors)
        assertEquals(270, messy.appearance.wallpaper.gradient.angle)
        assertEquals("Linear", messy.appearance.wallpaper.gradient.style)
    }

    @Test fun settings_roundTripThroughJson() {
        val settings = AppSettings().withTheme(ThemePresets.byId("foret")!!)
        val text = json.encodeToString(AppSettings.serializer(), settings)
        assertEquals(settings, json.decodeFromString(AppSettings.serializer(), text))
    }
}

class TopSitesTest {
    private val day = 86_400_000L
    private val now = 100 * day

    private fun visit(url: String, daysAgo: Int) = HistoryEntry(url, "", now - daysAgo * day)

    @Test fun mostVisitedFirst_searchPagesAndHiddenLeftOut() {
        val history = listOf(
            visit("https://fr.wikipedia.org/wiki/A", 3),
            visit("https://fr.wikipedia.org/wiki/B", 2),
            visit("https://www.youtube.com/watch?v=1", 5),
            visit("https://www.youtube.com/watch?v=2", 4),
            visit("https://www.youtube.com/watch?v=3", 1),
            visit("https://www.bing.com/search?q=meteo", 1),
            visit("https://www.bing.com/search?q=chat", 1),
            visit("https://www.bing.com/search?q=train", 1),
            visit("https://news.example.org/x", 1),
            visit("https://old.example.net/", 90),
        )
        val sites = TopSites.compute(history, hidden = listOf("news.example.org"), now = now)
        assertEquals(listOf("youtube.com", "fr.wikipedia.org"), sites.map { it.host })
        assertEquals("https://www.youtube.com/", sites[0].url)
        assertEquals("youtube", sites[0].label)
        assertEquals(3, sites[0].visits)
    }

    @Test fun tie_mostRecentFirst() {
        val sites = TopSites.compute(listOf(visit("https://a.com/", 5), visit("https://b.com/", 1)), emptyList(), now)
        assertEquals(listOf("b.com", "a.com"), sites.map { it.host })
    }

    @Test fun labels() {
        assertEquals("wikipedia", TopSites.labelOf("fr.wikipedia.org"))
        assertEquals("bbc", TopSites.labelOf("bbc.co.uk"))
        assertEquals("service-public", TopSites.labelOf("www.service-public.gouv.fr"))
        assertEquals("localhost", TopSites.labelOf("localhost"))
        assertEquals("https://example.com:8443/", TopSites.frontPage("https://Example.com:8443/a/b?c"))
    }
}

class SearchSuggestionsTest {
    @Test fun openSearchFormat() {
        val body = """["météo",["météo paris","Météo","météo demain","météo paris"]]"""
        assertEquals(listOf("météo paris", "météo demain"), SearchSuggestions.parse(body, "météo"))
    }

    @Test fun qwantFormat() {
        val body = """{"status":"success","data":{"items":[{"value":"chat noir","suggestType":12},{"value":"chat gpt"}]}}"""
        assertEquals(listOf("chat noir", "chat gpt"), SearchSuggestions.parse(body, "chat"))
    }

    @Test fun garbage_givesNothing() {
        assertEquals(emptyList<String>(), SearchSuggestions.parse("<html>", "x"))
        assertEquals(emptyList<String>(), SearchSuggestions.parse("""["x"]""", "x"))
        assertEquals(emptyList<String>(), SearchSuggestions.parse("""{"data":{}}""", "x"))
    }

    @Test fun limit() {
        val body = """["a",["a1","a2","a3","a4","a5","a6","a7"]]"""
        assertEquals(5, SearchSuggestions.parse(body, "a").size)
        assertEquals(2, SearchSuggestions.parse(body, "a", limit = 2).size)
    }

    @Test fun everyEngineHasAService() {
        SearchEngines.all.forEach { engine ->
            val url = SearchSuggestions.url(engine.id, "chat noir")
            assertTrue(engine.id, url != null && url.startsWith("https://") && url.endsWith("chat+noir"))
        }
        assertNull(SearchSuggestions.url("Bing", "   "))
        assertNull(SearchSuggestions.url("Inconnu", "x"))
    }
}
