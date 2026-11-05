package com.navigatueur.mobile.browser

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.support.v4.media.MediaMetadataCompat
import android.support.v4.media.session.MediaSessionCompat
import android.support.v4.media.session.PlaybackStateCompat
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import com.navigatueur.mobile.MainActivity
import com.navigatueur.mobile.NavigatueurApp

/**
 * Keeps a playing video or song going while Navigatueur is in the
 * background, and shows its controls in the notification shade, on the
 * lock screen and to headphones — the phone's version of the Windows app's
 * music mini-player.
 */
class MediaPlaybackService : Service() {
    private lateinit var mediaSession: MediaSessionCompat
    private val media get() = NavigatueurApp.instance.media

    override fun onCreate() {
        super.onCreate()
        instance = this
        getSystemService(NotificationManager::class.java).createNotificationChannel(
            NotificationChannel(CHANNEL, "Lecture en cours", NotificationManager.IMPORTANCE_LOW).apply { setShowBadge(false) }
        )
        mediaSession = MediaSessionCompat(this, "Navigatueur").apply {
            setCallback(object : MediaSessionCompat.Callback() {
                override fun onPlay() { media.play() }
                override fun onPause() { media.pause() }
                override fun onStop() { media.stop() }
                override fun onSkipToNext() { media.next() }
                override fun onSkipToPrevious() { media.previous() }
                override fun onSeekTo(pos: Long) { media.seekTo(pos / 1000.0) }
            })
            setSessionActivity(openAppIntent())
            isActive = true
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_TOGGLE -> media.togglePlay()
            ACTION_STOP -> {
                media.stop()
                stopSelfNow()
                return START_NOT_STICKY
            }
            ACTION_NEXT -> media.next()
            ACTION_PREVIOUS -> media.previous()
        }
        val notification = buildNotification()
        val type = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK else 0
        runCatching { ServiceCompat.startForeground(this, NOTIFICATION_ID, notification, type) }
        isRunning = true
        return START_NOT_STICKY
    }

    fun update() {
        if (!isRunning) return
        runCatching { getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, buildNotification()) }
    }

    fun stopSelfNow() {
        isRunning = false
        ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun buildNotification(): Notification {
        val playing = media.isPlaying
        val title = media.title ?: media.current?.displayTitle ?: "Lecture"
        val artist = media.artist ?: media.current?.addressText ?: ""

        mediaSession.setMetadata(
            MediaMetadataCompat.Builder()
                .putString(MediaMetadataCompat.METADATA_KEY_TITLE, title)
                .putString(MediaMetadataCompat.METADATA_KEY_ARTIST, artist)
                .putLong(MediaMetadataCompat.METADATA_KEY_DURATION, (media.duration * 1000).toLong().coerceAtLeast(0))
                .apply { media.artwork?.let { putBitmap(MediaMetadataCompat.METADATA_KEY_ALBUM_ART, it) } }
                .build()
        )
        val elapsed = if (playing) (System.currentTimeMillis() - media.positionAt) / 1000.0 else 0.0
        mediaSession.setPlaybackState(
            PlaybackStateCompat.Builder()
                .setActions(
                    PlaybackStateCompat.ACTION_PLAY or PlaybackStateCompat.ACTION_PAUSE or PlaybackStateCompat.ACTION_PLAY_PAUSE or
                        PlaybackStateCompat.ACTION_STOP or PlaybackStateCompat.ACTION_SKIP_TO_NEXT or PlaybackStateCompat.ACTION_SKIP_TO_PREVIOUS or
                        PlaybackStateCompat.ACTION_SEEK_TO
                )
                .setState(
                    if (playing) PlaybackStateCompat.STATE_PLAYING else PlaybackStateCompat.STATE_PAUSED,
                    ((media.position + elapsed) * 1000).toLong().coerceAtLeast(0),
                    if (playing) 1f else 0f,
                )
                .build()
        )

        return NotificationCompat.Builder(this, CHANNEL)
            .setSmallIcon(android.R.drawable.ic_media_play)
            .setContentTitle(title)
            .setContentText(artist)
            .setLargeIcon(media.artwork ?: media.current?.favicon)
            .setContentIntent(openAppIntent())
            .setDeleteIntent(serviceIntent(ACTION_STOP))
            .setOngoing(playing)
            .setSilent(true)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .addAction(android.R.drawable.ic_media_previous, "Précédent", serviceIntent(ACTION_PREVIOUS))
            .addAction(
                if (playing) android.R.drawable.ic_media_pause else android.R.drawable.ic_media_play,
                if (playing) "Pause" else "Lecture",
                serviceIntent(ACTION_TOGGLE),
            )
            .addAction(android.R.drawable.ic_media_next, "Suivant", serviceIntent(ACTION_NEXT))
            .setStyle(
                androidx.media.app.NotificationCompat.MediaStyle()
                    .setMediaSession(mediaSession.sessionToken)
                    .setShowActionsInCompactView(0, 1, 2)
            )
            .build()
    }

    private fun openAppIntent(): PendingIntent = PendingIntent.getActivity(
        this, 0, Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    private fun serviceIntent(action: String): PendingIntent = PendingIntent.getService(
        this, action.hashCode(), Intent(this, MediaPlaybackService::class.java).setAction(action),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    override fun onDestroy() {
        isRunning = false
        instance = null
        mediaSession.release()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    companion object {
        const val CHANNEL = "media"
        const val NOTIFICATION_ID = 7
        const val ACTION_TOGGLE = "com.navigatueur.mobile.media.TOGGLE"
        const val ACTION_STOP = "com.navigatueur.mobile.media.STOP"
        const val ACTION_NEXT = "com.navigatueur.mobile.media.NEXT"
        const val ACTION_PREVIOUS = "com.navigatueur.mobile.media.PREVIOUS"

        @Volatile var isRunning = false
            private set
        @Volatile var instance: MediaPlaybackService? = null
            private set
    }
}
