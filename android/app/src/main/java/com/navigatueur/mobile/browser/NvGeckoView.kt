package com.navigatueur.mobile.browser

import android.annotation.SuppressLint
import android.content.Context
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.ViewConfiguration
import android.util.Log
import com.navigatueur.mobile.BuildConfig
import org.mozilla.geckoview.GeckoView
import org.mozilla.geckoview.PanZoomController
import kotlin.math.abs

/**
 * The page view, with "pull to refresh": a pull down that starts while the
 * page is at its very top reloads it. The page's own scroll position says
 * whether it is at its top (the engine's "can scroll up" flag stays on there
 * because of the sliding toolbar); what the engine says about the touch
 * leaves alone maps, carousels and sites that handle the gesture themselves
 * or forbid overscrolling (their own pull-to-refresh).
 */
class NvGeckoView(context: Context) : GeckoView(context) {
    var pullEnabled = false
    /** Pull distance (px, already damped) that triggers a reload on release. */
    var pullThreshold = 200f
    /** Called while pulling with the damped distance; 0 when the pull ends. */
    var onPull: ((Float) -> Unit)? = null
    /** Called once when a pull ends: true if it went far enough to reload. */
    var onPullEnd: ((Boolean) -> Unit)? = null
    /** Whether the page shown is scrolled to its very top. */
    var isScrolledToTop: () -> Boolean = { false }

    private val touchSlop = ViewConfiguration.get(context).scaledTouchSlop
    private var pageAtTop = false
    private var pulling = false
    private var armed = false
    private var startX = 0f
    private var startY = 0f

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                pageAtTop = false
                pulling = false
                armed = false
                startX = event.x
                startY = event.y
                if (BuildConfig.DEBUG) Log.d(TAG, "down pullEnabled=$pullEnabled session=${session != null}")
                if (pullEnabled && session != null) {
                    requestFocus()
                    // Asking how the engine handles this touch also hands the touch to it.
                    onTouchEventForDetailResult(event).accept({ detail ->
                        val handled = detail?.handledResult()
                        pageAtTop = detail != null &&
                            (handled == PanZoomController.INPUT_RESULT_HANDLED || handled == PanZoomController.INPUT_RESULT_UNHANDLED) &&
                            (detail.overscrollDirections() and PanZoomController.OVERSCROLL_FLAG_VERTICAL) != 0 &&
                            isScrolledToTop()
                        if (BuildConfig.DEBUG) {
                            Log.d(TAG, "detail handled=$handled scrollable=${detail?.scrollableDirections()} overscroll=${detail?.overscrollDirections()} top=${isScrolledToTop()} atTop=$pageAtTop")
                        }
                    }, { error ->
                        pageAtTop = false
                        if (BuildConfig.DEBUG) Log.d(TAG, "detail failed", error)
                    })
                    return true
                }
            }
            MotionEvent.ACTION_POINTER_DOWN -> endPull(false)
            MotionEvent.ACTION_MOVE -> if (pageAtTop && pullEnabled) {
                val dy = event.y - startY
                val dx = event.x - startX
                if (!pulling && dy > touchSlop && dy > abs(dx) * 1.5f) {
                    pulling = true
                    if (BuildConfig.DEBUG) Log.d(TAG, "pulling")
                }
                if (pulling) {
                    val distance = ((dy - touchSlop) * 0.5f).coerceAtLeast(0f)
                    val nowArmed = distance >= pullThreshold
                    if (nowArmed && !armed) performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY)
                    armed = nowArmed
                    onPull?.invoke(distance)
                }
            }
            MotionEvent.ACTION_UP -> endPull(armed)
            MotionEvent.ACTION_CANCEL -> endPull(false)
        }
        return super.onTouchEvent(event)
    }

    private fun endPull(reload: Boolean) {
        if (BuildConfig.DEBUG && pulling) Log.d(TAG, "end reload=$reload")
        if (pulling) {
            pulling = false
            onPull?.invoke(0f)
            onPullEnd?.invoke(reload)
        }
        pageAtTop = false
        armed = false
    }

    private companion object {
        const val TAG = "NvPull"
    }
}
