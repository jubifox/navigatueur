package com.navigatueur.mobile.browser

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.Handler
import android.os.Looper
import android.provider.MediaStore
import android.webkit.MimeTypeMap
import android.webkit.URLUtil
import androidx.core.app.NotificationCompat
import androidx.core.content.FileProvider
import com.navigatueur.mobile.data.DownloadRecord
import com.navigatueur.mobile.data.Stores
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import org.mozilla.geckoview.WebResponse
import java.io.File
import java.io.OutputStream
import java.util.UUID

/**
 * Files a page hands over instead of displaying (a PDF, a zip, an APK…):
 * streamed straight from the engine into the phone's Downloads folder, with
 * a progress notification, and listed in Navigatueur's downloads screen.
 */
class Downloads(private val context: Context, private val stores: Stores) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val handler = Handler(Looper.getMainLooper())
    private val jobs = mutableMapOf<String, Job>()
    private val notifications = context.getSystemService(NotificationManager::class.java)
    var onMessage: ((String) -> Unit)? = null

    init {
        notifications.createNotificationChannel(
            NotificationChannel(CHANNEL, "Téléchargements", NotificationManager.IMPORTANCE_LOW)
        )
        // Anything still "running" from a previous run was cut off.
        stores.downloads.update { list -> list.map { if (it.state == "Running") it.copy(state = "Failed") else it } }
    }

    fun start(response: WebResponse, tab: BrowserTab) {
        val body = response.body ?: run {
            onMessage?.invoke("Le téléchargement n'a rien reçu")
            return
        }
        val headers = response.headers.mapKeys { it.key.lowercase() }
        val mime = headers["content-type"]?.substringBefore(';')?.trim()?.takeIf { it.isNotEmpty() && it != "application/octet-stream" }
        val name = sanitize(URLUtil.guessFileName(response.uri, headers["content-disposition"], mime))
        val total = headers["content-length"]?.toLongOrNull() ?: -1
        val record = DownloadRecord(UUID.randomUUID().toString(), name, response.uri, mime ?: guessMime(name), totalBytes = total)
        stores.downloads.update { listOf(record) + it.take(199) }
        onMessage?.invoke("Téléchargement de $name")

        jobs[record.id] = scope.launch {
            var target: Pair<Uri, OutputStream>? = null
            try {
                target = createTarget(record)
                val (uri, output) = target
                var received = 0L
                var lastUpdate = 0L
                val buffer = ByteArray(64 * 1024)
                body.use { input ->
                    output.use { out ->
                        while (isActive) {
                            val read = input.read(buffer)
                            if (read < 0) break
                            out.write(buffer, 0, read)
                            received += read
                            val now = System.currentTimeMillis()
                            if (now - lastUpdate > 400) {
                                lastUpdate = now
                                update(record.id) { it.copy(receivedBytes = received, contentUri = uri.toString()) }
                                notifyProgress(record, received)
                            }
                        }
                    }
                }
                if (!isActive) throw kotlinx.coroutines.CancellationException()
                finishTarget(uri)
                update(record.id) { it.copy(receivedBytes = received, totalBytes = received, contentUri = uri.toString(), state = "Done") }
                notifyDone(record.copy(contentUri = uri.toString()))
                handler.post { onMessage?.invoke("$name téléchargé") }
            } catch (e: Exception) {
                target?.first?.let { runCatching { context.contentResolver.delete(it, null, null) } }
                val cancelled = e is kotlinx.coroutines.CancellationException
                update(record.id) { it.copy(state = if (cancelled) "Cancelled" else "Failed") }
                notifications.cancel(record.id.hashCode())
                if (!cancelled) handler.post { onMessage?.invoke("Échec du téléchargement de $name") }
            } finally {
                jobs.remove(record.id)
            }
        }
    }

    fun cancel(record: DownloadRecord) {
        jobs[record.id]?.cancel()
    }

    fun remove(record: DownloadRecord, deleteFile: Boolean) {
        cancel(record)
        if (deleteFile) record.contentUri?.let { runCatching { context.contentResolver.delete(Uri.parse(it), null, null) } }
        stores.downloads.update { list -> list.filterNot { it.id == record.id } }
    }

    fun clearFinished() {
        stores.downloads.update { list -> list.filter { it.state == "Running" } }
    }

    fun openIntent(record: DownloadRecord): Intent? {
        val uri = record.contentUri?.let(Uri::parse) ?: return null
        return Intent(Intent.ACTION_VIEW)
            .setDataAndType(uri, record.mimeType ?: guessMime(record.fileName))
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
    }

    private fun update(id: String, change: (DownloadRecord) -> DownloadRecord) {
        stores.downloads.update { list -> list.map { if (it.id == id) change(it) else it } }
    }

    /** Android 10+: the shared Downloads folder through MediaStore. Before that: the app's own Download folder (no storage permission needed). */
    private fun createTarget(record: DownloadRecord): Pair<Uri, OutputStream> {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val values = ContentValues().apply {
                put(MediaStore.Downloads.DISPLAY_NAME, record.fileName)
                put(MediaStore.Downloads.MIME_TYPE, record.mimeType ?: "application/octet-stream")
                put(MediaStore.Downloads.IS_PENDING, 1)
            }
            val uri = context.contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values)
                ?: error("MediaStore refused the file")
            return uri to (context.contentResolver.openOutputStream(uri) ?: error("No output stream"))
        }
        val dir = context.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS) ?: File(context.filesDir, "downloads")
        dir.mkdirs()
        var file = File(dir, record.fileName)
        var n = 1
        while (file.exists()) file = File(dir, record.fileName.substringBeforeLast('.') + " ($n)." + record.fileName.substringAfterLast('.', "bin")).also { n++ }
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.files", file)
        return uri to file.outputStream()
    }

    private fun finishTarget(uri: Uri) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q && uri.scheme == "content" && uri.authority == MediaStore.AUTHORITY) {
            context.contentResolver.update(uri, ContentValues().apply { put(MediaStore.Downloads.IS_PENDING, 0) }, null, null)
        }
    }

    private fun notifyProgress(record: DownloadRecord, received: Long) {
        val builder = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(android.R.drawable.stat_sys_download)
            .setContentTitle(record.fileName)
            .setOnlyAlertOnce(true)
            .setOngoing(true)
            .setSilent(true)
        if (record.totalBytes > 0) {
            builder.setProgress(100, (received * 100 / record.totalBytes).toInt(), false)
        } else {
            builder.setProgress(0, 0, true)
        }
        runCatching { notifications.notify(record.id.hashCode(), builder.build()) }
    }

    private fun notifyDone(record: DownloadRecord) {
        val open = openIntent(record) ?: return
        val pending = PendingIntent.getActivity(context, record.id.hashCode(), open, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val notification = NotificationCompat.Builder(context, CHANNEL)
            .setSmallIcon(android.R.drawable.stat_sys_download_done)
            .setContentTitle(record.fileName)
            .setContentText("Téléchargement terminé")
            .setContentIntent(pending)
            .setAutoCancel(true)
            .build()
        runCatching { notifications.notify(record.id.hashCode(), notification) }
    }

    private fun sanitize(name: String): String =
        name.replace(Regex("[\\\\/:*?\"<>|\\u0000-\\u001f]"), "_").trim().ifEmpty { "telechargement" }.take(120)

    companion object {
        const val CHANNEL = "downloads"

        fun guessMime(name: String): String =
            MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substringAfterLast('.', "").lowercase()) ?: "application/octet-stream"
    }
}
