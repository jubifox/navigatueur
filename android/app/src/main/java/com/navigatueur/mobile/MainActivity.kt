package com.navigatueur.mobile

import android.Manifest
import android.app.PendingIntent
import android.app.PictureInPictureParams
import android.app.RemoteAction
import android.app.role.RoleManager
import android.content.ActivityNotFoundException
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.content.res.Configuration
import android.graphics.drawable.Icon
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.speech.RecognizerIntent
import android.util.Rational
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.core.content.ContextCompat
import androidx.core.content.pm.ShortcutInfoCompat
import androidx.core.content.pm.ShortcutManagerCompat
import androidx.core.graphics.drawable.IconCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.navigatueur.mobile.browser.BrowserTab
import com.navigatueur.mobile.browser.UiEvent
import com.navigatueur.mobile.core.UrlHelper
import com.navigatueur.mobile.ui.ActivityActions
import com.navigatueur.mobile.ui.AppRoot
import com.navigatueur.mobile.ui.AppUiState
import com.navigatueur.mobile.ui.Screen
import com.navigatueur.mobile.ui.theme.isDarkTheme
import org.mozilla.geckoview.BasicSelectionActionDelegate
import org.mozilla.geckoview.GeckoResult
import org.mozilla.geckoview.GeckoSession.PromptDelegate
import org.mozilla.geckoview.WebNotification
import java.io.File

class MainActivity : ComponentActivity(), ActivityActions {
    private val app get() = NavigatueurApp.instance
    private val state = AppUiState()
    private var inPictureInPicture by mutableStateOf(false)

    private var pendingFiles: Pair<PromptDelegate.FilePrompt, GeckoResult<PromptDelegate.PromptResponse>>? = null
    private var pendingPermissions: ((Boolean) -> Unit)? = null

