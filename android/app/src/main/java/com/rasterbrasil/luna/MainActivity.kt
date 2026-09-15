package com.rasterbrasil.luna

import android.content.Intent
import android.os.Bundle
import android.provider.Settings
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView

class MainActivity : android.app.Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val title = TextView(this).apply {
            text = "LUNA — Agente Android\n\nA próxima camada permite à LUNA observar a tela e executar ações no Android."
            textSize = 20f
            setPadding(40, 60, 40, 30)
        }
        val button = Button(this).apply {
            text = "Ativar acesso de acessibilidade"
            setOnClickListener {
                startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
            }
        }
        val status = TextView(this).apply {
            textSize = 16f
            setPadding(40, 20, 40, 40)
        }

        setContentView(LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            addView(title)
            addView(button)
            addView(status)
        })
    }
}
