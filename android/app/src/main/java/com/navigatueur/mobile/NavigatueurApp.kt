package com.navigatueur.mobile

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.ComponentCallbacks2
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.lifecycle.DefaultLifecycleObserver
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.ProcessLifecycleOwner
import com.navigatueur.mobile.browser.Downloads
import com.navigatueur.mobile.browser.ExtensionsManager
import com.navigatueur.mobile.browser.Favicons
import com.navigatueur.mobile.browser.MediaController
import com.navigatueur.mobile.browser.TabsController
import com.navigatueur.mobile.browser.UiEvent
import com.navigatueur.mobile.browser.Updater
import com.navigatueur.mobile.data.Stores
import com.navigatueur.mobile.engine.Engine
import com.navigatueur.mobile.engine.PhishingGuard
import org.mozilla.geckoview.WebNotification
import org.mozilla.geckoview.WebNotificationDelegate

/**
 * Composition root. GeckoView runs pages in separate processes that also
 * start this class: everything is only set up in the app's main process.
 */
class NavigatueurApp : Application() {
    lateinit var stores: Stores
        private set
    lateinit var tabs: TabsController
        private set
    lateinit var extensions: ExtensionsManager
        private set
    lateinit var media: MediaController
        private set
    lateinit var downloads: Downloads
        private set
    lateinit var updater: Updater
        private set
    lateinit var phishing: PhishingGuard
        private set

    val isMainProcess: Boolean
        get() = Build.VERSION.SDK_INT < Build.VERSION_CODES.P || getProcessName() == packageName

    override fun onCreate() {
        super.onCreate()
        if (!isMainProcess) return
        instance = this

        stores = Stores(this)
        Engine.start(this, stores.settings.value)

        phishing = PhishingGuard(this).also { it.load() }
        extensions = ExtensionsManager(stores)
        tabs = TabsController(stores, extensions, phishing, Favicons(stores.faviconsDir))
        extensions.tabsController = tabs
        media = MediaController(this, stores, tabs)
        downloads = Downloads(this, stores)
        updater = Updater(this, stores)
        tabs.media = media
        tabs.downloads = downloads
        downloads.onMessage = { tabs.emit(UiEvent.Message(it)) }

        extensions.start()
        tabs.restore()
        setUpWebNotifications()

        ProcessLifecycleOwner.get().lifecycle.addObserver(object : DefaultLifecycleObserver {
            override fun onStop(owner: LifecycleOwner) {
                tabs.activeTab?.session?.flushSessionState()
                tabs.persist()
                stores.flushAll()
            }

            override fun onStart(owner: LifecycleOwner) {
                updater.checkIfDue()
            }
        })
    }

    @Suppress("DEPRECATION")
    override fun onTrimMemory(level: Int) {
        super.onTrimMemory(level)
        if (!::tabs.isInitialized) return
        if (level >= ComponentCallbacks2.TRIM_MEMORY_RUNNING_LOW) tabs.suspendBackgroundTabs()
    }

    /** Sites the user allowed to send notifications show them as Android notifications. */
    private fun setUpWebNotifications() {
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(NotificationChannel(WEB_CHANNEL, "Notifications des sites", NotificationManager.IMPORTANCE_DEFAULT))
        Engine.runtime.webNotificationDelegate = object : WebNotificationDelegate {
            override fun onShowNotification(notification: WebNotification) {
                val open = PendingIntent.getActivity(
                    this@NavigatueurApp, notification.tag.hashCode(),
                    Intent(this@NavigatueurApp, MainActivity::class.java)
                        .setAction(MainActivity.ACTION_WEB_NOTIFICATION)
                        .putExtra(MainActivity.EXTRA_NOTIFICATION, notification)
                        .addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP),
                    PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
                )
                val built = NotificationCompat.Builder(this@NavigatueurApp, WEB_CHANNEL)
                    .setSmallIcon(R.drawable.ic_notification)
                    .setContentTitle(notification.title)
                    .setContentText(notification.text)
                    .setSubText(notification.origin)
                    .setContentIntent(open)
                    .setAutoCancel(true)
                    .setSilent(notification.silent)
                    .build()
                runCatching { manager.notify(notification.tag.hashCode(), built) }
            }

            override fun onCloseNotification(notification: WebNotification) {
                manager.cancel(notification.tag.hashCode())
            }
        }
    }

    companion object {
        const val WEB_CHANNEL = "web"

        lateinit var instance: NavigatueurApp
            private set
    }
}
