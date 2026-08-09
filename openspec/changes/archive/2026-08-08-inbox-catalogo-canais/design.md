## Context

`apps/inbox` hoje (change arquivada `estrutura-base-apps-inbox`) é um scaffold
ASP.NET Core Web API mínimo: `Program.cs` só registra health check, sem EF
Core, sem Mediator, sem `ConnectionStrings`. A decisão de persistência foi
deliberadamente adiada para quando houvesse necessidade concreta — esta change
é essa necessidade: cadastrar canais de entrada (WhatsApp, Telegram) que,
mais adiante, adapters reais vão consultar para saber como se conectar e
para onde rotear mensagens.

Investigação feita antes desta proposta (via `/opsx:explore`), lendo código
real, não assumido:

- **`apps/api` e `apps/workers` hoje não usam "schema separado"** — eles
  compartilham o mesmo banco físico (`buteco_agents`, mesmo
  `docker-compose.yml`) **e as mesmas tabelas** (`agents`, `mcp_servers`,
  `agent_mcp_servers`, `agent_delegations`), via dois `AppDbContext`
  independentes, mantidos manualmente em sincronia — inclusive migrations
  duplicadas em datas próximas (ex. `AddMcpServerCatalog` em `apps/api`
  2026-08-02, em `apps/workers` 2026-08-03, mesmo shape de tabela).
  `README.md` confirma o mecanismo: *"só apps/api aplica migration em
  runtime — apps/workers usa o mesmo schema mas nunca o cria"*. É um
  acoplamento mais forte do que "schema dentro do mesmo banco" — é
  literalmente a mesma tabela física, contrato mantido só por disciplina
  entre dois `AppDbContext` escritos à mão.
- `docker-compose.yml` hoje tem um único serviço `postgres`, uma única
  variável `POSTGRES_DB` (`buteco_agents`), sem nenhum script em
  `docker-entrypoint-initdb.d/`. Não existe hoje nenhum mecanismo de criação
  de um segundo banco no mesmo servidor.
- `AesGcmMcpCredentialCipher`/`IMcpCredentialCipher`/`McpCryptoOptions`
  (`Mcp:CredentialEncryptionKey`) já existem **duplicados** entre `apps/api`
  e `apps/workers` — não vivem em `libs/`. `libs/ProviderCatalog` é o único
  código hoje compartilhado entre apps, criado só quando houve necessidade
  concreta de dois processos concordarem sobre o mesmo contrato (nome de
  provedor + seção de configuração) — não é o caso aqui, cada app cifra e
  decifra seus próprios segredos, sem nenhum contrato a compartilhar.
- `ReplaceAgentMcpServersCommandHandler` (`apps/api`) já faz handshake de
  rede real contra um MCP server antes de persistir um vínculo, fail-fast,
  testado trocando o `HttpMessageHandler` primário de um `HttpClient`
  nomeado (`FakeMcpServerHttpMessageHandler`,
  `apps/api/tests/Support/`). `PushNotificationSender` (`apps/workers`) é o
  primeiro `HttpClient` nomeado do projeto com `.Timeout` customizado
  explícito (`TimeSpan.FromSeconds(5)`, ver `Program.cs` de `apps/workers`).
  São os dois precedentes mais próximos de "um handler chama outro serviço
  HTTP e precisa lidar com timeout/indisponibilidade", mesmo nenhum dos dois
  cruzar processo/app — esta change é a primeira vez que a chamada de rede
  atravessa a fronteira entre dois apps do monorepo.
- Não existe hoje, em `apps/inbox`, nenhuma configuração `Api:BaseUrl` (ou
  equivalente) para localizar `apps/api`. `PublicUrl:BaseUrl` existe, mas é
  interno a `apps/api`, usado só para montar o *agent card* A2A — não serve
  como endereço de um cliente HTTP de outro app.
