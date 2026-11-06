package com.navigatueur.mobile.data

import kotlinx.serialization.Serializable

/**
 * Everything Settings exposes. Field names follow the Windows app's
 * settings.json where the setting is the same (ThemeMode, AccentColorHex,
 * SearchEngine…); unknown or missing fields fall back to these defaults.
 */
@Serializable
data class AppSettings(
    /** "Dark", "Light" or "System". */
    val themeMode: String = "Dark",
    val accentColorHex: String = "#4C8DFF",
    val searchEngine: String = "Bing",
    /** Empty: the Navigatueur home page. */
    val homePageUrl: String = "",
    /** "RestoreSession" or "HomePage". */
    val startupBehavior: String = "RestoreSession",
    /** "Bottom" or "Top". */
    val toolbarPosition: String = "Bottom",
    val hideToolbarOnScroll: Boolean = true,
    /** v0.22.0's home picture; moved to [Appearance.wallpaper] on load (see [migrated]). */
    val newTabBackgroundPath: String? = null,
    val showFavoritesOnHome: Boolean = true,
    /** Dark Reader on for every site (per-site choices live in Dark Reader itself). */
    val forceDarkSites: Boolean = false,
    val phishingProtection: Boolean = true,
    val trackingProtection: Boolean = true,
    val httpsOnly: Boolean = false,
    val globalPrivacyControl: Boolean = true,
    val autoPictureInPicture: Boolean = true,
    val backgroundPlayback: Boolean = true,
    val maxLiveTabs: Int = 4,
    val suspendMinutes: Int = 15,
    /** Tab overview: "Grid" (pictures of the pages) or "List". */
    val tabLayout: String = "Grid",
    /** Built-in extensions the user switched on or off themselves (id → enabled), over the catalog's defaults. */
    val extensionChoices: Map<String, Boolean> = emptyMap(),
    /** Catalog extensions the user removed: not installed again on the next start. */
    val removedCatalogExtensions: List<String> = emptyList(),
    val hasSeenIntro: Boolean = false,
    val lastUpdateCheck: Long = 0,
    val lastExtensionUpdateCheck: Long = 0,
    /** Suggestions from the search engine while typing (never for private tabs). */
    val searchSuggestions: Boolean = true,
    /** Pulling down at the top of a page reloads it. */
    val pullToRefresh: Boolean = true,
    /** Swipes on the toolbar: sideways for the next tab, up (down for a top toolbar) for the tab overview. */
    val toolbarGestures: Boolean = true,
    /** Text size of pages, in percent; 0 follows Android's font size. */
    val textScale: Int = 0,
    /** Pinch zoom everywhere, even on sites that forbid it. */
    val forceZoom: Boolean = false,
    /** Sites the user removed from the home page's frequent sites. */
    val hiddenTopSites: List<String> = emptyList(),
    val appearance: Appearance = Appearance(),
)

/** Everything "Personnaliser" sets: colors, wallpaper, toolbar, shapes, home page. */
@Serializable
data class Appearance(
    /** The ready-made theme last applied, as long as nothing was changed since (shown as selected). */
    val themeId: String? = "navigatueur",
    /** Black panels and background in the dark theme (OLED screens). */
    val pureBlack: Boolean = false,
    /** How much the accent color tints the app's panels, 0–100. */
    val tint: Int = 0,
    val wallpaper: Wallpaper = Wallpaper(),
    /** The wallpaper also behind the tab overview, settings and lists, with see-through panels. */
    val wallpaperEverywhere: Boolean = false,
    /** "Solid" or "Glass" (see-through). */
    val toolbarStyle: String = "Solid",
    /** "Compact", "Normal" or "Large". */
    val toolbarSize: String = "Normal",
    /** Extra toolbar buttons, in order (see [ToolbarButtonIds]). */
    val toolbarButtons: List<String> = emptyList(),
    /** "Square", "Rounded" or "Round". */
    val corners: String = "Rounded",
    /** Big title of the home page; empty: none. */
    val homeTitle: String = "NAVIGATUEUR",
    val homeClock: Boolean = false,
    val homeTopSites: Boolean = true,
    val homeColumns: Int = 4,
)

@Serializable
data class Wallpaper(
    /** "None", "Glow" (a soft halo of the accent color), "Color", "Gradient" or "Image". */
    val kind: String = "Glow",
    val colorHex: String = "#1B2333",
    val gradient: GradientSpec = GradientSpec(),
    /** Copy of the picked picture in the app's own files. */
    val imagePath: String? = null,
    /** Darkening over the picture, 0–80 %. */
    val dim: Int = 35,
    /** Blur of the picture, 0–24 (Android 12 and later). */
    val blur: Int = 0,
    /** The gradient turns slowly. */
    val animated: Boolean = false,
)

