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
| `apps/inbox` | .NET 10, ASP.NET Core Web API. Host HTTP que recebe webhooks de canais externos. Catálogo de canais de entrada (`ChannelType` string aberta, validada contra adapters efetivamente registrados — não mais uma lista fechada — EF Core/Postgres, banco próprio `buteco_inbox`, credenciais criptografadas AES-GCM, `AgentId` validado via HTTP contra `apps/api`), CRM próprio de Contact/Session, e o orquestrador de mensagens — debounce persistido em Postgres e round-trip real com `apps/api` como cliente A2A (`SendMessage` via `A2A.A2AClient`, resposta via push notification recebida em `POST /internal/push-notifications`, entregue de volta ao canal de origem). Primeiro adapter real: WAHA (WhatsApp HTTP API), registrado sob `ChannelType` `"waha"` — recepção de webhook (`POST /webhooks/{channelId}`) e envio (`POST /api/sendText`) |

RabbitMQ é o broker entre `apps/api` e `apps/workers`. PostgreSQL + EF Core
para o catálogo de agentes e para o store durável de tasks/eventos do
protocolo [A2A](https://a2a-protocol.org/latest/) — nada de store em
memória. Fora de escopo por enquanto: MCP, adapters de canal (ChatWoot/Waha),
autenticação, push notification/streaming de task, e qualquer UI consumindo
isso no `apps/frontend` (ver `openspec/changes/backend-agente-a2a-mvp/`).

## Estrutura

```
docker-compose.yml        # Postgres + RabbitMQ + WAHA para desenvolvimento local
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
                           # entrada e adapter WAHA, banco próprio
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
- **WAHA** (WhatsApp HTTP API) na porta `3000` (padrão) — usado por
  `apps/inbox` para testar o adapter `waha`; sem sessão pré-configurada
  (ver checklist de round-trip manual na seção `apps/inbox` abaixo)

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

Lê `ConnectionStrings:Postgres`, `RabbitMq:*` e `Auth:*` via
`appsettings.Development.json` (defaults de dev, mesmos do
`.env.example`) ou variáveis de ambiente (`ConnectionStrings__Postgres`,
`RabbitMq__Host`, `Auth__TokenSigningKey`, etc.) — nunca hardcoded.
`Auth:TokenSigningKey`, `Auth:OperatorUsername` e
`Auth:OperatorPasswordHash` são obrigatórios — o processo falha ao subir
sem eles (fail-fast, mesmo padrão de `Api:BaseUrl` ausente em
`apps/inbox`). Ver "Autenticação" abaixo para gerar
`Auth:OperatorPasswordHash`.

Toda rota, exceto `GET /health`, `POST /auth/login` e
`GET /agents/{id}/.well-known/agent-card.json`, exige
`Authorization: Bearer <token>` (openspec/changes/auth-login-e-servico) —
os exemplos abaixo já fazem login primeiro e reutilizam o token:

```bash
curl -i http://localhost:<porta>/health

# login do operador — usuário/senha configurados em Auth:OperatorUsername/
# Auth:OperatorPasswordHash (default de dev: "operator"/"changeme")
TOKEN=$(curl -s -X POST http://localhost:<porta>/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"operator","password":"changeme"}' | jq -r '.token')

# provedores de LLM disponíveis (só aparecem os que têm variável de ambiente configurada)
curl -H "Authorization: Bearer $TOKEN" http://localhost:<porta>/providers

# cadastrar um agente — provider/model precisam estar entre os disponíveis em GET /providers
curl -X POST http://localhost:<porta>/agents \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"name":"Atendente","instructions":"Você é um atendente simpático.","provider":"openai","model":"gpt-5.6-sol"}'

# usar o agente via A2A (SendMessage, JSON-RPC)
curl -X POST http://localhost:<porta>/agents/<id>/a2a \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"role":"ROLE_USER","parts":[{"text":"Olá!"}],"messageId":"<uuid>"}}}'

# consultar o estado da task (GetTask)
curl -X POST http://localhost:<porta>/agents/<id>/a2a \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"jsonrpc":"2.0","id":2,"method":"GetTask","params":{"id":"<taskId do SendMessage>"}}'
```

#### Autenticação

`apps/api` e `apps/inbox` compartilham a mesma `Auth:TokenSigningKey` e
validam localmente qualquer token assinado com ela — sem chamada de rede
entre os dois processos (`openspec/changes/auth-login-e-servico/design.md`,
Decision 1). Único operador, credencial via variável de ambiente, sem
tabela de usuários. Default de dev (`appsettings.Development.json`):
usuário `operator`, senha `changeme`.

Para gerar um `Auth:OperatorPasswordHash` novo (produção, ou trocar a
senha de dev) — formato composto
`{iterations}.{saltBase64}.{hashBase64}`, PBKDF2-HMAC-SHA256, gerado
offline, nunca por código de produção (design.md, Decision 5):

```bash
python3 -c "
import hashlib, base64, secrets
password = 'sua-senha-aqui'
salt = secrets.token_bytes(16)
iterations = 100_000
h = hashlib.pbkdf2_hmac('sha256', password.encode(), salt, iterations, dklen=32)
print(f'{iterations}.{base64.b64encode(salt).decode()}.{base64.b64encode(h).decode()}')
"
```

Cole a saída em `Auth:OperatorPasswordHash` (`apps/api`, via
`Auth__OperatorPasswordHash` como variável de ambiente).
`Auth:OperatorTokenLifetime` é opcional — default de 30 minutos fixado no
próprio tipo (`TokenSigningOptions`, não no `appsettings.json`), reduza
ou amplie por ambiente só se precisar de um TTL diferente.

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

`apps/workers` também exige a variável de ambiente `TZ` do sistema
operacional (não lida via `IConfiguration` — ver `.env.example`) com o
nome IANA canônico da tz database, ex. `America/Sao_Paulo` (nunca com o
prefixo POSIX `:`). O processo **falha ao subir** se `TZ` estiver ausente,
vazia, ou se o fuso resolvido pelo SO não corresponder exatamente ao valor
declarado — checagem de integridade no startup, mesmo padrão da
classificação de rotas anônimas descrita em "Convenções" abaixo. É o único
ponto de acesso a relógio/fuso do worker: usado para renderizar o instante
de processamento e o dia da semana (sempre pt-BR, fixo) no bloco de
contexto temporal enviado ao LLM
(`openspec/changes/apps-workers-contexto-temporal`).

Esperado: log `Worker starting at: ...` no console (junto com o fuso
resolvido e o offset atual, ex. `Fuso horário do sistema: America/Sao_Paulo,
offset atual: -03:00:00`), e (com um job na fila) logs de transição de
estado da task. `Ctrl+C` para encerrar (loga `Worker stopping at: ...`).

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

Abra `http://localhost:5173`. Redireciona para `/login` — entre com o
operador configurado em `apps/api` (default de dev: `operator`/`changeme`,
ver "Autenticação" acima). Depois do login, renderiza o `AppShell`
(header + navbar + área principal) sem erros no console do navegador.

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

Lê `ConnectionStrings:Postgres`, `Inbox:CredentialEncryptionKey`,
`Api:BaseUrl`, `PublicUrl:BaseUrl`, `Debounce:*` e `Auth:TokenSigningKey`
via `appsettings.Development.json` (defaults de dev, mesmos do
`.env.example`) ou variáveis de ambiente (`ConnectionStrings__Postgres`,
`Inbox__CredentialEncryptionKey`, `Api__BaseUrl`,
`PublicUrl__BaseUrl`, `Debounce__Window`, `Debounce__SweepInterval`,
`Debounce__MaxDispatchAttempts`, `Auth__TokenSigningKey`) — nunca
hardcoded. `Api:BaseUrl` deve apontar para onde `apps/api` está
escutando (`http://localhost:5017` em dev) — usado para validar
`AgentId` de canal (`GET /agents/{id}`) e para o cliente A2A
(`POST /agents/{id}/a2a`), ambos autenticados com um token de serviço
que `apps/inbox` assina sozinho (`Auth:TokenSigningKey` — **precisa ser
o mesmo valor** configurado em `apps/api`, senão nenhum token de
operador nem de serviço é aceito entre os dois processos). `PublicUrl:BaseUrl`
deve ser a URL pela qual este processo é alcançável a partir de `apps/api`/
`apps/workers` (`http://localhost:5027` em dev) — usada no
`pushNotificationConfig.url` enviado em cada `SendMessage`.

Toda rota, exceto `GET /health`, `POST /webhooks/{channelId}` e
`POST /internal/push-notifications`, exige `Authorization: Bearer
<token>` — use o mesmo token de operador obtido via `POST /auth/login`
em `apps/api` (seção `apps/api` acima):

```bash
curl -i http://localhost:5027/health

# cadastrar um canal WAHA — credential é o JSON de WahaCredential
# serializado como string; agentId precisa existir em apps/api
# (GET /agents/{id}); apps/api precisa estar rodando, senão o cadastro é
# rejeitado (fail-fast)
curl -X POST http://localhost:5027/channels \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"channelType":"waha","name":"Suporte WhatsApp","credential":"{\"ServiceUrl\":\"http://localhost:3000\",\"SessionName\":\"default\",\"AuthToken\":\"<api key do WAHA>\"}","agentId":"<id de um agente existente em apps/api>"}'

# listar/consultar canais — credencial nunca aparece na resposta;
# webhookUrl vem pronto para configurar no WAHA (ver checklist abaixo)
curl -H "Authorization: Bearer $TOKEN" http://localhost:5027/channels
curl -H "Authorization: Bearer $TOKEN" http://localhost:5027/channels/<id>

# ativar/desativar (idempotente, nunca exclui o registro)
curl -X POST -H "Authorization: Bearer $TOKEN" http://localhost:5027/channels/<id>/deactivate
curl -X POST -H "Authorization: Bearer $TOKEN" http://localhost:5027/channels/<id>/activate
```

O orquestrador (`IInboundMessageOrchestrator`) é um serviço interno, sem
endpoint HTTP próprio — consumido diretamente por código (testes) e pelos
handlers de webhook de cada adapter (`IInboundWebhookHandler`, despachado
pela rota genérica `POST /webhooks/{channelId}`). Mensagens bufferizadas
por sessão aguardam a janela de debounce (`Debounce:Window`, varrida
periodicamente por um `BackgroundService`, `Debounce:SweepInterval`) antes
de disparar um `SendMessage` real contra `apps/api`.

#### Checklist de round-trip manual com WAHA

Verificação de ponta a ponta do adapter `waha` — depende de um WAHA real
conectado a um WhatsApp de teste, por isso é manual, não automatizada
(`openspec/changes/inbox-adapter-waha/design.md`, Decisions 3 e 6):

1. Suba o WAHA: `docker compose up -d waha` (ou `docker compose up -d`
   para tudo). Confirme com `curl -i http://localhost:3000/api/sessions`.
2. Cadastre o canal em `apps/inbox` (`POST /channels` acima) e anote o
   `webhookUrl` retornado (formato `http://localhost:5027/webhooks/<channelId>`).
3. Configure a sessão no próprio WAHA, apontando o webhook para a URL do
   passo 2 — passo manual, não automatizado por `apps/inbox` (Decision 3):
   ```bash
   curl -X POST http://localhost:3000/api/sessions \
     -H "Content-Type: application/json" \
     -d '{"name":"default","config":{"webhooks":[{"url":"<webhookUrl do passo 2>","events":["message"]}]}}'
   ```
4. Escaneie o QR code (`GET /api/screenshot?session=default` no WAHA, ou a
   UI do próprio WAHA) com um WhatsApp de teste até o status da sessão
   virar `WORKING`.
5. Envie uma mensagem de WhatsApp real para o número conectado. Confirme
   que uma `Session`/`PendingDispatch` foi criada em `apps/inbox` e que,
   depois da janela de debounce, o agente responde de volta no mesmo
   WhatsApp — round-trip completo (webhook → orquestrador → `apps/api` →
   `apps/workers` → push notification → `WahaOutboundMessageSender` →
   `POST /api/sendText`).

#### Checklist de round-trip manual com Telegram

Verificação de ponta a ponta do adapter `telegram` — depende de um bot
Telegram real, por isso é manual, não automatizada
(`openspec/changes/inbox-adapter-telegram/design.md`, Decisions 1, 4 e 9).
Ao contrário do WAHA, o cadastro do canal já configura o webhook
automaticamente — não há passo manual de configuração de sessão:

1. Crie um bot com o [@BotFather](https://t.me/BotFather) no Telegram
   (`/newbot`) e anote o `BotToken` retornado.
2. Cadastre o canal em `apps/inbox` — a credencial é o JSON de
   `TelegramCredential` serializado como string (só `BotToken`;
   `WebhookSecret` é preenchido automaticamente pelo provisionamento, não
   informado pelo cliente):
   ```bash
   curl -X POST http://localhost:5027/channels \
     -H "Content-Type: application/json" \
     -d '{"channelType":"telegram","name":"Suporte Telegram","credential":"{\"BotToken\":\"<token do BotFather>\"}","agentId":"<id de um agente existente em apps/api>"}'
   ```
   Se o `BotToken` for inválido ou o Telegram estiver inalcançável, o
   cadastro inteiro falha (HTTP 502) — nenhum canal é persistido sem o
   webhook de fato registrado (design.md, Decision 4).
3. Confirme que o webhook foi registrado, consultando `getWebhookInfo`
   diretamente no Telegram:
   ```bash
   curl "https://api.telegram.org/bot<token do BotFather>/getWebhookInfo"
   ```
   `url` deve bater com o `webhookUrl` retornado no passo 2
   (`http://localhost:5027/webhooks/<channelId>` em dev — só alcançável
   pelo Telegram se o processo estiver exposto publicamente, ex. via
   túnel/ngrok).
4. Envie uma mensagem para o bot no Telegram. Confirme que uma
   `Session`/`PendingDispatch` foi criada em `apps/inbox` e que, depois da
   janela de debounce, o agente responde de volta no Telegram — round-trip
   completo (webhook → verificação de `secret_token` → orquestrador →
   `apps/api` → `apps/workers` → push notification →
   `TelegramOutboundMessageSender` → `POST .../sendMessage`).

## Como testar cada app

Os testes de integração de `apps/api`, `apps/workers`, `apps/inbox` e dos
dois projetos cruzados em `tests/` (`CrossAppTaskStoreCompatibility.Tests`,
`InboxOrchestratorRoundTrip.Tests`) sobem Postgres/RabbitMQ efêmeros via
Testcontainers — não precisam do `docker compose up` da seção acima
rodando, mas precisam de Docker/Podman disponível.

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

# round-trip completo: apps/inbox (debounce + cliente A2A) -> apps/api -> apps/workers -> push notification
dotnet test tests/InboxOrchestratorRoundTrip.Tests

# apps/frontend
cd apps/frontend
npm run lint
npm run format:check
npm run build
```

## Como buildar as imagens Docker

Cada app tem um `Dockerfile` na própria pasta, mas o **build context é
sempre a raiz do monorepo** — necessário porque `Directory.Build.props`,
`Directory.Packages.props`, `global.json` e `libs/ProviderCatalog` vivem
fora da pasta de cada app. Rode os comandos abaixo a partir da raiz.

```bash
# apps/api
docker build -f apps/api/Dockerfile -t buteco-api .

# apps/inbox
docker build -f apps/inbox/Dockerfile -t buteco-inbox .

# apps/workers
docker build -f apps/workers/Dockerfile -t buteco-workers .

# apps/frontend — VITE_API_BASE_URL/VITE_INBOX_BASE_URL são build-time
# (embutidas no bundle) e OBRIGATÓRIAS: o build falha se não forem
# passadas, mesmo que vazias. Vazio ("") = caminho relativo, para quando
# o nginx do stack serve o SPA e faz proxy para apps/api/apps/inbox no
# mesmo domínio (ver docker-compose.prod.yml).
docker build -f apps/frontend/Dockerfile \
  --build-arg VITE_API_BASE_URL= \
  --build-arg VITE_INBOX_BASE_URL= \
  -t buteco-frontend .

# migration bundle (apps/api + apps/inbox — nunca apps/workers, que não
# aplica migration)
docker build -f deploy/migrate/Dockerfile -t buteco-migrate .
```

Todas as imagens finais usam a variante **default** (não `-alpine`) das
imagens `mcr.microsoft.com/dotnet/*` — a `-alpine` não traz a tz database
nem ICU por padrão, o que quebra a checagem de fuso horário de
`apps/workers` e a formatação de data em pt-BR (verificado empiricamente,
ver `openspec/changes/archive/2026-08-26-containerizacao-stack-servidor/design.md`,
decisão D4).

Para subir o stack completo numa VM (todas as imagens + Postgres/
RabbitMQ próprios + nginx interno), use `docker-compose.prod.yml` — não
confundir com o `docker-compose.yml` da seção acima, que é só infra de
desenvolvimento local. Sequência de deploy, inventário de variáveis de
ambiente e segredos compartilhados entre processos estão documentados em
`deploy/runbook.md`.

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
  verificar compatibilidade de schema, e `tests/InboxOrchestratorRoundTrip.Tests`,
  que referencia os três apps (`Buteco.Api`, `Buteco.Inbox`, `Buteco.Workers`)
  de propósito só para o teste de round-trip ponta a ponta do orquestrador
  de `apps/inbox` — em nenhum dos dois casos algum app referencia o
  projeto de teste de volta, nem ele é publicado com nenhum deles.
- **Central Package Management**: versões de pacotes NuGet ficam em
  `Directory.Packages.props` na raiz; os `.csproj` referenciam pacotes sem
  `Version`.
- **Mudanças planejadas com OpenSpec**: propostas, specs, design e tasks de
  cada mudança ficam em `openspec/changes/<nome-da-mudança>/`. Use
  `openspec status --change "<nome>"` para ver o progresso de uma mudança em
  andamento.
- **Autenticação e allowlist de rotas anônimas**: `apps/api` e `apps/inbox`
  exigem token (`Authorization: Bearer`) em toda rota por padrão — uma
  rota só fica anônima com `.AllowAnonymous()` **e**
  `AnonymousRouteClassification` (motivo documentado) anexados
  explicitamente no `Map*` correspondente. Uma checagem de integridade
  (`RouteAuthenticationExtensions.ValidateRouteAuthenticationClassification`,
  chamada no fim de cada `Program.cs`) derruba o boot se alguma rota
  ficar sem essa dupla marcação, ou se a allowlist de código citar uma
  rota que não existe mais — mesmo padrão de
  `ValidateChannelAdapterRegistrations` (`apps/inbox`). Ver
  `openspec/changes/auth-login-e-servico/design.md`.
- **Fuso horário do sistema (`apps/workers`)**: `TZ` do SO, único para o
  processo inteiro, sem opção por agente e sem chave em
  `appsettings.json`. Mesma família de checagem de integridade no
  startup das duas acima (`ValidateTimeZoneConfiguration`) — compara o
  fuso efetivamente resolvido contra o valor declarado em `TZ`, não só a
  presença da variável, e derruba o boot se não baterem. Ver
  `01-ARQUITETURA_E_CONVENCOES.md`, "Fuso horário do sistema", e
  `openspec/changes/apps-workers-contexto-temporal/design.md`.
