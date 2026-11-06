package com.navigatueur.mobile.data

import android.content.Context
import com.navigatueur.mobile.core.SuggestionKind
import com.navigatueur.mobile.core.SuggestionSource
import com.navigatueur.mobile.core.UrlHelper
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.serialization.KSerializer
import kotlinx.serialization.builtins.ListSerializer
import kotlinx.serialization.json.Json
import java.io.File

val AppJson = Json {
    ignoreUnknownKeys = true
    encodeDefaults = true
    prettyPrint = false
    coerceInputValues = true
}

/**
 * One JSON file in the app's private storage, held in memory as a
 * StateFlow. Writes are debounced and atomic (temp file + rename), so a
 * crash mid-write never leaves a half-written file; a file that can't be
 * read (hand-edited, older format) falls back to the default value.
 */
class JsonStore<T>(
    private val file: File,
    private val serializer: KSerializer<T>,
    private val default: () -> T,
    private val scope: CoroutineScope,
    private val debounceMillis: Long = 400,
) {
    private val state = MutableStateFlow(load())
    val flow: StateFlow<T> = state.asStateFlow()
    val value: T get() = state.value
    private var pendingWrite: Job? = null

    private fun load(): T = runCatching {
        if (file.isFile) AppJson.decodeFromString(serializer, file.readText()) else default()
    }.getOrElse { default() }

    fun update(change: (T) -> T) {
        state.update(change)
        scheduleWrite()
    }

    fun set(value: T) = update { value }

    private fun scheduleWrite() {
        pendingWrite?.cancel()
        pendingWrite = scope.launch(Dispatchers.IO) {
            delay(debounceMillis)
            writeNow()
        }
    }

    /** Writes immediately (app going to the background). */
    fun flush() {
        pendingWrite?.cancel()
        writeNow()
    }

    @Synchronized
    private fun writeNow() {
        runCatching {
            file.parentFile?.mkdirs()
            val temp = File(file.parentFile, file.name + ".tmp")
            temp.writeText(AppJson.encodeToString(serializer, state.value))
            if (!temp.renameTo(file)) {
                file.delete()
                temp.renameTo(file)
            }
        }
    }
}

/** All of the app's saved data. Created once by NavigatueurApp. */
class Stores(context: Context) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val dir = File(context.filesDir, "navigatueur")

    val settings = JsonStore(File(dir, "settings.json"), AppSettings.serializer(), { AppSettings() }, scope).apply {
        // A file from an older version is brought up to date once.
        val current = value
        val migrated = current.migrated()
        if (migrated != current) set(migrated)
    }
    val history = JsonStore(File(dir, "history.json"), ListSerializer(HistoryEntry.serializer()), { emptyList() }, scope, 1500)
    val bookmarks = JsonStore(File(dir, "bookmarks.json"), ListSerializer(Bookmark.serializer()), { emptyList() }, scope)
    val session = JsonStore(File(dir, "session.json"), SavedSession.serializer(), { SavedSession() }, scope, 1000)
    val downloads = JsonStore(File(dir, "downloads.json"), ListSerializer(DownloadRecord.serializer()), { emptyList() }, scope)
    val permissions = JsonStore(File(dir, "permissions.json"), ListSerializer(SitePermission.serializer()), { emptyList() }, scope)

    val backgroundsDir = File(dir, "backgrounds")
    val faviconsDir = File(context.cacheDir, "favicons")

    fun flushAll() {
        settings.flush(); history.flush(); bookmarks.flush(); session.flush(); downloads.flush(); permissions.flush()
    }

    // ------------------------------------------------------------ history

    fun recordVisit(url: String, title: String) {
        if (!UrlHelper.isWebUrl(url)) return
        val now = System.currentTimeMillis()
        history.update { list ->
            // A reload or an in-page step of the same page within a minute updates the last entry instead of adding one.
            val last = list.lastOrNull()
            val updated = if (last != null && last.url == url && now - last.visitedAt < 60_000) {
                list.dropLast(1) + last.copy(title = title.ifBlank { last.title }, visitedAt = now)
            } else {
                list + HistoryEntry(url, title, now)
            }
            if (updated.size > MAX_HISTORY) updated.takeLast(MAX_HISTORY) else updated
        }
    }

    fun updateLastTitle(url: String, title: String) {
        if (title.isBlank()) return
        history.update { list ->
            val index = list.indexOfLast { it.url == url }
            if (index < 0 || list[index].title == title) list
            else list.toMutableList().also { it[index] = it[index].copy(title = title) }
        }
    }

    // ---------------------------------------------------------- bookmarks

    fun isBookmarked(url: String?): Boolean {
        if (url == null) return false
        val key = UrlHelper.normalizeKey(url)
        return bookmarks.value.any { UrlHelper.normalizeKey(it.url) == key }
    }

    fun toggleBookmark(url: String, title: String): Boolean {
        val key = UrlHelper.normalizeKey(url)
        var added = false
        bookmarks.update { list ->
            if (list.any { UrlHelper.normalizeKey(it.url) == key }) {
                list.filterNot { UrlHelper.normalizeKey(it.url) == key }
            } else {
                added = true
                list + Bookmark(url, title.ifBlank { UrlHelper.displayHost(url) ?: url }, System.currentTimeMillis())
            }
        }
        return added
    }

    /** Renames a favorite or corrects its address. */
    fun updateBookmark(oldUrl: String, title: String, url: String) {
        val key = UrlHelper.normalizeKey(oldUrl)
        bookmarks.update { list ->
            list.map { if (UrlHelper.normalizeKey(it.url) == key) it.copy(title = title.trim().ifBlank { it.title }, url = url.trim().ifBlank { it.url }) else it }
        }
    }

    fun suggestionSources(): Sequence<SuggestionSource> =
        bookmarks.value.asSequence().map { SuggestionSource(it.url, it.title, SuggestionKind.Bookmark) } +
            history.value.asReversed().asSequence().map { SuggestionSource(it.url, it.title, SuggestionKind.History) }

    // -------------------------------------------------------- permissions

    fun rememberedPermission(host: String, kind: String): Boolean? =
        permissions.value.firstOrNull { it.host == host && it.kind == kind }?.allowed

    fun rememberPermission(host: String, kind: String, allowed: Boolean) {
        permissions.update { list -> list.filterNot { it.host == host && it.kind == kind } + SitePermission(host, kind, allowed) }
    }

    companion object {
        const val MAX_HISTORY = 5000
    }
}
