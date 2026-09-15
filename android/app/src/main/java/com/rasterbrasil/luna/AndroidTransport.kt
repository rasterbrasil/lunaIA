package com.rasterbrasil.luna

import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.io.PrintWriter
import java.net.Socket

object AndroidTransport {
    @Volatile var connected = false
        private set
    private var socket: Socket? = null
    private var writer: PrintWriter? = null
    private var reader: BufferedReader? = null

    fun connect(host: String, pairCode: String): Boolean {
        disconnect()
        return try {
            val s = Socket(host.trim(), 38765)
            s.soTimeout = 30000
            val w = PrintWriter(s.getOutputStream(), true)
            val r = BufferedReader(InputStreamReader(s.getInputStream()))
            w.println("LUNA-ANDROID/1|$pairCode")
            if (r.readLine() != "OK|LUNA-PC/1") { s.close(); return false }
            socket = s; writer = w; reader = r; connected = true; true
        } catch (_: Exception) { connected = false; false }
    }

    fun disconnect() {
        connected = false
        try { socket?.close() } catch (_: Exception) { }
        socket = null; writer = null; reader = null
    }

    @Synchronized
    fun handleCommand(line: String): String {
        return try {
            val j = JSONObject(line)
            val service = LunaAccessibilityService.instance
            if (service == null) return JSONObject().put("ok", false).put("message", "Acessibilidade da LUNA não está ativa.").toString()
            val ok = service.executeCommand(j)
            JSONObject().put("ok", ok).put("message", if (ok) service.lastResult else service.lastResult).toString()
        } catch (e: Exception) {
            JSONObject().put("ok", false).put("message", e.message ?: "Falha no Android.").toString()
        }
    }

    fun listenLoop(onStatus: (String) -> Unit) {
        Thread {
            try {
                while (connected) {
                    val line = reader?.readLine() ?: break
                    val result = handleCommand(line)
                    writer?.println(result)
                }
            } catch (_: Exception) { }
            finally { connected = false; onStatus("Desconectado") }
        }.start()
    }
}
