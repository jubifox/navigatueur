package com.navigatueur.mobile.engine

import android.content.Context
import android.util.Log
import com.navigatueur.mobile.BuildConfig
import com.navigatueur.mobile.data.AppSettings
import org.mozilla.geckoview.ContentBlocking
import org.mozilla.geckoview.GeckoPreferenceController
import org.mozilla.geckoview.GeckoRuntime
import org.mozilla.geckoview.GeckoRuntimeSettings
import java.util.Locale

/**
 * The single Gecko engine of the app (one per process, as GeckoView
 * requires). Settings that can change while the app runs (tracking
 * protection, HTTPS-only, color scheme…) are applied to it live.
 */
object Engine {
    private const val TAG = "Navigatueur"

    lateinit var runtime: GeckoRuntime
        private set

    val isReady: Boolean get() = ::runtime.isInitialized

    fun start(context: Context, settings: AppSettings) {
        if (isReady) return

        val contentBlocking = ContentBlocking.Settings.Builder()
            .antiTracking(antiTracking(settings))
            .enhancedTrackingProtectionLevel(if (settings.trackingProtection) ContentBlocking.EtpLevel.DEFAULT else ContentBlocking.EtpLevel.NONE)
            .cookieBehavior(ContentBlocking.CookieBehavior.ACCEPT_FIRST_PARTY_AND_ISOLATE_OTHERS)
            .cookieBehaviorPrivateMode(ContentBlocking.CookieBehavior.ACCEPT_FIRST_PARTY_AND_ISOLATE_OTHERS)
            // Google's Safe Browsing needs an API key Navigatueur doesn't have; its own phishing list does that job (PhishingGuard).
            .safeBrowsing(ContentBlocking.SafeBrowsing.NONE)
            .queryParameterStrippingEnabled(true)
            .queryParameterStrippingPrivateBrowsingEnabled(true)
            .build()

        val runtimeSettings = GeckoRuntimeSettings.Builder()
            .contentBlocking(contentBlocking)
            .preferredColorScheme(colorScheme(settings))
            .globalPrivacyControlEnabled(settings.globalPrivacyControl)
            .allowInsecureConnections(if (settings.httpsOnly) GeckoRuntimeSettings.HTTPS_ONLY else GeckoRuntimeSettings.ALLOW_ALL)
            .locales(arrayOf(Locale.getDefault().toLanguageTag(), "fr-FR", "en-US"))
            .consoleOutput(BuildConfig.DEBUG)
            .remoteDebuggingEnabled(BuildConfig.DEBUG)
            .inputAutoZoomEnabled(false)
            .loginAutofillEnabled(false)
            .translationsOfferPopup(false)
            // Extensions get their own process, like in Firefox: uBlock Origin compiling its lists
            // on first launch would otherwise hold up every page.
            .extensionsProcessEnabled(true)
            .extensionsProcessCrashThreshold(5)
            .extensionsProcessCrashTimeframe(60_000L)
            .build()

        runtime = GeckoRuntime.create(context.applicationContext, runtimeSettings)
        applyPreferences()
        applyAccessibility(settings)
    }

    /**
     * Gecko preferences that have no dedicated setting. Lets the app's own
     * helper extension put a playing video in fullscreen when the user leaves
     * the app (automatic picture-in-picture shows just the video, then),
     * which the web only allows after a tap otherwise.
     */
    private fun applyPreferences() {
        setPref("full-screen-api.allow-trusted-requests-only", false)
        setPref("full-screen-api.warning.timeout", 0)
        // Ask before a site opens a popup instead of silently dropping it.
        setPref("dom.disable_open_during_load", true)
    }

    private fun setPref(name: String, value: Any) {
        val result = when (value) {
            is Boolean -> GeckoPreferenceController.setGeckoPref(name, value, GeckoPreferenceController.PREF_BRANCH_USER)
            is Int -> GeckoPreferenceController.setGeckoPref(name, value, GeckoPreferenceController.PREF_BRANCH_USER)
            is String -> GeckoPreferenceController.setGeckoPref(name, value, GeckoPreferenceController.PREF_BRANCH_USER)
            else -> return
        }
        result.accept({}, { error -> Log.w(TAG, "Pref $name not set", error) })
    }

    fun apply(settings: AppSettings) {
        if (!isReady) return
        val runtimeSettings = runtime.settings
        runtimeSettings.setPreferredColorScheme(colorScheme(settings))
        runtimeSettings.setGlobalPrivacyControl(settings.globalPrivacyControl)
        runtimeSettings.setAllowInsecureConnections(if (settings.httpsOnly) GeckoRuntimeSettings.HTTPS_ONLY else GeckoRuntimeSettings.ALLOW_ALL)
        runtimeSettings.contentBlocking
            .setAntiTracking(antiTracking(settings))
            .setEnhancedTrackingProtectionLevel(if (settings.trackingProtection) ContentBlocking.EtpLevel.DEFAULT else ContentBlocking.EtpLevel.NONE)
        applyAccessibility(settings)
    }

    /** Text size of pages (0: Android's font size) and pinch zoom on every site. */
    private fun applyAccessibility(settings: AppSettings) {
        runCatching {
            val runtimeSettings = runtime.settings
            if (settings.textScale <= 0) {
                runtimeSettings.setAutomaticFontSizeAdjustment(true)
            } else {
                // A fixed factor is only accepted once the automatic one is off.
                runtimeSettings.setAutomaticFontSizeAdjustment(false)
                runtimeSettings.setFontSizeFactor(settings.textScale.coerceIn(50, 300) / 100f)
            }
        }.onFailure { Log.w(TAG, "Text size not applied", it) }
        setPref("browser.ui.zoom.force-user-scalable", settings.forceZoom)
    }

    private fun antiTracking(settings: AppSettings): Int =
        if (settings.trackingProtection) ContentBlocking.AntiTracking.DEFAULT or ContentBlocking.AntiTracking.STP
        else ContentBlocking.AntiTracking.NONE

    /** Pages follow the app's theme: a site with a dark theme of its own shows it while Navigatueur is dark. */
    private fun colorScheme(settings: AppSettings): Int = when (settings.themeMode) {
        "Light" -> GeckoRuntimeSettings.COLOR_SCHEME_LIGHT
        "System" -> GeckoRuntimeSettings.COLOR_SCHEME_SYSTEM
        else -> GeckoRuntimeSettings.COLOR_SCHEME_DARK
    }
}
