package com.navigatueur.mobile.data

import android.content.Context
import com.navigatueur.mobile.model.Bookmark
import com.navigatueur.mobile.model.BrowserSettings
import com.navigatueur.mobile.model.HistoryEntry
import kotlinx.serialization.json.Json
import kotlinx.serialization.builtins.ListSerializer
import java.io.File

/**
 * Same idea as the desktop app's settings.json persistence — plain JSON files
 * on disk, no database — just scaled down to what a phone browser actually
 * needs (history/bookmarks/settings, no session-restore of live tab state;
 * see BrowserTab's doc comment for why tabs themselves aren't persisted here).
 */
class BrowserRepository(context: Context) {
    private val json = Json { ignoreUnknownKeys = true; prettyPrint = false }
    private val historyFile = File(context.filesDir, "history.json")
    private val bookmarksFile = File(context.filesDir, "bookmarks.json")
    private val settingsFile = File(context.filesDir, "settings.json")

    fun loadHistory(): List<HistoryEntry> = readList(historyFile, HistoryEntry.serializer())

    fun saveHistory(entries: List<HistoryEntry>) = writeList(historyFile, HistoryEntry.serializer(), entries)

    fun loadBookmarks(): List<Bookmark> = readList(bookmarksFile, Bookmark.serializer())

    fun saveBookmarks(entries: List<Bookmark>) = writeList(bookmarksFile, Bookmark.serializer(), entries)

    fun loadSettings(): BrowserSettings =
        if (settingsFile.exists()) {
            runCatching { json.decodeFromString(BrowserSettings.serializer(), settingsFile.readText()) }
                .getOrDefault(BrowserSettings())
        } else {
            BrowserSettings()
        }

    fun saveSettings(settings: BrowserSettings) {
        settingsFile.writeText(json.encodeToString(BrowserSettings.serializer(), settings))
    }

    private fun <T> readList(file: File, serializer: kotlinx.serialization.KSerializer<T>): List<T> =
        if (file.exists()) {
            runCatching { json.decodeFromString(ListSerializer(serializer), file.readText()) }.getOrDefault(emptyList())
        } else {
            emptyList()
        }

    private fun <T> writeList(file: File, serializer: kotlinx.serialization.KSerializer<T>, values: List<T>) {
        file.writeText(json.encodeToString(ListSerializer(serializer), values))
    }
}
