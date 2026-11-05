package com.navigatueur.mobile.browser

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.os.Handler
import android.os.Looper
import android.util.LruCache
import androidx.core.graphics.scale
import com.navigatueur.mobile.core.UrlHelper
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import org.json.JSONArray
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

/**
 * Site icons. GeckoView doesn't hand them over, so the helper script in
 * each page lists the icons the page declares; the best one is fetched
 * once, shrunk, and kept per site (on disk, except for private tabs) so
 * sleeping and restored tabs show it too.
 */
class Favicons(private val dir: File) {
    private val memory = LruCache<String, Bitmap>(96)
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val handler = Handler(Looper.getMainLooper())
    private val inFlight = mutableSetOf<String>()

    private fun key(url: String?): String? = UrlHelper.displayHost(url)?.lowercase()

    private fun file(key: String) = File(dir, key.replace(Regex("[^a-z0-9.-]"), "_") + ".png")

    fun cached(url: String?): Bitmap? {
        val key = key(url) ?: return null
        memory.get(key)?.let { return it }
        val file = file(key)
        if (!file.isFile) return null
        return BitmapFactory.decodeFile(file.path)?.also { memory.put(key, it) }
    }

    fun load(tab: BrowserTab, icons: JSONArray?) {
        val key = key(tab.url) ?: return
        memory.get(key)?.let {
            tab.favicon = it
            return
        }
        val candidates = (0 until (icons?.length() ?: 0)).mapNotNull { i ->
            icons?.optJSONObject(i)?.let { it.optString("href") to it.optInt("size") }
        }.filter { (href, _) -> href.startsWith("https://") || href.startsWith("http://") || href.startsWith("data:image") }
        // Closest to 64 px from above, else the biggest.
        val best = candidates.filter { it.second >= 48 }.minByOrNull { it.second }
            ?: candidates.maxByOrNull { it.second }
            ?: return
        synchronized(inFlight) { if (!inFlight.add(key)) return }

        val persist = !tab.isPrivate
        scope.launch {
            val bitmap = runCatching { fetch(best.first) }.getOrNull()
            synchronized(inFlight) { inFlight.remove(key) }
            if (bitmap == null) return@launch
            memory.put(key, bitmap)
            if (persist) runCatching {
                dir.mkdirs()
                file(key).outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
            }
            handler.post { if (key(tab.url) == key) tab.favicon = bitmap }
        }
    }

    private fun fetch(href: String): Bitmap? {
        val bytes = if (href.startsWith("data:image")) {
            val comma = href.indexOf(',')
            if (comma < 0 || !href.substring(0, comma).contains("base64")) return null
            android.util.Base64.decode(href.substring(comma + 1), android.util.Base64.DEFAULT)
        } else {
            val connection = URL(href).openConnection() as HttpURLConnection
            connection.connectTimeout = 6000
            connection.readTimeout = 6000
            connection.instanceFollowRedirects = true
            connection.setRequestProperty("User-Agent", "Mozilla/5.0 (Android) Navigatueur")
            try {
                if (connection.responseCode !in 200..299) return null
                connection.inputStream.use { input -> input.readNBytesCompat(512 * 1024) }
            } finally {
                connection.disconnect()
            }
        }
        val decoded = BitmapFactory.decodeByteArray(bytes, 0, bytes.size) ?: return null
        val size = 64
        return if (decoded.width == size && decoded.height == size) decoded else decoded.scale(size, size)
    }

    fun clear() {
        memory.evictAll()
        dir.listFiles()?.forEach { it.delete() }
    }
}

private fun java.io.InputStream.readNBytesCompat(limit: Int): ByteArray {
    val out = java.io.ByteArrayOutputStream()
    val buffer = ByteArray(16 * 1024)
    while (out.size() < limit) {
        val read = read(buffer)
        if (read < 0) break
        out.write(buffer, 0, read)
    }
    return out.toByteArray()
}
