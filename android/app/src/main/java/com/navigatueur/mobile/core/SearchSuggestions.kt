package com.navigatueur.mobile.core

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

/**
 * What the search engine suggests while typing ("météo" → "météo paris",
 * "météo demain"…), from each engine's public suggestion service.
 */
object SearchSuggestions {
    /** The suggestion service of [engineId] for [query]; null for an unknown engine or an empty query. */
    fun url(engineId: String, query: String): String? {
        val q = query.trim()
        if (q.isEmpty()) return null
        val encoded = URLEncoder.encode(q, "UTF-8")
        return when (engineId) {
            "Google" -> "https://suggestqueries.google.com/complete/search?client=firefox&ie=utf-8&oe=utf-8&hl=fr&q=$encoded"
            "Bing" -> "https://api.bing.com/osjson.aspx?market=fr-FR&query=$encoded"
            "DuckDuckGo" -> "https://duckduckgo.com/ac/?type=list&kl=fr-fr&q=$encoded"
            "Qwant" -> "https://api.qwant.com/v3/suggest?locale=fr_FR&q=$encoded"
            "Ecosia" -> "https://ac.ecosia.org/autocomplete?type=list&q=$encoded"
            else -> null
        }
    }

    /**
     * Reads a service's answer: OpenSearch's ["query", ["s1", "s2"]] (Google,
     * Bing, DuckDuckGo, Ecosia) or Qwant's {"data": {"items": [{"value": …}]}}.
     * The query itself and duplicates are left out.
     */
    fun parse(body: String, query: String, limit: Int = 5): List<String> {
        val root = runCatching { Json.parseToJsonElement(body) }.getOrNull() ?: return emptyList()
        val raw: List<String> = runCatching {
            when (root) {
                is JsonArray -> (root.getOrNull(1) as? JsonArray).orEmpty()
                    .mapNotNull { (it as? JsonPrimitive)?.takeIf { p -> p.isString }?.content }
                is JsonObject -> root["data"]?.jsonObject?.get("items")?.jsonArray.orEmpty()
                    .mapNotNull { (it as? JsonObject)?.get("value")?.jsonPrimitive?.content }
                else -> emptyList()
            }
        }.getOrDefault(emptyList())
        val typed = query.trim()
        return raw.asSequence()
            .map { it.trim() }
            .filter { it.isNotEmpty() && !it.equals(typed, ignoreCase = true) }
            .distinctBy { it.lowercase() }
            .take(limit)
            .toList()
    }

    /** Asks the service (blocking: call it off the main thread). Any failure gives no suggestions. */
    fun fetch(engineId: String, query: String, limit: Int = 5): List<String> {
        val address = url(engineId, query) ?: return emptyList()
        return runCatching {
            val connection = URL(address).openConnection() as HttpURLConnection
            connection.connectTimeout = 2500
            connection.readTimeout = 2500
            connection.setRequestProperty("Accept", "application/json")
            try {
                if (connection.responseCode != 200) return emptyList()
                val body = connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
                parse(body, query, limit)
            } finally {
                connection.disconnect()
            }
        }.getOrDefault(emptyList())
    }
}