- `Mediator.Abstractions`/`Mediator.SourceGenerator` (3.0.2) e o padrão
  Commands/Queries já estão descritos e resolvidos em
  `openspec/changes/archive/2026-07-26-apps-api-cqrs-mediator/design.md`,
  incluindo um bug real já corrigido lá (`options.ServiceLifetime` precisa
  ser `Scoped` explicitamente, senão o DI do ASP.NET Core falha no
  `Build()` porque os handlers dependem de `AppDbContext`, que é `Scoped`).

## Goals / Non-Goals

**Goals:**
- `apps/inbox` ganha persistência própria (EF Core + Postgres) em um banco
  logicamente isolado de `apps/api`/`apps/workers`, sem nenhuma tabela em
  comum.
- `apps/inbox` ganha Mediator/CQRS, mesmo padrão estrutural já usado em
  `apps/api`.
- CRUD completo de `Channel`, com credenciais opacas e criptografadas, nunca
  expostas em nenhuma resposta de leitura.
- `AgentId` do canal é validado contra `apps/api` via HTTP no momento do
  cadastro/atualização, fail-fast.
- Toda a superfície nova é coberta por teste automatizado (handlers,
  endpoints, cifra, validação de `AgentId` incluindo os quatro casos:
  sucesso, 404, HTTP 500, `apps/api` inalcançável).

**Non-Goals:**
- Nenhum CRM (`Contact`/`Session`) — próxima change.
- Nenhum orquestrador, debounce ou fila de mensagens em `apps/inbox`.
- Nenhum adapter real de canal (WhatsApp/Telegram) nem tentativa de conexão
  contra a plataforma externa — diferente do "Testar conexão" de
  `McpServer`, aqui não existe ainda com o que testar; a credencial é
  opaca e nunca decifrada fora do próprio processo de `apps/inbox`.
- Nenhuma UI.
- Nenhuma mudança de código em `apps/api` ou `apps/workers`. `apps/api` é
  só consumido (leitura) via `GET /agents/{id}`, endpoint já existente.
- Nenhum `ProjectReference` novo, nenhum conteúdo novo em `libs/` — ver
  Decisão 3 sobre por que a cifra é copiada, não extraída para lib.

## Decisions

### Árvore de pastas proposta

```
apps/inbox/
├── Inbox.sln
├── src/
│   └── Buteco.Inbox/
│       ├── Buteco.Inbox.csproj
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       ├── Agents/
│       │   ├── IAgentReferenceValidator.cs
│       │   ├── AgentReferenceValidator.cs
│       │   └── AgentReferenceValidationResult.cs
│       ├── Channels/
│       │   ├── Entities/
│       │   │   ├── Channel.cs
│       │   │   └── ChannelType.cs
│       │   ├── Security/
│       │   │   ├── IChannelCredentialCipher.cs
│       │   │   ├── AesGcmChannelCredentialCipher.cs
│       │   │   └── InboxCryptoOptions.cs
│       │   ├── Commands/
│       │   │   ├── CreateChannel/
│       │   │   │   ├── CreateChannelCommand.cs
│       │   │   │   ├── CreateChannelCommandHandler.cs
│       │   │   │   └── CreateChannelResult.cs
│       │   │   ├── UpdateChannel/
│       │   │   │   ├── UpdateChannelCommand.cs
│       │   │   │   ├── UpdateChannelCommandHandler.cs
│       │   │   │   └── UpdateChannelResult.cs
│       │   │   ├── ActivateChannel/
│       │   │   │   ├── ActivateChannelCommand.cs
│       │   │   │   └── ActivateChannelCommandHandler.cs
│       │   │   └── DeactivateChannel/
│       │   │       ├── DeactivateChannelCommand.cs
│       │   │       └── DeactivateChannelCommandHandler.cs
│       │   ├── Queries/
│       │   │   ├── GetChannelById/
│       │   │   │   ├── GetChannelByIdQuery.cs
│       │   │   │   └── GetChannelByIdQueryHandler.cs
│       │   │   └── ListChannels/
│       │   │       ├── ListChannelsQuery.cs
│       │   │       └── ListChannelsQueryHandler.cs
│       │   ├── Requests/
│       │   │   ├── CreateChannelRequest.cs
│       │   │   └── UpdateChannelRequest.cs
│       │   ├── Responses/
│       │   │   └── ChannelResponse.cs
│       │   └── Endpoints/
│       │       └── ChannelEndpoints.cs
│       ├── Infrastructure/
│       │   ├── AppDbContext.cs
│       │   ├── InfrastructureServiceCollectionExtensions.cs
│       │   └── Migrations/
│       │       └── <timestamp>_InitialCreate.cs (+ .Designer.cs, ModelSnapshot)
│       └── Options/
│           ├── ApiOptions.cs
│           └── InboxCryptoOptions.cs (ver Channels/Security acima — mesmo
│               local relativo de McpCryptoOptions em apps/api, dentro do
│               namespace de segurança, não em Options/)
└── tests/
    └── Buteco.Inbox.Tests/
        ├── Buteco.Inbox.Tests.csproj
        ├── HealthCheckTests.cs                    (existente, inalterado)
        ├── ChannelEndpointsTests.cs
        ├── CreateChannelCommandHandlerTests.cs
        ├── AesGcmChannelCredentialCipherTests.cs
        └── Support/
            ├── InboxFactoryFixture.cs
            └── FakeAgentApiHttpMessageHandler.cs
```

