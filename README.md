# LUNA IA

> A assistente pessoal de Marcos — voz, Windows, celular e autonomia progressiva.

## Objetivo

Construir uma única LUNA capaz de conversar por voz e atuar em dispositivos autorizados:

- 🖥️ **LUNA PC** — agente residente no Windows, executando ações locais sob um sistema de permissões.
- 📱 **LUNA Mobile** — interface móvel para conversar com a mesma LUNA e acompanhar tarefas.
- ☁️ **LUNA Core** — camada de identidade, configuração, memória e sincronização.

## Princípios

1. Segurança antes de autonomia.
2. O agente local executa ações no dispositivo; o backend não recebe acesso irrestrito ao sistema operacional.
3. Ações sensíveis exigem confirmação explícita.
4. Nenhuma chave secreta deve ser colocada no aplicativo cliente.
5. Cada etapa deve ser testável antes de avançar.

## Fases

### v0.1 — Voz no Windows
- iniciar com o Windows;
- permanecer em segundo plano;
- wake word / ativação por voz;
- fala → texto;
- texto → fala;
- conversa contínua;
- resposta por voz.

### v0.2 — Primeiras ações
- abrir aplicativos;
- abrir sites;
- comandos básicos do Windows;
- registro das ações;
- permissões e confirmações.

### v0.3 — Visão e controle
- leitura da tela;
- mouse e teclado;
- automações guiadas;
- verificação do resultado.

### v0.4 — Mobile
- aplicativo Android/iOS;
- voz;
- notificações;
- sincronização com a mesma identidade da LUNA.

### v0.5+ — Autonomia progressiva
- planejamento de tarefas;
- execução em múltiplas etapas;
- comunicação PC ↔ celular;
- memória autorizada;
- níveis de autonomia.

## Status

**Fase atual: fundação do projeto.**

Nenhum segredo/API key deve ser commitado neste repositório.
