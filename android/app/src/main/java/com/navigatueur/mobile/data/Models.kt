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
    /** Copy of the picked picture in the app's own files. */
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
    val tabLayout: String = "Grid",
    /** Built-in extensions the user switched on or off themselves (id → enabled), over the catalog's defaults. */
    val extensionChoices: Map<String, Boolean> = emptyMap(),
    /** Catalog extensions the user removed: not installed again on the next start. */
    val removedCatalogExtensions: List<String> = emptyList(),
    val hasSeenIntro: Boolean = false,
    val lastUpdateCheck: Long = 0,
    val lastExtensionUpdateCheck: Long = 0,
)

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
