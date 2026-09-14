# Arquitetura LUNA PC 2.0

```text
LUNA PC
  |
  +-- Kernel
  |    +-- ciclo de vida
  |    +-- configuração
  |    +-- logs locais
  |    +-- estado
  |
  +-- Cognitive Core
  |    +-- contexto
  |    +-- memória
  |    +-- raciocínio
  |    +-- inferência local
  |
  +-- Tool System
  |    +-- Windows
  |    +-- arquivos
  |    +-- processos
  |    +-- navegador
  |    +-- automação
  |
  +-- Safety / Permissions
  |    +-- permissões
  |    +-- confirmação
  |    +-- auditoria
  |
  +-- I/O
       +-- texto
       +-- STT local
       +-- TTS local
       +-- visão local
```

## Regra arquitetural principal

O Kernel não conhece fornecedor de modelo, fornecedor de voz ou serviço de nuvem. Interfaces internas permitem trocar o motor sem reescrever o aplicativo.

## Dependências

O núcleo deve depender apenas do runtime necessário para executar o aplicativo. Componentes opcionais (modelo, STT, TTS e visão) entram como módulos locais e nunca como APIs remotas obrigatórias.

## Privacidade

Dados pessoais e memória ficam em armazenamento local. Sincronização externa será opt-in e não fará parte do funcionamento básico.
