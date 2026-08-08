## Why

O vínculo `AgentDelegation` (Source → Target) já existe no catálogo
(`backend-agente-delegacao-catalogo-vinculo`, aplicada), mas é só
cadastro — nenhum agente consegue de fato delegar uma tarefa a outro
durante o processamento. Esta é a última fatia de execução real antes da
UI: dar ao LLM do agente Source uma tool que, quando chamada, cria e
processa uma task real para o agente Target vinculado e devolve o
resultado, no mesmo `contextId` da conversa.

## What Changes

- Mirror somente-leitura de `AgentDelegation` em `apps/workers` (mesmo
  padrão de `McpServer`/`AgentMcpServer`), sem `ProjectReference` para
  `apps/api`.
- Publisher novo em `apps/workers` (mirror de `RabbitMqTaskJobPublisher`
  de `apps/api`) — `apps/workers` hoje só consome do RabbitMQ, nunca
  publica.
- `IAgentDelegationToolSetResolver`: dado o `agentId` do Source, monta
  uma tool por `AgentDelegation` vinculado (`AITool`, via
  `AIFunctionFactory.Create`), nome estável derivado do `Agent.Name` do
  Target via slug + dedupe determinístico (mirror de
  `AgentSkillMapper.Slugify`, hoje em `apps/api`). Integrado em
  `AgentExecutionService` junto do resolver de tools MCP já existente,
  populando `ChatOptions.Tools` na mesma chamada.
- A tool de delegação, quando chamada pelo LLM do Source: valida o
  Target fresco (`IsActive`, `Provider`/`Model`) e o próprio Source;
  valida profundidade da cadeia contra um teto configurado; cria e grava
  a task `submitted` do Target direto via `PostgresTaskStore` próprio
  (mesmo `contextId` do Source), com o contador de profundidade já em
  `AgentTask.Metadata` desde a criação; publica o job na fila
  `agent-tasks`; aguarda (via polling, com timeout) a task do Target
  chegar a um estado terminal; retorna o resultado (ou uma falha
  graciosa) para o LLM do Source continuar.
- **Requisito operacional novo, não opcional**: `apps/workers` precisa
  rodar com no mínimo 2 réplicas para que qualquer delegação funcione —
  numa única instância, o `prefetchCount: 1` do consumidor RabbitMQ
  garante autodeadlock (a instância que espera a task do Target é a
  única que poderia processá-la, mas está ocupada esperando). Documentado
  como Decision formal em `design.md`, não como nota de rodapé — ver
  seção de investigação bloqueante ali.

## Capabilities

### New Capabilities
- `agent-delegation-execution`: execução real de delegação entre agentes
  durante o processamento de uma task em `apps/workers` — tool exposta
  ao LLM do Source, criação/publicação/espera da task do Target,
  controle de profundidade, timeout e degradação graciosa.

### Modified Capabilities
(nenhuma — o ciclo de vida geral de uma task A2A, documentado em
`a2a-task-lifecycle`, não muda; delegação apenas cria tasks por um
caminho novo, reaproveitando o mesmo mecanismo `submitted → working →
completed/failed` já existente.)

## Impact

- **`apps/workers`** (único app afetado):
  - Novo: mirror de `AgentDelegation`, publisher RabbitMQ,
    `IAgentDelegationToolSetResolver`, tool de delegação, contador de
    profundidade em `AgentTask.Metadata`.
  - Editado: `AgentExecutionService` (integração do resolver de
    delegação junto ao de MCP), `Program.cs` (registro de DI).
  - Operacional: exige ≥ 2 réplicas de `apps/workers` para delegação
    funcionar sem deadlock — nenhuma configuração de réplicas existe
    hoje (`docker-compose.yml` só declara `postgres`/`rabbitmq`).
- **`apps/api`**: nenhuma mudança (non-goal explícito).
- **`apps/frontend`**: nenhuma mudança (non-goal explícito, sem UI nesta
  fatia).
