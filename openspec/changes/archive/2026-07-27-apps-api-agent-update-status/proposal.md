## Why

O catálogo de agentes em `apps/api` hoje só permite criar, listar e consultar
agentes — não há como corrigir um agente cadastrado errado nem como impedir
que ele continue recebendo e processando mensagens sem removê-lo do sistema.
O produto optou por não ter exclusão de agente (nem hard nem soft delete)
nesta fatia: em vez disso, agentes ganham um estado ativo/inativo que o
operador controla.

## What Changes

- `Agent` ganha o campo `IsActive` (bool, `true` por padrão na criação), com
  métodos mutadores próprios (`UpdateDetails`, `Activate`, `Deactivate`) —
  sem exclusão de dado, hard ou soft, nesta fatia.
- Novo endpoint `PUT /agents/{id}`: atualiza `Name` e `Instructions` com a
  mesma validação de `POST /agents`; 404 se o agente não existir.
- Novos endpoints `POST /agents/{id}/activate` e `POST /agents/{id}/deactivate`:
  alternam `IsActive`; idempotentes (chamar duas vezes seguidas não é erro);
  404 se o agente não existir.
- `AgentResponse` ganha o campo `IsActive`, refletido em `GET /agents`,
  `GET /agents/{id}` e nas respostas dos três endpoints acima. Sem query
  filter global escondendo agentes inativos — GET continua listando todo
  mundo, pois o inativo precisa ficar visível para alguém reativá-lo.
- `EnqueueingAgentHandler` (rota A2A `/agents/{id}/a2a`) passa a checar
  `IsActive` a cada `SendMessage`, lendo direto do banco (não do cache do
  registry). Se o agente estiver inativo, a task é levada para o estado
  `rejected` do protocolo A2A via `TaskUpdater.RejectAsync`, sem publicar
  job no RabbitMQ. Isso usa o vocabulário nativo do protocolo A2A em vez de
  inventar um código de erro HTTP próprio.

## Capabilities

### New Capabilities
(nenhuma — esta mudança estende capabilities já existentes)

### Modified Capabilities
- `agent-catalog`: novos requirements de atualização (`PUT /agents/{id}`) e
  de ativação/desativação (`POST /agents/{id}/activate|deactivate`),
  incluindo o campo `IsActive` exposto em todas as respostas de agente.
- `a2a-task-lifecycle`: novo requirement descrevendo que `SendMessage` para
  um agente inativo rejeita a task (`TASK_STATE_REJECTED`) em vez de
  publicar um job de execução.

## Impact

- **apps/api** (único app afetado; nenhuma mudança em apps/workers ou
  apps/frontend):
  - `Agents/Entities/Agent.cs`: campo `IsActive` + métodos mutadores.
  - `Agents/Commands/UpdateAgent/`, `Agents/Commands/ActivateAgent/`,
    `Agents/Commands/DeactivateAgent/` (novos, seguindo o padrão de
    `Agents/Commands/CreateAgent/`).
  - `Agents/Endpoints/AgentEndpoints.cs`: três novos handlers HTTP.
  - `Agents/Responses/AgentResponse.cs`: campo `IsActive`.
  - `Infrastructure/AppDbContext.cs` + nova migration EF Core
    (`is_active`, default `true`).
  - `A2A/EnqueueingAgentHandler.cs`: checagem de `IsActive` e rejeição via
    `TaskUpdater.RejectAsync`; ganha dependência de `IServiceScopeFactory`
    para ler o banco a cada execução (não reaproveita o cache do
    `AgentA2AServerRegistry`, que só verifica existência uma vez por
    processo).
  - `A2A/AgentA2AServerRegistry.cs`: repassa a nova dependência ao construir
    o handler.
  - Testes de integração (`Buteco.Api.Tests`, `WebApplicationFactory` +
    Postgres real via Testcontainers) cobrindo update, activate/deactivate
    (incluindo idempotência e 404) e a rejeição de `SendMessage` para
    agente inativo sem publish no RabbitMQ.
