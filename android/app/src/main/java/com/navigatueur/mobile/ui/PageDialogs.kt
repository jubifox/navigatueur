package com.navigatueur.mobile.ui

import android.app.DatePickerDialog
import android.app.DownloadManager
import android.app.TimePickerDialog
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.net.Uri
import android.os.Environment
import android.webkit.URLUtil
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.rounded.Check
import androidx.compose.material.icons.rounded.ContentCopy
import androidx.compose.material.icons.rounded.Download
import androidx.compose.material.icons.rounded.Image
import androidx.compose.material.icons.rounded.OpenInNew
import androidx.compose.material.icons.rounded.Share
import androidx.compose.material.icons.rounded.Tab
import androidx.compose.material.icons.rounded.VisibilityOff
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CheckboxDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import com.navigatueur.mobile.NavigatueurApp
import com.navigatueur.mobile.browser.ContextMenuRequest
import com.navigatueur.mobile.browser.PromptRequest
import com.navigatueur.mobile.ui.theme.Nv
import com.navigatueur.mobile.ui.theme.parseHexColor
import org.mozilla.geckoview.AllowOrDeny
import org.mozilla.geckoview.GeckoSession.ContentDelegate.ContextElement
import org.mozilla.geckoview.GeckoSession.PromptDelegate
import org.mozilla.geckoview.GeckoView
import java.util.Calendar
import java.util.Locale

/** Everything a page or an extension asks the user, shown over the page. */
@Composable
fun PageDialogs(app: NavigatueurApp, actions: ActivityActions) {
    val controller = app.tabs
    val active = controller.activeTabId

    // A page's dialog only shows while its tab is the visible one.
    controller.prompts.firstOrNull { it.tabId == active }?.let { request ->
        PromptDialog(request, onDone = { controller.prompts.remove(request) })
    }
    controller.contextMenu?.let { menu -> ContextMenuSheet(app, menu, actions, onDone = { controller.contextMenu = null }) }
    app.extensions.pendingInstall?.let { pending ->
        AlertDialog(
            onDismissRequest = { pending.decide(false) },
            title = { Text("Ajouter « ${pending.name} » ?") },
            text = {
                val origins = pending.origins.let { if (it.any { o -> o == "<all_urls>" || o.contains("*://*/") }) listOf("toutes les pages") else it }
                Text(
                    buildString {
                        append("Cette extension pourra :")
                        if (origins.isNotEmpty()) append("\n• lire et modifier ${origins.take(4).joinToString(", ")}")
                        pending.permissions.take(8).forEach { append("\n• $it") }
                    },
                    color = Nv.colors.textSecondary,
                )
            },
            confirmButton = { TextButton(onClick = { pending.decide(true) }) { Text("Ajouter") } },
            dismissButton = { TextButton(onClick = { pending.decide(false) }) { Text("Annuler") } },
        )
    }
    app.extensions.popup?.let { session -> ExtensionPopupSheet(session, onDone = { app.extensions.closePopup() }) }
}

