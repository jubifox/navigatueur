package com.navigatueur.mobile.browser

import android.graphics.Bitmap
import android.os.Handler
import android.os.Looper
import android.util.Log
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.navigatueur.mobile.data.Stores
import com.navigatueur.mobile.engine.Engine
import org.json.JSONObject
import org.mozilla.geckoview.AllowOrDeny
import org.mozilla.geckoview.GeckoResult
import org.mozilla.geckoview.GeckoSession
import org.mozilla.geckoview.WebExtension
import org.mozilla.geckoview.WebExtensionController

/** An extension Navigatueur installs for everyone — the mobile side of the Windows app's built-in extensions. */
data class CatalogExtension(
    val id: String,
    val amoSlug: String,
    val name: String,
    val description: String,
    val enabledByDefault: Boolean,
    val note: String? = null,
)

/** What the extensions screen shows for one installed extension. */
class InstalledExtension(
    val extension: WebExtension,
    val catalog: CatalogExtension?,
) {
    val id: String get() = extension.id
    val name: String get() = extension.metaData.name ?: catalog?.name ?: extension.id
    val description: String get() = catalog?.description ?: extension.metaData.description ?: ""
    val enabled: Boolean get() = extension.metaData.enabled
    val version: String get() = extension.metaData.version
    val optionsUrl: String? get() = extension.metaData.optionsPageUrl
}

/** The toolbar button an extension declares (uBlock Origin's shield and its blocked count, Dark Reader's moon…). */
class ExtensionAction(val extensionId: String, val title: String, val icon: Bitmap?, val badge: String?, val action: WebExtension.Action)

/**
 * Real Firefox extensions, through GeckoView. The catalog below is
 * installed from addons.mozilla.org on first launch — the same files
 * Firefox gets, signed by Mozilla, so nothing is bundled or redistributed —
 * and kept up to date from there. Users can also install any other
 * extension by opening its page on addons.mozilla.org.
 */
class ExtensionsManager(private val stores: Stores) {
    private val handler = Handler(Looper.getMainLooper())

    val installed = mutableStateListOf<InstalledExtension>()
    val actions = mutableStateMapOf<String, ExtensionAction>()
    var installing by mutableStateOf(setOf<String>())
        private set
    var lastError by mutableStateOf<String?>(null)

    /** An extension a page asked to install (from addons.mozilla.org): the UI asks the user. */
    var pendingInstall by mutableStateOf<PendingInstall?>(null)

    /** A popup an extension opened (its toolbar button was tapped): shown in a sheet. */
    var popup by mutableStateOf<GeckoSession?>(null)

    var helper: WebExtension? = null
        private set

    var tabsController: TabsController? = null

    class PendingInstall(val name: String, val permissions: List<String>, val origins: List<String>, val decide: (Boolean) -> Unit)

    private val controller get() = Engine.runtime.webExtensionController

    /** When each catalog extension was installed automatically: their welcome pages aren't opened right after. */
    private val autoInstalledAt = mutableMapOf<String, Long>()

