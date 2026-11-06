package com.navigatueur.mobile.core

import com.navigatueur.mobile.data.HistoryEntry
import java.net.URI

/** A site of the home page's "Sites fréquents". */
data class TopSite(
    /** Host without "www.", also the key the user hides it by. */
    val host: String,
    /** The site's front page, with the scheme and host last visited. */
    val url: String,
    /** Short name under the icon: "wikipedia" for fr.wikipedia.org. */
    val label: String,
    val visits: Int,
    val lastVisit: Long,
)

object TopSites {
    /**
     * The sites visited most over the last [days] days (search result pages
     * don't count), most visited first, then most recent; [hidden] hosts
     * are left out.
     */
    fun compute(history: List<HistoryEntry>, hidden: Collection<String>, now: Long, limit: Int = 8, days: Int = 60): List<TopSite> {
        val since = now - days * 86_400_000L
        val hiddenSet = hidden.toSet()
        class Tally(var visits: Int, var lastVisit: Long, var url: String)
        val tallies = LinkedHashMap<String, Tally>()
        for (entry in history) {
            if (entry.visitedAt < since || !UrlHelper.isWebUrl(entry.url) || SearchEngines.searchTermsOf(entry.url) != null) continue
            val host = UrlHelper.displayHost(entry.url) ?: continue
            if (host in hiddenSet) continue
            val front = frontPage(entry.url) ?: continue
            val tally = tallies.getOrPut(host) { Tally(0, 0, front) }
            tally.visits++
            if (entry.visitedAt >= tally.lastVisit) {
                tally.lastVisit = entry.visitedAt
                tally.url = front
            }
        }
        return tallies.entries
            .sortedWith(compareByDescending<Map.Entry<String, Tally>> { it.value.visits }.thenByDescending { it.value.lastVisit })
            .take(limit)
            .map { (host, tally) -> TopSite(host, tally.url, labelOf(host), tally.visits, tally.lastVisit) }
    }

    /** "https://fr.wikipedia.org/wiki/X" → "https://fr.wikipedia.org/". */
    fun frontPage(url: String): String? = runCatching {
        val uri = URI(url)
        val host = uri.host ?: return null
        val port = if (uri.port > 0) ":${uri.port}" else ""
        "${uri.scheme.lowercase()}://${host.lowercase()}$port/"
    }.getOrNull()

    /** The name people know a site by: the part before the public suffix ("youtube", "wikipedia", "bbc"). */
    fun labelOf(host: String): String {
        val parts = host.lowercase().split('.').filter { it.isNotEmpty() }
        if (parts.size < 2) return host
        // co.uk, com.au, gouv.fr…: the name is one step further left.
        val secondLevel = setOf("co", "com", "gouv", "gov", "org", "net", "ac", "edu")
        val index = if (parts.size >= 3 && parts[parts.size - 2] in secondLevel && parts.last().length == 2) parts.size - 3 else parts.size - 2
        return parts[index]
    }
}
