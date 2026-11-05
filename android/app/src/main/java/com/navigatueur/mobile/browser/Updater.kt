package com.navigatueur.mobile.browser

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.content.FileProvider
import com.navigatueur.mobile.BuildConfig
import com.navigatueur.mobile.core.Versions
import com.navigatueur.mobile.data.Stores
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

/**
 * Same update channel as the Windows app: the latest GitHub release. Each
 * release carries one APK per processor type next to the Windows installer;
 * the one for this phone is downloaded and handed to Android's installer.
 */
class Updater(private val context: Context, private val stores: Stores) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val handler = Handler(Looper.getMainLooper())

    data class Release(val version: String, val apkName: String, val apkUrl: String, val size: Long, val pageUrl: String)

    var available by mutableStateOf<Release?>(null)
        private set
    var checking by mutableStateOf(false)
        private set
    var lastCheckMessage by mutableStateOf<String?>(null)
        private set
    var downloadProgress by mutableFloatStateOf(-1f)
        private set

    fun checkIfDue() {
        if (System.currentTimeMillis() - stores.settings.value.lastUpdateCheck > 12 * 3600_000L) check(quiet = true)
    }

    fun check(quiet: Boolean = false) {
        if (checking) return
        checking = true
        scope.launch {
            val result = runCatching { fetchLatest() }
            handler.post {
                checking = false
                stores.settings.update { it.copy(lastUpdateCheck = System.currentTimeMillis()) }
                result.onSuccess { release ->
                    available = release
                    lastCheckMessage = when {
                        release != null -> "Version ${release.version} disponible"
                        else -> "Navigatueur est à jour (${BuildConfig.VERSION_NAME})"
                    }
                }.onFailure {
                    if (!quiet) lastCheckMessage = "Impossible de vérifier les mises à jour"
                }
            }
        }
    }

    private fun fetchLatest(): Release? {
        val connection = URL("https://api.github.com/repos/jubifox/navigatueur/releases/latest").openConnection() as HttpURLConnection
        connection.setRequestProperty("Accept", "application/vnd.github+json")
        connection.setRequestProperty("User-Agent", "Navigatueur-Android")
        connection.connectTimeout = 10_000
        connection.readTimeout = 10_000
        val json = try {
            JSONObject(connection.inputStream.bufferedReader().readText())
        } finally {
            connection.disconnect()
        }
        val version = json.optString("tag_name").removePrefix("v")
        if (!Versions.isNewer(version, BuildConfig.VERSION_NAME)) return null
        val assets = json.optJSONArray("assets") ?: return null
        val byName = (0 until assets.length()).mapNotNull { assets.optJSONObject(it) }.associateBy { it.optString("name") }
        val name = Versions.pickApk(byName.keys.toList(), Build.SUPPORTED_ABIS.toList()) ?: return null
        val asset = byName.getValue(name)
        return Release(version, name, asset.optString("browser_download_url"), asset.optLong("size"), json.optString("html_url"))
    }

    /** Android asks once whether Navigatueur may install apps; until then this opens that setting. */
    fun canInstall(): Boolean = context.packageManager.canRequestPackageInstalls()

    fun installPermissionIntent(): Intent =
        Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}")).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

    fun downloadAndInstall(onReady: (Intent) -> Unit) {
        val release = available ?: return
        if (downloadProgress >= 0f) return
        downloadProgress = 0f
        scope.launch {
            val result = runCatching {
                val dir = File(context.cacheDir, "updates").apply { mkdirs() }
                dir.listFiles()?.forEach { it.delete() }
                val file = File(dir, release.apkName)
                val connection = URL(release.apkUrl).openConnection() as HttpURLConnection
                connection.instanceFollowRedirects = true
                connection.setRequestProperty("User-Agent", "Navigatueur-Android")
                try {
                    val total = connection.contentLengthLong.takeIf { it > 0 } ?: release.size
                    connection.inputStream.use { input ->
                        file.outputStream().use { output ->
                            val buffer = ByteArray(128 * 1024)
                            var received = 0L
                            var lastPost = 0L
                            while (true) {
                                val read = input.read(buffer)
                                if (read < 0) break
                                output.write(buffer, 0, read)
                                received += read
                                val now = System.currentTimeMillis()
                                if (total > 0 && now - lastPost > 250) {
                                    lastPost = now
                                    val progress = received.toFloat() / total
                                    handler.post { downloadProgress = progress }
                                }
                            }
                        }
                    }
                } finally {
                    connection.disconnect()
                }
                file
            }
            handler.post {
                downloadProgress = -1f
                result.onSuccess { file ->
                    val uri = FileProvider.getUriForFile(context, "${context.packageName}.files", file)
                    onReady(
                        Intent(Intent.ACTION_VIEW)
                            .setDataAndType(uri, "application/vnd.android.package-archive")
                            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
                    )
                }.onFailure { lastCheckMessage = "Le téléchargement de la mise à jour a échoué" }
            }
        }
    }
}
