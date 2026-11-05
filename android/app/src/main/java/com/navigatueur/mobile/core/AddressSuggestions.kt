package com.navigatueur.mobile.core

enum class SuggestionKind { Bookmark, History, Tab }

data class SuggestionSource(val url: String, val title: String, val kind: SuggestionKind)

data class AddressSuggestion(val url: String, val title: String, val kind: SuggestionKind, val score: Int)

/**
 * Ranks bookmarks, history and open tabs against what's being typed — the
 * Windows app's AddressSuggestions: a match at the start of the host beats
 * one at the start of a title word, which beats a match anywhere; bookmarks
 * get a boost; each page appears once.
 */
object AddressSuggestions {
    fun rank(query: String, sources: Sequence<SuggestionSource>, limit: Int = 6): List<AddressSuggestion> {
        val needle = query.trim()
        if (needle.isEmpty() || limit <= 0) return emptyList()

        val best = HashMap<String, AddressSuggestion>()
        for (source in sources) {
            if (source.url.isBlank() || !UrlHelper.isWebUrl(source.url)) continue
            val score = score(needle, source)
            if (score <= 0) continue
            val key = UrlHelper.normalizeKey(source.url)
            val existing = best[key]
            if (existing == null || existing.score < score) {
                best[key] = AddressSuggestion(source.url, source.title, source.kind, score)
            }
        }

        return best.values
            .sortedWith(compareByDescending<AddressSuggestion> { it.score }.thenBy { it.url.length })
            .take(limit)
    }

    private fun score(needle: String, source: SuggestionSource): Int {
        val host = UrlHelper.displayHost(source.url) ?: ""
        val urlWithoutScheme = stripScheme(source.url)
        val title = source.title

        val score = when {
            host.startsWith(needle, ignoreCase = true) -> 100
            urlWithoutScheme.startsWith(needle, ignoreCase = true) -> 90
            startsAnyWord(title, needle) -> 70
            urlWithoutScheme.contains(needle, ignoreCase = true) -> 50
            title.contains(needle, ignoreCase = true) -> 40
            else -> return 0
        }

        return when (source.kind) {
            SuggestionKind.Bookmark -> score + 15
            SuggestionKind.Tab -> score + 10
            SuggestionKind.History -> score
        }
    }

    private fun stripScheme(url: String): String {
        val index = url.indexOf("://")
        val rest = if (index < 0) url else url.substring(index + 3)
        return if (rest.startsWith("www.", ignoreCase = true)) rest.substring(4) else rest
    }

    private fun startsAnyWord(text: String, needle: String): Boolean =
        text.split(' ', '-', '|', '·', ':', '/').any { it.isNotEmpty() && it.startsWith(needle, ignoreCase = true) }
}