    private val filePicker = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        val (prompt, geckoResult) = pendingFiles ?: return@registerForActivityResult
        pendingFiles = null
        val data = result.data
        val uris = buildList {
            data?.clipData?.let { clip -> for (i in 0 until clip.itemCount) add(clip.getItemAt(i).uri) }
            if (isEmpty()) data?.data?.let(::add)
        }
        geckoResult.complete(
            when {
                result.resultCode != RESULT_OK || uris.isEmpty() -> prompt.dismiss()
                uris.size == 1 -> prompt.confirm(this, uris[0])
                else -> prompt.confirm(this, uris.toTypedArray())
            }
        )
    }

    private val permissionRequester = registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { results ->
        val callback = pendingPermissions
        pendingPermissions = null
        callback?.invoke(results.isNotEmpty() && results.values.all { it })
    }

    private val notificationPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { }

    /** Android's "default browser" dialog only answers an activity that waits for its result. */
    private val defaultBrowserRequest = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) {
        if (!isDefaultBrowser()) openDefaultAppsSettings()
    }

    private val backgroundPicker = registerForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        if (uri == null) return@registerForActivityResult
        runCatching {
            val dir = app.stores.backgroundsDir.apply { mkdirs() }
            val target = File(dir, "wallpaper-${System.currentTimeMillis()}.jpg")
            contentResolver.openInputStream(uri)?.use { input -> target.outputStream().use { input.copyTo(it) } }
            // Only the new picture is kept.
            dir.listFiles()?.filter { it != target }?.forEach { it.delete() }
            app.stores.settings.update { s ->
                s.copy(appearance = s.appearance.copy(themeId = null, wallpaper = s.appearance.wallpaper.copy(kind = "Image", imagePath = target.path)))
            }
        }.onFailure { app.tabs.emit(UiEvent.Message("Impossible d'utiliser cette image")) }
    }

    private var pendingVoice: ((String) -> Unit)? = null

    private val voiceRecognizer = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        val callback = pendingVoice
        pendingVoice = null
        val spoken = result.data?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)?.firstOrNull()
        if (result.resultCode == RESULT_OK && !spoken.isNullOrBlank()) callback?.invoke(spoken)
    }

    private val pipReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            if (intent.action == ACTION_PIP_TOGGLE) app.media.togglePlay()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)

        app.tabs.selectionDelegateFactory = { BasicSelectionActionDelegate(this) }
        app.tabs.tabs.forEach { tab -> tab.session?.selectionActionDelegate = BasicSelectionActionDelegate(this) }
        app.media.onStateChanged = { updatePictureInPictureParams() }
        ContextCompat.registerReceiver(this, pipReceiver, IntentFilter(ACTION_PIP_TOGGLE), ContextCompat.RECEIVER_NOT_EXPORTED)

        setContent {
            val settings by app.stores.settings.flow.collectAsState()
            val dark = isDarkTheme(settings.themeMode)
            SideEffect {
                WindowCompat.getInsetsController(window, window.decorView).apply {
                    isAppearanceLightStatusBars = !dark
                    isAppearanceLightNavigationBars = !dark
                }
            }
            LaunchedEffect(Unit) {
                snapshotFlow { app.tabs.activeTab?.isFullScreen == true }.collect { full -> setSystemBarsHidden(full) }
            }
            AppRoot(app, state, inPictureInPicture, this)
        }

        if (savedInstanceState == null) handleIntent(intent)
        firstRun()
    }

    private fun firstRun() {
        if (app.stores.settings.value.hasSeenIntro) return
        app.stores.settings.update { it.copy(hasSeenIntro = true) }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleIntent(intent)
    }

    /** Links from other apps, shared text, "search with Navigatueur", notifications and home-screen shortcuts. */
    private fun handleIntent(intent: Intent?) {
        intent ?: return
        val tabs = app.tabs
        when (intent.action) {
            Intent.ACTION_VIEW -> intent.dataString?.takeIf { it.isNotBlank() }?.let { url ->
                tabs.newTab(url, private = false)
                state.screen = Screen.Browser
            }
            Intent.ACTION_SEND -> intent.getStringExtra(Intent.EXTRA_TEXT)?.let { text ->
                val url = Regex("https?://\\S+").find(text)?.value ?: text
                tabs.loadInput(tabs.newTab(private = false), url)
                state.screen = Screen.Browser
            }
            Intent.ACTION_WEB_SEARCH, Intent.ACTION_SEARCH -> intent.getStringExtra("query")?.let { query ->
                tabs.loadInput(tabs.newTab(private = false), query)
                state.screen = Screen.Browser
            }
            Intent.ACTION_PROCESS_TEXT -> intent.getCharSequenceExtra(Intent.EXTRA_PROCESS_TEXT)?.toString()?.let { text ->
                tabs.loadInput(tabs.newTab(private = false), text)
                state.screen = Screen.Browser
            }
            ACTION_WEB_NOTIFICATION -> {
                @Suppress("DEPRECATION")
                val notification = if (Build.VERSION.SDK_INT >= 33) intent.getParcelableExtra(EXTRA_NOTIFICATION, WebNotification::class.java)
                else intent.getParcelableExtra(EXTRA_NOTIFICATION)
                notification?.click()
            }
            ACTION_NEW_TAB -> {
                state.screen = Screen.Browser
                state.openAddress(null, newTab = true, private = intent.getBooleanExtra(EXTRA_PRIVATE, false))
            }
        }
    }

    // ------------------------------------------------- picture-in-picture

    private fun pipParams(): PictureInPictureParams {
        val (w, h) = app.media.videoAspect()
        val builder = PictureInPictureParams.Builder().setAspectRatio(Rational(w, h))
        val toggle = PendingIntent.getBroadcast(this, 1, Intent(ACTION_PIP_TOGGLE).setPackage(packageName), PendingIntent.FLAG_IMMUTABLE)
        val playing = app.media.isPlaying
        builder.setActions(
            listOf(
                RemoteAction(
                    Icon.createWithResource(this, if (playing) android.R.drawable.ic_media_pause else android.R.drawable.ic_media_play),
                    if (playing) "Pause" else "Lecture", if (playing) "Pause" else "Lecture", toggle,
                )
            )
        )
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            builder.setAutoEnterEnabled(app.media.wantsPictureInPicture())
            builder.setSeamlessResizeEnabled(true)
        }
        return builder.build()
    }

    private fun updatePictureInPictureParams() {
        if (!packageManager.hasSystemFeature(PackageManager.FEATURE_PICTURE_IN_PICTURE)) return
        runCatching { setPictureInPictureParams(pipParams()) }
    }

    override fun onUserLeaveHint() {
        super.onUserLeaveHint()
        // Android 12+ enters on its own (setAutoEnterEnabled); before that, it has to be asked here.
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S && app.media.wantsPictureInPicture()) enterPictureInPicture()
    }

    override fun enterPictureInPicture() {
        if (!packageManager.hasSystemFeature(PackageManager.FEATURE_PICTURE_IN_PICTURE)) return
        runCatching { enterPictureInPictureMode(pipParams()) }
    }

    override fun onPictureInPictureModeChanged(isInPictureInPictureMode: Boolean, newConfig: Configuration) {
        super.onPictureInPictureModeChanged(isInPictureInPictureMode, newConfig)
        inPictureInPicture = isInPictureInPictureMode
        if (isInPictureInPictureMode) {
            state.menuOpen = false
            state.closeAddress()
            app.media.onEnteredPictureInPicture()
        } else {
            app.media.onExitedPictureInPicture()
        }
    }

    private fun setSystemBarsHidden(hidden: Boolean) {
        val controller = WindowCompat.getInsetsController(window, window.decorView)
        if (hidden) {
            controller.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            controller.hide(WindowInsetsCompat.Type.systemBars())
        } else {
            controller.show(WindowInsetsCompat.Type.systemBars())
        }
    }

    // ------------------------------------------------------ ActivityActions

    override fun startActivitySafely(intent: Intent): Boolean = try {
        startActivity(intent)
        true
    } catch (e: ActivityNotFoundException) {
        app.tabs.emit(UiEvent.Message("Aucune application ne peut ouvrir ceci"))
        false
    } catch (e: SecurityException) {
        false
    }

    override fun openExternal(uri: String, fallbackUrl: String?) {
        val intent = runCatching {
            if (uri.startsWith("intent:")) Intent.parseUri(uri, Intent.URI_INTENT_SCHEME) else Intent(Intent.ACTION_VIEW, Uri.parse(uri))
        }.getOrNull()?.apply {
            addCategory(Intent.CATEGORY_BROWSABLE)
            component = null
            selector = null
        }
        val opened = intent != null && runCatching { startActivity(intent); true }.getOrDefault(false)
        if (!opened) {
            val tab = app.tabs.activeTab
            if (fallbackUrl != null && tab != null) app.tabs.loadUrl(tab, fallbackUrl)
            else app.tabs.emit(UiEvent.Message("Aucune application ne peut ouvrir ce lien"))
        }
    }

    override fun share(title: String?, text: String?, url: String?) {
        val body = listOfNotNull(text?.takeIf { it.isNotBlank() }, url).joinToString("\n")
        val send = Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, body)
        title?.let { send.putExtra(Intent.EXTRA_SUBJECT, it) }
        startActivitySafely(Intent.createChooser(send, title ?: "Partager"))
    }

    override fun handle(event: UiEvent.PickFiles) {
        val prompt = event.prompt
        val types = prompt.mimeTypes?.filter { it.isNotBlank() }.orEmpty()
        val intent = Intent(Intent.ACTION_GET_CONTENT).addCategory(Intent.CATEGORY_OPENABLE)
            .setType(if (types.size == 1) types[0] else "*/*")
            .putExtra(Intent.EXTRA_ALLOW_MULTIPLE, prompt.type == PromptDelegate.FilePrompt.Type.MULTIPLE)
        if (types.size > 1) intent.putExtra(Intent.EXTRA_MIME_TYPES, types.toTypedArray())
        pendingFiles?.let { (p, r) -> r.complete(p.dismiss()) }
        pendingFiles = prompt to event.result
        runCatching { filePicker.launch(intent) }.onFailure {
            pendingFiles = null
            event.result.complete(prompt.dismiss())
        }
    }

    override fun requestPermissions(event: UiEvent.AndroidPermissions) {
        val missing = event.permissions.filter { ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED }
        if (missing.isEmpty()) {
            event.onResult(true)
            return
        }
        pendingPermissions?.invoke(false)
        pendingPermissions = event.onResult
        permissionRequester.launch(missing.toTypedArray())
    }

    override fun pickBackgroundImage() {
        backgroundPicker.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
    }

    override fun addToHomeScreen(tab: BrowserTab) {
        if (!ShortcutManagerCompat.isRequestPinShortcutSupported(this)) {
            app.tabs.emit(UiEvent.Message("Votre écran d'accueil n'accepte pas les raccourcis"))
            return
        }
        val icon = tab.favicon?.let(IconCompat::createWithBitmap) ?: IconCompat.createWithResource(this, R.mipmap.ic_launcher)
        val shortcut = ShortcutInfoCompat.Builder(this, "site-" + UrlHelper.normalizeKey(tab.url).hashCode())
            .setShortLabel(tab.displayTitle.take(24))
            .setLongLabel(tab.displayTitle)
            .setIcon(icon)
            .setIntent(Intent(Intent.ACTION_VIEW, Uri.parse(tab.url), this, MainActivity::class.java))
            .build()
        ShortcutManagerCompat.requestPinShortcut(this, shortcut, null)
    }

    override fun isDefaultBrowser(): Boolean {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val roles = getSystemService(RoleManager::class.java)
            if (roles != null && roles.isRoleAvailable(RoleManager.ROLE_BROWSER)) return roles.isRoleHeld(RoleManager.ROLE_BROWSER)
        }
        val handler = packageManager.resolveActivity(Intent(Intent.ACTION_VIEW, Uri.parse("https://example.com")), PackageManager.MATCH_DEFAULT_ONLY)
        return handler?.activityInfo?.packageName == packageName
    }

    override fun requestDefaultBrowser() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val roles = getSystemService(RoleManager::class.java)
            if (roles != null && roles.isRoleAvailable(RoleManager.ROLE_BROWSER)) {
                runCatching { defaultBrowserRequest.launch(roles.createRequestRoleIntent(RoleManager.ROLE_BROWSER)) }
                    .onFailure { openDefaultAppsSettings() }
                return
            }
        }
        openDefaultAppsSettings()
    }

    /** Settings > Apps > Default apps, where the browser is picked (the dialog was refused, or this Android has none). */
    private fun openDefaultAppsSettings() {
        startActivitySafely(Intent(android.provider.Settings.ACTION_MANAGE_DEFAULT_APPS_SETTINGS))
    }

    override fun moveToBackground() {
        moveTaskToBack(true)
    }

    private fun voiceIntent() = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH)
        .putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_WEB_SEARCH)
        .putExtra(RecognizerIntent.EXTRA_PROMPT, "Dites ce que vous cherchez")

    override val canVoiceSearch: Boolean
        get() = voiceIntent().resolveActivity(packageManager) != null

    override fun voiceSearch(onResult: (String) -> Unit) {
        pendingVoice = onResult
        runCatching { voiceRecognizer.launch(voiceIntent()) }.onFailure {
            pendingVoice = null
            app.tabs.emit(UiEvent.Message("La recherche vocale n'est pas disponible"))
        }
    }

    override fun onDestroy() {
        runCatching { unregisterReceiver(pipReceiver) }
        if (app.media.onStateChanged != null) app.media.onStateChanged = null
        app.tabs.selectionDelegateFactory = null
        super.onDestroy()
    }

    companion object {
        const val ACTION_WEB_NOTIFICATION = "com.navigatueur.mobile.WEB_NOTIFICATION"
        const val ACTION_NEW_TAB = "com.navigatueur.mobile.NEW_TAB"
        const val ACTION_PIP_TOGGLE = "com.navigatueur.mobile.PIP_TOGGLE"
        const val EXTRA_NOTIFICATION = "notification"
        const val EXTRA_PRIVATE = "private"
    }
}
