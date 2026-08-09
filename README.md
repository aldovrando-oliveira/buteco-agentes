# Buteco Agents

Plataforma de gestão de agentes de IA. Monorepo com três apps independentes —
`apps/api`, `apps/workers` e `apps/frontend` — que nunca referenciam código
interno umas das outras. Compartilhamento real de código só acontece via
`libs/` explícita, pequena e versionada, criada apenas quando houver
necessidade concreta (ver `openspec/changes/*/design.md` para o histórico de
decisões — `libs/ProviderCatalog` é o primeiro caso real, ver
`openspec/changes/backend-multi-provedor-llm/design.md`).

## Stack

| App | Stack |
| --- | --- |
| `apps/api` | .NET 10, ASP.NET Core Web API. CRUD de agentes (EF Core/Postgres, com `provider`/`model` por agente) e endpoint A2A por agente (`/agents/{id}/a2a`, via `A2A`/`A2A.AspNetCore`). Expõe `GET /providers` (provedores de LLM disponíveis por configuração de ambiente). Nunca chama o LLM — só persiste a task e publica um job no RabbitMQ |
| `apps/workers` | .NET 10, Worker Service. Consome o RabbitMQ, monta o agente (`Microsoft.Agents.AI`) com o system prompt cadastrado, resolve o `IChatClient` do provedor do agente (OpenAI via `Microsoft.Extensions.AI.OpenAI`, Anthropic via `Anthropic`, Gemini via `Google.GenAI`) e escreve o resultado de volta no Postgres |
| `apps/frontend` | Vite + React 19 + TypeScript + Mantine 9 (ESLint + Prettier) — ainda não consome o backend |
| `apps/inbox` | .NET 10, ASP.NET Core Web API. Host HTTP que vai receber webhooks de canais externos (ChatWoot, Waha, e futuros adapters). Hoje: catálogo de canais de entrada (WhatsApp, Telegram) — CRUD (EF Core/Postgres, banco próprio `buteco_inbox`, sem tabelas em comum com `apps/api`/`apps/workers`), credenciais criptografadas (AES-GCM, mesmo padrão de McpServer), `AgentId` validado via HTTP contra `apps/api`. Ainda sem orquestrador, CRM (Contact/Session) ou adapter real de canal |

