## Why

Hoje `apps/api` e `apps/workers` são scaffolds vazios (health check e worker
sem trabalho). A plataforma de gestão de agentes não tem nenhum fluxo real:
não é possível cadastrar um agente nem conversar com ele. Esta mudança entrega
a primeira fatia ponta a ponta — cadastro de agente + invocação real via A2A
— que prova o desenho arquitetural core do produto (API só orquestra e
protocola; Workers executa o LLM; RabbitMQ desacopla os dois; Postgres é o
estado durável e compartilhado) antes de qualquer investimento em MCP, canais
de entrada ou UI.

## What Changes

- Adiciona pacotes NuGet a `apps/api` e `apps/workers`. **Em ambos os apps**:
  `A2A` (1.0.0-preview2 — SDK core do protocolo A2A, **preview**),
  `Microsoft.EntityFrameworkCore.Design` (10.0.10),
  `Npgsql.EntityFrameworkCore.PostgreSQL` (10.0.3) e `RabbitMQ.Client`
  (7.2.1). **Só em `apps/api`**: `A2A.AspNetCore` (1.0.0-preview2,
  **preview** — ver Riscos em `design.md`), que fornece o binding HTTP+JSON-RPC
  do protocolo A2A usado para hospedar o endpoint. **Não** usamos
  `Microsoft.Agents.AI` nem `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` em
  `apps/api` — a camada de conveniência desses pacotes registra um único
  `A2AServer` por app via DI no startup, incompatível com agentes criados
  em runtime (ver Decisão 2 em `design.md`, revisada durante a
  implementação). **Só em `apps/workers`**: `Microsoft.Agents.AI` (1.15.0)
  para montar e rodar o `AIAgent` de verdade, e `Microsoft.Extensions.AI.OpenAI`
  (10.8.1), o `IChatClient` que efetivamente chama o LLM.
- `apps/api`: CRUD de `Agent` (criar, listar, obter por id) persistido via EF
  Core/Postgres, com campos `Id`, `Nome`, `Instructions` (system prompt).
- `apps/api`: cada agente cadastrado passa a responder em
  `/agents/{id}/a2a` — uma única rota com parâmetro `{id}`, mapeada uma vez
  no startup via `A2A.AspNetCore` (`MapA2A`), que resolve dinamicamente
  (em um registro próprio em memória, por id do agente) o `A2AServer`
  correspondente, cada um com seu `IAgentHandler` e `ITaskStore`
  customizados (Postgres) — a API nunca chama o LLM, apenas persiste a
  task como `submitted` e publica um job no RabbitMQ.
- `apps/workers`: consumidor RabbitMQ que, para cada job, monta um `AIAgent`
  (Microsoft.Agents.AI) com o system prompt do agente cadastrado, chama o
  `IChatClient` (endpoint formato OpenAI, configurável), e escreve o
  progresso/resultado de volta no mesmo store Postgres usando o protocolo de
  transição de estado do pacote `A2A` (`submitted` → `working` →
  `completed`/`failed`).
- Adiciona `docker-compose.yml` na raiz do monorepo com Postgres e RabbitMQ
  (management UI) para desenvolvimento local, e `.env.example` documentando
  as variáveis lidas por `apps/api`/`apps/workers` via configuração.
- Adota Central Package Management na raiz do monorepo (`global.json`,
  `Directory.Build.props`, `Directory.Packages.props`), já que esta mudança
  introduz ~10 pacotes novos com a mesma versão exigida em `apps/api` e
  `apps/workers` ao mesmo tempo; os `.csproj` passam a referenciar pacotes
  sem `Version` (ver Decisão 7 em `design.md`).
- Adiciona um projeto de teste dedicado,
  `tests/CrossAppTaskStoreCompatibility.Tests`, fora de `Api.sln` e
  `Workers.sln`, que grava uma task via `PostgresTaskStore` de `apps/api` e
  lê de volta via `PostgresTaskStore` de `apps/workers` (e vice-versa),
  verificando automaticamente que as duas implementações independentes
  concordam no schema (ver Decisão 8 em `design.md`).
- Testes automatizados novos em `apps/api/tests` (CRUD do agente, criação de
  task) e `apps/workers/tests` (consumo do job até `completed`, com
  `IChatClient` mockado).
- **BREAKING**: nenhuma — não há consumidores externos hoje.

## Capabilities

### New Capabilities
- `agent-catalog`: cadastro, listagem e consulta de agentes de IA (nome +
  system prompt) persistidos em Postgres via `apps/api`.
- `a2a-task-lifecycle`: exposição de cada agente cadastrado em uma rota A2A
  própria e o ciclo de vida completo de uma task A2A — submissão pela API,
  execução assíncrona pelos Workers via RabbitMQ, e reflexo do estado final
  para quem consulta a task pela API.
- `local-dev-environment`: infraestrutura de desenvolvimento local
  (Postgres + RabbitMQ via Docker Compose) e configuração de connection
  strings/URIs via variáveis de ambiente em `apps/api` e `apps/workers`.

### Modified Capabilities
(nenhuma — `api-scaffold`, `workers-scaffold` e `frontend-scaffold`
continuam válidas como estão; esta mudança adiciona funcionalidade nova sobre
o scaffold, sem alterar os requisitos já arquivados de build isolado/health
check/worker mínimo)

## Impact

- **apps/api**: novo `AppDbContext` (EF Core) próprio, migrations Postgres,
  endpoints REST de CRUD de `Agent`, um registro em memória de `A2AServer`
  por agente (`ITaskStore` + `IAgentHandler` customizados) resolvido
  dinamicamente pela rota `/agents/{id}/a2a`, publisher RabbitMQ,
  `appsettings.json`/variáveis de ambiente para connection string e URI do
  broker.
- **apps/workers**: novo `AppDbContext` (EF Core, schema compartilhado com a
  API mas projeto independente), consumidor RabbitMQ (`BackgroundService`),
  construção de `AIAgent` a partir dos dados do agente, `IChatClient`
  configurável por endpoint/chave via configuração.
- **Infra**: `docker-compose.yml` e `.env.example` na raiz do monorepo.
  `openspec/config.yaml` atualizado para refletir `.NET 10` (estava citando
  ".NET 9", desatualizado em relação ao scaffold real já em `net10.0`).
  Novos `global.json`, `Directory.Build.props` e `Directory.Packages.props`
  na raiz (Central Package Management), afetando os quatro `.csproj`
  existentes além dos projetos novos desta mudança.
- **Novo diretório `tests/`** (fora de `apps/`): projeto
  `tests/CrossAppTaskStoreCompatibility.Tests`, único ponto do monorepo que
  referencia `Buteco.Api` e `Buteco.Workers` ao mesmo tempo — só para
  verificação de compatibilidade de schema, nunca publicado com nenhum dos
  dois apps.
- **apps/frontend**: nenhum impacto nesta mudança (non-goal explícito).
- **Fora de escopo** (mudanças futuras): integração MCP, adapters de canal
  (ChatWoot/Waha), autenticação/autorização, push notification/webhook de
  task, streaming SSE, UI consumindo o backend.
