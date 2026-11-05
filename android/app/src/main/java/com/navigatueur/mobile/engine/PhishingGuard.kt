package com.navigatueur.mobile.engine

import android.content.Context
import com.navigatueur.mobile.BuildConfig
import com.navigatueur.mobile.core.DomainSet
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.File

/**
 * The Windows app's phishing list (Resources/Phishing/phishing-domains.txt,
 * packed into the APK as is): a page on a listed domain is replaced by a
 * warning, with a way through for the user who knows better.
 *
 * Reading ~400 000 lines takes a moment, so it happens once in the
 * background and the result is kept as a small binary file for the next
 * starts. Until it's ready, nothing is blocked.
 */
class PhishingGuard(private val context: Context) {
    @Volatile
    private var domains: DomainSet = DomainSet.EMPTY

    /** Hosts the user chose to open anyway, for this run of the app. */
    private val bypassed = mutableSetOf<String>()

    fun load() {
        CoroutineScope(SupervisorJob() + Dispatchers.IO).launch {
            domains = runCatching { readCache() ?: parseAsset().also(::writeCache) }.getOrDefault(DomainSet.EMPTY)
        }
    }

    fun isPhishing(host: String?): Boolean = host != null && host.lowercase() !in bypassed && domains.contains(host)

    fun allowOnce(host: String) {
        bypassed += host.lowercase()
    }

    private val cacheFile get() = File(context.filesDir, "phishing-${BuildConfig.VERSION_CODE}.bin")

    private fun parseAsset(): DomainSet = context.assets.open(ASSET).bufferedReader().useLines { DomainSet.parse(it) }

    private fun readCache(): DomainSet? {
        val file = cacheFile
        if (!file.isFile) return null
        return DataInputStream(file.inputStream().buffered()).use { input ->
            val count = input.readInt()
            DomainSet.fromHashes(LongArray(count) { input.readLong() })
        }
    }

    private fun writeCache(set: DomainSet) {
        context.filesDir.listFiles { f -> f.name.startsWith("phishing-") && f.name.endsWith(".bin") }?.forEach { it.delete() }
        val hashes = set.toLongArray()
        DataOutputStream(cacheFile.outputStream().buffered()).use { output ->
            output.writeInt(hashes.size)
            hashes.forEach(output::writeLong)
        }
    }

    companion object {
        const val ASSET = "phishing-domains.txt"
    }
}