    fun start() {
        controller.setPromptDelegate(object : WebExtensionController.PromptDelegate {
            override fun onInstallPromptRequest(
                extension: WebExtension,
                permissions: Array<out String>,
                origins: Array<out String>,
                dataCollectionPermissions: Array<out String>,
            ): GeckoResult<WebExtension.PermissionPromptResponse> {
                if (catalog.any { it.id == extension.id }) {
                    return GeckoResult.fromValue(WebExtension.PermissionPromptResponse(true, true, false))
                }
                val result = GeckoResult<WebExtension.PermissionPromptResponse>()
                handler.post {
                    pendingInstall = PendingInstall(extension.metaData.name ?: extension.id, permissions.toList(), origins.toList()) { allow ->
                        pendingInstall = null
                        result.complete(WebExtension.PermissionPromptResponse(allow, false, false))
                    }
                }
                return result
            }

            override fun onUpdatePrompt(
                extension: WebExtension,
                newPermissions: Array<out String>,
                newOrigins: Array<out String>,
                newDataCollectionPermissions: Array<out String>,
            ): GeckoResult<AllowOrDeny> = GeckoResult.allow()

            override fun onOptionalPrompt(
                extension: WebExtension,
                permissions: Array<out String>,
                origins: Array<out String>,
                dataCollectionPermissions: Array<out String>,
            ): GeckoResult<AllowOrDeny> = GeckoResult.allow()
        })

        controller.setAddonManagerDelegate(object : WebExtensionController.AddonManagerDelegate {
            override fun onInstalled(extension: WebExtension) = refresh()
            override fun onUninstalled(extension: WebExtension) = refresh()
            override fun onEnabled(extension: WebExtension) = refresh()
            override fun onDisabled(extension: WebExtension) = refresh()
            override fun onReady(extension: WebExtension) = refresh()
        })

        controller.ensureBuiltIn(HELPER_LOCATION, HELPER_ID).accept({ ext ->
            helper = ext
            tabsController?.let { tabs -> tabs.tabs.forEach { tab -> tab.session?.let { attachSession(it, tab) } } }
        }, { error -> Log.e(TAG, "Helper extension failed", error) })

        controller.list().accept({ list ->
            onList(list.orEmpty())
            installMissing(list.orEmpty())
            updateIfDue(list.orEmpty())
        }, { error -> Log.e(TAG, "list failed", error) })
    }

    fun refresh() {
        controller.list().accept({ list -> onList(list.orEmpty()) }, { })
    }

    private fun onList(list: List<WebExtension>) {
        handler.post {
            installed.clear()
            list.filter { !it.isBuiltIn }
                .sortedWith(compareBy({ ext -> catalog.indexOfFirst { it.id == ext.id }.let { if (it < 0) 99 else it } }, { it.metaData.name ?: "" }))
                .forEach { ext ->
                    installed += InstalledExtension(ext, catalog.firstOrNull { it.id == ext.id })
                    ext.setActionDelegate(actionDelegate(ext))
                    ext.setTabDelegate(tabDelegate(ext))
                }
            applyForceDark()
        }
    }

    /** Installs the catalog's extensions that aren't there yet, from addons.mozilla.org. */
    fun installMissing(current: List<WebExtension>) {
        val present = current.map { it.id }.toSet()
        catalog.filter { it.id !in present && it.id !in stores.settings.value.removedExtensions() }
            .forEach { install(it) }
    }

    fun install(entry: CatalogExtension) {
        if (entry.id in installing) return
        installing = installing + entry.id
        autoInstalledAt[entry.id] = System.currentTimeMillis()
        controller.install(amoUrl(entry.amoSlug), WebExtensionController.INSTALLATION_METHOD_MANAGER).accept({ ext ->
            handler.post {
                autoInstalledAt[entry.id] = System.currentTimeMillis()
                installing = installing - entry.id
                val wanted = stores.settings.value.extensionChoices[entry.id] ?: entry.enabledByDefault
                if (ext != null && !wanted) controller.disable(ext, WebExtensionController.EnableSource.USER).accept({ refresh() }, { refresh() })
                else refresh()
            }
        }, { error ->
            handler.post {
                installing = installing - entry.id
                lastError = "${entry.name} n'a pas pu être installé (connexion à addons.mozilla.org ?)"
            }
            Log.w(TAG, "Install of ${entry.id} failed", error)
        })
    }

    fun setEnabled(item: InstalledExtension, enabled: Boolean) {
        stores.settings.update { it.copy(extensionChoices = it.extensionChoices + (item.id to enabled)) }
        val result = if (enabled) controller.enable(item.extension, WebExtensionController.EnableSource.USER)
        else controller.disable(item.extension, WebExtensionController.EnableSource.USER)
        result.accept({ refresh() }, { refresh() })
    }

