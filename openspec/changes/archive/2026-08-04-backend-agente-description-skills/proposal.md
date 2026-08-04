## Why

O protocolo A2A exige que cada agente publique um `AgentCard` descrevendo
suas capacidades (`description`, `skills`) para que outros agentes ou
orquestradores decidam se e como invocá-lo. Hoje `Agent` não tem onde
guardar esse dado — a change seguinte (`backend-a2a-agent-card`) que monta
o `AgentCard` real precisa de um campo já persistido para consumir. Esta
change entrega só o dado puro: `Description` e `Skills` em `Agent`, sem
nenhuma lógica ou formato A2A ainda.

## What Changes

- Migration EF Core aditiva em `agents`: coluna `description` (text,
  nullable) e coluna `skills` (jsonb, NOT NULL DEFAULT `'[]'`).
- `Agent` (entidade): novas propriedades `Description` (nullable) e
  `Skills` (lista, shape `{ Name, Description? }`), com atualização via o
  mutador de detalhes já existente.
- `CreateAgentCommand`/`CreateAgentCommandHandler` e
  `UpdateAgentCommand`/`UpdateAgentCommandHandler`: passam a aceitar
  `Description` e `Skills`. `Update` substitui a lista inteira de `Skills`
  (replace completo, sem merge).
- `AgentResponse`: ganha `Description` e `Skills`, refletido em
  `POST /agents`, `GET /agents`, `GET /agents/{id}` e `PUT /agents/{id}`.
- Validação: cada item em `Skills` precisa ter `Name` não vazio — rejeição
  HTTP 400 caso contrário, sem persistir nada.

## Capabilities

### New Capabilities
(nenhuma)

### Modified Capabilities
- `agent-catalog`: os requisitos de cadastro, listagem, consulta e
  atualização de agente passam a incluir os campos `description` e
  `skills` (aceitos na criação/atualização, refletidos nas respostas,
  com default seguro para agentes já existentes).

## Impact

- **apps/api**: `Agents/Entities/Agent.cs`, `Agents/Commands/CreateAgent/*`,
  `Agents/Commands/UpdateAgent/*`, `Agents/Requests/*`,
  `Agents/Responses/AgentResponse.cs`, `Agents/Endpoints/AgentEndpoints.cs`,
  `Infrastructure/AppDbContext.cs` + nova migration EF Core.
- **apps/workers**: nenhuma mudança.
- **apps/frontend**: nenhuma mudança.
- Sem mudança de infraestrutura, dependências ou contratos externos além
  do shape de `AgentResponse` (aditivo, não quebra clientes existentes que
  ignoram campos desconhecidos).
