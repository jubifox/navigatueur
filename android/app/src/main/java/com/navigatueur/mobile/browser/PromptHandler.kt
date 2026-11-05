package com.navigatueur.mobile.browser

import android.os.Handler
import android.os.Looper
import org.mozilla.geckoview.GeckoResult
import org.mozilla.geckoview.GeckoSession
import org.mozilla.geckoview.GeckoSession.PromptDelegate
import org.mozilla.geckoview.GeckoSession.PromptDelegate.PromptResponse

/**
 * Dialogs a page opens (alert, confirm, select menus, date pickers, file
 * uploads, HTTP passwords…). GeckoView draws none of them itself: each
 * becomes a [PromptRequest] the UI shows, in order.
 */
class PromptHandler(private val controller: TabsController, private val tab: BrowserTab) : PromptDelegate {
    private val handler = Handler(Looper.getMainLooper())

    private fun <T : PromptRequest> queue(make: (GeckoResult<PromptResponse>) -> T): GeckoResult<PromptResponse> {
        val result = GeckoResult<PromptResponse>()
        val request = make(result)
        request.prompt.setDelegate(object : PromptDelegate.PromptInstanceDelegate {
            // The page closed it itself (navigated away, timed out).
            override fun onPromptDismiss(prompt: PromptDelegate.BasePrompt) {
                handler.post { controller.prompts.remove(request) }
            }
        })
        handler.post { controller.prompts += request }
        return result
    }

    override fun onAlertPrompt(session: GeckoSession, prompt: PromptDelegate.AlertPrompt): GeckoResult<PromptResponse> =
        queue { PromptRequest.Alert(tab.id, prompt, it) }

    override fun onButtonPrompt(session: GeckoSession, prompt: PromptDelegate.ButtonPrompt): GeckoResult<PromptResponse> =
        queue { PromptRequest.Confirm(tab.id, prompt, it) }

    override fun onTextPrompt(session: GeckoSession, prompt: PromptDelegate.TextPrompt): GeckoResult<PromptResponse> =
        queue { PromptRequest.Text(tab.id, prompt, it) }

    override fun onAuthPrompt(session: GeckoSession, prompt: PromptDelegate.AuthPrompt): GeckoResult<PromptResponse> =
        queue { PromptRequest.Auth(tab.id, prompt, it) }

    override fun onChoicePrompt(session: GeckoSession, prompt: PromptDelegate.ChoicePrompt): GeckoResult<PromptResponse> =
        queue { PromptRequest.Choice(tab.id, prompt, it) }

    override fun onColorPrompt(session: GeckoSession, prompt: PromptDelegate.ColorPrompt): GeckoResult<PromptResponse> =
        queue { PromptRequest.Color(tab.id, prompt, it) }

    override fun onDateTimePrompt(session: GeckoSession, prompt: PromptDelegate.DateTimePrompt): GeckoResult<PromptResponse> =
        queue { PromptRequest.DateTime(tab.id, prompt, it) }

    override fun onPopupPrompt(session: GeckoSession, prompt: PromptDelegate.PopupPrompt): GeckoResult<PromptResponse> =
        queue { PromptRequest.Popup(tab.id, prompt, it) }

    override fun onBeforeUnloadPrompt(session: GeckoSession, prompt: PromptDelegate.BeforeUnloadPrompt): GeckoResult<PromptResponse> =
        queue { PromptRequest.BeforeUnload(tab.id, prompt, it) }

    override fun onRepostConfirmPrompt(session: GeckoSession, prompt: PromptDelegate.RepostConfirmPrompt): GeckoResult<PromptResponse> =
        queue { PromptRequest.Repost(tab.id, prompt, it) }

    override fun onFilePrompt(session: GeckoSession, prompt: PromptDelegate.FilePrompt): GeckoResult<PromptResponse> {
        val result = GeckoResult<PromptResponse>()
        controller.emit(UiEvent.PickFiles(tab.id, prompt, result))
        return result
    }

    override fun onSharePrompt(session: GeckoSession, prompt: PromptDelegate.SharePrompt): GeckoResult<PromptResponse> {
        controller.emit(UiEvent.Share(prompt.title, prompt.text, prompt.uri))
        return GeckoResult.fromValue(prompt.confirm(PromptDelegate.SharePrompt.Result.SUCCESS))
    }
}