@Composable
private fun PromptDialog(request: PromptRequest, onDone: () -> Unit) {
    fun finish(response: () -> PromptDelegate.PromptResponse) {
        request.answer(response)
        onDone()
    }
    val dismiss = { request.dismiss(); onDone() }
    val colors = Nv.colors
    val title = request.prompt.title?.takeIf { it.isNotBlank() }

    when (request) {
        is PromptRequest.Alert -> AlertDialog(
            onDismissRequest = dismiss,
            title = title?.let { { Text(it) } },
            text = { Text(request.prompt.message ?: "") },
            confirmButton = { TextButton(onClick = dismiss) { Text("OK") } },
        )

        is PromptRequest.Confirm -> AlertDialog(
            onDismissRequest = dismiss,
            title = title?.let { { Text(it) } },
            text = { Text(request.prompt.message ?: "") },
            confirmButton = { TextButton(onClick = { finish { request.prompt.confirm(PromptDelegate.ButtonPrompt.Type.POSITIVE) } }) { Text("OK") } },
            dismissButton = { TextButton(onClick = { finish { request.prompt.confirm(PromptDelegate.ButtonPrompt.Type.NEGATIVE) } }) { Text("Annuler") } },
        )

        is PromptRequest.Text -> {
            var value by remember(request) { mutableStateOf(request.prompt.defaultValue ?: "") }
            AlertDialog(
                onDismissRequest = dismiss,
                title = title?.let { { Text(it) } },
                text = {
                    Column {
                        request.prompt.message?.let { Text(it); Spacer(Modifier.height(10.dp)) }
                        OutlinedTextField(value = value, onValueChange = { value = it }, singleLine = true)
                    }
                },
                confirmButton = { TextButton(onClick = { finish { request.prompt.confirm(value) } }) { Text("OK") } },
                dismissButton = { TextButton(onClick = dismiss) { Text("Annuler") } },
            )
        }

        is PromptRequest.Auth -> {
            var user by remember(request) { mutableStateOf(request.prompt.authOptions.username ?: "") }
            var password by remember(request) { mutableStateOf(request.prompt.authOptions.password ?: "") }
            val passwordOnly = request.prompt.authOptions.flags and 8 != 0 // AuthOptions.Flags.ONLY_PASSWORD
            AlertDialog(
                onDismissRequest = dismiss,
                title = { Text(title ?: "Identification requise") },
                text = {
                    Column {
                        request.prompt.message?.let { Text(it, color = colors.textSecondary); Spacer(Modifier.height(10.dp)) }
                        if (!passwordOnly) OutlinedTextField(value = user, onValueChange = { user = it }, singleLine = true, label = { Text("Nom d'utilisateur") })
                        OutlinedTextField(
                            value = password, onValueChange = { password = it }, singleLine = true, label = { Text("Mot de passe") },
                            visualTransformation = PasswordVisualTransformation(), keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
                        )
                    }
                },
                confirmButton = { TextButton(onClick = { finish { if (passwordOnly) request.prompt.confirm(password) else request.prompt.confirm(user, password) } }) { Text("Se connecter") } },
                dismissButton = { TextButton(onClick = dismiss) { Text("Annuler") } },
            )
        }

        is PromptRequest.Choice -> ChoiceDialog(request, title, onFinish = ::finish, onDismiss = dismiss)

        is PromptRequest.Color -> {
            var hex by remember(request) { mutableStateOf(request.prompt.defaultValue ?: "#000000") }
            val palette = listOf("#000000", "#FFFFFF", "#E04F4F", "#E0A52A", "#F5D90A", "#4FE0A0", "#30A46C", "#4FD1E0", "#4C8DFF", "#3E63DD", "#B14FE0", "#F472B6", "#8B8D98", "#5F5F68", "#A0522D", "#FF8C00")
            AlertDialog(
                onDismissRequest = dismiss,
                title = { Text(title ?: "Choisir une couleur") },
                text = {
                    Column {
                        palette.chunked(4).forEach { row ->
                            Row(Modifier.fillMaxWidth().padding(vertical = 5.dp), horizontalArrangement = Arrangement.SpaceEvenly) {
                                row.forEach { color ->
                                    Box(
                                        Modifier.size(38.dp).clip(CircleShape).background(parseHexColor(color))
                                            .border(if (color.equals(hex, true)) 3.dp else 1.dp, colors.textSecondary, CircleShape)
                                            .clickable { hex = color },
                                    )
                                }
                            }
                        }
                        Spacer(Modifier.height(8.dp))
                        OutlinedTextField(value = hex, onValueChange = { hex = it }, singleLine = true, label = { Text("Code") })
                    }
                },
                confirmButton = { TextButton(onClick = { finish { request.prompt.confirm(hex.lowercase(Locale.ROOT)) } }) { Text("OK") } },
                dismissButton = { TextButton(onClick = dismiss) { Text("Annuler") } },
            )
        }

        is PromptRequest.DateTime -> DateTimePicker(request, onFinish = ::finish, onDismiss = dismiss)

        is PromptRequest.Popup -> AlertDialog(
            onDismissRequest = { finish { request.prompt.confirm(AllowOrDeny.DENY) } },
            title = { Text("Fenêtre bloquée") },
            text = { Text("Ce site veut ouvrir ${request.prompt.targetUri ?: "une nouvelle fenêtre"}.") },
            confirmButton = { TextButton(onClick = { finish { request.prompt.confirm(AllowOrDeny.ALLOW) } }) { Text("Ouvrir") } },
            dismissButton = { TextButton(onClick = { finish { request.prompt.confirm(AllowOrDeny.DENY) } }) { Text("Bloquer") } },
        )

        is PromptRequest.BeforeUnload -> AlertDialog(
            onDismissRequest = { finish { request.prompt.confirm(AllowOrDeny.DENY) } },
            title = { Text("Quitter la page ?") },
            text = { Text("Ce que vous avez saisi pourrait être perdu.") },
            confirmButton = { TextButton(onClick = { finish { request.prompt.confirm(AllowOrDeny.ALLOW) } }) { Text("Quitter") } },
            dismissButton = { TextButton(onClick = { finish { request.prompt.confirm(AllowOrDeny.DENY) } }) { Text("Rester") } },
        )

        is PromptRequest.Repost -> AlertDialog(
            onDismissRequest = { finish { request.prompt.confirm(AllowOrDeny.DENY) } },
            title = { Text("Renvoyer le formulaire ?") },
            text = { Text("Recharger cette page renverra les informations déjà envoyées (un achat pourrait être fait deux fois).") },
            confirmButton = { TextButton(onClick = { finish { request.prompt.confirm(AllowOrDeny.ALLOW) } }) { Text("Renvoyer") } },
            dismissButton = { TextButton(onClick = { finish { request.prompt.confirm(AllowOrDeny.DENY) } }) { Text("Annuler") } },
        )
    }
}