@Serializable
data class GradientSpec(
    /** Two or three "#RRGGBB" colors. */
    val colors: List<String> = listOf("#4C8DFF", "#B14FE0"),
    /** Direction like CSS's linear-gradient: 0 = toward the top, 90 = toward the right, 180 = toward the bottom. */
    val angle: Int = 135,
    /** "Linear" or "Radial". */
    val style: String = "Linear",
)

object ToolbarButtonIds {
    const val BACK = "back"
    const val FORWARD = "forward"
    const val HOME = "home"
    const val NEW_TAB = "newTab"
    const val SHARE = "share"
    const val BOOKMARK = "bookmark"
    val all = listOf(BACK, FORWARD, HOME, NEW_TAB, SHARE, BOOKMARK)
    const val MAX = 3
}

private val hexColor = Regex("^#[0-9A-Fa-f]{6}$")

/** Settings written by an older version, brought up to date (v0.22.0's home picture becomes the "Image" wallpaper). */
fun AppSettings.migrated(): AppSettings {
    var settings = this
    val legacyImage = newTabBackgroundPath
    if (legacyImage != null) {
        val wallpaper = settings.appearance.wallpaper
        if (wallpaper.imagePath == null) {
            settings = settings.copy(appearance = settings.appearance.copy(wallpaper = wallpaper.copy(kind = "Image", imagePath = legacyImage)))
        }
        settings = settings.copy(newTabBackgroundPath = null)
    }
    return settings.copy(appearance = settings.appearance.normalized(), textScale = if (settings.textScale == 0) 0 else settings.textScale.coerceIn(50, 300))
}

/** Values out of range (hand-edited file, older or newer version) pulled back to something the app can show. */
fun Appearance.normalized(): Appearance = copy(
    tint = tint.coerceIn(0, 100),
    homeColumns = homeColumns.coerceIn(3, 6),
    toolbarButtons = toolbarButtons.filter { it in ToolbarButtonIds.all }.distinct().take(ToolbarButtonIds.MAX),
    wallpaper = wallpaper.copy(
        dim = wallpaper.dim.coerceIn(0, 80),
        blur = wallpaper.blur.coerceIn(0, 24),
        colorHex = wallpaper.colorHex.takeIf { hexColor.matches(it) } ?: Wallpaper().colorHex,
        gradient = wallpaper.gradient.normalized(),
    ),
)

fun GradientSpec.normalized(): GradientSpec {
    val valid = colors.filter { hexColor.matches(it) }.take(3)
    return copy(
        colors = when (valid.size) {
            0 -> GradientSpec().colors
            1 -> listOf(valid[0], valid[0])
            else -> valid
        },
        angle = ((angle % 360) + 360) % 360,
        style = if (style == "Radial") "Radial" else "Linear",
    )
}

@Serializable
data class HistoryEntry(val url: String, val title: String, val visitedAt: Long)

@Serializable
data class Bookmark(val url: String, val title: String, val addedAt: Long)

@Serializable
data class SavedGroup(
    val id: String,
    val name: String,
    val colorHex: String,
    val isCollapsed: Boolean = false,
)

@Serializable
data class SavedTab(
    val id: String,
    val url: String,
    val title: String,
    val groupId: String? = null,
    val isPinned: Boolean = false,
    val isDesktopMode: Boolean = false,
    /** GeckoSession.SessionState.toString(): back/forward history and scroll position. */
    val engineState: String? = null,
)

@Serializable
data class SavedSession(
    val tabs: List<SavedTab> = emptyList(),
    val groups: List<SavedGroup> = emptyList(),
    val activeTabId: String? = null,
)

@Serializable
data class DownloadRecord(
    val id: String,
    val fileName: String,
    val url: String,
    val mimeType: String?,
    /** content:// URI of the file in Downloads, once written. */
    val contentUri: String? = null,
    val totalBytes: Long = -1,
    val receivedBytes: Long = 0,
    /** "Running", "Done", "Failed", "Cancelled". */
    val state: String = "Running",
    val startedAt: Long = System.currentTimeMillis(),
)

/** A remembered answer to a site's request (camera, location, notifications…). */
@Serializable
data class SitePermission(val host: String, val kind: String, val allowed: Boolean)
