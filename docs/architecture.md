# Arquitetura

Como o sistema é organizado, o que cada app faz, qual é o modelo de domínio e
quais regras de negócio governam cada decisão.

Este documento descreve o **estado atual** do sistema. Para as convenções de
código e as premissas de desenvolvimento, ver [conventions.md](conventions.md).
Para integrar um cliente externo via A2A, ver
[a2a-integration.md](a2a-integration.md).

---

## Índice

- [O que é](#o-que-é)
- [Os quatro apps](#os-quatro-apps)
- [Isolamento entre apps](#isolamento-entre-apps)
- [Modelo de domínio](#modelo-de-domínio)
- [Regras de negócio transversais](#regras-de-negócio-transversais)
- [Protocolo A2A](#protocolo-a2a)
- [Contexto do agente](#contexto-do-agente)
- [Contrato de plugin de canal](#contrato-de-plugin-de-canal)
- [Autenticação](#autenticação)
- [Fuso horário do sistema](#fuso-horário-do-sistema)

---

## O que é

Plataforma multi-agente construída sobre o protocolo
[A2A](https://a2a-protocol.org/latest/) (Agent-to-Agent). Permite cadastrar
agentes de IA, vinculá-los a ferramentas externas via MCP, configurar
delegação entre agentes, associar bases de conhecimento, e integrá-los a
canais de mensagem reais (WhatsApp via WAHA, Telegram) através de um CRM
mínimo de contatos e sessões.

```
   canal externo          apps/inbox              apps/api           apps/workers
  (WhatsApp/Telegram)
        │                     │                       │                    │
        │  webhook            │                       │                    │
        ├────────────────────▶│                       │                    │
        │                     │ debounce por sessão   │                    │
        │                     │ (buffer persistido)   │                    │
        │                     │                       │                    │
        │                     │  SendMessage (A2A)    │                    │
        │                     ├──────────────────────▶│                    │
        │                     │                       │  job (RabbitMQ)    │
        │                     │                       ├───────────────────▶│
        │                     │                       │                    │ chama o LLM
        │                     │                       │                    │ resolve MCP
        │                     │                       │                    │ delega
        │                     │                       │◀───────────────────┤ grava resultado
        │                     │  push notification    │                    │
        │                     │◀───────────────────────────────────────────┤
        │  resposta           │                       │                    │
        │◀────────────────────┤                       │                    │
```

`apps/frontend` é o painel de operação sobre `apps/api` e `apps/inbox`, fora
desse caminho de mensagem.

---

## Os quatro apps

| App | Papel | Stack | Banco |
|---|---|---|---|
| `apps/api` | CRUD de agentes, catálogo MCP, delegação, bases de conhecimento, protocolo A2A (`SendMessage`/`GetTask`), AgentCard, emissão de push notification, login do operador e emissão de token | .NET 10, ASP.NET Core Minimal API, CQRS via `Mediator`, EF Core + Npgsql, `RabbitMQ.Client` (publisher), pacote `A2A` | Postgres compartilhado com `apps/workers` |
| `apps/workers` | Executa tasks: chama o LLM, resolve tools MCP, executa delegação, dispara push notification | .NET 10 Worker Service, `Microsoft.Agents.AI` (`ChatClientAgent`, `Compaction`), EF Core espelhado, consumidor RabbitMQ | Mesmo Postgres de `apps/api` |
| `apps/inbox` | Catálogo de canais de entrada, CRM (`Contact`/`Session`), histórico de mensagens, orquestrador de debounce, adapters de canal | .NET 10 Minimal API, CQRS próprio, EF Core | Postgres **próprio** (`buteco_inbox`), isolado |
| `apps/frontend` | Painel de gestão: agentes, MCP, delegação, canais, bases de conhecimento, sessões e histórico de conversa | React 19, TypeScript, Vite, Mantine v9, `react-router`, `@tanstack/react-query`, Vitest + Testing Library | — |

`apps/api` **nunca chama o LLM**. Ele persiste a task e publica um job no
RabbitMQ; quem executa é `apps/workers`.

`apps/workers` compartilha o schema de `apps/api` — duas instâncias de
`AppDbContext` mantidas sincronizadas por disciplina, não por schema separado
— mas **nunca cria nem migra** esse schema.

---

## Isolamento entre apps

Nenhum `.csproj` ou arquivo do frontend referencia código de outro app. Não
existe `ProjectReference` cruzado entre `apps/api`, `apps/workers`,
`apps/inbox` e `apps/frontend`.

Referências entre domínios de apps diferentes são sempre validadas **via
HTTP autenticado**, nunca por chave estrangeira. `apps/inbox` valida o
`AgentId` de um canal chamando `GET /agents/{id}` em `apps/api`; não há FK
entre os dois bancos porque não há banco em comum.

Há exatamente três exceções, todas deliberadas:

- **`libs/ProviderCatalog`** — referenciada por `apps/api` e `apps/workers`
  (nunca um pelo outro). Carrega apenas a identidade de cada provedor de LLM
  e o nome da seção de configuração: o mínimo que os dois processos precisam
  concordar. Não é um mecanismo geral de código compartilhado.
- **`tests/CrossAppTaskStoreCompatibility.Tests`** — referencia `Buteco.Api`
  e `Buteco.Workers` ao mesmo tempo, só para verificar que as duas
  implementações de `ITaskStore` concordam no schema.
- **`tests/InboxOrchestratorRoundTrip.Tests`** — referencia os três apps
  backend, só para o teste de round-trip ponta a ponta do orquestrador.

Em nenhum dos dois casos de teste algum app referencia o projeto de teste de
volta, nem ele é publicado junto.

A régua para criar uma nova `libs/` está em [conventions.md](conventions.md).

---

## Modelo de domínio

### `Agent` (`apps/api`)

`Name`, `Instructions`, `IsActive`, `Provider` e `Model` (ambos nullable),
`Description` (nullable) e `Skills` (jsonb, `{ Name, Description? }`).

`Provider` e `Model` nulos significam que o agente **precisa de
reconfiguração** — é um estado legítimo, não um defeito de dados. Acontece
quando o provedor que o agente usava deixa de estar disponível.

Dois vínculos N:N:

- **`McpServers`**, via `AgentMcpServer`, com `AllowedTools` (jsonb) por
  vínculo — a seleção de tools é por par agente-servidor, não global.
- **`DelegatesTo`**, via `AgentDelegation`, **unidirecional**: A delegar
  para B não implica que B delegue para A.

### `McpServer` (`apps/api`)

`Name`, `Description`, `Url`, `AuthType` (`None` ou `BearerToken`) e
`EncryptedCredential` (AES-GCM).

### `KnowledgeBase` (`apps/api`)

`Name`, `Description`, `IsActive`.

`Description` **não é campo decorativo**: é o texto que vira a descrição da
tool exposta ao modelo, e é por ele que o modelo decide se a base é relevante
para a pergunta. Por isso é obrigatória e não vazia — ao contrário de
`McpServer.Description`.

### `KnowledgeDocument` (`apps/api`)

`KnowledgeBaseId`, `Title`, `SourceType`, `ExtractedText`,
`ContentLengthBytes`, `IndexingStatus`, `IndexedAt`, `FailureReason`,
`ContentRevision`.

Quatro pontos que não se deduzem lendo os campos:

- **`ContentLengthBytes` é coluna gerada pelo Postgres**
  (`GENERATED ALWAYS AS (octet_length("ExtractedText")) STORED`), nunca
  escrita pela aplicação. Está em bytes UTF-8, a mesma unidade do teto de
  1 MiB validado no cadastro, e mede o texto **já extraído** — a extração
  remove BOM e normaliza `CRLF`, então validar a entrada crua faria os dois
  números medirem coisas diferentes.
- **`SourceType` é string aberta**, não enum fechado. Identifica o extrator a
  aplicar, resolvido via DI keyed com checagem de integridade bidirecional no
  startup. Extensão de arquivo e `SourceType` são conceitos distintos. A
  extração **preserva a marcação** — é normalização, não conversão para texto
  puro.
- **`IndexingStatus` tem exatamente quatro valores** (`Pending`, `Indexing`,
  `Indexed`, `Failed`) e não ganha um valor para reindexação. A distinção
  entre "nunca indexado" e "há conteúdo indexado respondendo agora" é
  carregada por `IndexedAt` (nulo × preenchido), em qualquer dos quatro
  estados. Regra para a UI: informação derivada da indexação aparece sempre
  que `IndexedAt` não for nulo, e é **omitida** quando for — nunca zerada,
  o que afirmaria que a indexação rodou e não achou nada.
- **`ContentRevision` é coluna explícita, não `xmin`**, e incrementa apenas
  quando `ExtractedText` muda. O consumidor de indexação muta a própria linha
  ao transicionar de estado, e um token de linha invalidaria o próprio
  trabalho em curso.

### `Channel` (`apps/inbox`)

`ChannelType` (string aberta, validada em runtime contra os adapters
efetivamente registrados via DI — não enum fechado), `Name`,
`EncryptedCredentials` (AES-GCM, com chave própria de `apps/inbox`, de shape
opaco que varia por `ChannelType`), `AgentId` (Guid opaco, validado via HTTP
contra `apps/api`), `IsActive` e `WebhookUrl` (computada, nunca persistida).

### `Contact` e `Session` (`apps/inbox`)

`Contact` é identificado pelo par único `(ChannelId, ExternalId)`. O mesmo
`ExternalId` em canais diferentes gera `Contact`s distintos — **não há
unificação de identidade entre canais**.

`Contact` tem dois campos de origem externa com propósitos **opostos**:

| Campo | Comportamento |
|---|---|
| `Metadata` | congelado na criação |
| `DisplayName` | nullable, reescrito a cada mensagem de entrada (do `pushName` no WAHA, do `username`/`first_name` no Telegram) |

`Session` amarra várias conversas do mesmo `Contact` ao longo do tempo. A
fronteira é por **inatividade automática** (timeout configurável), sem
encerramento explícito. Ao expirar, a `Session` anterior recebe `ClosedAt`, e
um índice único parcial (`sessions."ContactId" WHERE "ClosedAt" IS NULL`)
garante no banco, no máximo, uma `Session` aberta por `Contact`.

### `PendingDispatch` (`apps/inbox`)

Buffer de debounce persistido por `Session`: agrupa mensagens antes de
disparar um `SendMessage` real contra `apps/api`. Concorrência otimista via
`xmin` do Postgres.

É **buffer, não histórico** — some quando o ciclo de disparo termina.

> **Área sensível.** A coleção de mensagens do `PendingDispatch` é owned/JSON
> e já produziu perda silenciosa de mensagem sob concorrência real (aliasing
> de change tracker do EF Core após re-leitura na mesma instância de
> `DbContext`). A distinção que importa é entre `OwnsMany().ToJson()`
> (snapshot estrutural por elemento) e `HasConversion` + `ValueComparer`
> (serializa o valor inteiro a cada `SaveChanges`, imune ao aliasing).
> Qualquer mudança que encoste nessa coleção, ou que releia a entidade depois
> de mutá-la, precisa de teste com concorrência de verdade — não caminho
> feliz.

### `Message` (`apps/inbox`)

Histórico durável por `Session`, em **tabela relacional própria,
deliberadamente separada do `PendingDispatch`** — não o estende nem toca sua
coleção owned/JSON, justamente para não reabrir a área sensível acima.

Guarda `Direction`, `Content`, `ContentType` (`Text`, `Image`, `Audio`,
`Document` — marcador de tipo; mídia binária não é persistida) e
`OccurredAt`. Conforme a direção:

- **entrada**: identificador externo da mensagem (com índice único parcial,
  para deduplicar webhook reentregue) e `DispatchStatus` (`Pending`,
  `Dispatching`, `Failed`, `Completed`). Esse status é espelhado dos pontos
  que mutam ou removem o `PendingDispatch` e **sobrevive à remoção dele** —
  é o que torna o silêncio de uma conversa legível na interface. `Failed`
  agrupa três causas distintas de "não haverá resposta" sob um valor só,
  por decisão.
- **saída**: `DeliveryStatus` (`Sent` ou `Failed`, com motivo) — sucesso ou
  falha do **envio ao provedor**, nunca recibo de entrega ou de leitura pelo
  destinatário final. Essa distinção é de contrato, não de interface: o
  painel renderiza um indicador só, jamais dois, porque o dado não existe.

Os quatro enums de `Message` atravessam a API como **string**, nunca como
inteiro ordinal.

---

## Regras de negócio transversais

### Exclusão: catálogo × conteúdo

O critério, aplicável a qualquer entidade futura:

> **Catálogo referenciado → `IsActive`. Conteúdo sem referência → exclusão
> real.**

Entidades de catálogo com vínculos apontando para elas usam soft delete por
`IsActive`, com o filtro aplicado no momento da resolução. Um `McpServer`
desativado continua referenciado por `AgentMcpServer`, e o histórico de
execuções que o usou precisa continuar fazendo sentido — apagá-lo quebraria
a leitura do passado.

`KnowledgeDocument` é a única entidade do repositório com **exclusão real**.
É conteúdo, nada aponta para ele além dos seus próprios fragmentos, e o caso
de uso concreto — o operador subiu o arquivo errado, ou um com dado que não
devia estar ali — é exatamente aquele em que "continua no banco, invisível" é
a resposta errada. Há também o custo medido: cerca de 60 MB de vetores por
7.500 fragmentos.

Duas consequências práticas:

- A FK de `KnowledgeDocument` para `KnowledgeBase` usa **`Restrict`**, não o
  `Cascade` default do EF Core. Hoje é inerte, já que a base não tem
  exclusão; a diferença é qual dos dois lados falha de forma segura se
  alguém adicionar exclusão de base — `Restrict` obriga a decidir o destino
  dos documentos em vez de apagá-los em silêncio.
- `DELETE` numa rota que não oferece o verbo responde **405**, não 404. A
  distinção entre "recurso inexistente" e "operação não oferecida" é
  informação.

### Credenciais

Toda credencial é **write-only**: nunca retornada em nenhuma resposta,
criptografada com AES-GCM, com chave por domínio via variável de ambiente. Na
edição vale o padrão "deixe em branco para manter a atual".

A chave de `apps/inbox` é própria e nunca é a mesma de MCP — são segredos de
domínios diferentes, cada um decifrável apenas pelo processo que o gerou. Ver
[configuration.md](configuration.md).

---

## Protocolo A2A

Cada agente expõe `GET /agents/{id}/.well-known/agent-card.json`, montado a
cada requisição a partir do estado atual, sem cache. As `Skills` do agente
são mapeadas para `AgentSkill` via slug determinístico com dedupe.

**Descoberta é pública, uso não.** O AgentCard continua acessível sem token,
mas declara `SecuritySchemes` e `SecurityRequirements` (HTTP Bearer) para o
endpoint A2A do agente — a exigência de credencial fica declarada de forma
compatível com a spec, não implícita.

Push notification (`pushNotificationConfig` no `SendMessage`) é suportada
ponta a ponta: `apps/workers` dispara o webhook ao concluir uma task, em
fire-and-forget, sem retry.

Os dois endereços do agente — endpoint de execução e card de descoberta —
saem também na resposta de `GET /agents/{id}` e da listagem, montados no
servidor por um ponto único que o próprio card consome. **Nunca são
construídos pelo consumidor** a partir de host mais identificador: a URL
pública é configuração do servidor, e o host de onde a página foi servida não
é o host público atrás de um proxy. Sem URL pública configurada, o bloco vem
ausente em vez de relativo.

O guia completo para clientes externos — métodos, enumeradores, estados da
task, erros do protocolo e recomendações de polling — está em
[a2a-integration.md](a2a-integration.md).

---

## Contexto do agente

`apps/workers` concatena às `Instructions` cadastradas do agente um ou mais
blocos de contexto, montados em memória a cada execução. Eles **nunca** são
persistidos em `Agent.Instructions` nem entram no histórico de conversa.

Dois blocos hoje, cada um uma função pura em arquivo próprio, com seu próprio
marcador delimitando "isto não é uma mensagem do usuário, não responda a ele
diretamente":

- **contexto temporal** — instante de processamento, instante da mensagem
  quando disponível, e a regra de precedência para expressões de tempo
  relativas;
- **contexto de canal** — tipo do canal e identificador do contato.

> **O que entra num bloco não é uma decisão só de utilidade — é uma decisão
> de risco.** Um valor **atribuído pelo provedor ou pelo adapter do canal**
> (como `Channel.ChannelType` ou `Contact.ExternalId`) não é a mesma
> categoria de dado que **texto livre digitado pelo usuário final** (como
> `Contact.DisplayName`, que por isso fica fora do prompt). O primeiro pode
> entrar num bloco de contexto sem abrir a classe de risco de injeção de
> prompt; o segundo abre. O marcador de "não é mensagem do usuário" é uma
> dica textual, não uma fronteira estrutural. A pergunta a fazer antes de
> adicionar qualquer valor novo é **de onde ele vem**, não só o que ele
> ajuda o agente a fazer.

---

## Contrato de plugin de canal

Três contratos **obrigatórios** em conjunto por `ChannelType`, resolvidos via
DI keyed:

| Contrato | Responsabilidade |
|---|---|
| `IChannelConfigValidator` | valida a credencial antes de criptografar |
| `IOutboundMessageSender` | entrega a resposta do agente ao canal |
| `IInboundWebhookHandler` | processa o webhook recebido em `POST /webhooks/{channelId}` (rota genérica única) |

E um contrato **opcional**, `IChannelWebhookProvisioner`, que configura o
webhook automaticamente do lado externo no momento do cadastro.

`ValidateChannelAdapterRegistrations` roda no startup e garante que os
contratos obrigatórios — e o opcional, quando presente — estejam completos
por tipo, derrubando o boot se não estiverem.

O contrato **não carrega status de entrega**: um `IOutboundMessageSender` que
retorna sem lançar é sucesso; exceção é falha. Foi isso que permitiu
persistir status de entrega sem tocar o contrato de plugin.

Adapters implementados:

| Adapter | Provisionamento de webhook | Autenticidade do webhook de entrada |
|---|---|---|
| **WAHA** (self-hosted, engine GOWS) | manual | não verificada — risco aceito, classificado explicitamente na allowlist de rotas anônimas |
| **Telegram** (Bot API) | automático via `setWebhook`, com `secret_token` gerado por canal | verificação nativa |

---

## Autenticação

**Token stateless assinado com HMAC**, sem biblioteca JWT e sem sessão em
banco. `apps/api` emite (login do operador e token de serviço); `apps/api` e
`apps/inbox` validam **localmente**, compartilhando apenas a chave de
assinatura via configuração. Não há chamada de rede entre os processos para
validar token — foi o que permitiu autenticar dois apps com bancos isolados
sem introduzir um store compartilhado.

- **Operador único**, com credencial via variável de ambiente (usuário mais
  hash PBKDF2), por `POST /auth/login` em `apps/api`. Sem tabela de usuários
  e sem RBAC. TTL de 30 minutos por padrão, configurável — o default vive no
  tipo de Options, não no `appsettings.json`.
- **Token de serviço** (`apps/inbox` → `apps/api`): mesmo mecanismo de
  assinatura, `sub` distinto, assinado a cada requisição de saída com TTL
  fixo curto, e **escopado** — só autoriza as rotas que `apps/inbox` de fato
  consome; qualquer outra responde `403`.
- **Enforcement por padrão**: toda rota HTTP de `apps/api` e `apps/inbox`
  exige token. Uma rota só fica anônima com `.AllowAnonymous()` **e** um
  `AnonymousRouteClassification` (motivo documentado) anexados explicitamente
  no `Map*` correspondente.
  `RouteAuthenticationExtensions.ValidateRouteAuthenticationClassification`,
  chamada no fim de cada `Program.cs`, derruba o boot se alguma rota ficar
  sem essa dupla marcação — ou se a allowlist citar uma rota que não existe
  mais.
- **Frontend**: módulo fino de token sobre `sessionStorage` (ler, anexar,
  limpar em `401`), importado por cada `request<T>` de feature — sem cliente
  HTTP compartilhado.

Rotas anônimas são decisão de segurança, não detalhe de implementação:
adicionar uma passa por revisão.

---

## Fuso horário do sistema

Fuso e idioma usados por `apps/workers` para renderizar data e hora são
decisão de sistema, não de agente: `TZ` do sistema operacional, único para o
processo inteiro, resolvido no startup. Sem opção por agente e sem chave em
`appsettings.json`.

`TZ` deve usar o nome IANA canônico da tz database (por exemplo
`America/Sao_Paulo`), **sem o prefixo POSIX `:`** — o .NET resolve o fuso
corretamente com o prefixo, mas o remove do identificador que expõe, o que
quebraria a checagem mesmo com a configuração correta.

`ValidateTimeZoneConfiguration`, chamada no startup, compara o fuso
**efetivamente resolvido** contra o valor declarado em `TZ` — não apenas a
presença da variável — e falha a inicialização se não baterem, cobrindo `TZ`
ausente, vazia ou inválida com o mesmo erro.

Dia da semana em texto renderizado pelo worker é sempre em pt-BR, pelo mesmo
motivo — nunca herdado do `CurrentCulture` do host.
