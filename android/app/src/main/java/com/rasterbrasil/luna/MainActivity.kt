package com.rasterbrasil.luna

import android.content.Intent
import android.os.Bundle
import android.provider.Settings
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView

class MainActivity : android.app.Activity() {
    private lateinit var host: EditText
    private lateinit var code: EditText
    private lateinit var status: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val prefs=getSharedPreferences("luna",MODE_PRIVATE)
        host=EditText(this).apply{hint="IP do computador (ex.: 192.168.0.10)";setText(prefs.getString("pc_host",""))}
        code=EditText(this).apply{hint="Código de pareamento da LUNA PC";setText(prefs.getString("pair_code",""))}
        status=TextView(this).apply{textSize=16f;setPadding(40,20,40,30)}
        val title=TextView(this).apply{text="LUNA — Agente Android\n\nConecte este celular ao cérebro da LUNA no PC.";textSize=20f;setPadding(40,50,40,25)}
        val accessibility=Button(this).apply{text="Ativar acesso de acessibilidade";setOnClickListener{startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))}}
        val connect=Button(this).apply{text="Conectar ao cérebro da LUNA";setOnClickListener{
            val h=host.text.toString().trim();val c=code.text.toString().trim();prefs.edit().putString("pc_host",h).putString("pair_code",c).apply()
            val service=LunaAccessibilityService.instance
            if(service!=null){service.connectToPc(h,c);status.text="🟡 Conectando ao LUNA PC..."}
            else status.text="🟡 Dados salvos. Ative a acessibilidade da LUNA e depois toque novamente em conectar."
        }}
        status.text=if(AndroidTransport.connected)"🟢 Cérebro conectado." else "🔴 Desconectado."
        setContentView(LinearLayout(this).apply{orientation=LinearLayout.VERTICAL;addView(title);addView(host);addView(code);addView(accessibility);addView(connect);addView(status)})
    }
}
