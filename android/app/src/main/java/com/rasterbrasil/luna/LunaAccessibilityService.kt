package com.rasterbrasil.luna

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.GestureDescription
import android.content.Intent
import android.graphics.Path
import android.os.Bundle
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo
import org.json.JSONObject

class LunaAccessibilityService : AccessibilityService() {
    companion object { @Volatile var instance: LunaAccessibilityService? = null; private set }
    @Volatile var lastResult: String = "Pronto."; private set

    override fun onServiceConnected() {
        super.onServiceConnected(); instance = this
        val p = getSharedPreferences("luna", MODE_PRIVATE)
        connectToPc(p.getString("pc_host", "") ?: "", p.getString("pair_code", "") ?: "")
    }

    fun connectToPc(host: String, code: String) {
        if (host.isBlank() || code.isBlank()) { lastResult = "Informe IP e código de pareamento."; return }
        Thread {
            if (AndroidTransport.connect(host, code)) {
                lastResult = "Conectado ao LUNA PC."
                AndroidTransport.listenLoop { lastResult = it }
            } else lastResult = "Não consegui conectar ao LUNA PC."
        }.start()
    }

    override fun onDestroy() { AndroidTransport.disconnect(); instance = null; super.onDestroy() }
    override fun onAccessibilityEvent(event: AccessibilityEvent?) = Unit
    override fun onInterrupt() = Unit

    fun executeCommand(j: JSONObject): Boolean {
        val action=j.optString("action"); val target=j.optString("target"); val value=j.optString("value")
        val ok=when(action){
            "android_home"->performGlobalAction(GLOBAL_ACTION_HOME)
            "android_back"->performGlobalAction(GLOBAL_ACTION_BACK)
            "android_recents"->performGlobalAction(GLOBAL_ACTION_RECENTS)
            "android_click"->click(j.optDouble("x").toFloat(),j.optDouble("y").toFloat())
            "android_click_text"->clickNodeByText(target)
            "android_type"->setTextByText(target,value)
            "android_open_app"->openApp(target)
            "android_observe"->{lastResult=observe();true}
            else->false
        }
        if(action!="android_observe") lastResult=if(ok) "Android executou: $action${if(target.isBlank())"" else " — $target"}. Tela: ${observe()}" else "Android não conseguiu executar: $action${if(target.isBlank())"" else " — $target"}."
        return ok
    }

    private fun openApp(name:String):Boolean{
        val n=name.lowercase().replace("á","a").replace("ã","a").replace("ç","c").trim()
        val pkgs=when{n.contains("chrome")->listOf("com.android.chrome");n.contains("whatsapp")->listOf("com.whatsapp");n.contains("youtube")->listOf("com.google.android.youtube");n.contains("config")||n.contains("settings")->listOf("com.android.settings");n.contains("calculadora")||n.contains("calculator")->listOf("com.google.android.calculator","com.sec.android.app.popupcalculator");n.contains("gmail")->listOf("com.google.android.gm");else->emptyList()}
        for(pkg in pkgs){val intent=packageManager.getLaunchIntentForPackage(pkg);if(intent!=null){intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);startActivity(intent);return true}}
        return false
    }
    fun click(x:Float,y:Float):Boolean{val path=Path().apply{moveTo(x,y)};val g=GestureDescription.Builder().addStroke(GestureDescription.StrokeDescription(path,0,80)).build();return dispatchGesture(g,null,null)}
    fun clickNodeByText(text:String):Boolean=findNodeByText(rootInActiveWindow,text)?.performAction(AccessibilityNodeInfo.ACTION_CLICK)==true
    fun setTextByText(targetText:String,value:String):Boolean{val node=findNodeByText(rootInActiveWindow,targetText)?:return false;node.performAction(AccessibilityNodeInfo.ACTION_FOCUS);val args=Bundle().apply{putCharSequence(AccessibilityNodeInfo.ACTION_ARGUMENT_SET_TEXT_CHARSEQUENCE,value)};return node.performAction(AccessibilityNodeInfo.ACTION_SET_TEXT,args)}
    private fun observe():String{val root=rootInActiveWindow?:return "nenhuma janela ativa";val parts=mutableListOf<String>();collectText(root,parts,0);return parts.distinct().take(40).joinToString(" | ").ifBlank{"janela ativa sem texto acessível"}}
    private fun collectText(node:AccessibilityNodeInfo?,out:MutableList<String>,depth:Int){if(node==null||depth>12||out.size>=60)return;node.text?.toString()?.trim()?.takeIf{it.isNotBlank()}?.let{out.add(it)};node.contentDescription?.toString()?.trim()?.takeIf{it.isNotBlank()}?.let{out.add(it)};for(i in 0 until node.childCount)collectText(node.getChild(i),out,depth+1)}
    private fun findNodeByText(node:AccessibilityNodeInfo?,text:String):AccessibilityNodeInfo?{if(node==null)return null;if(node.text?.toString()?.contains(text,true)==true||node.contentDescription?.toString()?.contains(text,true)==true)return node;for(i in 0 until node.childCount)findNodeByText(node.getChild(i),text)?.let{return it};return null}
}
