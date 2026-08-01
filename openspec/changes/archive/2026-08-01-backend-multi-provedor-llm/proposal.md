## Why

`apps/workers` hoje está hardcoded para um único `IChatClient` OpenAI-compatible
(`ChatClientOptions`, singleton construído uma vez no `Program.cs`). O produto
precisa suportar múltiplos provedores de LLM (OpenAI, Gemini, Anthropic) por
agente, com a regra de que um provedor só fica disponível para seleção quando
suas variáveis de ambiente estiverem configuradas — sem chave de Gemini
configurada, Gemini não aparece como opção.

## What Changes

- `Agent` ganha os campos `Provider` (string, nullable) e `Model` (string,
  nullable). Um agente com qualquer um dos dois nulo entra num estado
  implícito "precisa de reconfiguração": nenhum default é assumido para
  agentes já cadastrados.
- Novo endpoint `GET /providers`: retorna, para cada provedor cujas variáveis
  de ambiente estejam configuradas, o provedor e a lista de modelos
  disponíveis (catálogo estático/curado no código, sem consulta dinâmica a
  cada fornecedor). Provedores não configurados não aparecem na resposta.
- `POST /agents` e `PUT /agents/{id}` passam a aceitar `provider` e `model` e
  validam que a combinação está entre as atualmente disponíveis (mesma fonte
  usada por `GET /providers`) — HTTP 400 (`ValidationProblem`, mesmo padrão
  já usado para nome/instruções) caso contrário.
- `apps/workers` passa a resolver o `IChatClient` correto a partir do
  `Provider` do agente no momento da execução (fábrica/resolver por
  provedor, lendo a chave/config específica de cada um), em vez de um único
  `IChatClient` singleton fixo. `RunAsync` e o fluxo de streaming/não-streaming
  do `AgentExecutionService` não mudam — só a origem do `IChatClient` muda.
- `EnqueueingAgentHandler` (rota A2A) passa a rejeitar `SendMessage` via
  protocolo A2A (`TaskUpdater.RejectAsync`, sem publicar job no RabbitMQ)
  também quando o agente está no estado "precisa de reconfiguração"
  (`Provider`/`Model` nulos) ou quando o `Provider` do agente não está mais
  configurado no ambiente — mesmo padrão já usado para agente inativo.
- Nova biblioteca compartilhada `libs/ProviderCatalog` (primeiro uso de
  `libs/` no projeto), contendo apenas a identidade de cada provedor e o
  nome da variável de ambiente que ele exige — o único dado que precisa
  concordar entre `apps/api` e `apps/workers`. O catálogo de modelos
  disponíveis por provedor permanece só em `apps/api`, que é o único app que
  o consome.

**BREAKING**: `POST /agents` e `PUT /agents/{id}` passam a exigir `provider`
e `model` no corpo da requisição para que o agente saia do estado "precisa
de reconfiguração".

## Capabilities

### New Capabilities
- `llm-provider-catalog`: catálogo de provedores/modelos de LLM disponíveis
  por configuração de ambiente, exposto via `GET /providers` e usado para
  validar `Provider`/`Model` em `POST /agents` e `PUT /agents/{id}`.

### Modified Capabilities
- `agent-catalog`: `Agent` ganha `provider`/`model` (nullable, estado
  "precisa de reconfiguração" quando ausentes); `POST /agents` e
  `PUT /agents/{id}` passam a validar `provider`/`model` contra o catálogo
  disponível.
- `a2a-task-lifecycle`: `SendMessage` passa a rejeitar a task (mesmo
  vocabulário `TASK_STATE_REJECTED` já usado para agente inativo) quando o
  agente está sem `provider`/`model` configurados ou quando o `provider` do
  agente deixou de estar disponível no ambiente.

## Impact

- **apps/api**:
  - `Agents/Entities/Agent.cs`: campos `Provider`/`Model` (nullable) +
    mutador de atualização ajustado.
  - `Agents/Commands/CreateAgent/`, `Agents/Commands/UpdateAgent/`: aceitam
    e validam `Provider`/`Model`.
  - Novo `Providers/` (ou equivalente): endpoint `GET /providers`, lendo o
    catálogo de modelos (curado em `apps/api`) e a disponibilidade de cada
    provedor (via `libs/ProviderCatalog` + `IConfiguration`).
  - `A2A/EnqueueingAgentHandler.cs`: checagem adicional de
    "agente precisa de reconfiguração" e "provider indisponível", reusando
    o mesmo ponto e padrão já usado para `IsActive`.
  - Nova migration EF Core: `provider`/`model` nullable em `agents`.
- **apps/workers**:
  - Novo resolver/fábrica de `IChatClient` por provedor (substitui o
    singleton único hoje registrado em `Program.cs`), usando `Anthropic`
    (oficial, beta) para Anthropic e `Google.GenAI` (oficial) para Gemini,
    mantendo `Microsoft.Extensions.AI.OpenAI` para OpenAI.
  - `AgentExecutionService`: passa a obter o `IChatClient` do resolver a
    partir do `Provider` do agente, em vez de recebê-lo fixo via DI.
- **libs/** (novo): `libs/ProviderCatalog`, referenciado por `apps/api` e
  `apps/workers` — único conteúdo compartilhado do monorepo até hoje.
- Nenhuma mudança em `apps/frontend` nesta fatia.
