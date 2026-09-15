# LUNA IA — Nível 2: Memória

A memória de longo prazo é local e independente do contexto do Qwen.

## Tipos
- UserProfile: informações que o usuário ensina explicitamente.
- Preference: preferências de resposta e comportamento.
- Project: projetos importantes e associações.
- Computer: informações sobre o computador principal.
- Application: aplicativos e escolhas recorrentes.
- Decision: regras e decisões ensinadas pelo usuário.
- Skill: espaço reservado para habilidades aprendidas.
- Experience: resultado de tarefas executadas e verificadas.
- Conversation: histórico local de mensagens, limitado para evitar crescimento infinito.

## Persistência
Arquivo local: `%LOCALAPPDATA%/LunaIA/memory/long-term-memory.json`.

A memória é carregada na inicialização e gravada de forma atômica. Um arquivo de memória corrompido não impede a LUNA de iniciar.

## Princípio
O Qwen recebe apenas as memórias relevantes para a pergunta ou tarefa. A memória não é confundida com o conhecimento do modelo e não exige novo treinamento do modelo.
