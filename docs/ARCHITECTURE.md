# Arquitetura inicial

## Visão

```text
                 LUNA CORE
                     │
          ┌──────────┴──────────┐
          │                     │
      LUNA PC              LUNA MOBILE
      Windows                 Android/iOS
          │                     │
    execução local        voz/notificações
          │                     │
          └──────────┬──────────┘
                     │
                  Supabase
```

## Responsabilidades

### LUNA PC

Processo local residente no Windows. Será responsável por microfone, áudio, execução de ferramentas locais e, posteriormente, visão/controle da interface.

### LUNA Mobile

Cliente móvel. Não executa comandos arbitrários no PC. Solicita tarefas através da camada autorizada e recebe estado/resultados.

### Supabase

Usado para identidade, pareamento de dispositivos, configuração, tarefas, eventos e memória que o usuário autorizar. RLS será aplicado às tabelas expostas.

### Cérebro de IA

A integração com o modelo de IA ficará atrás de uma camada própria para permitir trocar o provedor sem reescrever o agente.

## Segurança

- Segredos ficam somente no ambiente apropriado, nunca no cliente.
- O PC valida comandos antes de executar ferramentas.
- Ferramentas terão permissões individuais.
- Operações destrutivas, financeiras, autenticação e outras ações sensíveis terão confirmação.
- Eventos de execução serão registrados para auditoria.

## Primeira entrega técnica

A primeira entrega é o esqueleto do agente Windows, separado do backend, preparado para receber posteriormente STT, TTS, wake word e o adaptador do cérebro de IA.