@Composable
private fun ChoiceDialog(
    request: PromptRequest.Choice,
    title: String?,
    onFinish: (() -> PromptDelegate.PromptResponse) -> Unit,
    onDismiss: () -> Unit,
) {
    val colors = Nv.colors
    val multiple = request.prompt.type == PromptDelegate.ChoicePrompt.Type.MULTIPLE
    // Flattened: option groups become headers.
    val rows = remember(request) {
        buildList {
            request.prompt.choices.forEach { choice ->
                if (choice.items != null) {
                    add(choice to true)
                    choice.items!!.forEach { add(it to false) }
                } else add(choice to false)
            }
        }
    }
    val selected = remember(request) { mutableStateListOf<String>().apply { addAll(rows.filter { it.first.selected }.map { it.first.id }) } }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = title?.let { { Text(it) } },
        text = {
            LazyColumn(Modifier.heightIn(max = 420.dp)) {
                items(rows) { (choice, isGroup) ->
                    when {
                        choice.separator -> Box(Modifier.fillMaxWidth().padding(vertical = 6.dp).height(1.dp).background(colors.panelBorder))
                        isGroup -> Text(choice.label, color = colors.textTertiary, fontSize = 12.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 10.dp, bottom = 2.dp))
                        else -> Row(
                            Modifier
                                .fillMaxWidth()
                                .clip(RoundedCornerShape(8.dp))
                                .clickable(enabled = !choice.disabled) {
                                    if (multiple) {
                                        if (choice.id in selected) selected.remove(choice.id) else selected.add(choice.id)
                                    } else {
                                        onFinish { request.prompt.confirm(choice) }
                                    }
                                }
                                .padding(vertical = 11.dp, horizontal = 6.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            if (multiple) {
                                Checkbox(choice.id in selected, null, colors = CheckboxDefaults.colors(checkedColor = colors.accent), modifier = Modifier.size(24.dp))
                                Spacer(Modifier.width(10.dp))
                            }
                            Text(
                                choice.label,
                                color = if (choice.disabled) colors.textTertiary else colors.text,
                                fontWeight = if (!multiple && choice.selected) FontWeight.SemiBold else FontWeight.Normal,
                                modifier = Modifier.weight(1f),
                            )
                            if (!multiple && choice.selected) Icon(Icons.Rounded.Check, null, tint = colors.accent, modifier = Modifier.size(18.dp))
                        }
                    }
                }
            }
        },
        confirmButton = {
            if (multiple) TextButton(onClick = { onFinish { request.prompt.confirm(selected.toTypedArray()) } }) { Text("OK") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Annuler") } },
    )
}

/** HTML date/time inputs, with Android's own pickers. */
@Composable
private fun DateTimePicker(request: PromptRequest.DateTime, onFinish: (() -> PromptDelegate.PromptResponse) -> Unit, onDismiss: () -> Unit) {
    val context = LocalContext.current
    LaunchedEffect(request) {
        val prompt = request.prompt
        val calendar = Calendar.getInstance()
        val default = prompt.defaultValue.orEmpty()
        runCatching {
            Regex("(\\d{4})-(\\d{2})-(\\d{2})").find(default)?.destructured?.let { (y, m, d) -> calendar.set(y.toInt(), m.toInt() - 1, d.toInt()) }
            Regex("(\\d{2}):(\\d{2})").find(default)?.destructured?.let { (h, min) -> calendar.set(Calendar.HOUR_OF_DAY, h.toInt()); calendar.set(Calendar.MINUTE, min.toInt()) }
        }
        var answered = false
        fun date(onPicked: (Calendar) -> Unit) {
            DatePickerDialog(context, { _, y, m, d -> calendar.set(y, m, d); onPicked(calendar) }, calendar.get(Calendar.YEAR), calendar.get(Calendar.MONTH), calendar.get(Calendar.DAY_OF_MONTH)).apply {
                setOnDismissListener { if (!answered) onDismiss() }
            }.show()
        }
        fun time(onPicked: (Calendar) -> Unit) {
            TimePickerDialog(context, { _, h, min -> calendar.set(Calendar.HOUR_OF_DAY, h); calendar.set(Calendar.MINUTE, min); onPicked(calendar) },
                calendar.get(Calendar.HOUR_OF_DAY), calendar.get(Calendar.MINUTE), true).apply {
                setOnDismissListener { if (!answered) onDismiss() }
            }.show()
        }
        fun answer(text: String) {
            answered = true
            onFinish { prompt.confirm(text) }
        }
        fun ymd(c: Calendar) = String.format(Locale.ROOT, "%04d-%02d-%02d", c.get(Calendar.YEAR), c.get(Calendar.MONTH) + 1, c.get(Calendar.DAY_OF_MONTH))
        fun hm(c: Calendar) = String.format(Locale.ROOT, "%02d:%02d", c.get(Calendar.HOUR_OF_DAY), c.get(Calendar.MINUTE))

        when (prompt.type) {
            PromptDelegate.DateTimePrompt.Type.TIME -> time { answer(hm(it)) }
            PromptDelegate.DateTimePrompt.Type.MONTH -> date { answer(String.format(Locale.ROOT, "%04d-%02d", it.get(Calendar.YEAR), it.get(Calendar.MONTH) + 1)) }
            PromptDelegate.DateTimePrompt.Type.WEEK -> date {
                val week = Calendar.getInstance(Locale.FRANCE).apply { minimalDaysInFirstWeek = 4; firstDayOfWeek = Calendar.MONDAY; timeInMillis = it.timeInMillis }
                answer(String.format(Locale.ROOT, "%04d-W%02d", week.getWeekYear(), week.get(Calendar.WEEK_OF_YEAR)))
            }
            PromptDelegate.DateTimePrompt.Type.DATETIME_LOCAL -> date { picked -> time { answer(ymd(picked) + "T" + hm(it)) } }
            else -> date { answer(ymd(it)) }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ContextMenuSheet(app: NavigatueurApp, menu: ContextMenuRequest, actions: ActivityActions, onDone: () -> Unit) {
    val context = LocalContext.current
    val controller = app.tabs
    val colors = Nv.colors
    val source = controller.tabById(menu.tabId)
    val isImage = menu.mediaType == ContextElement.TYPE_IMAGE && menu.mediaUrl != null

    fun copy(text: String) {
        (context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("URL", text))
        controller.emit(com.navigatueur.mobile.browser.UiEvent.Message("Copié"))
    }

    ModalBottomSheet(onDismissRequest = onDone, sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true), containerColor = colors.panel) {
        Column(Modifier.fillMaxWidth().navigationBarsPadding().padding(bottom = 12.dp)) {
            Text(
                menu.linkUrl ?: menu.mediaUrl ?: "",
                color = colors.textTertiary, fontSize = 13.sp, maxLines = 2, overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(horizontal = 22.dp, vertical = 6.dp),
            )
            menu.linkUrl?.let { link ->
                ContextItem(Icons.Rounded.Tab, "Ouvrir dans un nouvel onglet") { onDone(); controller.newTab(link, private = menu.isPrivate, select = false, parent = source); controller.emit(com.navigatueur.mobile.browser.UiEvent.Message("Ouvert en arrière-plan")) }
                ContextItem(Icons.Rounded.OpenInNew, "Ouvrir et afficher") { onDone(); controller.newTab(link, private = menu.isPrivate, select = true, parent = source) }
                if (!menu.isPrivate) ContextItem(Icons.Rounded.VisibilityOff, "Ouvrir dans un onglet privé") { onDone(); controller.newTab(link, private = true) }
                ContextItem(Icons.Rounded.ContentCopy, "Copier le lien") { onDone(); copy(link) }
                ContextItem(Icons.Rounded.Share, "Partager le lien") { onDone(); actions.share(menu.linkText, null, link) }
                ContextItem(Icons.Rounded.Download, "Télécharger le lien") { onDone(); systemDownload(context, link) }
            }
            if (isImage) {
                val image = menu.mediaUrl!!
                ContextItem(Icons.Rounded.Image, "Ouvrir l'image dans un nouvel onglet") { onDone(); controller.newTab(image, private = menu.isPrivate, select = true, parent = source) }
                if (!image.startsWith("data:")) ContextItem(Icons.Rounded.Download, "Enregistrer l'image") { onDone(); systemDownload(context, image) }
                ContextItem(Icons.Rounded.ContentCopy, "Copier l'adresse de l'image") { onDone(); copy(image) }
                ContextItem(Icons.Rounded.Share, "Partager l'image") { onDone(); actions.share(menu.title, null, image) }
            } else if (menu.mediaUrl != null && menu.linkUrl == null) {
                ContextItem(Icons.Rounded.ContentCopy, "Copier l'adresse du média") { onDone(); copy(menu.mediaUrl) }
            }
        }
    }
}

/** Images and links saved from the long-press menu go through Android's download manager. */
private fun systemDownload(context: Context, url: String) {
    runCatching {
        val name = URLUtil.guessFileName(url, null, null)
        val request = DownloadManager.Request(Uri.parse(url))
            .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
            .setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, name)
            .setTitle(name)
        context.getSystemService(DownloadManager::class.java).enqueue(request)
        NavigatueurApp.instance.tabs.emit(com.navigatueur.mobile.browser.UiEvent.Message("Téléchargement de $name"))
    }.onFailure {
        NavigatueurApp.instance.tabs.emit(com.navigatueur.mobile.browser.UiEvent.Message("Impossible de télécharger ce fichier"))
    }
}

@Composable
private fun ContextItem(icon: ImageVector, title: String, onClick: () -> Unit) {
    val colors = Nv.colors
    Row(Modifier.fillMaxWidth().clickable(onClick = onClick).padding(horizontal = 22.dp, vertical = 13.dp), verticalAlignment = Alignment.CenterVertically) {
        Icon(icon, null, tint = colors.textSecondary, modifier = Modifier.size(21.dp))
        Spacer(Modifier.width(18.dp))
        Text(title, color = colors.text, fontSize = 15.sp)
    }
}

/** An extension's panel (uBlock Origin's, Dark Reader's…), in a sheet. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ExtensionPopupSheet(session: org.mozilla.geckoview.GeckoSession, onDone: () -> Unit) {
    ModalBottomSheet(
        onDismissRequest = onDone,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        containerColor = Nv.colors.panel,
    ) {
        AndroidView(
            factory = { context -> GeckoView(context).apply { setSession(session) } },
            onRelease = { it.releaseSession() },
            modifier = Modifier.fillMaxWidth().fillMaxHeight(0.75f).navigationBarsPadding(),
        )
    }
}