### 1. Banco próprio (`buteco_inbox`), não o padrão de tabela física compartilhada de api/workers

`apps/inbox` recebe seu próprio banco lógico no mesmo servidor Postgres do
`docker-compose.yml` — `buteco_inbox`, ao lado de `buteco_agents` — sem
nenhuma tabela em comum com `apps/api`/`apps/workers`.

**Caminho técnico**: `docker-compose.yml` continua com um único serviço
`postgres`, sem nenhum script novo em `docker-entrypoint-initdb.d/`.
`Database.MigrateAsync()`/`dotnet ef database update` (Npgsql) cria o banco
de destino automaticamente se ele não existir, desde que a role de conexão
tenha `CREATEDB` — o usuário `buteco` do compose é o `POSTGRES_USER` inicial
da imagem oficial `postgres`, que recebe esse privilégio por padrão.
**Verificado empiricamente durante o apply** (não só por documentação):
rodando `dotnet ef database update` de fato contra
`Host=localhost;Port=15532;Database=buteco_inbox;...` num Postgres local
(container já em execução via `podman compose`, sem `buteco_inbox`
previamente existente), o EF Core logou `CREATE DATABASE buteco_inbox;`
seguido da criação de `__EFMigrationsHistory` e da tabela `channels` — sem
nenhuma mudança em `docker-compose.yml`. Confirmado via
`psql -U buteco -l` (dentro do container) que o banco e a tabela existem
após o comando. Nenhum fallback em `docker-entrypoint-initdb.d/` é
necessário.

`.env.example`/`appsettings.Development.json` de `apps/inbox` ganham sua
própria `ConnectionStrings:Postgres`, apontando para
`Database=buteco_inbox` no mesmo host/porta já usado por `apps/api`/`apps/workers`.

**Alternativa considerada**: seguir o padrão real já em uso entre
`apps/api`/`apps/workers` — mesma tabela física, dois `AppDbContext`
sincronizados manualmente (ver Context). Rejeitada por dois motivos: (1) é o
mesmo raciocínio de isolamento entre apps já usado desde
`estrutura-base-monorepo` (cada app com seu próprio `AppDbContext`, e aqui
levado ao seu fim lógico — nem a tabela é compartilhada); (2) diferente de
`apps/api`/`apps/workers` (que precisam concordar sobre a mesma linha de
`agents`/`mcp_servers` porque um escreve o catálogo e o outro o executa),
`apps/inbox` não precisa ler nem escrever nenhuma linha pertencente a
`apps/api` além de confirmar que um `AgentId` existe — o que a Decisão 4
resolve via HTTP, sem FK.

