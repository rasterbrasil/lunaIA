# LUNA IA

> A inteligência pessoal de Marcos — Windows, celular, visão, memória e autonomia progressiva.

## Objetivo

Construir uma única LUNA capaz de conversar e atuar em dispositivos autorizados, mantendo uma identidade e uma arquitetura próprias.

- 🧠 **LUNA IA** — identidade, raciocínio, memória, planejamento e coordenação.
- 🖥️ **LUNA no Windows** — agente local que executa ações no computador sob permissões.
- 📱 **LUNA Mobile** — interface móvel para conversar com a mesma LUNA e acompanhar tarefas.
- ☁️ **LUNA Core** — camada opcional de configuração, sincronização e estado compartilhado.

O objetivo não é criar apenas uma interface para outro serviço de IA. A arquitetura deve permitir que a LUNA opere localmente e evolua para um motor de raciocínio próprio.

## Princípios

1. Segurança antes de autonomia.
2. O agente local executa ações no dispositivo; o backend não recebe acesso irrestrito ao sistema operacional.
3. Ações sensíveis exigem confirmação explícita.
4. Nenhuma chave secreta deve ser colocada no aplicativo cliente.
5. Cada etapa deve ser testável antes de avançar.
6. A LUNA deve saber diferenciar entendimento, planejamento, execução e verificação.

## Fases

### 1 — Fundação local
- aplicativo Windows residente;
- memória local persistente;
- comandos e ferramentas locais;
- registro de ações;
- sistema de permissões.

### 2 — Planejamento e execução
- interpretar pedidos em linguagem natural;
- dividir tarefas em etapas;
- executar ações em sequência;
- verificar resultados;
- recuperar de falhas simples.

### 3 — Visão e controle
- captura local da tela;
- interpretação visual/OCR;
- mouse e teclado;
- automações guiadas;
- verificação visual após cada etapa.

### 4 — Voz
- ativação por voz;
- reconhecimento de fala;
- voz natural em português brasileiro;
- conversa contínua.

### 5 — Mobile
- aplicativo Android/iOS;
- voz;
- notificações;
- sincronização com a mesma identidade da LUNA.

### 6 — Inteligência própria
- motor de raciocínio local;
- contexto e memória de longo prazo autorizada;
- ferramentas estruturadas;
- planejamento de tarefas complexas;
- autonomia progressiva.

## Status

**Projeto ativo — LUNA IA em construção.**

Nenhum segredo/API key deve ser commitado neste repositório.
