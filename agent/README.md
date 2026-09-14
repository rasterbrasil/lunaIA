# LUNA PC Agent

Este diretório contém o agente residente do Windows.

## Cérebro local

A LUNA PC não depende da API da OpenAI ou de outro provedor de IA para conversar. O agente usa um motor de inferência local e conversa com ele somente por `127.0.0.1`.

Modelo inicial: `qwen3:4b-instruct`.

- Endpoint padrão: `http://127.0.0.1:11434/api/chat`
- Modelo configurável por `LUNA_LOCAL_AI_MODEL`
- Endpoint configurável por `LUNA_LOCAL_AI_URL`
- Não há chave de API no agente.
- Depois do download inicial do modelo, a conversa pode funcionar sem internet.

A preparação inicial está em `scripts/setup-luna-offline.ps1`. Ela instala o motor local, quando necessário, e baixa o modelo uma única vez.

## Próximas camadas

A implementação será incremental. Antes de dar autonomia ampla ao sistema operacional, o agente terá:

1. configuração local segura;
2. ciclo de vida (iniciar/parar);
3. logging estruturado;
4. camada de áudio desacoplada;
5. adaptador de IA local desacoplado;
6. memória local da LUNA;
7. registro de ferramentas e permissões;
8. confirmação explícita para ações sensíveis;
9. visão da tela e automação do Windows;
10. sincronização opcional com serviços externos somente quando o usuário pedir.

Não coloque tokens ou credenciais neste diretório.