    fun uninstall(item: InstalledExtension) {
        if (item.catalog != null) {
            stores.settings.update { it.copy(extensionChoices = it.extensionChoices + (item.id to false), removedCatalogExtensions = it.removedCatalogExtensions + item.id) }
        }
        controller.uninstall(item.extension).accept({ refresh() }, { refresh() })
    }

    fun reinstall(entry: CatalogExtension) {
        stores.settings.update { it.copy(removedCatalogExtensions = it.removedCatalogExtensions - entry.id, extensionChoices = it.extensionChoices - entry.id) }
        install(entry)
    }

    /** The extension a moz-extension:// page belongs to (shown instead of its random address). */
    fun nameFor(url: String?): String? {
        if (url == null || !url.startsWith("moz-extension://")) return null
        return installed.firstOrNull { item -> item.extension.metaData.baseUrl.let { it.isNotEmpty() && url.startsWith(it) } }?.name
    }

    /** Dark mode for every site = Dark Reader switched on. */
    fun applyForceDark() {
        val darkReader = installed.firstOrNull { it.id == DARK_READER_ID } ?: return
        val wanted = stores.settings.value.forceDarkSites
        if (darkReader.enabled != wanted) {
            val result = if (wanted) controller.enable(darkReader.extension, WebExtensionController.EnableSource.USER)
            else controller.disable(darkReader.extension, WebExtensionController.EnableSource.USER)
            result.accept({ refresh() }, { })
        }
    }

    fun setForceDark(enabled: Boolean) {
        stores.settings.update { it.copy(forceDarkSites = enabled, extensionChoices = it.extensionChoices + (DARK_READER_ID to enabled)) }
        if (installed.none { it.id == DARK_READER_ID }) catalog.firstOrNull { it.id == DARK_READER_ID }?.let(::install) else applyForceDark()
    }

    /** Once a day: asks addons.mozilla.org for newer versions. */
    private fun updateIfDue(list: List<WebExtension>) {
        val now = System.currentTimeMillis()
        if (now - stores.settings.value.lastExtensionUpdateCheck < 24 * 3600_000L) return
        stores.settings.update { it.copy(lastExtensionUpdateCheck = now) }
        list.filter { !it.isBuiltIn }.forEach { ext -> controller.update(ext).accept({ }, { }) }
    }

    // ------------------------------------------------- sessions & actions

    /** Connects a tab's session to the helper script (icons, theme color, fullscreen video). */
    fun attachSession(session: GeckoSession, tab: BrowserTab) {
        val ext = helper ?: return
        session.webExtensionController.setMessageDelegate(ext, object : WebExtension.MessageDelegate {
            override fun onConnect(port: WebExtension.Port) {
                tab.pagePort = port
                port.setDelegate(object : WebExtension.PortDelegate {
                    override fun onPortMessage(message: Any, port: WebExtension.Port) {
                        val json = message as? JSONObject ?: return
                        handler.post { tabsController?.onPageMessage(tab, json) }
                    }

                    override fun onDisconnect(port: WebExtension.Port) {
                        if (tab.pagePort === port) tab.pagePort = null
                    }
                })
            }
        }, NATIVE_APP)
    }

    private fun actionDelegate(ext: WebExtension) = object : WebExtension.ActionDelegate {
        override fun onBrowserAction(extension: WebExtension, session: GeckoSession?, action: WebExtension.Action) {
            if (session != null) return // per-tab overrides: the default is enough for the menu
            val title = action.title ?: extension.metaData.name ?: extension.id
            val icon = action.icon
            if (icon == null) {
                handler.post { actions[extension.id] = ExtensionAction(extension.id, title, null, action.badgeText, action) }
                return
            }
            icon.getBitmap(96).accept({ bitmap ->
                handler.post { actions[extension.id] = ExtensionAction(extension.id, title, bitmap, action.badgeText, action) }
            }, {
                handler.post { actions[extension.id] = ExtensionAction(extension.id, title, null, action.badgeText, action) }
            })
        }

        override fun onTogglePopup(extension: WebExtension, action: WebExtension.Action): GeckoResult<GeckoSession> {
            Log.d(TAG, "togglePopup ${extension.id}")
            return openPopup()
        }

        override fun onOpenPopup(extension: WebExtension, action: WebExtension.Action): GeckoResult<GeckoSession> {
            Log.d(TAG, "openPopup ${extension.id}")
            return openPopup()
        }
    }

