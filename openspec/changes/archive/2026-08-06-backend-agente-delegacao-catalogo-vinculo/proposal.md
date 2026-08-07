## Why

Agentes vão poder delegar tasks para outros agentes, mas hoje não existe onde
cadastrar quem pode delegar para quem. Esta change entrega só a base de
dados — o vínculo de delegação entre agentes — para que uma change futura
(execução: profundidade/ciclos em runtime, `contextId` compartilhado,
timeout, concorrência do Worker) tenha o que consumir. Mesma sequência já
usada na linha de MCP: catálogo/vínculo primeiro, execução depois.

## What Changes

- Nova entidade `AgentDelegation` (vínculo unidirecional Agent → Agent:
  `SourceAgentId`, `TargetAgentId`, chave composta), tabela relacional
  própria com FK para `Agent` nos dois lados.
- `PUT /agents/{id}/delegations`: substitui o conjunto completo de
  delegações de saída do agente `id` (Source), corpo
  `{ targetAgentIds: [...] }`. Valida agente Source existente (404),
  `TargetAgentId`s existentes (400), rejeita auto-delegação (400), aceita
  Target inativo.
- `GET /agents`, `GET /agents/{id}` (via `AgentResponse`) passam a incluir
  `delegatesTo`: os agentes para os quais o agente delega (id + name).
- **BREAKING**: `AgentResponse` ganha um novo campo obrigatório
  (`delegatesTo`) — consumidores existentes que fazem parsing estrito do
  shape da resposta precisam ser atualizados.

Fora de escopo desta change (ver design.md para detalhamento e Non-Goals):
nenhuma execução real de delegação, nenhum controle de profundidade ou
detecção de ciclo (indireto ou bidirecional), nenhum endpoint de consulta
reversa, e nenhuma mudança em `apps/workers` ou `apps/frontend`.

## Capabilities

### New Capabilities
- `agent-delegation-binding`: vínculo unidirecional entre agentes
  cadastrados (definir/substituir o conjunto de delegações de saída de um
  agente).

### Modified Capabilities
- `agent-catalog`: `AgentResponse` (retornado por `POST /agents`,
  `GET /agents`, `GET /agents/{id}`, `PUT /agents/{id}`,
  `POST /agents/{id}/activate`, `POST /agents/{id}/deactivate`) passa a
  incluir os agentes para os quais o agente delega (`delegatesTo`).

## Impact

- **apps/api**: novo módulo `AgentDelegations` (entidade, command/handler,
  endpoint, request, lookup compartilhado) espelhando a estrutura de
  `AgentMcpBindings`; nova migration EF Core (tabela `agent_delegations`);
  `AgentResponse` alterado (novo campo `delegatesTo` e novo parâmetro em
  `AgentResponse.FromEntity`, tocando os mesmos pontos de chamada que hoje
  já passam `mcpServers`: `CreateAgent`, `UpdateAgent`, `ActivateAgent`,
  `DeactivateAgent`, `GetAgentById`, `ListAgents`, e
  `ReplaceAgentMcpServersCommandHandler`, que passa a também buscar
  `delegatesTo`).
- **apps/workers**: nenhuma mudança nesta fatia.
- **apps/frontend**: nenhuma mudança nesta fatia (sem UI).
- Consumidores de `GET /agents`/`GET /agents/{id}` precisam tolerar o novo
  campo `delegatesTo` no shape de `AgentResponse`.
