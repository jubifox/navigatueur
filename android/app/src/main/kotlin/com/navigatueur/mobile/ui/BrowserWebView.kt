package com.navigatueur.mobile.ui

import android.app.DownloadManager
import android.net.Uri
import android.os.Bundle
import android.webkit.CookieManager
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.key
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.getSystemService
import com.navigatueur.mobile.data.AdBlockManager
import com.navigatueur.mobile.model.BrowserTab
import java.io.ByteArrayInputStream

/**
 * Only the active tab's WebView is ever alive at a time — `key(tab.id)`
 * forces Compose to fully dispose and recreate the AndroidView (and the
 * WebView inside it) on every tab switch, saving/restoring navigation
 * history via WebView.saveState/restoreState on the tab itself. Deliberately
 * different from the desktop app, which keeps up to 3 tabs' WebView2
 * instances resident (see Phase 2 plan) — a phone has far less headroom, and
 * "restore in ~50ms from saved state" is imperceptible on a tab switch.
 */
@Composable
fun BrowserWebView(
    tab: BrowserTab,
    adBlock: AdBlockManager,
    onProgressChanged: (Int) -> Unit,
    onPageNavigated: (url: String, title: String) -> Unit,
    onWebViewReady: (WebView?) -> Unit,
) {
    val context = LocalContext.current

    key(tab.id) {
        var liveWebView: WebView? = null

        DisposableEffect(tab.id) {
            onDispose {
                liveWebView?.let { webView ->
                    val bundle = Bundle()
                    webView.saveState(bundle)
                    tab.savedState = bundle
                    webView.destroy()
                }
                onWebViewReady(null)
            }
        }

        AndroidView(
            modifier = Modifier,
            factory = { ctx ->
                WebView(ctx).apply {
                    settings.javaScriptEnabled = true
                    settings.domStorageEnabled = true
                    settings.setSupportZoom(true)
                    settings.builtInZoomControls = true
                    settings.displayZoomControls = false
                    settings.loadWithOverviewMode = true
                    settings.useWideViewPort = true

                    webViewClient = object : WebViewClient() {
                        override fun shouldInterceptRequest(
                            view: WebView,
                            request: WebResourceRequest,
                        ): WebResourceResponse? {
                            val host = request.url.host ?: return null
                            return if (adBlock.isEnabled && adBlock.isBlocked(host)) {
                                WebResourceResponse("text/plain", "utf-8", ByteArrayInputStream(ByteArray(0)))
                            } else {
                                null
                            }
                        }

                        override fun onPageFinished(view: WebView, url: String) {
                            onPageNavigated(url, view.title ?: url)
                        }
                    }

                    webChromeClient = object : WebChromeClient() {
                        override fun onProgressChanged(view: WebView, newProgress: Int) {
                            onProgressChanged(newProgress)
                        }

                        override fun onReceivedTitle(view: WebView, title: String?) {
                            if (title != null) {
                                onPageNavigated(view.url ?: tab.url.orEmpty(), title)
                            }
                        }
                    }

                    setDownloadListener { url, _, contentDisposition, mimeType, _ ->
                        val request = DownloadManager.Request(Uri.parse(url)).apply {
                            addRequestHeader("cookie", CookieManager.getInstance().getCookie(url))
                            setMimeType(mimeType)
                            setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
                            val fileName = Uri.parse(url).lastPathSegment ?: "download"
                            setDestinationInExternalPublicDir(android.os.Environment.DIRECTORY_DOWNLOADS, fileName)
                        }
                        ctx.getSystemService<DownloadManager>()?.enqueue(request)
                    }

                    val savedState = tab.savedState
                    val startUrl = tab.url
                    when {
                        savedState != null -> restoreState(savedState)
                        startUrl != null -> loadUrl(startUrl)
                    }

                    liveWebView = this
                    onWebViewReady(this)
                }
            },
        )
    }
}
