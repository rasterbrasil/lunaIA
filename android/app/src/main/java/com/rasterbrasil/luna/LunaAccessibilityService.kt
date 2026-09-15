package com.rasterbrasil.luna

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.GestureDescription
import android.graphics.Path
import android.os.Bundle
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo

/**
 * Corpo de ação da LUNA no Android.
 * A autorização é concedida pelo próprio usuário nas configurações de acessibilidade.
 */
class LunaAccessibilityService : AccessibilityService() {
    companion object {
        @Volatile
        var instance: LunaAccessibilityService? = null
            private set
    }

    override fun onServiceConnected() {
        super.onServiceConnected()
        instance = this
    }

    override fun onDestroy() {
        instance = null
        super.onDestroy()
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        // A percepção detalhada será enviada ao cérebro compartilhado na próxima camada.
        // Manter o callback leve evita bloquear a UI do Android.
    }

    override fun onInterrupt() = Unit

    fun click(x: Float, y: Float): Boolean {
        val path = Path().apply { moveTo(x, y) }
        val gesture = GestureDescription.Builder()
            .addStroke(GestureDescription.StrokeDescription(path, 0, 80))
            .build()
        return dispatchGesture(gesture, null, null)
    }

    fun clickNodeByText(text: String): Boolean =
        findNodeByText(rootInActiveWindow, text)?.performAction(AccessibilityNodeInfo.ACTION_CLICK) == true

    fun setTextByText(targetText: String, value: String): Boolean {
        val node = findNodeByText(rootInActiveWindow, targetText) ?: return false
        node.performAction(AccessibilityNodeInfo.ACTION_FOCUS)
        val args = Bundle().apply {
            putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE, value)
        }
        return node.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT, args)
    }

    fun back(): Boolean = performGlobalAction(GLOBAL_ACTION_BACK)
    fun home(): Boolean = performGlobalAction(GLOBAL_ACTION_HOME)
    fun recentApps(): Boolean = performGlobalAction(GLOBAL_ACTION_RECENTS)

    private fun findNodeByText(node: AccessibilityNodeInfo?, text: String): AccessibilityNodeInfo? {
        if (node == null) return null
        if (node.text?.toString()?.contains(text, ignoreCase = true) == true ||
            node.contentDescription?.toString()?.contains(text, ignoreCase = true) == true) {
            return node
        }
        for (i in 0 until node.childCount) {
            val found = findNodeByText(node.getChild(i), text)
            if (found != null) return found
        }
        return null
    }
}