    private fun openPopup(): GeckoResult<GeckoSession> {
        closePopup()
        val session = GeckoSession()
        session.open(Engine.runtime)
        popup = session
        return GeckoResult.fromValue(session)
    }

    fun closePopup() {
        popup?.let { runCatching { it.close() } }
        popup = null
    }

    private fun tabDelegate(ext: WebExtension) = object : WebExtension.TabDelegate {
        override fun onNewTab(extension: WebExtension, createDetails: WebExtension.CreateTabDetails): GeckoResult<GeckoSession>? {
            // A catalog extension's welcome page right after Navigatueur installed it: not opened (nobody asked for it).
            val installedAt = autoInstalledAt[extension.id]
            if (installedAt != null && System.currentTimeMillis() - installedAt < 3 * 60_000L) return null
            // Shown at once when it comes from the extension's own panel (a button there), else opened behind.
            val fromPopup = popup != null
            if (fromPopup) handler.post { closePopup() }
            val session = tabsController?.sessionForExtensionTab(createDetails.url ?: "", fromPopup && createDetails.active != false) ?: return null
            return GeckoResult.fromValue(session)
        }

        override fun onOpenOptionsPage(extension: WebExtension) {
            extension.metaData.optionsPageUrl?.let { url -> handler.post { tabsController?.let { it.newTab(url, private = false) } } }
        }
    }

    companion object {
        private const val TAG = "NavigatueurExt"
        const val HELPER_ID = "navigatueur@navigatueur.app"
        const val HELPER_LOCATION = "resource://android/assets/extensions/navigatueur/"
        const val NATIVE_APP = "navigatueur"
        const val DARK_READER_ID = "addon@darkreader.org"

        fun amoUrl(slug: String) = "https://addons.mozilla.org/firefox/downloads/latest/$slug/latest.xpi"

        /** Same set as the Windows app's built-in extensions, plus Dark Reader for the dark mode of sites. */
        val catalog = listOf(
            CatalogExtension("uBlock0@raymondhill.net", "ublock-origin", "uBlock Origin", "Bloque les publicités, les traqueurs et les sites malveillants.", true),
            CatalogExtension("gdpr@cavi.au.dk", "consent-o-matic", "Consent-O-Matic", "Répond tout seul aux bandeaux de cookies, en refusant ce qui n'est pas nécessaire.", true),
            CatalogExtension("sponsorBlocker@ajay.app", "sponsorblock", "SponsorBlock", "Saute les passages sponsorisés des vidéos YouTube.", true),
            CatalogExtension("jid1-BoFifL9Vbdl2zQ@jetpack", "decentraleyes", "Decentraleyes", "Sert localement les bibliothèques courantes au lieu de les demander à des sites tiers.", true),
            CatalogExtension("{a7399979-5203-4489-9861-b168187b52e1}", "purpleadblock", "Purple AdBlock", "Bloque les publicités des lives Twitch.", true),
            CatalogExtension(DARK_READER_ID, "darkreader", "Dark Reader", "Assombrit les sites qui n'ont pas de thème sombre. Activé par « Mode sombre pour tous les sites ».", false),
            CatalogExtension("{aecec67f-0d10-4fa7-b7c7-609a2db280cf}", "violentmonkey", "Violentmonkey", "Exécute des scripts utilisateur sur les sites de votre choix.", false),
            CatalogExtension("firefox@betterttv.net", "betterttv", "BetterTTV", "Émoticônes et options en plus pour le chat Twitch.", false, "Conçu pour Twitch en version ordinateur : activez « Version ordinateur » dans le menu."),
        )
    }
}

private fun com.navigatueur.mobile.data.AppSettings.removedExtensions(): Set<String> = removedCatalogExtensions.toSet()
