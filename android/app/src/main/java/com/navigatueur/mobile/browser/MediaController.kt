package com.navigatueur.mobile.browser

import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.os.Handler
import android.os.Looper
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.core.content.ContextCompat
import com.navigatueur.mobile.data.Stores
import org.json.JSONObject
import org.mozilla.geckoview.MediaSession

/**
 * What's playing, across tabs: drives the media notification (play/pause
 * from the notification shade, the lock screen or headphones), keeps
 * playback alive in the background, and decides when leaving the app turns
 * the video into a picture-in-picture window.
 */
class MediaController(private val context: Context, private val stores: Stores, private val tabs: TabsController) {
    private val handler = Handler(Looper.getMainLooper())

    var current by mutableStateOf<BrowserTab?>(null)
        private set
    var title by mutableStateOf<String?>(null)
        private set
    var artist by mutableStateOf<String?>(null)
        private set
    var artwork by mutableStateOf<Bitmap?>(null)
        private set
    var duration = 0.0
        private set
    var position = 0.0
        private set
    var positionAt = 0L
        private set

    /** Set while Navigatueur put the video in fullscreen itself to show it in picture-in-picture. */
    private var fullscreenForPip: BrowserTab? = null

    /** Called by the activity when the picture-in-picture state changes. */
    var onStateChanged: (() -> Unit)? = null

    val isPlaying: Boolean get() = current?.isMediaPlaying == true

    fun onActivated(tab: BrowserTab) {
        if (current == null || current?.isMediaPlaying != true) current = tab
        refresh()
    }

    fun onDeactivated(tab: BrowserTab) {
        if (current === tab) {
            current = tabs.tabs.firstOrNull { it.isMediaPlaying && it !== tab }
            if (current == null) {
                title = null; artist = null; artwork = null
            }
        }
        refresh()
    }

    fun onMetadata(tab: BrowserTab, meta: MediaSession.Metadata) {
        if (current !== tab) return
        title = meta.title?.takeIf { it.isNotBlank() } ?: tab.displayTitle
        artist = meta.artist?.takeIf { it.isNotBlank() } ?: tab.addressText
        val image = meta.artwork
        if (image == null) {
            artwork = null
            refresh()
        } else {
            image.getBitmap(256).accept({ bitmap -> handler.post { artwork = bitmap; refresh() } }, { handler.post { refresh() } })
        }
    }

    fun onPlaybackChanged(tab: BrowserTab) {
        if (tab.isMediaPlaying) {
            if (current !== tab) {
                // Starting something pauses what played in another tab, like any media player.
                current?.takeIf { it.isMediaPlaying }?.mediaSession?.pause()
                current = tab
                title = tab.displayTitle
                artist = tab.addressText
                artwork = null
            }
        }
        refresh()
    }

    fun onPosition(tab: BrowserTab, state: MediaSession.PositionState) {
        if (current !== tab) return
        duration = state.duration
        position = state.position
        positionAt = System.currentTimeMillis()
        refresh()
    }

    fun onVideoFullscreen(tab: BrowserTab, enabled: Boolean) {
        if (!enabled && fullscreenForPip === tab) fullscreenForPip = null
        onStateChanged?.invoke()
    }

    fun onTabSessionClosed(tab: BrowserTab) {
        if (current === tab) {
            current = null
            title = null; artist = null; artwork = null
            refresh()
        }
    }

    fun play() = current?.mediaSession?.play()
    fun pause() = current?.mediaSession?.pause()
    fun stop() {
        current?.mediaSession?.stop()
        current?.mediaSession?.pause()
    }
    fun next() = current?.mediaSession?.nextTrack()
    fun previous() = current?.mediaSession?.previousTrack()
    fun seekTo(seconds: Double) = current?.mediaSession?.seekTo(seconds, true)

    fun togglePlay() {
        if (isPlaying) pause() else play()
    }

    /** Starts/stops the foreground service holding the media notification. */
    private fun refresh() {
        val tab = current
        val shouldShow = tab != null && tab.hasMedia && (tab.isMediaPlaying || MediaPlaybackService.isRunning) && stores.settings.value.backgroundPlayback
        if (shouldShow) {
            val intent = Intent(context, MediaPlaybackService::class.java)
            runCatching { ContextCompat.startForegroundService(context, intent) }
            MediaPlaybackService.instance?.update()
        } else if (MediaPlaybackService.isRunning) {
            MediaPlaybackService.instance?.stopSelfNow()
        }
        onStateChanged?.invoke()
    }

    // ------------------------------------------------- picture-in-picture

    /** Leaving the app now should turn the active tab's video into a picture-in-picture window. */
    fun wantsPictureInPicture(): Boolean {
        val tab = tabs.activeTab ?: return false
        return stores.settings.value.autoPictureInPicture && tab.isMediaPlaying && tab.hasMedia && !tab.isHome
    }

    /** Width/height for the picture-in-picture window. */
    fun videoAspect(): Pair<Int, Int> {
        val tab = tabs.activeTab
        val w = tab?.mediaWidth ?: 0
        val h = tab?.mediaHeight ?: 0
        if (w <= 0 || h <= 0) return 16 to 9
        // Android only accepts ratios between 1:2.39 and 2.39:1.
        val ratio = (w.toDouble() / h).coerceIn(1 / 2.3, 2.3)
        return (ratio * 1000).toInt() to 1000
    }

    /** In picture-in-picture: the playing video fills the page (the site's own fullscreen), so the small window shows just it. */
    fun onEnteredPictureInPicture() {
        val tab = tabs.activeTab ?: return
        if (tab.isFullScreen) return
        fullscreenForPip = tab
        tab.pagePort?.let { port -> runCatching { port.postMessage(JSONObject().put("type", "fullscreenVideo")) } }
    }

    fun onExitedPictureInPicture() {
        val tab = fullscreenForPip ?: return
        fullscreenForPip = null
        tab.pagePort?.let { port -> runCatching { port.postMessage(JSONObject().put("type", "exitFullscreen")) } }
    }

    fun onFullscreenVideoAnswer(tab: BrowserTab, ok: Boolean, width: Long, height: Long) {
        if (ok && width > 0 && height > 0) {
            tab.mediaWidth = width
            tab.mediaHeight = height
            onStateChanged?.invoke()
        }
        if (!ok && fullscreenForPip === tab) fullscreenForPip = null
    }
}
