package com.navigatueur.mobile.data

import android.content.Context

/**
 * Domain-exact-match blocking against the same blocklist the desktop app
 * ships (Resources/AdBlock/blocklist-domains.txt, copied into assets/ here)
 * so both platforms block the same things. Simpler than the desktop's
 * AdBlockService (no cosmetic ##selector hiding or scriptlet injection here —
 * out of scope for v1 of the mobile port), but the actual request-blocking
 * behavior is the same idea: exact host lookup in a HashSet, O(1) per request.
 */
class AdBlockManager private constructor(private val blockedHosts: Set<String>) {
    var isEnabled: Boolean = true

    fun isBlocked(host: String): Boolean = blockedHosts.contains(host.lowercase())

    companion object {
        fun load(context: Context): AdBlockManager {
            val hosts = runCatching {
                context.assets.open("blocklist-domains.txt").bufferedReader().use { reader ->
                    reader.lineSequence()
                        .map { it.trim() }
                        .filter { it.isNotEmpty() && !it.startsWith("#") }
                        .toHashSet()
                }
            }.getOrDefault(emptySet())
            return AdBlockManager(hosts)
        }
    }
}
