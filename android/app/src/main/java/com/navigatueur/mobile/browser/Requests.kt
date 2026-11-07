package com.navigatueur.mobile.browser

import org.mozilla.geckoview.GeckoResult
import org.mozilla.geckoview.GeckoSession.PromptDelegate
import org.mozilla.geckoview.GeckoSession.PromptDelegate.PromptResponse

/**
 * Something a page asked for that needs the user (alert, select menu, file
 * picker, permission…). The UI shows the first pending one; answering it
 * completes the engine's [result].
 */
sealed class PromptRequest(val tabId: String, val result: GeckoResult<PromptResponse>) {
    abstract val prompt: PromptDelegate.BasePrompt

    private var answered = false

    /** Completes the request once; later answers (a dialog closing after its button was pressed) are ignored. */
    fun answer(response: () -> PromptResponse) {
        if (answered) return
        answered = true
        result.complete(response())
    }

    fun dismiss() = answer { prompt.dismiss() }

    class Alert(tabId: String, override val prompt: PromptDelegate.AlertPrompt, result: GeckoResult<PromptResponse>) : PromptRequest(tabId, result)
    class Confirm(tabId: String, override val prompt: PromptDelegate.ButtonPrompt, result: GeckoResult<PromptResponse>) : PromptRequest(tabId, result)
    class Text(tabId: String, override val prompt: PromptDelegate.TextPrompt, result: GeckoResult<PromptResponse>) : PromptRequest(tabId, result)
    class Auth(tabId: String, override val prompt: PromptDelegate.AuthPrompt, result: GeckoResult<PromptResponse>) : PromptRequest(tabId, result)
    class Choice(tabId: String, override val prompt: PromptDelegate.ChoicePrompt, result: GeckoResult<PromptResponse>) : PromptRequest(tabId, result)
    class Color(tabId: String, override val prompt: PromptDelegate.ColorPrompt, result: GeckoResult<PromptResponse>) : PromptRequest(tabId, result)
    class DateTime(tabId: String, override val prompt: PromptDelegate.DateTimePrompt, result: GeckoResult<PromptResponse>) : PromptRequest(tabId, result)
    class Popup(tabId: String, override val prompt: PromptDelegate.PopupPrompt, result: GeckoResult<PromptResponse>) : PromptRequest(tabId, result)
    class BeforeUnload(tabId: String, override val prompt: PromptDelegate.BeforeUnloadPrompt, result: GeckoResult<PromptResponse>) : PromptRequest(tabId, result)
    class Repost(tabId: String, override val prompt: PromptDelegate.RepostConfirmPrompt, result: GeckoResult<PromptResponse>) : PromptRequest(tabId, result)
}

/** A site asking for something the user decides once (and Navigatueur may remember). */
class PermissionRequest(
    val tabId: String,
    val host: String,
    /** "geolocation", "notifications", "camera", "microphone", "camera+microphone", "storage-access", "local-network", "media-keys". */
    val kind: String,
    val onDecision: (allow: Boolean, remember: Boolean) -> Unit,
) {
    val description: String
        get() = when (kind) {
            "geolocation" -> "connaître votre position"
            "notifications" -> "vous envoyer des notifications"
            "camera" -> "utiliser la caméra"
            "microphone" -> "utiliser le micro"
            "camera+microphone" -> "utiliser la caméra et le micro"
            "storage-access" -> "accéder à ses cookies depuis un autre site"
            "local-network" -> "accéder aux appareils de votre réseau local"
            "media-keys" -> "lire des contenus protégés (DRM)"
            else -> "obtenir une autorisation ($kind)"
        }
}

/** Long press on a link, an image or a video in a page. */
data class ContextMenuRequest(
    val tabId: String,
    val linkUrl: String?,
    val linkText: String?,
    val mediaUrl: String?,
    val mediaType: Int,
    val title: String?,
    val isPrivate: Boolean,
)

/** One-off things the screen reacts to. */
sealed class UiEvent {
    data class Message(val text: String) : UiEvent()

    /** A message with an "Annuler" button (a tab just closed…). */
    class Undo(val text: String, val undo: () -> Unit) : UiEvent()
    data class OpenExternal(val uri: String, val fallbackUrl: String?) : UiEvent()
    data class Share(val title: String?, val text: String?, val url: String?) : UiEvent()
    data class PickFiles(val tabId: String, val prompt: PromptDelegate.FilePrompt, val result: GeckoResult<PromptResponse>) : UiEvent()
    data class AndroidPermissions(val permissions: Array<String>, val onResult: (Boolean) -> Unit) : UiEvent()
    data object ShowToolbar : UiEvent()
}
