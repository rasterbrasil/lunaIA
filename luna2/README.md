# LUNA PC 2.0

Projeto iniciado do zero para a nova geração da LUNA PC.

## Objetivo

Construir uma assistente pessoal para Windows com núcleo local, sem depender de API de IA em nuvem para funcionar.

A versão 2.0 não reutiliza o agente antigo. O protótipo anterior permanece no repositório apenas como histórico/laboratório.

## Princípios

- local-first;
- sem API de IA obrigatória;
- sem chaves secretas no aplicativo;
- memória local sob controle do usuário;
- permissões explícitas para ações sensíveis;
- voz e visão desacopladas do núcleo;
- arquitetura modular para permitir, futuramente, um modelo treinado pela própria LUNA.

## Importante

Um aplicativo não vira um modelo de IA treinado do zero apenas por ser local. O núcleo de inferência e o treinamento do modelo serão projetos separados. Primeiro construiremos o sistema operacional da LUNA; depois desenvolveremos o cérebro/modelo próprio em camadas.

## Fases

1. Kernel local e ciclo de vida.
2. Conversa por texto e contexto local.
3. Memória persistente local.
4. Ferramentas e permissões do Windows.
5. Voz local: STT/TTS.
6. Visão da tela.
7. Treinamento/ajuste do cérebro próprio.
8. Sincronização opcional com outros dispositivos.