RabbitMQ é o broker entre `apps/api` e `apps/workers`. PostgreSQL + EF Core
para o catálogo de agentes e para o store durável de tasks/eventos do
protocolo [A2A](https://a2a-protocol.org/latest/) — nada de store em
memória. Fora de escopo por enquanto: MCP, adapters de canal (ChatWoot/Waha),
autenticação, push notification/streaming de task, e qualquer UI consumindo
isso no `apps/frontend` (ver `openspec/changes/backend-agente-a2a-mvp/`).

## Estrutura

```
docker-compose.yml        # Postgres + RabbitMQ para desenvolvimento local
.env.example               # variáveis usadas pelo compose e pelos apps
global.json                 # pina o SDK do .NET
Directory.Build.props       # propriedades comuns aos projetos .NET
Directory.Packages.props    # Central Package Management (versões dos pacotes)
libs/
  ProviderCatalog/          # identidade de cada provedor de LLM + nome da
                             # seção de configuração — só o que apps/api e
                             # apps/workers precisam concordar entre si
  ProviderCatalog.Tests/
apps/
  api/                    # ASP.NET Core Web API
    Api.sln
    src/Buteco.Api/
    tests/Buteco.Api.Tests/
  workers/                # .NET Worker Service
    Workers.sln
    src/Buteco.Workers/
    tests/Buteco.Workers.Tests/
  frontend/               # Vite + React + TS + Mantine
    src/
    package.json
  inbox/                  # ASP.NET Core Web API — catálogo de canais de
                           # entrada (WhatsApp, Telegram), banco próprio
    Inbox.sln
    src/Buteco.Inbox/
    tests/Buteco.Inbox.Tests/
tests/
  CrossAppTaskStoreCompatibility.Tests/  # único projeto que referencia
                                          # Buteco.Api e Buteco.Workers ao
                                          # mesmo tempo — só para verificar
                                          # que as duas implementações de
                                          # ITaskStore concordam no schema
openspec/                 # Propostas, specs, design e tasks de cada mudança
```

## Pré-requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download) (`dotnet --version` deve reportar `10.x`)
- [Node.js](https://nodejs.org/) 20+ e npm
- Docker (ou Podman com `podman machine` rodando) — para o `docker-compose.yml`
  de desenvolvimento e para os testes de integração, que sobem Postgres/RabbitMQ
  efêmeros via [Testcontainers](https://testcontainers.com/)

## Como subir as dependências (Postgres + RabbitMQ)

```bash
cp .env.example .env   # ajuste as portas se 5432/5672/15672 já estiverem em uso
docker compose up -d   # ou: podman compose up -d
```

Isso sobe:

- **Postgres** na porta `5432` (padrão), banco/usuário/senha definidos no `.env`
- **RabbitMQ** na porta `5672` (AMQP) e `15672` (UI de management —
  `http://localhost:15672`, mesmas credenciais do `.env`)

Os dados persistem em volumes nomeados entre `docker compose down`/`up`. Para
apagar tudo: `docker compose down -v`.

## Como subir cada app

Antes de rodar `apps/api` pela primeira vez, aplique as migrations (só
`apps/api` aplica migration em runtime — `apps/workers` usa o mesmo schema
mas nunca o cria, ver `design.md`):

```bash
cd apps/api/src/Buteco.Api
dotnet ef database update
```

### apps/api

```bash
cd apps/api
dotnet run --project src/Buteco.Api
```

Lê `ConnectionStrings:Postgres` e `RabbitMq:*` via `appsettings.Development.json`
(defaults de dev, mesmos do `.env.example`) ou variáveis de ambiente
(`ConnectionStrings__Postgres`, `RabbitMq__Host`, etc.) — nunca hardcoded.

```bash
curl -i http://localhost:<porta>/health

# provedores de LLM disponíveis (só aparecem os que têm variável de ambiente configurada)
curl http://localhost:<porta>/providers

# cadastrar um agente — provider/model precisam estar entre os disponíveis em GET /providers
curl -X POST http://localhost:<porta>/agents \
  -H "Content-Type: application/json" \
  -d '{"name":"Atendente","instructions":"Você é um atendente simpático.","provider":"openai","model":"gpt-5.6-sol"}'

# usar o agente via A2A (SendMessage, JSON-RPC)
curl -X POST http://localhost:<porta>/agents/<id>/a2a \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"role":"ROLE_USER","parts":[{"text":"Olá!"}],"messageId":"<uuid>"}}}'

# consultar o estado da task (GetTask)
curl -X POST http://localhost:<porta>/agents/<id>/a2a \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"GetTask","params":{"id":"<taskId do SendMessage>"}}'
```

A task nasce `TASK_STATE_SUBMITTED` e, com `apps/workers` rodando, avança
para `TASK_STATE_WORKING` e depois `TASK_STATE_COMPLETED` (ou `TASK_STATE_FAILED`
se o LLM falhar) — sem streaming/push notification nesta fatia, é preciso
consultar `GetTask` de novo para ver o resultado.

### apps/workers

```bash
cd apps/workers
dotnet run --project src/Buteco.Workers
```

Além de `ConnectionStrings:Postgres` e `RabbitMq:*`, lê a configuração de cada
provedor de LLM — só usada de fato para o provedor referenciado pelo agente
que está sendo executado, mas todas devem existir em qualquer ambiente onde
`apps/api` também rode (ver nota abaixo):

- `OpenAI:BaseUrl`/`OpenAI:ApiKey`/`OpenAI:Model` — OpenAI (ou Azure
  OpenAI/gateway compatível; basta trocar `BaseUrl`/`ApiKey`).
- `Anthropic:ApiKey` — Claude, via pacote oficial `Anthropic` (beta).
- `Gemini:ApiKey` — Gemini, via pacote oficial `Google.GenAI`.

Um provedor só aparece em `GET /providers` (`apps/api`) quando sua variável
de ambiente está configurada **no ambiente de `apps/api`**. `apps/api` e
`apps/workers` são processos/deploys separados — a mesma chave precisa estar
configurada nos dois para o agente funcionar de ponta a ponta; se
`apps/api` achar um provedor disponível mas `apps/workers` não tiver a
mesma chave, a task termina `failed` (log de erro no worker), não trava.

Esperado: log `Worker starting at: ...` no console, e (com um job na fila)
logs de transição de estado da task. `Ctrl+C` para encerrar (loga
`Worker stopping at: ...`).

**Delegação entre agentes exige ≥ 2 instâncias de `apps/workers` rodando
ao mesmo tempo.** O consumidor RabbitMQ processa no máximo uma mensagem
não confirmada por instância (`prefetchCount: 1`); se um agente delega
para outro (tool de delegação, ver
`openspec/changes/apps-workers-delegacao-execucao`) e só há uma
instância ativa, ela fica esperando a task delegada terminar sem nunca
poder consumi-la ela mesma — a delegação sempre expira pelo timeout
(120s) em vez de completar. Para testar esse fluxo localmente, rode um
segundo `dotnet run --project src/Buteco.Workers` em outro terminal
(mesma configuração, sem nenhum ajuste adicional).

### apps/frontend

```bash
cd apps/frontend
npm install   # primeira vez
npm run dev
```

Abra `http://localhost:5173`. Deve renderizar o `AppShell` (header + navbar +
área principal) sem erros no console do navegador.

### apps/inbox

Antes de rodar pela primeira vez, aplique a migration (banco próprio,
`buteco_inbox` — sem tabelas em comum com `apps/api`/`apps/workers`, ver
`design.md` da change `inbox-catalogo-canais`; `dotnet ef database update`
cria o banco automaticamente se ele ainda não existir no mesmo servidor
Postgres do `docker-compose.yml`):

```bash
cd apps/inbox/src/Buteco.Inbox
dotnet ef database update
```

```bash
cd apps/inbox
dotnet run --project src/Buteco.Inbox
```

Lê `ConnectionStrings:Postgres`, `Inbox:CredentialEncryptionKey` e
`Api:BaseUrl` via `appsettings.Development.json` (defaults de dev, mesmos
do `.env.example`) ou variáveis de ambiente
(`ConnectionStrings__Postgres`, `Inbox__CredentialEncryptionKey`,
`Api__BaseUrl`) — nunca hardcoded. `Api:BaseUrl` deve apontar para onde
`apps/api` está escutando (`http://localhost:5017` em dev) — usado para
validar, via `GET /agents/{id}`, o `agentId` de cada canal cadastrado.

```bash
curl -i http://localhost:5027/health

# cadastrar um canal — agentId precisa existir em apps/api (GET /agents/{id});
# apps/api precisa estar rodando, senão o cadastro é rejeitado (fail-fast)
curl -X POST http://localhost:5027/channels \
  -H "Content-Type: application/json" \
  -d '{"channelType":"WhatsApp","name":"Suporte","credential":"token-do-whatsapp","agentId":"<id de um agente existente em apps/api>"}'

# listar/consultar canais — credencial nunca aparece na resposta
curl http://localhost:5027/channels
curl http://localhost:5027/channels/<id>

# ativar/desativar (idempotente, nunca exclui o registro)
curl -X POST http://localhost:5027/channels/<id>/deactivate
curl -X POST http://localhost:5027/channels/<id>/activate
```

Ainda sem orquestrador, CRM (Contact/Session) ou adapter real de canal —
as credenciais são opacas, sem nenhuma tentativa de conexão contra a
plataforma externa (WhatsApp/Telegram) nesta fatia.

## Como testar cada app

Os testes de integração de `apps/api`, `apps/workers`, `apps/inbox` e
`tests/CrossAppTaskStoreCompatibility.Tests` sobem Postgres/RabbitMQ
efêmeros via Testcontainers — não precisam do `docker compose up` da seção
acima rodando, mas precisam de Docker/Podman disponível.

```bash
# libs/ProviderCatalog
dotnet test libs/ProviderCatalog.Tests

# apps/api
dotnet test apps/api/Api.sln

# apps/workers
dotnet test apps/workers/Workers.sln

# apps/inbox
dotnet test apps/inbox/Inbox.sln

# compatibilidade de schema entre apps/api e apps/workers
dotnet test tests/CrossAppTaskStoreCompatibility.Tests

# apps/frontend
cd apps/frontend
npm run lint
npm run format:check
npm run build
```

## Convenções

- **Isolamento entre apps**: nenhum `.csproj` ou arquivo do frontend pode
  referenciar código de outro app diretamente. `libs/` só existe quando
  houver necessidade real de compartilhamento, com justificativa explícita
  no `design.md` da mudança que a criar — `libs/ProviderCatalog`
  (`openspec/changes/backend-multi-provedor-llm/design.md`) é o único caso
  hoje: `apps/api` e `apps/workers` referenciam essa lib (nunca um ao
  outro), e ela carrega só a identidade de cada provedor de LLM e o nome da
  seção de configuração — o mínimo que precisa concordar entre os dois
  processos, não um mecanismo geral de código compartilhado. Outra exceção,
  de natureza diferente, é `tests/CrossAppTaskStoreCompatibility.Tests`,
  que referencia `Buteco.Api` e `Buteco.Workers` de propósito só para
  verificar compatibilidade de schema — nenhum dos dois apps o referencia
  de volta, e ele nunca é publicado com nenhum dos dois.
- **Central Package Management**: versões de pacotes NuGet ficam em
  `Directory.Packages.props` na raiz; os `.csproj` referenciam pacotes sem
  `Version`.
- **Mudanças planejadas com OpenSpec**: propostas, specs, design e tasks de
  cada mudança ficam em `openspec/changes/<nome-da-mudança>/`. Use
  `openspec status --change "<nome>"` para ver o progresso de uma mudança em
  andamento.
