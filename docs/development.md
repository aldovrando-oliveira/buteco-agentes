# Ambiente de desenvolvimento

Como preparar a máquina, subir a infraestrutura, rodar cada um dos quatro
apps, executar os testes e construir as imagens Docker.

Para o inventário completo de variáveis de ambiente, ver
[configuration.md](configuration.md). Para deploy em servidor, ver
[deployment.md](deployment.md).

---

## Índice

- [Pré-requisitos](#pré-requisitos)
- [Testcontainers com Podman](#testcontainers-com-podman)
- [Subir as dependências (Postgres, RabbitMQ, WAHA)](#subir-as-dependências-postgres-rabbitmq-waha)
- [Migrations](#migrations)
- [Rodar cada app](#rodar-cada-app)
  - [apps/api](#appsapi)
  - [apps/workers](#appsworkers)
  - [apps/inbox](#appsinbox)
  - [apps/frontend](#appsfrontend)
- [Autenticação em desenvolvimento](#autenticação-em-desenvolvimento)
- [Checklists de round-trip manual](#checklists-de-round-trip-manual)
- [Rodar os testes](#rodar-os-testes)
- [Construir as imagens Docker](#construir-as-imagens-docker)

---

## Pré-requisitos

- **.NET SDK 10** — `dotnet --version` deve reportar `10.x`. A versão exata
  está fixada em `global.json` (`10.0.301`, com `rollForward: latestFeature`).
- **Node.js 20+** e npm — para `apps/frontend`.
- **Docker**, ou **Podman** com `podman machine` rodando — para o
  `docker-compose.yml` de desenvolvimento e para os testes de integração, que
  sobem Postgres e RabbitMQ efêmeros via
  [Testcontainers](https://testcontainers.com/).

Se você usa Podman, leia a próxima seção antes de rodar qualquer teste.

---

## Testcontainers com Podman

O Testcontainers fala com o daemon indicado por `DOCKER_HOST`, e com Podman
essa variável não é definida sozinha. Sem ela os testes de integração falham
todos de uma vez, com erro de conexão ao daemon — que é fácil de confundir
com o código estar quebrado.

O trecho abaixo detecta o runtime disponível e só exporta a variável quando é
o Podman que está no ar, então serve nas duas máquinas sem alteração:

```bash
if ! command -v docker >/dev/null && command -v podman >/dev/null; then
  export DOCKER_HOST="unix://$(podman machine inspect --format '{{.ConnectionInfo.PodmanSocket.Path}}' | head -1)"
  export TESTCONTAINERS_RYUK_DISABLED=true
fi
```

`TESTCONTAINERS_RYUK_DISABLED` é necessário porque o contêiner de limpeza do
Testcontainers pressupõe socket do Docker. Sem Ryuk, os contêineres efêmeros
são removidos ao fim da execução pelo próprio Testcontainers; se uma execução
for interrompida, sobram contêineres a remover à mão com `podman ps -a`.

**As duas variáveis falham de formas diferentes, e a segunda engana mais.**

| Variável ausente | Sintoma |
|---|---|
| `DOCKER_HOST` | erro de conexão ao daemon — sintoma claro |
| `TESTCONTAINERS_RYUK_DISABLED` | a conexão funciona e o Ryuk tenta bind-montar o socket do host dentro de um contêiner; o Podman no macOS responde `operation not supported` (`DockerApiException`, HTTP 500) e **toda** classe que usa Testcontainers reprova em ~1 s — o que parece falha de teste em massa |

O sinal que distingue os dois casos é a pilha terminar em
`ResourceReaper.GetAndStartNewAsync` dentro do `InitializeAsync` da fixture:
aí é infraestrutura, não código.

---

## Subir as dependências (Postgres, RabbitMQ, WAHA)

```bash
cp .env.example .env   # ajuste as portas se 5432/5672/15672 já estiverem em uso
docker compose up -d   # ou: podman compose up -d
```

Isso sobe:

- **Postgres** na porta `5432` (padrão), com banco, usuário e senha definidos
  no `.env`.
- **RabbitMQ** nas portas `5672` (AMQP) e `15672` (UI de management, em
  `http://localhost:15672`, com as mesmas credenciais do `.env`).
- **WAHA** (WhatsApp HTTP API) na porta `3000` (padrão), usado por
  `apps/inbox` para exercitar o adapter `waha`. Sobe sem sessão
  pré-configurada — ver
  [checklist de round-trip com WAHA](#checklist-de-round-trip-manual-com-waha).

Os dados persistem em volumes nomeados entre `docker compose down` e
`docker compose up`. Para apagar tudo, inclusive os volumes:

```bash
docker compose down -v
```

Este compose é **apenas infraestrutura de desenvolvimento local**. Não
confundir com `docker-compose.prod.yml`, que sobe o stack completo em
servidor — ver [deployment.md](deployment.md).

---

## Migrations

Dois bancos independentes, cada um com sua migration:

```bash
# apps/api — banco compartilhado com apps/workers
cd apps/api/src/Buteco.Api
dotnet ef database update

# apps/inbox — banco próprio (buteco_inbox), criado automaticamente
# se ainda não existir no mesmo servidor Postgres
cd apps/inbox/src/Buteco.Inbox
dotnet ef database update
```

`apps/workers` compartilha o schema de `apps/api`, mas **nunca o cria** — não
aplica migration em runtime nem em tempo de deploy. `apps/inbox` não tem
nenhuma tabela em comum com os outros dois.

---

## Rodar cada app

### apps/api

```bash
cd apps/api
dotnet run --project src/Buteco.Api
```

Escuta em `http://localhost:5017` em desenvolvimento.

Lê `ConnectionStrings:Postgres`, `RabbitMq:*` e `Auth:*` via
`appsettings.Development.json` (defaults de dev, os mesmos do `.env.example`)
ou por variáveis de ambiente (`ConnectionStrings__Postgres`,
`RabbitMq__Host`, `Auth__TokenSigningKey`, etc.) — nunca fixados em código.

`Auth:TokenSigningKey`, `Auth:OperatorUsername` e `Auth:OperatorPasswordHash`
são **obrigatórios**: o processo falha ao subir sem eles. Ver
[Autenticação em desenvolvimento](#autenticação-em-desenvolvimento) para
gerar o hash.

Toda rota exige `Authorization: Bearer <token>`, exceto `GET /health`,
`POST /auth/login` e `GET /agents/{id}/.well-known/agent-card.json`. Os
exemplos abaixo fazem login primeiro e reaproveitam o token:

```bash
curl -i http://localhost:5017/health

# login do operador (default de dev: operator / changeme)
TOKEN=$(curl -s -X POST http://localhost:5017/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"operator","password":"changeme"}' | jq -r '.token')

# provedores de LLM disponíveis — só aparecem os que têm variável
# de ambiente configurada no ambiente de apps/api
curl -H "Authorization: Bearer $TOKEN" http://localhost:5017/providers

# cadastrar um agente — provider/model precisam estar entre os
# disponíveis em GET /providers
curl -X POST http://localhost:5017/agents \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"name":"Atendente","instructions":"Você é um atendente simpático.","provider":"openai","model":"gpt-5.6-sol"}'

# usar o agente via A2A (SendMessage, JSON-RPC)
curl -X POST http://localhost:5017/agents/<id>/a2a \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"role":"ROLE_USER","parts":[{"text":"Olá!"}],"messageId":"<uuid>"}}}'

# consultar o estado da task (GetTask)
curl -X POST http://localhost:5017/agents/<id>/a2a \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"jsonrpc":"2.0","id":2,"method":"GetTask","params":{"id":"<taskId do SendMessage>"}}'
```

A task nasce `TASK_STATE_SUBMITTED` e, com `apps/workers` rodando, avança
para `TASK_STATE_WORKING` e depois `TASK_STATE_COMPLETED` (ou
`TASK_STATE_FAILED` se o LLM falhar).

O guia completo de integração externa via A2A — AgentCard, push
notification, enumeradores, erros do protocolo e recomendações de polling —
está em [a2a-integration.md](a2a-integration.md).

### apps/workers

```bash
cd apps/workers
dotnet run --project src/Buteco.Workers
```

Além de `ConnectionStrings:Postgres` e `RabbitMq:*`, lê a configuração de
cada provedor de LLM:

| Variável | Provedor |
|---|---|
| `OpenAI:BaseUrl` / `OpenAI:ApiKey` / `OpenAI:Model` | OpenAI, ou Azure OpenAI / gateway compatível (basta trocar `BaseUrl` e `ApiKey`) |
| `Anthropic:ApiKey` | Claude, via pacote oficial `Anthropic` |
| `Gemini:ApiKey` | Gemini, via pacote oficial `Google.GenAI` |

Só a configuração do provedor referenciado pelo agente em execução é usada de
fato, mas **a mesma chave precisa existir nos dois processos**. `apps/api` e
`apps/workers` são deploys separados: se `apps/api` anunciar um provedor
disponível em `GET /providers` e `apps/workers` não tiver a mesma chave, a
task termina `failed` (com log de erro no worker) em vez de travar.

`apps/workers` também exige a variável de ambiente **`TZ` do sistema
operacional** — não lida via `IConfiguration` — com o nome IANA canônico da
tz database, por exemplo `America/Sao_Paulo`, nunca com o prefixo POSIX `:`.
O processo **falha ao subir** se `TZ` estiver ausente, vazia, ou se o fuso
resolvido pelo SO não corresponder exatamente ao valor declarado. É uma
checagem de integridade no startup, o mesmo padrão descrito em
[conventions.md](conventions.md). É o único ponto de acesso a relógio e fuso
do worker, usado para renderizar o instante de processamento e o dia da
semana (sempre em pt-BR) no bloco de contexto temporal enviado ao LLM.

Esperado no console: `Worker starting at: ...`, junto com o fuso resolvido e
o offset atual (ex. `Fuso horário do sistema: America/Sao_Paulo, offset
atual: -03:00:00`) e, com um job na fila, os logs de transição de estado da
task. `Ctrl+C` encerra e loga `Worker stopping at: ...`.

> **Delegação entre agentes exige duas ou mais instâncias de `apps/workers`
> rodando ao mesmo tempo.** O consumidor RabbitMQ processa no máximo uma
> mensagem não confirmada por instância (`prefetchCount: 1`). Se um agente
> delega para outro e só há uma instância ativa, ela fica esperando a task
> delegada terminar sem nunca poder consumi-la ela mesma — a delegação
> sempre expira pelo timeout de 120 s em vez de completar. Para exercitar
> esse fluxo localmente, rode um segundo
> `dotnet run --project src/Buteco.Workers` em outro terminal, com a mesma
> configuração e sem nenhum ajuste adicional.

### apps/inbox

Aplique a migration antes do primeiro run (ver [Migrations](#migrations)).

```bash
cd apps/inbox
dotnet run --project src/Buteco.Inbox
```

Escuta em `http://localhost:5027` em desenvolvimento.

Lê `ConnectionStrings:Postgres`, `Inbox:CredentialEncryptionKey`,
`Api:BaseUrl`, `PublicUrl:BaseUrl`, `Debounce:*` e `Auth:TokenSigningKey` via
`appsettings.Development.json` ou variáveis de ambiente. Dois valores
merecem atenção:

- **`Api:BaseUrl`** deve apontar para onde `apps/api` está escutando
  (`http://localhost:5017` em dev). É usado para validar o `AgentId` de um
  canal (`GET /agents/{id}`) e para o cliente A2A (`POST /agents/{id}/a2a`),
  ambos autenticados com um token de serviço que `apps/inbox` assina
  sozinho. Por isso `Auth:TokenSigningKey` **precisa ter o mesmo valor** nos
  dois processos — senão nenhum token, de operador ou de serviço, é aceito
  entre eles.
- **`PublicUrl:BaseUrl`** deve ser a URL pela qual este processo é alcançável
  a partir de `apps/api` e `apps/workers` (`http://localhost:5027` em dev).
  É usada no `pushNotificationConfig.url` enviado em cada `SendMessage`.

Toda rota exige `Authorization: Bearer <token>`, exceto `GET /health`,
`POST /webhooks/{channelId}` e `POST /internal/push-notifications`. Use o
mesmo token de operador obtido em `apps/api`:

```bash
curl -i http://localhost:5027/health

# cadastrar um canal WAHA — credential é o JSON de WahaCredential
# serializado como string; agentId precisa existir em apps/api, que
# precisa estar rodando (o cadastro é rejeitado em fail-fast se não estiver)
curl -X POST http://localhost:5027/channels \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"channelType":"waha","name":"Suporte WhatsApp","credential":"{\"ServiceUrl\":\"http://localhost:3000\",\"SessionName\":\"default\",\"AuthToken\":\"<api key do WAHA>\"}","agentId":"<id de um agente existente em apps/api>"}'

# listar e consultar canais — a credencial nunca aparece na resposta;
# webhookUrl vem pronto para configurar no canal externo
curl -H "Authorization: Bearer $TOKEN" http://localhost:5027/channels
curl -H "Authorization: Bearer $TOKEN" http://localhost:5027/channels/<id>

# ativar e desativar (idempotente, nunca exclui o registro)
curl -X POST -H "Authorization: Bearer $TOKEN" http://localhost:5027/channels/<id>/deactivate
curl -X POST -H "Authorization: Bearer $TOKEN" http://localhost:5027/channels/<id>/activate
```

O orquestrador (`IInboundMessageOrchestrator`) é um serviço interno, sem
endpoint HTTP próprio — consumido diretamente por código e pelos handlers de
webhook de cada adapter (`IInboundWebhookHandler`, despachados pela rota
genérica `POST /webhooks/{channelId}`). Mensagens bufferizadas por sessão
aguardam a janela de debounce (`Debounce:Window`, varrida periodicamente por
um `BackgroundService` a cada `Debounce:SweepInterval`) antes de disparar um
`SendMessage` real contra `apps/api`.

### apps/frontend

```bash
cd apps/frontend
npm install   # primeira vez
npm run dev
```

Abra `http://localhost:5173`. A aplicação redireciona para `/login` — entre
com o operador configurado em `apps/api` (default de dev: `operator` /
`changeme`).

---

## Autenticação em desenvolvimento

`apps/api` e `apps/inbox` compartilham a mesma `Auth:TokenSigningKey` e
validam localmente qualquer token assinado com ela, sem chamada de rede entre
os dois processos. Há um único operador, com credencial vinda de
configuração — não existe tabela de usuários.

Default de desenvolvimento (`appsettings.Development.json`): usuário
`operator`, senha `changeme`.

Para gerar um `Auth:OperatorPasswordHash` novo — em produção, ou para trocar
a senha de dev — use o formato composto
`{iterations}.{saltBase64}.{hashBase64}`, PBKDF2-HMAC-SHA256. O hash é
gerado **offline**, nunca por código de produção:

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

Cole a saída em `Auth:OperatorPasswordHash` (variável de ambiente
`Auth__OperatorPasswordHash` em `apps/api`).

`Auth:OperatorTokenLifetime` é opcional, com default de 30 minutos fixado no
próprio tipo (`TokenSigningOptions`, não no `appsettings.json`). Ajuste por
ambiente só se precisar de um TTL diferente.

---

## Checklists de round-trip manual

Os dois checklists abaixo dependem de serviços externos reais, por isso são
manuais e não automatizados.

### Checklist de round-trip manual com WAHA

Depende de um WAHA real conectado a um WhatsApp de teste.

1. Suba o WAHA: `docker compose up -d waha` (ou `docker compose up -d` para
   tudo). Confirme com `curl -i http://localhost:3000/api/sessions`.
2. Cadastre o canal em `apps/inbox` (`POST /channels`, exemplo acima) e anote
   o `webhookUrl` retornado — formato
   `http://localhost:5027/webhooks/<channelId>`.
3. Configure a sessão no próprio WAHA, apontando o webhook para a URL do
   passo 2. Este passo é manual, não automatizado por `apps/inbox`:
   ```bash
   curl -X POST http://localhost:3000/api/sessions \
     -H "Content-Type: application/json" \
     -d '{"name":"default","config":{"webhooks":[{"url":"<webhookUrl do passo 2>","events":["message"]}]}}'
   ```
4. Escaneie o QR code (`GET /api/screenshot?session=default` no WAHA, ou a UI
   do próprio WAHA) com um WhatsApp de teste, até o status da sessão virar
   `WORKING`.
5. Envie uma mensagem de WhatsApp real para o número conectado. Confirme que
   uma `Session` e um `PendingDispatch` foram criados em `apps/inbox` e que,
   depois da janela de debounce, o agente responde no mesmo WhatsApp —
   round-trip completo: webhook → orquestrador → `apps/api` →
   `apps/workers` → push notification → `WahaOutboundMessageSender` →
   `POST /api/sendText`.

### Checklist de round-trip manual com Telegram

Depende de um bot Telegram real. Ao contrário do WAHA, o cadastro do canal já
configura o webhook automaticamente — não há passo manual de configuração de
sessão.

1. Crie um bot com o [@BotFather](https://t.me/BotFather) (`/newbot`) e anote
   o `BotToken` retornado.
2. Cadastre o canal em `apps/inbox`. A credencial é o JSON de
   `TelegramCredential` serializado como string, contendo apenas `BotToken` —
   `WebhookSecret` é preenchido pelo provisionamento, não informado pelo
   cliente:
   ```bash
   curl -X POST http://localhost:5027/channels \
     -H "Content-Type: application/json" \
     -H "Authorization: Bearer $TOKEN" \
     -d '{"channelType":"telegram","name":"Suporte Telegram","credential":"{\"BotToken\":\"<token do BotFather>\"}","agentId":"<id de um agente existente em apps/api>"}'
   ```
   Se o `BotToken` for inválido ou o Telegram estiver inalcançável, o cadastro
   inteiro falha com HTTP 502 — nenhum canal é persistido sem o webhook de
   fato registrado.
3. Confirme o registro do webhook consultando o Telegram diretamente:
   ```bash
   curl "https://api.telegram.org/bot<token do BotFather>/getWebhookInfo"
   ```
   O campo `url` deve bater com o `webhookUrl` retornado no passo 2. Em
   desenvolvimento (`http://localhost:5027/...`) o Telegram só alcança o
   processo se ele estiver exposto publicamente, por exemplo via túnel.
4. Envie uma mensagem para o bot. Confirme que uma `Session` e um
   `PendingDispatch` foram criados em `apps/inbox` e que, depois da janela de
   debounce, o agente responde no Telegram — round-trip completo: webhook →
   verificação de `secret_token` → orquestrador → `apps/api` →
   `apps/workers` → push notification → `TelegramOutboundMessageSender` →
   `POST .../sendMessage`.

---

## Rodar os testes

Os testes de integração de `apps/api`, `apps/workers`, `apps/inbox` e dos
dois projetos cruzados em `tests/` sobem Postgres e RabbitMQ efêmeros via
Testcontainers. Eles **não** precisam do `docker compose up` acima rodando,
mas precisam de Docker ou Podman disponível — se você usa Podman, veja
[Testcontainers com Podman](#testcontainers-com-podman).

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

# round-trip completo: apps/inbox (debounce + cliente A2A)
# -> apps/api -> apps/workers -> push notification
dotnet test tests/InboxOrchestratorRoundTrip.Tests

# apps/frontend
cd apps/frontend
npm run lint
npm run format:check
npm run test
npm run build
```

Os dois projetos em `tests/` são as únicas exceções ao isolamento entre apps,
e existem exatamente para verificar acordos que nenhum app sozinho consegue
verificar — ver [conventions.md](conventions.md).

---

## Construir as imagens Docker

Cada app tem um `Dockerfile` na própria pasta, mas o **build context é sempre
a raiz do monorepo**. Isso é necessário porque `Directory.Build.props`,
`Directory.Packages.props`, `global.json` e `libs/ProviderCatalog` vivem fora
da pasta de cada app. Rode os comandos a partir da raiz:

```bash
# apps/api
docker build -f apps/api/Dockerfile -t buteco-api .

# apps/inbox
docker build -f apps/inbox/Dockerfile -t buteco-inbox .

# apps/workers
docker build -f apps/workers/Dockerfile -t buteco-workers .

# apps/frontend
docker build -f apps/frontend/Dockerfile \
  --build-arg VITE_API_BASE_URL= \
  --build-arg VITE_INBOX_BASE_URL= \
  -t buteco-frontend .

# migration bundle — apps/api e apps/inbox, nunca apps/workers
docker build -f deploy/migrate/Dockerfile -t buteco-migrate .
```

`VITE_API_BASE_URL` e `VITE_INBOX_BASE_URL` são **build-time** (embutidas no
bundle) e **obrigatórias**: o build do frontend falha se não forem passadas,
mesmo que vazias. Vazio (`""`) significa caminho relativo, para quando o
nginx do stack serve o SPA e faz proxy para `apps/api` e `apps/inbox` no
mesmo domínio.

Todas as imagens finais usam a variante **default** das imagens
`mcr.microsoft.com/dotnet/*`, nunca a `-alpine`. A `-alpine` não traz a tz
database nem o ICU por padrão, o que quebra a checagem de fuso horário de
`apps/workers` e a formatação de data em pt-BR — verificado empiricamente.

Para subir o stack completo em um servidor, ver
[deployment.md](deployment.md).
