package com.navigatueur.mobile.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.navigatueur.mobile.browser.BrowserTab
import com.navigatueur.mobile.browser.TabsController
import com.navigatueur.mobile.core.UrlHelper
import com.navigatueur.mobile.ui.theme.Nv

/** The tab's previous and next pages, newest at the top: a tap goes straight there. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TabHistorySheet(controller: TabsController, tab: BrowserTab, onDone: () -> Unit) {
    val colors = Nv.colors
    val entries = tab.history.withIndex().reversed()
    val current = tab.historyIndex
    // Opens with the page shown in view.
    val listState = rememberLazyListState(initialFirstVisibleItemIndex = entries.indexOfFirst { it.index == current }.coerceAtLeast(0).let { (it - 2).coerceAtLeast(0) })

    ModalBottomSheet(
        onDismissRequest = onDone,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
        containerColor = colors.sheet,
    ) {
        Column(Modifier.fillMaxWidth().navigationBarsPadding()) {
            Text(
                "Pages de cet onglet",
                color = colors.text, fontWeight = FontWeight.SemiBold, fontSize = 17.sp,
                modifier = Modifier.padding(start = 22.dp, bottom = 8.dp),
            )
            LazyColumn(
                state = listState,
                modifier = Modifier.fillMaxWidth().heightIn(max = (LocalConfiguration.current.screenHeightDp * 0.6f).dp),
            ) {
                itemsIndexed(entries, key = { _, entry -> entry.index }) { _, (index, item) ->
                    val isCurrent = index == current
                    Row(
                        Modifier
                            .fillMaxWidth()
                            .clickable(enabled = !isCurrent) {
                                controller.goToHistory(tab, index)
                                onDone()
                            }
                            .padding(horizontal = 22.dp, vertical = 11.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Box(
                            Modifier.size(8.dp).clip(Nv.shape(4.dp))
                                .background(if (isCurrent) colors.accent else if (index < current) colors.textTertiary else colors.surfaceActive)
                        )
                        Spacer(Modifier.width(16.dp))
                        Column(Modifier.weight(1f)) {
                            Text(
                                item.title.ifBlank { UrlHelper.displayHost(item.url) ?: item.url },
                                color = if (isCurrent) colors.accent else colors.text,
                                fontWeight = if (isCurrent) FontWeight.SemiBold else FontWeight.Normal,
                                fontSize = 15.sp, maxLines = 1, overflow = TextOverflow.Ellipsis,
                            )
                            Text(
                                when {
                                    isCurrent -> "Page affichée"
                                    index < current -> UrlHelper.displayHost(item.url) ?: item.url
                                    else -> "Suivante · " + (UrlHelper.displayHost(item.url) ?: item.url)
                                },
                                color = colors.textTertiary, fontSize = 13.sp, maxLines = 1, overflow = TextOverflow.Ellipsis,
                            )
                        }
                    }
                }
            }
            Spacer(Modifier.size(8.dp))
        }
    }
}