### 2. Mediator/CQRS adotado agora, mesma receita de `apps-api-cqrs-mediator`

`apps/inbox` ganha `Mediator.Abstractions`/`Mediator.SourceGenerator` (3.0.2,
já pinado em `Directory.Packages.props`) desde o início — não adia para uma
próxima change, como todo catálogo deste projeto já faz. `Commands/<Op>/`
para escrita (`ICommand`/`ICommandHandler`), `Queries/<Op>/` para leitura
(`IQuery`/`IQueryHandler`), mesma distinção documentada em
`apps-api-cqrs-mediator/design.md`.

`Program.cs` registra:
```csharp
builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
```
`ServiceLifetime.Scoped` **explícito**, não o padrão (`Singleton`) — sem
isso, o `Build()` do ASP.NET Core falha (*"Cannot consume scoped service
AppDbContext from singleton"*), exatamente o bug já documentado e corrigido
em `apps-api-cqrs-mediator`. Registrado aqui de antemão para não repetir a
mesma descoberta por tentativa e erro.

**Alternativa considerada**: adiar Mediator para uma change futura,
começando com handlers inline nos endpoints (como `apps/api` fazia antes de
`apps-api-cqrs-mediator`). Rejeitada — introduziria uma inconsistência
deliberada logo na primeira fatia com lógica de negócio real de
`apps/inbox`, sabendo de antemão que o padrão do projeto já é CQRS; melhor
adotar direto do que gerar um refactor previsível depois.

### 3. Credenciais opacas, cifradas com AES-GCM copiado (não extraído para `libs/`)

`Channel.EncryptedCredentials` guarda um blob `jsonb` **opaco** — sem campos
tipados por `ChannelType`. Nenhum adapter de WhatsApp/Telegram existe ainda
para validar contra; tipar agora seria adivinhar o shape exato que cada
plataforma vai exigir. Mesmo raciocínio já usado para não tipar
`Skills`/`AllowedTools` além do mínimo necessário em `apps/api`.

Cifra: `AesGcmChannelCredentialCipher`/`IChannelCredentialCipher`, cópia
funcionalmente idêntica de `AesGcmMcpCredentialCipher` (`apps/api`/`apps/workers`)
— nonce de 96 bits aleatório por chamada, chave de 256 bits vinda de
configuração, formato persistido `nonce || ciphertext || tag` em base64.
Chave própria: `Inbox:CredentialEncryptionKey`
(`Inbox__CredentialEncryptionKey` como env var), nunca a mesma chave de
`Mcp:CredentialEncryptionKey` — são segredos de domínios diferentes, cada um
só decifrável pelo processo que o gerou.

**Alternativa considerada**: extrair a cifra para uma `libs/CredentialCipher`
compartilhada entre os três apps. Rejeitada — mesmo padrão já estabelecido
(a cifra já está duplicada entre `apps/api` e `apps/workers` hoje, não
extraída), e `libs/` neste projeto só recebe conteúdo quando há necessidade
concreta de dois processos concordarem sobre o mesmo contrato em tempo real
(caso de `libs/ProviderCatalog`) — não é o caso aqui: cada app cifra e
decifra só os próprios segredos, isoladamente, sem nenhum contrato a
compartilhar entre processos.

**Alternativa considerada**: campos tipados por `ChannelType` desde já
(ex. `WhatsAppCredentials { PhoneNumberId, AccessToken }`,
`TelegramCredentials { BotToken }`). Rejeitada por falta de necessidade
concreta ainda — mesmo raciocínio já usado para não tipar `AllowedTools`/
`Skills` além do mínimo.

### 4. `AgentId` validado via HTTP síncrono contra `apps/api`, fail-fast

Como `apps/inbox` não compartilha banco com `apps/api` (Decisão 1), não há
FK possível — `AgentId` é um `Guid` opaco. `IAgentReferenceValidator`
(`Agents/`) chama `GET /agents/{id}` em `apps/api` via `HttpClient` nomeado
(`IHttpClientFactory`, mesmo mecanismo de `McpConnectionTester`/
`PushNotificationSender`), com `.Timeout` curto e explícito
(`TimeSpan.FromSeconds(5)`, mesmo valor já usado por `PushNotificationSender`
em `apps/workers` — primeiro precedente concreto de timeout customizado no
projeto). `CreateChannelCommandHandler`/`UpdateChannelCommandHandler` chamam
o validador antes de qualquer escrita; se `apps/api` responder `404` ou a
chamada falhar por qualquer motivo (timeout, conexão recusada, erro 5xx), o
comando é rejeitado — mesma filosofia fail-fast de toda validação de
referência cruzada já existente no projeto (Provider/Model contra
`GET /providers`, `AllowedTools` contra `tools/list` ao vivo em
`ReplaceAgentMcpServersCommandHandler`).

Nova configuração: `Api:BaseUrl` em `apps/inbox`
(`http://localhost:5017` em dev, mesma porta onde `apps/api` já escuta) —
não existe hoje um valor genérico para isso; `PublicUrl:BaseUrl` é interno a
`apps/api`.

Testes: mesmo mecanismo de `FakeMcpServerHttpMessageHandler` — um
`FakeAgentApiHttpMessageHandler` (`apps/inbox/tests/Support/`) substitui o
`HttpMessageHandler` primário do cliente nomeado, sem rede real, simulando
os quatro casos exigidos: (a) sucesso (HTTP 200); (b) não encontrado (HTTP
404); (c) HTTP 500 — resposta HTTP completa, com status de erro no corpo;
e (d) host inalcançável via `HttpRequestException` — sem nenhuma resposta
HTTP. (c) e (d) são cenários tecnicamente distintos e ambos precisam ser
simulados, não só (d): em (c) o transporte HTTP conclui normalmente e é o
status da resposta que sinaliza falha, enquanto em (d) a chamada nem chega
a completar.

**Alternativa considerada**: aceitar `AgentId` sem validar, só descobrir o
erro quando o canal tentar rotear uma mensagem de verdade. Rejeitada —
seria a primeira referência cruzada de todo o projeto sem validação
fail-fast no cadastro, quebrando um padrão já consistente em toda a base
(Provider/Model, AllowedTools, e agora AgentId); um cadastro de canal
silenciosamente quebrado só apareceria como erro em produção, no momento de
rotear uma mensagem real — pior lugar possível para descobrir isso.

**Alternativa considerada**: validação assíncrona (aceitar o cadastro,
validar em background, marcar o canal como inválido depois). Rejeitada —
adiciona um estado intermediário ("canal cadastrado mas ainda não
validado") sem necessidade concreta nesta fatia; `apps/api` é uma
dependência local do mesmo ambiente de desenvolvimento/produção, não um
serviço de terceiros com latência ou disponibilidade imprevisível a ponto
de justificar esse custo agora.

### 5. `ChannelType` como enum fechado (`WhatsApp`, `Telegram`)

Mesmo raciocínio de `McpServerAuthType` (`None`/`BearerToken`) em
`apps/api`: enum fechado nesta fatia, persistido como `string` no banco
(`HasConversion<string>()`), expansível quando um novo canal for desenhado
— não string livre, que aceitaria qualquer valor sem validação.

### 6. `IsActive`, sem exclusão

Mesmo padrão de todo catálogo já existente no projeto (`Agent`,
`McpServer`): `POST /channels/{id}/activate` e
`POST /channels/{id}/deactivate`, idempotentes, nunca `DELETE`. Um canal
desativado continua existindo (histórico, auditoria), só para de ser
elegível para roteamento quando o orquestrador existir.

### 7. Canal vinculado a agente inativo é permitido — validação não verifica `IsActive`

`IAgentReferenceValidator` (Decisão 4) só distingue três estados a partir
da resposta de `GET /agents/{id}`: encontrado (200), não encontrado (404) e
falha de comunicação (qualquer outro status ou exceção). Como
`GET /agents/{id}` em `apps/api` já responde HTTP 200 normalmente para um
agente com `isActive: false` — mesmo mecanismo do requirement "Servidor MCP
inativo permanece configurável" da spec `mcp-server-catalog` — a
consequência prática é que cadastrar ou atualizar um canal apontando para
um agente inativo já funciona, sem nenhum código adicional no validador.
Registrado aqui como decisão **intencional**, não como efeito colateral não
examinado da forma como o validador foi construído: é consistente com os
dois precedentes já existentes no projeto que tratam configuração e estado
de ativação como independentes — `AgentMcpServer` permite vincular um
`McpServer` inativo a um agente (`mcp-server-catalog`, Requirement
"Servidor MCP inativo permanece configurável"), e `AgentDelegation` permite
apontar para um agente-alvo inativo (change arquivada
`backend-agente-delegacao-catalogo-vinculo`). O raciocínio comum aos três
casos: `isActive` é um estado de runtime/exibição, não uma trava de
configuração — só o roteamento de mensagens em tempo real (quando o
orquestrador de `apps/inbox` existir) precisaria decidir se ignora um canal
cujo agente está inativo; bloquear o cadastro agora anteciparia uma regra
de roteamento que ainda não existe.

**Alternativa considerada**: `IAgentReferenceValidator` inspecionar o campo
`isActive` do payload de `GET /agents/{id}` e rejeitar o cadastro/
atualização quando o agente estiver inativo. Rejeitada — quebraria a
consistência com os dois precedentes citados (ambos já estabelecidos antes
desta change), introduzindo uma trava de configuração que nenhum outro
catálogo do projeto impõe hoje.

## Risks / Trade-offs

- **[Risco] `apps/api` indisponível bloqueia todo cadastro/atualização de
  canal** (Decisão 4, fail-fast intencional) → Aceito: é a mesma
  característica de toda validação de referência cruzada já existente no
  projeto (Provider/Model, AllowedTools) — consistência de comportamento
  entre validações vale mais do que tolerância a essa indisponibilidade
  específica nesta fatia. Se isso se mostrar um problema real em uso, é o
  gatilho concreto para revisitar (ex. cache curto do resultado de
  validação, retry com backoff).
- **[Trade-off] Cifra AES-GCM duplicada pela terceira vez (apps/api,
  apps/workers, agora apps/inbox)** → Aceito, consistente com o padrão já
  estabelecido (ver Decisão 3); reavaliar extração para `libs/` só se um
  quarto caso concreto de necessidade de contrato compartilhado aparecer.
- **[Trade-off] Nenhum teste de carga/latência real da chamada
  `apps/inbox → apps/api`** → Aceito nesta fatia; volume de cadastro de
  canais é baixo (operação administrativa, não caminho de execução de
  mensagens), sem necessidade concreta de otimizar agora.

## Migration Plan

Sem dado em produção para migrar — `apps/inbox` não tem persistência hoje.
Passos de rollout:
1. Aplicar a migration inicial (`Channel`) manualmente em cada ambiente,
   mesmo mecanismo já usado por `apps/api` (`dotnet ef database update`,
   nunca automático em runtime).
2. Configurar `Inbox:CredentialEncryptionKey` e `Api:BaseUrl` em cada
   ambiente antes do primeiro deploy — sem a chave configurada, o processo
   falha ao subir (mesmo comportamento de `AesGcmMcpCredentialCipher`
   quando `Mcp:CredentialEncryptionKey` está ausente).

Rollback: reverter o commit; sem dado em produção, sem estado persistido
incompatível entre versões.

## Open Questions

(nenhuma — a única incerteza técnica real desta change, criação automática
do segundo banco, foi verificada empiricamente durante o apply, ver
Decisão 1)
