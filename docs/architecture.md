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
- [Os cinco apps](#os-cinco-apps)
- [Isolamento entre apps](#isolamento-entre-apps)
- [Modelo de domínio](#modelo-de-domínio)
- [Regras de negócio transversais](#regras-de-negócio-transversais)
- [Namespace de tools do agente](#namespace-de-tools-do-agente)
- [Protocolo A2A](#protocolo-a2a)
- [Contexto do agente](#contexto-do-agente)
- [Histórico de conversa e compactação](#histórico-de-conversa-e-compactação)
- [Contrato de plugin de canal](#contrato-de-plugin-de-canal)
- [Contrato de conector](#contrato-de-conector)
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
desse caminho de mensagem. `apps/connectors` também fica fora dele: é quem fala com
provedores de arquivos (Google Drive) para as bases de conhecimento.

---

## Os cinco apps

| App | Papel | Stack | Banco |
|---|---|---|---|
| `apps/api` | CRUD de agentes, catálogo MCP, delegação, bases de conhecimento, protocolo A2A (`SendMessage`/`GetTask`), AgentCard, emissão de push notification, login do operador e emissão de token | .NET 10, ASP.NET Core Minimal API, CQRS via `Mediator`, EF Core + Npgsql, `RabbitMQ.Client` (publisher), pacote `A2A` | Postgres compartilhado com `apps/workers` |
| `apps/workers` | Executa tasks: chama o LLM, resolve tools MCP, executa delegação, dispara push notification | .NET 10 Worker Service, `Microsoft.Agents.AI` (`ChatClientAgent`, `Compaction`), EF Core espelhado, consumidor RabbitMQ | Mesmo Postgres de `apps/api` |
| `apps/inbox` | Catálogo de canais de entrada, CRM (`Contact`/`Session`), histórico de mensagens, orquestrador de debounce, adapters de canal | .NET 10 Minimal API, CQRS próprio, EF Core | Postgres **próprio** (`buteco_inbox`), isolado |
| `apps/connectors` | Conectores de provedor de arquivos para bases de conhecimento: navegação de pastas, descrição de pasta, listagem da raiz e markdown de arquivo. Hoje, Google Drive por service account | .NET 10 Minimal API, chamadas REST ao Google sem biblioteca do provedor | — |
| `apps/frontend` | Painel de gestão: agentes, MCP, delegação, canais, bases de conhecimento, sessões e histórico de conversa | React 19, TypeScript, Vite, Mantine v9, `react-router`, `@tanstack/react-query`, Vitest + Testing Library | — |

`apps/api` **nunca chama o LLM**. Ele persiste a task e publica um job no
RabbitMQ; quem executa é `apps/workers`.

`apps/workers` compartilha o schema de `apps/api` — duas instâncias de
`AppDbContext` mantidas sincronizadas por disciplina, não por schema separado
— mas **nunca cria nem migra** esse schema.

O client de LLM de `apps/workers` é **uma instância viva por par
`(provider, model)`**, resolvida na primeira mensagem que precisa dela e
reutilizada pelo resto da vida do processo — nunca construída por mensagem. O
motivo é concreto: os SDKs de Gemini e de Anthropic instanciam um `HttpClient`
próprio por client, então construir por mensagem vazava um pool de conexões por
mensagem (o SDK da OpenAI não, porque usa um `HttpClient` estático
compartilhado — foi essa assimetria que escondeu o defeito, já que a indexação
de conhecimento só usa OpenAI). A chave é `(provider, model)` e **só é
suficiente porque a credencial é por processo, não por agente**; credencial por
agente obrigaria a mudar a chave. **Rotação de chave de provedor exige reiniciar
o processo** — ver [configuration.md](configuration.md).

---

## Isolamento entre apps

Nenhum `.csproj` ou arquivo do frontend referencia código de outro app. Não
existe `ProjectReference` cruzado entre `apps/api`, `apps/workers`,
`apps/inbox`, `apps/connectors` e `apps/frontend`.

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
- **`tests/ApiConnectorsRoundTrip.Tests`** — referencia `Buteco.Api` e
  `Buteco.Connectors`, só para provar a validação de pasta pela chamada real entre
  os dois, com o conector falso do `apps/connectors` incluído por link.

Em nenhum dos três casos de teste algum app referencia o projeto de teste de
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

`Name`, `Description`, `IsActive`, `ContentMode` (`Manual` ou `Synced`), a origem
(`SyncProvider`, `SyncFolderId`, `SyncFolderName`, `SyncFolderUrl`) e o estado da
sincronização (`LastSyncCompletedAt`, `LastSyncFinishedAt`, `LastSyncErrorCode`,
`LastSyncErrorDetail`, `SyncFailingSince`, `SyncIgnoredFiles`).

`Description` **não é campo decorativo**: é o texto que vira a descrição da
tool exposta ao modelo, e é por ele que o modelo decide se a base é relevante
para a pergunta. Por isso é obrigatória e não vazia — ao contrário de
`McpServer.Description`.

O que não se deduz lendo os campos da sincronização:

- **`ContentMode` é fechado e imutável; o provedor é string aberta.** O tipo tem
  comportamento no `apps/api` (em base `Synced`, só o subject de serviço escreve
  documento, e o operador recebe `409`); o provedor pertence ao app que sincroniza.
  Tipo, provedor e id da pasta nunca mudam depois do cadastro. Nome e URL da pasta
  são snapshot da última sincronização concluída: o operador não os altera, e só a
  gravação de um ciclo bem-sucedido os atualiza. Base manual tem tudo isso nulo, e
  `CHECK`s garantem as combinações.
- **Uma pasta pertence a uma base só, inclusive inativa.** Índice único parcial em
  `(SyncProvider, SyncFolderId)`, sem filtro por `IsActive`, porque base inativa
  continua sendo sincronizada. O id é comparado como veio.
- **Motivos são códigos, nunca frases.** `LastSyncErrorCode` e o `code` de cada
  item de `SyncIgnoredFiles` (`jsonb`) seguem a forma `access-denied`; o texto
  exibido é do frontend.
- **"Falhando desde" é derivado da transição.** Preenchido na primeira falha depois
  de um sucesso, mantido nas seguintes, limpo no sucesso; a falha nunca apaga a
  última concluída. `LastSyncFinishedAt` muda em todo ciclo, sucesso ou falha, e é
  o que o polling da tela acompanha.
- **Na resposta, base manual tem `syncSource` e `syncState` nulos**, e base
  sincronizada que nunca terminou um ciclo tem `ignoredFiles: null`, não lista
  vazia: nenhuma listagem aconteceu.
- **Nesta etapa nenhuma rota do operador cria base `Synced`.** O cadastro recusa
  `contentMode: "Synced"` com `400`; a criação, com a pasta validada pelo app que
  acessa o provedor, é a etapa seguinte da linha.

### `KnowledgeDocument` (`apps/api`)

`KnowledgeBaseId`, `Title`, `SourceType`, `ExtractedText`,
`ContentLengthBytes`, `IndexingStatus`, `IndexedAt`, `FailureReason`,
`ContentRevision`, `ContentHash`, `FragmentCount`, `IndexingAttempts`,
`LastAttemptAt`, `KnowledgeBaseContentMode`, `ExternalRef`, `ExternalVersion`.

Os quatro últimos são escritos pelo consumidor de indexação de `apps/workers`,
nunca por `apps/api` — com **uma** exceção, a rota de reindexação, que limpa
`FailureReason`, `IndexingAttempts` e `LastAttemptAt` ao abrir uma rodada nova.

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

  A consequência concreta dessa regra, e ela **não** é "omitir quando o
  documento não está `Indexed`": documento que falhou depois de ter sido
  indexado, ou que está sendo reindexado, continua exibindo a **contagem
  anterior**, porque os fragmentos antigos continuam vivos no índice e
  respondendo às consultas — `FailAsync` não toca `IndexedAt` nem
  `FragmentCount`, e a exclusão dos antigos só acontece dentro da transação de
  sucesso. Quem separa os casos é `IndexedAt`, nunca o estado.
- **`ContentRevision` é coluna explícita, não `xmin`**, e incrementa apenas
  quando `ExtractedText` muda. O consumidor de indexação muta a própria linha
  ao transicionar de estado, e um token de linha invalidaria o próprio
  trabalho em curso.
- **`ExternalRef` existe se e somente se a base é `Synced`, e o banco garante.**
  A regra cruza tabelas, então o documento carrega `KnowledgeBaseContentMode`, uma
  cópia do tipo da base, e a FK para a base é **composta**
  (`KnowledgeBaseId`, `KnowledgeBaseContentMode`) → (`Id`, `ContentMode`), em
  `Restrict`. Com a cópia amarrada à base, uma `CHECK` na própria tabela amarra a
  cópia à referência. A cópia não envelhece porque o tipo é imutável. `ExternalRef`
  é único por base e comparado como veio.
- **`ExternalVersion` é opaco.** É o marcador de mudança do provedor
  (`modifiedTime` de Google Doc, `md5Checksum` de `.md`), gravado como veio e nunca
  interpretado pelo `apps/api`. Quem decide se o documento mudou continua sendo o
  texto extraído e o título: um upsert com o marcador novo e o mesmo texto e título
  grava só o marcador, sem evento, sem indexação e sem tocar `UpdatedAt`.

### `KnowledgeIndexingRequest` (`apps/api`)

`KnowledgeDocumentId`, `ContentRevision`, `CreatedAt`, na tabela
`knowledge_indexing_requests`. É o pedido de indexação que ainda não chegou à fila
(change `indexacao-sem-job-orfao`, #138): toda escrita que pede indexação o grava no
mesmo `SaveChanges` do documento, e o despacho o apaga depois de o broker confirmar a
mensagem. Só o `apps/api` lê e escreve; o `apps/workers` não espelha a tabela.

- **Existe porque `Pending` não diz se há mensagem na fila.** Documento esperando na
  fila e documento cuja publicação falhou eram a mesma linha. O pedido é o registro
  do que falta entregar.
- **FK em cascata para o documento**: excluir documento, ou base, leva os pedidos
  junto.
- **O despacho é o único dependente do publisher de indexação no `apps/api`**, e roda
  no fim de cada escrita e numa varredura de 30 s
  (`KnowledgeIndexingRequestSweepService`, o primeiro `BackgroundService` deste app),
  com `FOR UPDATE SKIP LOCKED`, seguro com várias instâncias. Com o broker fora do ar
  a escrita responde o sucesso de sempre e o documento fica `Pending` até a varredura
  publicar.

### `KnowledgeDocumentEvent` (`apps/api`)

`KnowledgeBaseId`, `DocumentId`, `DocumentTitle`, `Type` (`Created`,
`Updated`, `Deleted`), `ContentChanged`, `TitleChanged`, `Author`,
`OccurredAt`. É o histórico de mudanças nos documentos de uma base, lido por
`GET /knowledge-bases/{id}/document-events`, paginado por cursor, do mais
recente para o mais antigo.

- **Gravado por `apps/api` no mesmo `SaveChangesAsync` da escrita do
  documento**: escrita recusada não deixa evento, e evento sem escrita não
  existe. Reindexar não gera evento, e as transições de indexação de
  `apps/workers` também não.
- **`Updated` só quando o texto extraído ou o título mudou de fato.** O
  critério de conteúdo é o mesmo que incrementa `ContentRevision`, e **não** o
  `ContentHash`: numa linha anterior ao hash, reenviar o mesmo texto volta o
  documento para a fila de indexação, mas não é mudança do documento.
  `ContentChanged` e `TitleChanged` só existem em `Updated` e são nulos nos
  outros tipos, com o formato garantido por uma `CHECK` no banco.
- **FK só para a base, em cascata, e nenhuma para o documento.** O evento de
  exclusão sobrevive ao documento, por isso `DocumentId` é coluna solta e
  `DocumentTitle` é snapshot. Os eventos morrem com a base, pela cascata, que a
  exclusão de base alcança desde a #108.
- **`Author` é o subject do token que escreveu, gravado como veio**
  (`operator`, ou `service:connectors` nas escritas da sincronização). O rótulo de
  apresentação é do frontend.
- **Sem eventos retroativos e sem retenção.** O histórico começa vazio na
  implantação, e o gatilho para rever o crescimento está numa issue com o
  rótulo `aguardando gatilho`.

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

`KnowledgeDocument` foi a primeira entidade do repositório com **exclusão real**;
`KnowledgeBase` é a segunda, como exceção (abaixo).
É conteúdo, nada aponta para ele além dos seus próprios fragmentos, e o caso
de uso concreto — o operador subiu o arquivo errado, ou um com dado que não
devia estar ali — é exatamente aquele em que "continua no banco, invisível" é
a resposta errada. Há também o custo medido: cerca de 60 MB de vetores por
7.500 fragmentos **em 1536 dimensões** — com as 4096 dimensões do modelo
recomendado pela etapa `0b`, são cerca de 123 MB para os mesmos 7.500
fragmentos.

> Esses 7.500 são volume de referência **de disco**. Como volume de **latência**
> de consulta eles já passam do teto: a busca exata custa ~47 µs por fragmento
> da base consultada, então 7.500 fragmentos numa mesma base dão ~333 ms, contra
> o teto de 200 ms que dispara a criação do índice ANN. O volume de latência é
> **~4.300 fragmentos na maior base** — e é a maior base que conta, porque a
> consulta filtra por `KnowledgeBaseId`.

Duas consequências práticas:

- A FK de `KnowledgeDocument` para `KnowledgeBase` usa **`Restrict`**, não o
  `Cascade` default do EF Core. A diferença é qual dos dois lados falha de
  forma segura quando alguém adiciona exclusão de base — `Restrict` obriga a
  decidir o destino dos documentos em vez de apagá-los em silêncio. A #108
  decidiu (abaixo), e a FK continua `Restrict`.
- `DELETE` numa rota que não oferece o verbo responde **405**, não 404. A
  distinção entre "recurso inexistente" e "operação não oferecida" é
  informação.

### A exceção: exclusão de base de conhecimento

`DELETE /knowledge-bases/{id}` existe desde a #108, apesar de a base ter
vínculos de agente apontando para ela. O critério acima ganha uma segunda
pergunta: **alguém lê o passado por esta entidade, e ela retém um recurso
exclusivo?** Ninguém lê o passado pela base (métricas de indexação e de
embedding não têm FK para ela; o histórico de documentos é dela e morre com
ela), e uma base sincronizada retém a pasta, que é única e imutável — sem
exclusão, uma pasta perdida ficava presa para sempre. `Agent`, `McpServer` e
`Channel` têm passado lido pelo id e **continuam só se desativando**.

- **Só base inativa**; ativa responde `409` com `code: "knowledge-base-active"`.
  A perda de conhecimento acontece na desativação, que é reversível.
- **Tudo da base vai junto, numa transação:** `FOR UPDATE` na base, os
  documentos apagados pela aplicação (fragmentos pela cascata), e a base
  (eventos e vínculos pelas cascatas). As métricas ficam.
- **A FK documento → base continua `Restrict`**, como rede para qualquer outro
  caminho que esqueça os documentos.
- **Escrita de `/sync` que encontra a base excluída no meio responde `404`**,
  nunca `500`; o app que sincroniza lê o `404` como fim do ciclo daquela base.

### Credenciais

Toda credencial é **write-only**: nunca retornada em nenhuma resposta,
criptografada com AES-GCM, com chave por domínio via variável de ambiente. Na
edição vale o padrão "deixe em branco para manter a atual".

A chave de `apps/inbox` é própria e nunca é a mesma de MCP — são segredos de
domínios diferentes, cada um decifrável apenas pelo processo que o gerou. Ver
[configuration.md](configuration.md).

---

## Namespace de tools do agente

O conjunto de tools entregue ao LLM numa execução é a **união de três
conjuntos resolvidos separadamente**: as tools MCP (dos `McpServer`
vinculados, filtradas por `AllowedTools`), as tools de delegação (uma por
`AgentDelegation`) e as tools de conhecimento (uma por `KnowledgeBase`
vinculada **e ativa**). Os três dividem um único espaço de nome, e é
`apps/workers` que garante a unicidade no ponto que os une — não cada resolvedor
por si, porque nenhum deles sabe o que os outros produziram.

```
   tools MCP resolvidas       tools de delegação        tools de conhecimento
   (McpServer +               (uma por                  (uma por KnowledgeBase
    AllowedTools)              AgentDelegation)          vinculada e ativa)
            │                        │                          │
            └────────────────────────┼──────────────────────────┘
                                     ▼
                          ToolNameDeduplicator     ← único ponto que sabe
                                     │                que os conjuntos dividem
                                     ▼                namespace
                    conjunto final entregue ao LLM
```

### Regras

- **Colisão é resolvida renomeando, nunca descartando.** As duas tools
  permanecem no conjunto e permanecem chamáveis de forma independente. Do
  lado MCP a renomeação usa `WithName`, que troca só o nome exposto e mantém
  a chamada remota usando o nome de protocolo original; do lado da delegação
  a função é encapsulada, não reconstruída.
- **Precedência declarada**, da mais forte para a mais fraca: **MCP →
  delegação → conhecimento**. A tool do conjunto mais forte mantém o nome
  pretendido e a do mais fraco é a renomeada. Dentro de um mesmo conjunto, a
  primeira na ordem de resolução mantém o nome. Isso é propriedade do
  deduplicador, não consequência da ordem de concatenação. Conhecimento é o
  último porque é o conjunto mais novo e o que tem menos nome em uso — renomear
  uma tool de conhecimento é a mudança que quebra menos.
- **Limite de 64 caracteres**, com o alfabeto `[a-zA-Z0-9_-]`. A garantia vale
  **depois** do sufixo de dedupe: um sufixo aplicado a um nome já no limite
  encurta a base para caber, nunca ultrapassa. A ordem de operações é fixa —
  sanitizar, truncar, deduplicar.
- **Comparação sensível a caixa** (`StringComparison.Ordinal`). `Search` e
  `search` não colidem, porque é assim que o runtime que resolve a chamada
  também compara.
- **Conjunto estável entre execuções**: o mesmo cadastro produz sempre os
  mesmos nomes, incluindo quais tools foram renomeadas e para quais nomes. A
  consulta de `AgentMcpServer` é ordenada explicitamente por `McpServerId` —
  por identificador e não por nome, porque o nome é editável e renomear um
  servidor não deve reordenar o conjunto.
- **Toda renomeação emite aviso no log**, com o agente, o nome pretendido, o
  nome final e a origem de cada lado. É `Warning`, não `Error`: a execução
  segue correta e completa.

> **Por que a origem do limite importa.** Os 64 caracteres vêm de
> `FunctionObject.name` na especificação OpenAPI do OpenAI, superfície **Chat
> Completions** — a que `apps/workers` usa. O Gemini declara 128. O Anthropic
> não publica o seu em nenhuma fonte primária, então o limite **não** é "o
> mínimo entre os três provedores": está verificado contra dois. A restrição
> de caractere inicial que o código aplica não vem de provedor nenhum — é
> escolha do repositório, mantida para não alterar nomes já expostos.

O caminho de colisão realmente alcançável é **intra-MCP**, não MCP contra
delegação: dois `McpServer.Name` que diferem só em pontuação sanitizam para a
mesma cadeia (`Zendesk MCP` e `Zendesk.MCP`), e dois nomes longos com o mesmo
prefixo de 64 colidem na truncagem.

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

## Histórico de conversa e compactação

Uma conversa é uma sequência de tasks A2A com o mesmo `contextId`. O histórico
**não** é remontado a partir das mensagens: `apps/workers` persiste a sessão do
agente serializada em `AgentTask.Metadata`, sob a chave `conversationSession`,
e a próxima execução do mesmo `contextId` carrega a sessão da task `completed`
mais recente. Tasks que terminaram `failed` ou `rejected` **não** entram — o
conteúdo de um turno que não deu certo não contamina o seguinte.

A sessão é gravada como **string JSON escapada**, não como estrutura aninhada.
O `jsonb` do Postgres normaliza a ordem das propriedades, e o polimorfismo de
`AIContent` exige `"$type"` como **primeira** propriedade do objeto: guardada
como estrutura, a sessão deixaria de ser desserializável assim que houvesse mais
de uma persistida.

**Dois limites, com papéis diferentes:**

| mecanismo | valor | papel |
|---|---|---|
| truncamento por número de mensagens | 200 | **teto de segurança** — degrada para descarte puro, e a essa altura já invalida o bookkeeping incremental da compactação |
| gatilho de compactação | 10 turnos de usuário | **faixa de operação** — resume a porção mais antiga em vez de descartá-la |

A compactação é incremental: cada resumo novo parte do resumo anterior somado
aos turnos desde então, preservando os grupos mais recentes crus. Ela roda
**dentro** do turno do usuário, como uma requisição a mais ao provedor — por
isso aparece em `provider_calls` com finalidade própria (`Compaction`), separada
das requisições do turno.

### A requisição de resumo não pode terminar em turno de modelo

O pacote de compactação monta a requisição de resumo terminando **sempre** em
mensagem de assistente: a contagem de turnos só cai quando o grupo de assistente
do turno também sai, então o laço de exclusão para logo depois dele. **O Gemini
recusa essa forma** com `400 "Requests ending with a model turn are not
supported."`; OpenAI e Anthropic aceitam.

Por isso `apps/workers` acrescenta uma **mensagem final de usuário** à requisição
de resumo quando a porção resumida termina em assistente — sem alterar papel,
conteúdo ou ordem do histórico. O texto acrescentado é uma **afirmação de
fronteira**, não um segundo comando de resumir: quem comanda é a instrução de
sistema do pacote, e dois comandos concorrentes produzem resumo pior que um.

> Este é um defeito do par **(payload, provedor)**, não da compactação. Foi
> medido em produção antes de ser corrigido: enquanto durou, **toda** conversa
> acima de dez turnos num agente Gemini seguia com o histórico cru, crescendo
> ~300 a 500 tokens por turno, sem teto.

### Falha de resumo não derruba o turno — mas aparece

Uma chamada de resumo que falha é capturada pela estratégia do pacote, que
restaura os grupos e segue sem compactar: o turno do usuário chega a `completed`
do mesmo jeito. Essa escolha continua valendo — **o que mudou é que a falha
deixou de ser silenciosa**. O provider de compactação recebe o `ILoggerFactory`
do host, então o aviso é registrado; sem ele, o aviso ia para um logger nulo, e
uma compactação quebrada era indistinguível de uma compactação que nunca
disparou.

A mesma leitura vale pelo dado, sem depender de log: em `provider_calls`, linhas
com finalidade `Compaction` e `Failed = true` são compactação quebrada, e a
entrada por turno crescendo sem parar é o sintoma que o usuário sente primeiro —
custo e latência subindo a cada mensagem da mesma conversa.

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
| **WAHA** (self-hosted, engine GOWS) | manual | não verificada — risco aceito, classificado explicitamente na allowlist de rotas anônimas (premissa alterada, ver nota abaixo) |
| **Telegram** (Bot API) | automático via `setWebhook`, com `secret_token` gerado por canal | verificação nativa |

*(Premissa alterada em 19/09/2026: o aceite do WAHA foi feito antes de haver
deploy atendendo tráfego real, e hoje vale contra um piloto em produção. A
decisão não foi revista aqui; está registrada como candidata em
[`02-HISTORICO_E_STATUS.md`](../02-HISTORICO_E_STATUS.md).)*

---

## Contrato de conector

**Fronteira entre os dois apps que falam com sistemas externos:** canal de conversa
(entrada e saída de mensagens) pertence ao `apps/inbox`; provedor de arquivos para
base de conhecimento pertence ao `apps/connectors`.

Cada provedor é registrado no `apps/connectors` sob uma chave (`google-drive`) com
três registros obrigatórios:

| Registro | Responsabilidade |
|---|---|
| `IFolderNavigator` (keyed) | navegar (o nível de cima do que a conta enxerga, ou as subpastas de uma pasta) e descrever uma pasta, verificando o acesso |
| `IFolderContentSource` (keyed) | listar a raiz de uma pasta, separando arquivos suportados de ignorados (com o motivo), e entregar o markdown de um arquivo |
| `ConnectorAccount` | a chave e o e-mail da conta com que o provedor acessa |

`ValidateConnectorRegistrations` roda no startup e derruba o boot se uma chave tiver
um registro sem os outros dois. Toda falha de operação sai como **código** estável
(`access-denied`, `api-not-configured`, `rate-limited`, ...), nunca como frase: o
texto exibido é do frontend. A listagem da raiz é completa ou falha, e toda operação
sobre uma pasta lê a pasta antes, porque uma consulta numa pasta sem acesso devolve
lista vazia em vez de erro.

O conector **Google Drive** só é registrado quando a chave da service account está
configurada. Ele decide o tipo de cada arquivo pelo `mimeType`, nunca pela extensão;
exporta Google Doc como markdown e retira as imagens embutidas em base64; baixa `.md`
sem transformar; e ignora atalho, subpasta, tipo não suportado e arquivo com download
bloqueado para leitores, cada um com o seu código. Erros do Google são distinguidos
pelo `reason`, não pelo status. Um conector falso existe só nos testes.

### O `apps/api` como cliente da descrição de pasta

A base sincronizada nasce por `POST /knowledge-bases` com `contentMode: "Synced"`,
`provider` e `folderId`. O `apps/api` valida a pasta chamando
`GET /connectors/providers/{provider}/folder?id=` e grava **o nome e a URL que vêm
da resposta**, nunca os do corpo enviado pelo painel.

- **`Connectors:BaseUrl` é opcional.** Sem ele o `apps/api` sobe, nenhuma outra
  rota muda, e o cadastro sincronizado responde `503` com o código
  `connectors-not-configured`. Presente e inválido, o boot falha.
- **Código, não frase.** A falha do `apps/connectors` chega ao cliente com o
  `code` e o `detail` que ele deu, e com o status por natureza dele (só `404`
  `provider-not-configured` vira `422`). Os códigos próprios do `apps/api` são
  `connectors-unavailable` (`503`: sem resposta) e `connectors-error` (`502`:
  resposta fora do contrato, inclusive `401` e `403` do outro app, que nunca são
  repassados com o mesmo status para o painel não deslogar o operador).
- **Pasta em uso** responde `409` com o código `folder-in-use` e a base que já a usa,
  ativa ou inativa, antes de chamar o `apps/connectors` e também na corrida entre
  dois cadastros, pelo índice único da pasta.
- **Cadeia de limites de tempo, amarrada entre três lugares:** 30 s por chamada de
  metadado no `apps/connectors` (`GoogleDriveHttp.MetadataTimeout`) < **35 s** no
  `HttpClient` do `apps/api` (`ConnectorsFolderClient.Timeout`) < 60 s, o
  `proxy_read_timeout` padrão do nginx do painel, que não fixa a diretiva. Assim o
  código mais preciso do `apps/connectors` chega ao painel, e o painel recebe o
  código do `apps/api` e não um `504` do proxy. Mudar um dos três exige rever os
  outros.

A chamada real entre os dois apps é provada por `tests/ApiConnectorsRoundTrip.Tests`.
Change `criacao-base-sincronizada` (#104).

### O ciclo de sincronização (`apps/connectors` → `apps/api`)

O `apps/connectors` mantém cada base `Synced` igual à raiz da pasta, inclusive as
inativas, uma base por vez: logo depois do boot e a cada 5 minutos, e na hora pelo
"Sincronizar agora" (`POST /connectors/knowledge-bases/{id}/sync`, só do operador,
`202`). Escreve no `apps/api` pelas rotas de `/sync`, assinando `service:connectors`;
o `apps/api` continua sendo o único dono da escrita de documentos.

- **Ordem:** descrever a pasta, listar a raiz inteira, ler referências e marcadores,
  enviar só o que mudou de marcador, excluir o que sumiu, gravar o desfecho.
- **Nunca exclui por leitura incompleta:** só o que sumiu da listagem (suportados e
  ignorados), só com a pasta descrita, a listagem completa e os upserts terminados.
  Arquivo ignorado que continua na pasta mantém o documento.
- **Falha de arquivo é ignorado com código; falha de base é `Failed`; cota
  (`rate-limited`) grava `Failed` e encerra a rodada.** `404` na base encerra só ela.
- **Recusa de conteúdo determinística (#120) fica em memória** com o marcador, e não é
  baixada de novo enquanto o marcador não mudar.
- **`Api:BaseUrl` é opcional:** sem ela o ciclo não roda e o resto do app não muda.
- **Uma instância só:** o lock por base e a memória de recusas são do processo.

Os cenários no sentido novo também estão em `tests/ApiConnectorsRoundTrip.Tests`.
Change `ciclo-de-sincronizacao` (#105).

---

## Autenticação

**Token stateless assinado com HMAC**, sem biblioteca JWT e sem sessão em
banco. `apps/api` emite o token do operador, no login; cada serviço assina
o **próprio** token de serviço com a mesma chave, a cada requisição de saída —
o `apps/inbox` com `service:inbox` em
`apps/inbox/src/Buteco.Inbox/Auth/ServiceTokenDelegatingHandler.cs`, e o
`apps/api` com `service:api` em
`apps/api/src/Buteco.Api/Auth/ServiceTokenDelegatingHandler.cs`, só para chamar o
`apps/connectors`, e o `apps/connectors` com `service:connectors` em
`apps/connectors/src/Buteco.Connectors/Auth/ServiceTokenDelegatingHandler.cs`, só para
o ciclo de sincronização chamar o `apps/api`. Os três handlers são cópias com o subject
trocado, sem `libs/`. `apps/api` e `apps/inbox` validam **localmente**,
compartilhando apenas a chave de assinatura via configuração. Não há chamada de rede entre os processos para
validar token — foi o que permitiu autenticar dois apps com bancos isolados
sem introduzir um store compartilhado.

- **Operador único**, com credencial via variável de ambiente (usuário mais
  hash PBKDF2), por `POST /auth/login` em `apps/api`. Sem tabela de usuários
  e sem RBAC. TTL de 30 minutos por padrão, configurável — o default vive no
  tipo de Options, não no `appsettings.json`.
- **Tokens de serviço**, assinados pelo próprio serviço a cada requisição de
  saída, com `sub` distinto e TTL fixo curto (5 min), e **escopados** no
  `apps/api` por uma tabela de subjects (`ServiceScopeAuthorizationHandler`):
  - `operator` passa em todas as rotas;
  - `service:inbox` (`apps/inbox` → `apps/api`) só nas duas rotas que consome;
  - `service:connectors` só nas cinco rotas de `/sync/knowledge-bases`
    (listagem das bases sincronizadas e das referências dos documentos, upsert e
    exclusão por `ExternalRef`, gravação do resultado de um ciclo). O app que
    sincroniza vai obter o token como o `apps/inbox`: recebe a mesma
    `Auth__TokenSigningKey` e assina com um `DelegatingHandler` próprio, sem
    `libs/`;
  - **qualquer outro subject recebe `403`**, mesmo com assinatura válida. Até a
    linha de bases sincronizadas a regra era o inverso, e um subject novo assinado
    com a chave tinha o acesso do operador.

  A lista de rotas de cada serviço é conferida no boot contra os endpoints
  mapeados (casa por método e padrão de rota). Quem guarda a chave assina qualquer
  subject, inclusive `operator`: a tabela protege contra defeito de código, não
  contra o comprometimento de um app que guarda a chave (issue #117).

  O `apps/inbox` aceita **só o subject `operator`** nas rotas autenticadas;
  qualquer outro subject válido recebe `403`, inclusive o `service:inbox` que ele
  mesmo assina para chamar o `apps/api` (#116). Nenhum serviço chama rota
  autenticada dele, então ainda não há lista de rotas por subject de serviço.
  **Um serviço que precise de rota do `apps/inbox` traz a lista de rotas do seu
  subject e a checagem de boot dessa lista, no molde do `apps/api`.** A regra é
  duplicada, sem `libs/`: `Auth/SubjectAuthorization.cs` no `apps/inbox`, com o
  par em `Auth/ServiceScopeAuthorizationHandler.cs` e
  `Auth/ServiceScopeRouteValidation.cs` no `apps/api`.
- **Enforcement por padrão**: toda rota HTTP de `apps/api` e `apps/inbox`
  exige token. Uma rota só fica anônima com `.AllowAnonymous()` **e** um
  `AnonymousRouteClassification` (motivo documentado) anexados explicitamente
  no `Map*` correspondente.
  `RouteAuthenticationExtensions.ValidateRouteAuthenticationClassification`,
  chamada no fim de cada `Program.cs`, derruba o boot se alguma rota ficar
  sem essa dupla marcação — ou se a allowlist citar uma rota que não existe
  mais. O `apps/connectors` segue a mesma regra.
- **`apps/connectors`** valida o token localmente com a mesma
  `Auth:TokenSigningKey`, e o boot falha se ela estiver vazia. A autorização é uma
  tabela explícita de subjects, e aqui **o operador não passa em tudo**:
  - `operator` só na listagem de provedores, na navegação de pastas e no
    "Sincronizar agora" (`/connectors/providers`,
    `/connectors/providers/{providerKey}/folders` e
    `POST /connectors/knowledge-bases/{knowledgeBaseId}/sync`);
  - `service:api` só na descrição de pasta
    (`/connectors/providers/{providerKey}/folder`), que o `apps/api` chama para
    validar a pasta de uma base sincronizada, assinando o próprio token. Esse
    subject **não** está na tabela do `apps/api`: um token `service:api` que chegue
    lá recebe `403`;
  - qualquer outro subject recebe `403`.

  A tabela é conferida no boot nos dois sentidos: entrada sem rota mapeada, e rota
  autenticada que não está na lista de nenhum subject.
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
