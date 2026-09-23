# Buteco Agentes — Arquitetura e Convenções

> Documento de referência estável. Atualizar só quando uma decisão
> arquitetural nova mudar algo aqui descrito — não a cada change aplicada
> (isso vai no arquivo de histórico/status, separado).

## O que é

Plataforma multi-agente com protocolo A2A (Agent-to-Agent), permitindo
cadastrar agentes de IA, vinculá-los a ferramentas externas (MCP),
delegação entre agentes, e integração com canais de entrada reais
(WhatsApp via WAHA, Telegram) através de um CRM mínimo de contatos e
sessões.

Workflow de desenvolvimento: spec-driven via OpenSpec
(`/opsx:explore` → `/opsx:propose` → revisão → `/opsx:apply` →
`/opsx:sync` → `/opsx:archive`). Specs vivos em
`openspec/specs/{capability}/spec.md`.

## Os quatro apps

| App | Papel | Stack | Banco |
|---|---|---|---|
| `apps/api` | CRUD de agentes, catálogo MCP, delegação, protocolo A2A (`SendMessage`/`GetTask`), AgentCard, push notification (emissor), login do operador e emissão de token | .NET 10, ASP.NET Core Minimal API, CQRS via `Mediator.Abstractions`+`SourceGenerator` v3.0.2, EF Core+Npgsql, `RabbitMQ.Client` (publisher), pacote `A2A` | Postgres compartilhado com `apps/workers` (mesmas tabelas, dois `AppDbContext` mantidos sincronizados por disciplina, não por schema separado) |
| `apps/workers` | Executa tasks: chama o LLM, resolve tools MCP, executa delegação, dispara push notification | .NET 10 Worker Service, `Microsoft.Agents.AI` 1.15.0 (`ChatClientAgent`, `Compaction`), EF Core mirror, consumidor RabbitMQ | Mesmo Postgres de `apps/api` |
| `apps/frontend` | UI de gestão (agentes, MCP, delegação, canais), tela de login, sessões e histórico de conversa por canal | React 19.2+, TypeScript, Vite, Mantine v9, `react-router` v8, `@tanstack/react-query` v5, `react-markdown`+`remark-gfm`, Vitest+Testing Library | — |
| `apps/inbox` | Catálogo de canais de entrada, CRM (Contact/Session), histórico de mensagens, orquestrador de debounce, adapters de canal (WAHA, Telegram) | .NET 10 Minimal API, CQRS próprio (Mediator), EF Core | Postgres **próprio** (`buteco_inbox`), isolado — sem tabela em comum com `apps/api`/`apps/workers` |

Isolamento estrito entre apps: nenhum `ProjectReference` cruzado.
Referências entre domínios de apps diferentes são sempre validadas via
HTTP (ex. `apps/inbox` valida `AgentId` chamando `GET /agents/{id}` em
`apps/api`), nunca por FK direta. Essas chamadas HTTP entre apps são
autenticadas (ver "Autenticação", abaixo).

## Modelo de domínio (estado atual)

**`Agent`** (`apps/api`): `Name`, `Instructions`, `IsActive`, `Provider`/
`Model` (nullable — agente "precisa de reconfiguração" quando nulos),
`Description` (nullable), `Skills` (jsonb, `{ Name, Description? }`),
vínculos N:N: `McpServers` (via `AgentMcpServer`, com `AllowedTools`
jsonb por vínculo), `DelegatesTo` (via `AgentDelegation`, **unidirecional**
— A→B não implica B→A) e `KnowledgeBases` (via `AgentKnowledgeBase`).

**`McpServer`** (`apps/api`): `Name`, `Description`, `Url`, `AuthType`
(`None`/`BearerToken`), `EncryptedCredential` (AES-GCM).

**`Channel`** (`apps/inbox`): `ChannelType` (string aberta, validada em
runtime contra adapters efetivamente registrados via DI — não enum
fechado), `Name`, `EncryptedCredentials` (AES-GCM, chave própria de
`apps/inbox`, shape opaco que varia por `ChannelType`), `AgentId` (Guid
opaco, validado via HTTP contra `apps/api`), `IsActive`, `WebhookUrl`
(computada, nunca persistida).

**`Contact`/`Session`** (`apps/inbox`, CRM): `Contact` identificado por
`(ChannelId, ExternalId)` único — mesmo `ExternalId` em canais diferentes
gera `Contact`s distintos, sem unificação de identidade entre canais.
`Contact` tem dois campos de origem externa com semânticas **opostas de
propósito**: `Metadata` (congelado na criação) e `DisplayName` (nullable,
reescrito a cada mensagem de entrada, extraído do `pushName` no WAHA e do
`username`/`first_name` no Telegram). `Session` amarra várias conversas
do mesmo `Contact` ao longo do tempo; fronteira por **inatividade
automática** (timeout configurável) — sem encerramento explícito. Ao
expirar, a `Session` anterior tem `ClosedAt` preenchido
(`inbox-session-indice-unico`) e um índice único parcial
(`sessions."ContactId" WHERE "ClosedAt" IS NULL`) garante, por banco, no
máximo uma `Session` aberta por `Contact` — mesmo idioma de índice único
+ catch + detach + re-busca já usado por `Contact`/`PendingDispatch`
abaixo. O estado aberta/encerrada é **derivável** por isso, mas ainda não
é promovido como contrato de API documentado (ver histórico).

**`PendingDispatch`** (`apps/inbox`): buffer de debounce persistido por
`Session`, mensagens agrupadas antes de disparar `SendMessage` real
contra `apps/api`. Concorrência otimista via `xmin` do Postgres. É
**buffer, não histórico**: some quando o ciclo de disparo termina.

> **Área sensível**: a coleção de mensagens do `PendingDispatch` é
> owned/JSON e já produziu perda silenciosa de mensagem sob concorrência
> real (aliasing de change tracker do EF Core após re-leitura na mesma
> instância de `DbContext`). Corrigido com detach+rebusca. A varredura
> feita em `inbox-mensagens-persistidas` classificou todas as superfícies
> owned/JSON do repo e confirmou que essa é a única com o padrão de risco
> — a distinção que importa é entre `OwnsMany().ToJson()` (snapshot
> estrutural por elemento) e `HasConversion`+`ValueComparer` (serializa o
> valor inteiro a cada `SaveChanges`, imune ao aliasing). Qualquer mudança
> que encoste nessa coleção, ou que re-leia a entidade depois de mutá-la,
> precisa de teste com concorrência de verdade — não caminho feliz.

**`Message`** (`apps/inbox`): histórico durável por `Session`, **tabela
relacional própria, deliberadamente separada do `PendingDispatch`** — não
o estende nem toca sua coleção owned/JSON, justamente para não reabrir a
área sensível acima. Guarda `Direction`, `Content`, `ContentType`
(`Text`/`Image`/`Audio`/`Document` — marcador de tipo; mídia binária não
é persistida), `OccurredAt`, e, conforme a direção:

- **entrada**: identificador externo da mensagem (índice único parcial,
  dedup de webhook reentregue) e `DispatchStatus`
  (`Pending`/`Dispatching`/`Failed`/`Completed`), espelhado dos pontos que
  mutam ou removem o `PendingDispatch` e **sobrevivendo à remoção dele** —
  é o que torna o silêncio de uma conversa legível na UI. `Failed` agrupa
  três causas distintas de "não haverá resposta" sob um valor só, decisão
  consciente;
- **saída**: `DeliveryStatus` (`Sent`/`Failed`, com motivo) — sucesso ou
  falha do **envio ao provedor**, nunca recibo de entrega ou leitura do
  destinatário final. Essa distinção é de contrato, não de UI: a interface
  renderiza um indicador só, jamais dois, porque o dado não existe.

Os quatro enums de `Message` atravessam a API como **string**
(`JsonStringEnumConverter` por enum, mesmo padrão de `McpServerAuthType`),
nunca como inteiro ordinal.

**`KnowledgeBase`** (`apps/api`): `Name`, `Description`, `IsActive`.
`Description` **não é campo decorativo** — é o texto que vira a descrição da
tool exposta ao modelo, e é por ele que o modelo decide se a base é relevante
para a pergunta; por isso é obrigatória e não vazia, ao contrário de
`McpServer.Description`. Segue o padrão da casa: `IsActive`, sem exclusão.

> **E não-vazia NÃO basta — a etapa `0d` mediu.** Com 7 bases e 83 perguntas, o
> modelo escolheu a base certa em **61%** dos casos, e o erro é irrecuperável (a
> passagem certa não fica entre os candidatos). O padrão medido: uma base de
> descrição **genérica canibaliza as vizinhas** — a que cobria "descontos,
> acordos, parcelamento, campanhas" tinha 5 perguntas e foi chamada 24 vezes,
> enquanto a maior base, com 28, foi chamada 11. Não é viés de posição: com ela
> em último lugar na lista de tools, continuou atraindo 20 dos 33 erros.
>
> **Orientação de operação, não requisito** (nenhum teste pode afirmar qualidade
> de texto de operador): descrição **específica e delimitada**, dizendo do que a
> base trata **e do que não trata**. Bases irmãs precisam que cada uma exclua o
> assunto da outra. Genérica é pior que curta — curta faz o modelo não chamar,
> genérica faz ele chamar **no lugar de outra**.
>
> **Sintoma a procurar em uso real:** uma base sendo chamada muito acima da sua
> fatia de perguntas. É a mesma assinatura do fragmento-atrator de `0c`, uma
> escala acima. O item completo, com onde isso encosta na tela, está no `02`.

**`KnowledgeDocument`** (`apps/api`): `KnowledgeBaseId`, `Title`, `SourceType`,
`ExtractedText`, `ContentLengthBytes`, `IndexingStatus`, `IndexedAt` (nullable),
`FailureReason` (nullable), `ContentRevision`. Quatro coisas que não se
adivinham lendo os campos:

- **`ContentLengthBytes` é coluna gerada pelo Postgres**
  (`GENERATED ALWAYS AS (octet_length("ExtractedText")) STORED`), nunca escrita
  pela aplicação — não existe caminho de escrita de conteúdo que a deixe
  defasada. Está em **bytes UTF-8**, a mesma unidade do teto de 1 MiB validado
  no cadastro, e mede a mesma string que a validação mede (o texto **já
  extraído**: a extração remove BOM e normaliza `CRLF`, então validar a entrada
  crua faria os dois números medirem coisas diferentes). Coluna gerada em vez de
  projeção porque `string.Length` traduz para `length()`, que conta caracteres,
  e o provider Npgsql não tem mapeamento LINQ para `octet_length`.
- **`SourceType` é string aberta**, não enum fechado: identifica o extrator a
  aplicar, resolvido via DI **keyed**, com checagem de integridade bidirecional
  no startup — mesmo idioma de `Channel.ChannelType`. Extensão de arquivo e
  `SourceType` são conceitos distintos (o cliente sugere `markdown` para
  `.md`/`.markdown`/`.txt`; texto puro é markdown válido). A extração
  **preserva a marcação**: é normalização, não conversão para texto puro, porque
  a fragmentação da etapa de indexação divide por cabeçalho.
- **`IndexingStatus` tem exatamente quatro valores** (`Pending`/`Indexing`/
  `Indexed`/`Failed`) e **não** ganha um valor para reindexação: a distinção
  entre "nunca indexado" e "há conteúdo indexado respondendo agora" é carregada
  por `IndexedAt` (nulo × preenchido), em qualquer dos quatro estados. A regra
  para a UI é uma só: informação derivada da indexação aparece sempre que
  `IndexedAt` não for nulo, e é omitida quando for — nunca zerada, que afirmaria
  que a indexação rodou e não achou nada.
- **`ContentRevision` é coluna explícita, não `xmin`**, e incrementa **apenas**
  quando `ExtractedText` muda. O consumidor de indexação muta a própria linha ao
  transicionar de estado, e um token de linha invalidaria o próprio trabalho em
  curso; esta coluna, que ele nunca escreve, permanece estável ao longo das
  transições dele.

Na etapa de catálogo, `Indexing`/`Indexed`/`Failed`, `IndexedAt` e
`FailureReason` **nascem sem nenhum escritor** — não há fila nem consumidor, e
todo documento criado ou atualizado permanece `Pending` indefinidamente. Isso é
requisito declarado, não defeito: quem os escreve é o consumidor da etapa de
indexação.

**`KnowledgeFragment`** — o pedaço indexável de um documento, com o vetor que o
encontra. `KnowledgeDocumentId`, `KnowledgeBaseId` (desnormalizado: a busca
filtra por base antes de ordenar por distância), `Ordinal`, `Text` (o texto
**emitido**, com o prefixo de caminho de cabeçalhos — é ele que foi embedado),
`Embedding` em `vector(4096)`, e três colunas de proveniência
(`EmbeddingProvider`, `EmbeddingModel`, `EmbeddingDimensions`).

As três de proveniência não são metadado decorativo: são o lado do índice na
checagem bidirecional do boot de `apps/workers` (convenção 8), e sem elas a
troca silenciosa de modelo — que corrompe o índice sem erro nenhum — deixa de
ser detectável.

**E desde `knowledge-index-diagnostics` elas têm um segundo leitor**:
`GET /knowledge-index/diagnostics` em `apps/api` devolve as combinações gravadas,
cada uma com a contagem de fragmentos. A rota é **global** — não aceita id de base
—, e a razão é a mesma que torna a checagem de boot possível: a dimensão é fixada
pelo tipo da coluna (gravar 1536 numa `vector(4096)` responde
`expected 4096 dimensions, not 1536`, medido) e só pode existir uma combinação no
índice inteiro. Ela devolve o **gravado**, nunca o declarado, e não poderia fazer
diferente: `apps/api` não tem a seção `Embedding` de configuração. Mais de uma
combinação é resposta válida dessa rota e não erro — é o estado em que
`apps/workers` está no chão e o operador abre o painel para entender por quê.

**O `SELECT DISTINCT` sobre as três colunas é mais barato do que o tamanho da
tabela sugere, e isso foi medido** (150.000 fragmentos, `pgvector/pgvector:pg18`):
a varredura lê **só o heap**, 234 MB em 30.000 páginas, porque o `attstorage` da
coluna de vetor é `EXTERNAL` e os 2,4 GB de vetor ficam em TOAST, fora do caminho
da projeção. De 0,24 s a 0,82 s, e **sem sujar o cache** — o *ring buffer* de
leitura em massa deixou 282 páginas em `shared_buffers` das 30.000 lidas, então a
rota não evicta o cache que a busca usa. Gatilho registrado para criar um índice nas
três colunas: p95 da rota acima de 500 ms ou heap acima de 500 MB; o índice custaria
1,08 MB para 150.000 linhas, porque a deduplicação do btree colapsa valores
idênticos — **o invariante que torna a varredura cara é o mesmo que torna o índice
barato**. E `LIMIT 1` não serve: leria três páginas e seria cego à corrupção de duas
combinações, que é o único estado que motiva a tela.

**A tabela nasce na migração de `apps/api`, que não escreve nela.** Quem escreve
é `apps/workers`, e o motivo é de deploy: o `migrator` do compose só empacota
bundles de `apps/api` e `apps/inbox`. É contraintuitivo o bastante para alguém
"consertar".

**Sem índice ANN**, e não por esquecimento: HNSW recusa mais de 2000 dimensões em
`vector` e mais de 4000 em `halfvec` (medido em pgvector 0.8.6), então qualquer
índice exige uma coluna **derivada e truncada**, que nasce por SQL quando fizer
falta. Gatilho: p95 da consulta acima de 200 ms. Custo de exercê-lo, também
medido: `ADD COLUMN ... GENERATED ... STORED` **reescreve a tabela sob
`ACCESS EXCLUSIVE`**, e durante o `ALTER` até leitura bloqueia.

**O gatilho tem volume medido, e ele NÃO é o mesmo 7.500 do custo de disco.** Os
dois números existiam em lugares diferentes respondendo a perguntas diferentes, e
confundi-los faz o teto de latência parecer folgado quando não é. Medido em
2026-09-12, `pgvector/pgvector:pg18`, busca exata com `ORDER BY <=> LIMIT 5` sobre
`vector(4096)`, buffers quentes:

| fragmentos **na base consultada** | latência |
|---|---|
| 272 (índice real de desenvolvimento) | ~14 ms |
| 2.000 | 111 ms |
| **~4.300** | **~200 ms — é aqui que o gatilho dispara** |
| 7.500 | 333 ms |
| 20.000 | ~950 ms |

Inclinação ~47 µs por fragmento, linear e sem joelho. Três leituras:

- **Os 7.500 fragmentos citados adiante são volume de referência DE DISCO**, para
  dimensionar armazenamento. Como volume de **latência** eles já estão acima do
  teto: dariam 333 ms. Os dois números não são comparáveis e não devem ser
  citados como se fossem o mesmo orçamento.
- **O filtro da consulta é `KnowledgeBaseId`, então o que conta é a MAIOR base,
  não o índice inteiro.** Cem bases de 500 fragmentos não acionam nada; uma base
  de 5.000 aciona. ~4.300 fragmentos são ~4,9 MB de texto naquela base, pela
  média real medida de 1.146 caracteres por fragmento.
- **O pior caso não é o regime quente.** A primeira consulta após ociosidade
  pagou 265 ms com apenas 272 fragmentos, por leitura de TOAST. Ao lado da
  segunda ida ao LLM, que custa segundos, continua sendo ruído — mas é esse o
  número a orçar, não os 14 ms.

**As quatro colunas que `KnowledgeDocument` ganhou na etapa de indexação:**

- `ContentHash` (SHA-256 do texto extraído) — um propósito só: atualização com
  conteúdo idêntico não volta a `Pending`, não enfileira e não gasta embedding.
  **Nulo significa "nunca indexado sob esta regra"**, e é o que faz a linha
  criada antes da etapa 2a ser enfileirada mesmo com conteúdo idêntico — o
  comportamento **oposto** ao da linha nova, de propósito.
- `FragmentCount` — mantido pelo consumidor, na mesma transação que grava os
  fragmentos. Exibido quando `IndexedAt` não é nulo, **omitido** quando é nulo,
  nunca zerado.
- `IndexingAttempts` e `LastAttemptAt` — tentativas sobre a **revisão corrente**,
  zeradas quando o conteúdo muda. Uma tentativa é uma **execução** do consumidor,
  não uma chamada HTTP.

**`AgentKnowledgeBase`** (`apps/api`): vínculo N:N entre `Agent` e
`KnowledgeBase` — `AgentId`, `KnowledgeBaseId`, e **nada mais**. A ausência de
coluna extra é decisão com causa, não omissão: `AgentMcpServer.AllowedTools`
existe porque as tools de um servidor MCP **não são catálogo persistido em lugar
nenhum** (são descobertas ao vivo via `tools/list`), então a seleção precisa
morar no vínculo; bases de conhecimento têm id e são catálogo persistido, e a
seleção é o próprio conjunto de ids. Consequências registradas junto, para que a
etapa de execução não as reabra:

- Sem análogo de `AllowedTools` (seleção de documentos permitidos): quem precisa
  de granularidade menor cria outra base. `k` é constante nomeada em
  `apps/workers` até haver dois consumidores reais querendo valores diferentes
  (convenção 2), e **limiar de similaridade não existe** — a etapa `0c` o
  reprovou com 20 negativas, e a busca devolve os `k` mais próximos com a
  distância explícita para o agente decidir. Não há flag "injetar sempre" — o
  acesso é sob demanda, decidido pelo modelo a partir de
  `KnowledgeBase.Description`.
**O conjunto de tools entregue ao LLM é a união de TRÊS conjuntos**, desde a
etapa 4: tools MCP (por `AgentMcpServer`, filtradas por `AllowedTools`), tools de
delegação (uma por `AgentDelegation`) e tools de conhecimento (uma por
`AgentKnowledgeBase` cuja base está **ativa**). Os três dividem um espaço de nome
único, e `ToolNameDeduplicator` é o único ponto que sabe disso — a precedência na
colisão é **MCP → delegação → conhecimento**, declarada pela ordem dos parâmetros
e não pela ordem de concatenação.

**A tool de conhecimento se chama `search_<slug do nome da base>`**, e o esquema
foi escolhido por censo, não por gosto: o alternativo (`<Nome>__search`) colide
com MCP **por composição**, e o censo do banco de dev contém o padrão que torna
isso concreto — o servidor *"Informações Gerais"* sanitiza para
`Informa__es_Gerais`, onde o `__` não é o separador. Com `search_<slug>` a
colisão por composição é **impossível**, porque o slugificador nunca emite `_`;
sobra só a truncagem em 64, que é o mesmo caminho da colisão MCP × delegação.

- **Base inativa continua vinculável e editável**; o filtro por `IsActive`
  pertence à resolução (`where kb.IsActive`, idioma de `McpToolSetResolver`),
  não à remoção do vínculo. Vale igual para **agente** inativo: desativar um
  agente impede que ele execute, não que o operador configure os vínculos dele
  — mesmo raciocínio que permite criar e atualizar documento em base inativa.
- FKs em `Cascade` nos dois lados, como `AgentMcpServer` e `AgentDelegation` — e
  deliberadamente diferente do `Restrict` de `KnowledgeDocument` para a base.
  Não há conflito: enquanto existir documento, o `Restrict` bloqueia a exclusão
  da base antes de o `Cascade` do vínculo ser alcançado. Hoje as duas cascatas
  são inertes (nem `Agent` nem `KnowledgeBase` têm rota de exclusão).
- A lista exposta em `AgentResponse.knowledgeBases` é ordenada por nome **com
  desempate por id**. Não é preciosismo: nome de base não é único por requisito,
  e sem o desempate a ordem entre homônimas é a que o plano do Postgres
  devolver. Ver o item em aberto sobre os quatro sites que ainda não desempatam,
  em `02-HISTORICO_E_STATUS.md`.

**`KnowledgeDocument` é a única entidade do repositório com exclusão real**
(`DELETE`, primeiro `MapDelete` da base). Ver "Exclusão: catálogo × conteúdo",
abaixo.

## Exclusão: catálogo × conteúdo

O padrão da casa era, até aqui, **soft delete por `IsActive`** com o filtro
aplicado no momento da resolução — e não havia nenhum `MapDelete` no
repositório. Esse padrão não é uma regra sobre "tudo": ele se formou para
**entidades de catálogo com vínculos apontando para elas**. Um `McpServer`
desativado continua referenciado por `AgentMcpServer`, e o histórico de
execuções que o usou precisa continuar fazendo sentido; apagá-lo quebraria
leitura de passado.

`KnowledgeDocument` é a primeira entidade que não se encaixa nisso: é
**conteúdo**, nada aponta para ele além dos seus próprios fragmentos, e o caso
de uso concreto — o operador subiu o arquivo errado, ou um com dado que não
devia estar ali — é exatamente aquele em que "continua no banco, invisível" é a
resposta errada. Some-se o custo medido: ~60 MB de vetores por 7.500
fragmentos **em 1536 dimensões** — com as 4096 dimensões do modelo que a
etapa `0b` recomendou, são ~123 MB para os mesmos 7.500 fragmentos.

> **7.500 aqui é volume de referência de DISCO, e só isso.** Não é o volume em
> que a consulta continua rápida: como latência, 7.500 fragmentos **numa mesma
> base** dão 333 ms, acima do teto de 200 ms. Ver o gatilho do índice ANN acima,
> com a tabela medida — lá o volume que importa é ~4.300 fragmentos na maior
> base.

O critério, reutilizável para qualquer entidade futura, é esse: **catálogo
referenciado → `IsActive`; conteúdo sem referência → exclusão real.** Duas
consequências práticas registradas junto:

- A FK de `KnowledgeDocument` para `KnowledgeBase` usa **`Restrict`**, não o
  `Cascade` default do EF Core. Hoje é inerte (a base não tem exclusão); a
  diferença é qual das duas falha de forma segura se alguém adicionar exclusão
  de base um dia — `Restrict` obriga a decidir o destino dos documentos em vez
  de os apagar em silêncio.
- `DELETE` numa rota que não oferece o verbo responde **405**, não 404 — a
  distinção entre "recurso inexistente" e "operação não oferecida" é
  informação, e é ela que os testes afirmam.

## Filas de trabalho

Duas filas, com propósitos e topologias distintas — e a distinção é decisão, não
acidente.

**`agent-tasks`** — execução de tarefa de agente. Consumidor com
`prefetchCount: 1`, e `AgentDelegationConcurrencyTests` existe para provar o que
isso implica: dentro de uma instância o consumo é **serializado**, a ponto de uma
delegação que aguarda a task do alvo ser autodeadlock estrutural. Falha descarta
a mensagem (`BasicNackAsync(requeue: false)`), sem retry.

**`knowledge-indexing`** — indexação de documento. **Fila própria justamente por
causa do `prefetchCount: 1` da outra:** indexação de minutos ali não seria "fila
mais lenta", seria a mesma classe de bloqueio, com execução de agente atrás.

Três publicadores, todos em `apps/api`: criar documento, atualizar documento com
conteúdo diferente, e **reindexar** — este último a única entrada que publica sem
o conteúdo ter mudado. A reindexação abre uma **rodada nova** sobre a mesma
revisão: publica com `Attempt = 1`, e limpa `FailureReason`, `IndexingAttempts` e
`LastAttemptAt` para que o documento não leia como rodada encerrada enquanto a
mensagem espera na fila. Não toca `ContentHash` nem `ContentRevision`.

É o **primeiro consumidor do repositório com política de tentativas**, e não
havia molde a herdar. A forma: **três execuções**, espaçadas por 1 e 5 minutos,
por **duas filas de espera** (`knowledge-indexing-wait-60s` e
`-wait-300s`), cada uma com `x-message-ttl` **fixo** e dead-letter de volta para
a principal.

**TTL fixo por fila, nunca por mensagem** — e o motivo é um defeito conhecido e
silencioso: RabbitMQ só expira mensagem quando ela chega à **cabeça** da fila,
então numa fila única com TTL por mensagem uma de 300 s na frente segura uma de
60 s atrás. Duas filas de TTL fixo custam uma declaração a mais e não têm o
problema.

**Uma tentativa é uma execução**, não uma chamada HTTP ao provedor — não há retry
dentro da execução. Uma camada só, um contador só, um significado só: é o que
torna verdadeiro o texto que a tela mostra ao operador ("três tentativas, a
última às 03:14").

**O `catch` do consumidor tem duas saídas**, e isso é o que o molde de
`TaskJobConsumer` não cobre: republicar na fila de espera, ou terminar em
`Failed`. As duas contam tentativa; **só** o esgotamento do limite grava
`Failed`, e essa gravação **preserva** `IndexedAt` e os fragmentos anteriores.
Falhar ao gravar o estado de falha não devolve a mensagem para a fila — criaria
um laço que falharia pelo mesmo motivo.

## Autenticação

**Token stateless assinado com HMAC**, sem biblioteca JWT e sem sessão em
banco. `apps/api` emite (login do operador e token de serviço); `apps/api`
e `apps/inbox` validam **localmente**, compartilhando apenas a chave de
assinatura via configuração — nenhuma chamada de rede entre os processos
para validar token. Foi o que permitiu autenticar dois apps com bancos
isolados sem introduzir store compartilhado.

- **Operador único**, credencial via variável de ambiente (usuário +
  hash PBKDF2), `POST /auth/login` em `apps/api`. Sem tabela de usuários,
  sem RBAC. TTL de 30 minutos por padrão, configurável — o default vive
  no tipo de Options, não no `appsettings.json`.
- **Token de serviço** (`apps/inbox` → `apps/api`): mesmo mecanismo de
  assinatura, `sub` distinto, assinado a cada requisição de saída com TTL
  fixo curto, **escopado** — só autoriza as rotas que `apps/inbox` de
  fato consome; qualquer outra responde `403`.
- **Enforcement por padrão**: toda rota HTTP de `apps/api` e `apps/inbox`
  exige token; as exceções vivem numa allowlist explícita com motivo
  classificado por enum, validada no startup (ver convenção 8).
- **Frontend**: módulo fino de token sobre `sessionStorage`
  (ler/anexar/limpar em `401`), importado por cada `request<T>` de
  feature — sem cliente HTTP compartilhado (convenção 7 preservada).

Rotas anônimas são decisão de segurança, não detalhe de implementação:
adicionar uma passa por revisão.

## Fuso horário do sistema (`apps/workers` e `apps/api`)

**Uma variável, `TZ`, entregue aos DOIS serviços com `:?` no compose de
produção** — e é isso que faz os dois processos concordarem sobre que dia é hoje,
por construção e não por disciplina. Cada um a usa de um jeito, e a distinção
importa:

| | como lê | para quê |
|---|---|---|
| `apps/workers` | fuso do SO, pelo runtime (`TimeProvider.System.LocalTimeZone`) | renderizar instante e dia da semana (pt-BR fixo) no bloco de contexto temporal |
| `apps/api` | o MESMO valor, via `IConfiguration` — **nunca** `TimeZoneInfo.Local` | o balde diário da agregação de métricas, em SQL com `AT TIME ZONE` |

**Os dois falham a inicialização** se o fuso resolvido não corresponder ao valor
declarado — a mesma checagem, copiada, porque os apps não se referenciam.

**Por que `apps/api` lê de `TZ` e não de uma variável própria da agregação, e por
que isso contraria uma decisão registrada.** A decisão anterior dizia *"o nome vem
de configuração explícita da agregação, nunca do `TZ` do processo de
`apps/api`"*. O motivo escrito dela era que o balde não dependesse de qual
container respondeu — e o `:?` entrega **o mesmo valor a toda réplica**, então
esse motivo fica preservado. O que a decisão anterior deixava aberto é que **nada
fazia o fuso do balde e o do worker concordarem**, e divergir desloca **27,7%** da
série em um dia inteiro, sem erro, sem log e sem sintoma (medido em 23/09/2026
sobre 260 linhas de `a2a_tasks`). Convenção 9, registrada em
`rotas-de-agregacao-sistema`.

**Antes dessa change, `apps/api` não recebia `TZ` e rodava em UTC.** A divergência
era latente porque nada ali renderizava data local; a etapa de agregação é o que
a ativou. Consequência a notar no deploy: **o serviço `api` agora não sobe sem
`TZ`**, onde antes subia.

**E o balde nunca vira índice.** A conversão de fuso sai no `GROUP BY`, sobre as
linhas já restritas pela janela — o filtro de período é um range sobre o instante
e usa índice comum. `timezone(text, timestamptz)` é `IMMUTABLE` no pg18, então um
índice de expressão **seria** possível; não usá-lo é escolha, porque congelaria o
nome do fuso no schema, que é o oposto de mantê-lo em configuração.

Fuso e idioma usados pelo worker para renderizar data/hora são decisão de
sistema, não de agente: `TZ` do SO, único para o processo inteiro,
resolvido no startup — sem opção por agente, sem chave em
`appsettings.json`. `TZ` deve usar o nome IANA canônico da tz database
(ex. `America/Sao_Paulo`), sem o prefixo POSIX `:` — o `.NET` resolve o
fuso corretamente com o prefixo, mas o remove do identificador que expõe,
o que quebraria a checagem abaixo mesmo com a configuração correta. O
processo falha a inicialização se o fuso resolvido não corresponder
exatamente ao valor declarado (checagem de integridade no startup,
convenção 8) — cobre `TZ` ausente, vazia ou inválida com o mesmo erro.
Dia da semana em texto renderizado pelo worker é sempre pt-BR, pelo mesmo
motivo — nunca herdado de `CurrentCulture` do host.

## AgentCard / protocolo A2A

Cada agente expõe `GET /agents/{id}/.well-known/agent-card.json`,
montado a cada requisição a partir do estado atual (sem cache).
`Skills` do agente mapeadas para `AgentSkill` via slug determinístico
com dedupe. Push notification (`pushNotificationConfig` no `SendMessage`)
suportado ponta a ponta — `apps/workers` dispara webhook ao concluir uma
task, fire-and-forget, sem retry.

Descoberta é pública, uso não: o `AgentCard` continua acessível sem
token, mas declara `SecuritySchemes`/`SecurityRequirements` (HTTP Bearer)
para o endpoint A2A do agente — a exigência de credencial fica declarada
de forma compatível com a spec, não implícita.

Os dois endereços do agente — endpoint de execução e card de descoberta —
saem também na resposta de `GET /agents/{id}` e da listagem, montados no
servidor por um ponto único que o próprio card consome (`agente-enderecos-a2a`).
Nunca são construídos pelo consumidor a partir de host mais identificador: a
url pública é configuração do servidor, e o host de onde a página foi servida
não é o host público atrás de proxy. Sem url pública configurada, o bloco vem
**ausente** em vez de relativo — o card de descoberta ainda emite endereço
relativo nesse caso, e corrigir isso é change própria (ver "Itens em aberto"
no arquivo 02).

A re-serialização do `AgentTask` inteiro em `PostgresTaskStore.SaveTaskAsync`
(`Serialize(task, A2AJsonUtilities.DefaultOptions)`) **não é uma rede de
segurança genérica** para qualquer valor colocado em `AgentTask.Metadata`
fora do contrato A2A: ela só reaplica naming policy/`DefaultIgnoreCondition`
sobre objetos .NET serializados a fresco, não sobre um `JsonElement` já
materializado — esse é copiado verbatim. Um codec de `Metadata` que
serializa sem `A2AJsonUtilities.DefaultOptions` só tem o defeito
mascarado pela re-serialização se o valor nunca foi materializado como
`JsonElement` antes dela (ex.: uma string escalar); se já foi (ex.: um
objeto), o formato errado chega ao disco. Achado em
`push-notification-config-codec-encoder`, comparando com o defeito
benigno de `ConversationSessionCodec` corrigido em
`crossapp-session-codec-encoder`.

## Histórico de conversa e compactação (`apps/workers`)

Conversa é o conjunto de tasks com o mesmo `contextId`. O histórico **não** é
remontado das mensagens: `AgentExecutionService` serializa a sessão do agente e
a grava em `AgentTask.Metadata["conversationSession"]` no caminho de sucesso
(`apps-workers-historico-conversa`); a execução seguinte do mesmo `contextId`
carrega a sessão da task `completed` mais recente (`LoadSessionAsync`,
`ListTasksAsync` escopado por `agentId` e ordenado por `StatusTimestamp`).
Tasks `failed`/`rejected` ficam de fora por desenho.

`ConversationSessionCodec` grava a sessão como **string JSON escapada**: o
`jsonb` normaliza ordem de propriedades e o polimorfismo de `AIContent` exige
`"$type"` primeiro (Decisão 10 daquela change; encoder corrigido em
`crossapp-session-codec-encoder`).

**Dois limites com papéis diferentes** — constantes globais em
`AgentExecutionService`, não configuráveis por agente:

- `MaxHistoryMessages = 200` (`RecentMessageChatReducer`) — **teto de
  segurança**. Truncar ativamente invalida o bookkeeping incremental do
  `CompactionProvider`, por isso fica bem acima da faixa de operação
  (`apps-workers-resumo-historico-conversa`, Decisão 9).
- `SummarizationTurnThreshold = 10` (`CompactionTriggers.TurnsExceed`) — faixa
  de operação. Conta **turnos de usuário**: dispara quando a 11ª mensagem de
  usuário entra no contexto.

A compactação roda **dentro** do turno, como requisição a mais ao provedor, e é
marcada por `CompactionCallChatClient` para entrar em `provider_calls` com
`Purpose = Compaction` (`metricas-execucao-coleta`, D8).

**A requisição de resumo nunca termina em turno de modelo**
(`compactacao-historico`, D1/D2). A estratégia do pacote termina sempre em
mensagem de assistente — o `TurnIndex` é copiado para os grupos de assistente e
a contagem de turnos só cai quando esse grupo também é excluído —, e o Gemini
recusa com `400 "Requests ending with a model turn are not supported."`
`CompactionCallChatClient` acrescenta uma mensagem final de usuário quando a
última é de assistente, sem alterar o histórico. O texto é **afirmação de
fronteira**, não segundo comando de resumir: a instrução `system` do pacote é
quem comanda, e dois comandos concorrentes pioram o resumo. Trocar o texto muda
o resumo — não é string de formatação.

**Falha de resumo não derruba o turno, e não é silenciosa.**
`SummarizationCompactionStrategy` captura a exceção, restaura os grupos e segue
(guarda: `SummarizationCallFailure_DoesNotPreventUserTurnFromCompleting`). O
`CompactionProvider` recebe o `ILoggerFactory` do host — sem ele cai em
`NullLoggerFactory`, e foi esse silêncio que deixou a compactação quebrada
contra o Gemini do primeiro deploy até o piloto, com seis falhas e zero linhas
de log. Pelo dado, o sintoma é `Purpose = Compaction` com `Failed = true` e a
entrada por turno crescendo sem parar.

**`ChatClientAgent` continua sem `services`**, de propósito: o middleware
empilhado por `WithDefaultAgentMiddleware` resolve o logger de lá, não do
`loggerFactory`, então o log de invocação de tool segue apagado — decisão de
volume de log, com gatilho registrado no `02`.

## Contexto do agente (blocos concatenados às `Instructions`)

`apps/workers` (`AgentExecutionService`) concatena às `Instructions`
cadastradas do agente um ou mais blocos de contexto, montados em memória a
cada execução — nunca persistidos em `Agent.Instructions`, nunca parte do
histórico de conversa. Dois blocos hoje: contexto temporal (instante de
processamento, instante da mensagem quando disponível, regra de
precedência para expressões de tempo relativas —
`apps-workers-contexto-temporal`, `inbox-instante-mensagem`) e contexto
de canal (tipo do canal, identificador do contato —
`inbox-contexto-canal`). Cada bloco é uma função pura em arquivo próprio
(`TemporalContextBlockBuilder`, `ChannelContextBlockBuilder`), com seu
próprio marcador delimitando "isto não é uma mensagem do usuário, não
responda a ele diretamente" — arquivos separados de propósito, sem
infraestrutura compartilhada entre eles antes de haver um terceiro bloco
(convenção 2).

**O que entra num bloco não é uma decisão só de utilidade — é uma decisão
de risco.** `inbox-contexto-canal` formulou a distinção, ao decidir manter
`Contact.DisplayName` fora do prompt: um valor **atribuído pelo
provedor/adapter do canal** (`Channel.ChannelType`, `Contact.ExternalId`)
não é a mesma categoria de dado que **texto livre digitado pelo usuário
final** (`Contact.DisplayName`). O primeiro pode entrar num bloco de
contexto sem abrir a classe de risco de injeção de prompt; o segundo abre
— e o marcador de "não é mensagem do usuário" usado pelos dois blocos
acima é uma dica textual, não uma fronteira estrutural: nunca foi testado
sob conteúdo adversarial, porque nunca carregou nenhum até agora. Vale
para qualquer dado futuro cogitado para entrar na janela de contexto do
agente, não só para os dois blocos existentes — a pergunta a fazer antes
de adicionar um valor novo é de onde ele vem, não só o que ele ajuda o
agente a fazer.

## Contrato de plugin de canal (`apps/inbox`)

Três contratos obrigatórios em conjunto por `ChannelType`, resolvidos via
DI **keyed** (`AddKeyedSingleton`): `IChannelConfigValidator` (valida
credencial antes de criptografar), `IOutboundMessageSender` (entrega
resposta do agente ao canal), `IInboundWebhookHandler` (processa webhook
recebido em `POST /webhooks/{channelId}`, rota genérica única). Um quarto
contrato **opcional**, `IChannelWebhookProvisioner` (configura o webhook
automaticamente do lado externo no cadastro — só o Telegram implementa;
WAHA fica manual). Checagem de integridade no startup
(`ValidateChannelAdapterRegistrations`) garante que os contratos
obrigatórios (e o opcional, quando presente) estejam completos por tipo,
falhando o boot se não estiverem.

O contrato **não** carrega status de entrega: `IOutboundMessageSender` que
retorna sem lançar é sucesso, exceção é falha. Foi o que permitiu
persistir status de entrega sem tocar o contrato de plugin.

Adapters reais hoje: **WAHA** (self-hosted, engine GOWS, config manual do
webhook, sem verificação de autenticidade do webhook de entrada — risco
aceito, classificado explicitamente na allowlist de rotas anônimas) e
**Telegram** (Bot API, `setWebhook` automático com `secret_token` gerado
por canal, verificação nativa do webhook de entrada).

## Convenções estabelecidas (o "estilo da casa")

Essas regras não estão escritas em nenhum lugar do código — são o
padrão que se formou change após change. Vale checar contra elas antes
de propor algo nesta base:

1. **Sequenciamento**: catálogo/cadastro → vínculo → execução → UI,
   sempre nessa ordem, em toda linha de trabalho (MCP, delegação, canais,
   histórico de conversa). Corolário: se a etapa de UI descobrir que
   precisa de um dado que o backend não serve, isso é achado a reportar e
   sequenciar — nunca backend feito de improviso dentro da change de tela.
2. **Sem abstração prematura**: esperar 2-3 consumidores reais antes de
   extrair código compartilhado (`libs/ProviderCatalog` só nasceu com 2
   consumidores reais). O mesmo vale para configuração: uma opção só
   existe quando há cenário real de alguém precisar de outro valor — TTL
   do token de operador é configurável (política de produto, varia por
   ambiente), TTL do token de serviço não é (nunca sai do processo).
   Mesmo raciocínio decide jsonb vs. tabela relacional: jsonb quando não
   há necessidade de FK/consulta relacional, tabela quando os dois lados
   do vínculo são entidades com identidade própria.
   No frontend a régua é a mesma e o gatilho é repetição **já observada**,
   nunca prevista: os três componentes visuais compartilhados do painel
   (card seccionado, rótulo de seção, cabeçalho de detalhe) saíram de cinco
   cópias idênticas, quatro estruturas iguais e três cabeçalhos repetidos —
   contados antes de extrair.
3. **Credenciais**: sempre write-only, nunca retornadas em nenhuma
   resposta, criptografadas com AES-GCM (chave por domínio/app via env
   var), padrão "deixe em branco para manter a atual" na edição. Segredo
   compartilhado entre apps (chave de assinatura de token) vai por
   configuração com o mesmo valor nos dois, nunca por chamada entre
   processos.
4. **Degradação graciosa**: falha de dependência externa (MCP, alvo de
   delegação, entrega de webhook, envio ao canal) nunca derruba a task
   principal — é logada, não propagada. Quando a falha precisa ser visível
   para o operador, ela vira **estado persistido além do log**; a
   degradação continua graciosa, só deixa de ser invisível. Todo
   `BackgroundService` deste monorepo captura suas próprias falhas
   recuperáveis por unidade de trabalho (mesmo nível de granularidade de
   `TaskJobConsumer`/`DebounceSweepService`) — nunca depende de
   `HostOptions.BackgroundServiceExceptionBehavior` para isso:
   `Ignore` não reinicia o serviço após a primeira exceção (fica "vivo
   mas morto", pior que o crash que evita), e é política de host, não de
   dependência específica (`inbox-sweep-service-resiliencia`). O defeito
   que essa degradação graciosa evita não é exclusivo de
   `BackgroundService`: já apareceu duas vezes com a mesma forma — um
   `try/catch` correto existe, mas a chamada que mais realisticamente
   falha (consulta ao banco, decifragem de credencial) está posicionada
   fora dele, então a exceção escapa antes de chegar à proteção
   (`DebounceSweepService`, um `BackgroundService`;
   `PushNotificationEndpoints.DeliverResponseAsync`, um handler de
   endpoint HTTP comum — `inbox-push-notification-decrypt-resiliente`).
   Ao revisar qualquer `try/catch` de degradação graciosa, checar se
   **todas** as chamadas capazes de falhar antes do resultado esperado
   estão dentro dele, não só a que motivou o `catch` originalmente — não
   é uma checagem restrita a `BackgroundService`.
5. **Testes de integração com infraestrutura real** — Testcontainers
   Postgres/RabbitMQ, não mocks, para os caminhos principais; fakes só
   para dependências HTTP externas. Toda mudança de comportamento pede
   asserção explícita (nunca "processado corretamente" genérico), e todo
   caso "com item" ganha o par "sem item"/vazio testado.
6. **Nunca confiar em SDK de terceiro de memória** — decompilar
   (`ilspycmd`) ou buscar documentação real antes de codificar contra
   qualquer comportamento não verificado. Vale também para nomes de
   header, campos de payload e rotas de sistemas externos citados dentro
   de spec: requisito errado sobrevive ao archive.

   **E o alvo mais perigoso não é o SDK de terceiro — é o próprio
   repositório descrito de memória dentro de um item aberto.** Um item de
   `02-HISTORICO_E_STATUS.md` afirmava que "o catálogo de MCP e o de
   agentes ordenam por nome", e pedia decidir se a listagem de bases, que
   ordena por `CreatedAt`, era acidente a corrigir. Lido no código, os
   **quatro** catálogos de `apps/api` ordenam por `CreatedAt` e quem ordena
   por nome são as consultas de vínculo: não havia divergência, não havia
   acidente, e a decisão pedida não existia. A premissa saiu do item para
   o prompt da change seguinte e voltou como enunciado — item aberto é
   lido depois, por quem não tem o contexto de quem o escreveu, e é aí que
   ele deixa de ser anotação e vira instrução. O dano é o mesmo das outras
   formas desta convenção, e chega mais longe: decisão tomada contra uma
   realidade que não existe.

   **Conferir referência não é conferir afirmação**, e é a parte que
   engana. O mesmo item já tinha passado por uma conferência que corrigiu
   as linhas defasadas (`:34`/`:51` viraram `:36`/`:53`) — saiu dela com as
   linhas certas **e** a frase sobre os catálogos errada, agora com a
   autoridade de ter sido revisado. Linha, caminho de arquivo e nome de
   símbolo se conferem rápido e dão sensação de item verificado; a frase
   em prosa que descreve *o que o código faz* é a que decide a próxima
   change, e é a que ninguém abre o arquivo para checar.

   **A forma mais difícil de pegar: a fonte foi consultada corretamente e a
   PERGUNTA estava errada.** As formas acima são sobre não verificar, ou
   verificar a referência em vez da afirmação. Esta é outra coisa — verificação
   bem feita, medição correta, número certo, respondendo a uma pergunta que não
   era a que decidia.

   Medido em `knowledge-base-indexacao`: a verificação contou os sítios de
   `PostgreSqlBuilder` para dimensionar a troca de imagem do Postgres, e chegou a
   11 — número correto. Mas **imagem e provider são camadas diferentes**: a
   imagem decide se a extensão existe no servidor; `UseNpgsql` decide se o EF
   sabe **mapear o tipo**. Nenhuma implica a outra. Os sítios que a mudança
   realmente alcançava eram **~38**, e o erro só apareceu na primeira
   materialização, com uma mensagem que parece defeito de modelo.

   **Por que engana mais que as outras:** o resultado tem toda a aparência de
   evidência. Há um número, ele saiu de uma varredura no código, e ele está
   certo. Não há nada a "conferir de novo" — o que falta é perguntar *o que
   exatamente esta mudança alcança*, e só depois medir. O sintoma a procurar é
   uma verificação que mede **um nome** (uma string, um tipo, um arquivo) quando
   a mudança age sobre um **mecanismo**.

   **Corolário útil, medido na mesma rodada: no EF Core, mapeamento inválido
   falha de forma GLOBAL e IMEDIATA, nunca latente.** A validação é do **modelo
   inteiro**, na primeira materialização — não por entidade, quando alguém for
   consultá-la. Foi isso que derrubou 21 testes que não tocam a entidade nova, e
   é a mesma propriedade que provou que o caminho de produção não estava
   passando por acidente: sem `UseVector()` em `AddInfrastructure`, `apps/api`
   falharia em **qualquer** operação de banco, não só no dia em que alguém
   lesse fragmento. Vale guardar porque o medo de "caso silencioso esperando o
   primeiro consumidor" é natural aqui, e nesta camada ele não existe.

   **E o alvo se estende a comportamento de infraestrutura que o código
   pressupõe sem dizer.** Duas medições de `ordenacao-desempate-listas-vinculo`
   valem como precedente, e uma delas é resultado **negativo**:

   - **A collation do PostgreSQL e o comparador de `string` do .NET discordam.**
     Verificado nos dois runtimes reais (`postgres:18` com
     `datcollate=en_US.utf8`/`datlocprovider=c`; .NET 10 com ICU): para
     `suporte-alfa` × `Suporte Alfa` a ordem sai invertida, e igual nas três
     culturas testadas. Ordenar "por nome" em dois lugares com dois comparadores
     não é a mesma ordenação, e nenhum teste que desserialize a resposta enxerga
     isso — só o que compara as duas superfícies com um par medido.
   - **A crença de que `Guid.CompareTo` compara o primeiro campo com sinal, e
     portanto discordaria do `uuid` do PostgreSQL, é FALSA no .NET 10.** Medido:
     100.000 pares aleatórios e os casos de fronteira, zero discordâncias,
     conferido contra um PostgreSQL real. Registrar o resultado negativo importa
     tanto quanto o positivo — sem essa medição, o desempate por `Id` nas duas
     superfícies teria sido uma aposta, e "verificar" teria custado o mesmo que
     supor errado.

   **E o alvo inclui a própria ferramenta de processo.** Os 39 `Purpose`
   placeholder das specs vivas não foram 39 esquecimentos: o passo 4d de
   `.claude/skills/openspec-sync-specs/SKILL.md` **manda** escrever
   `Purpose ... (can be brief, mark as TBD)` e não manda procurar um `Purpose`
   na delta. Foram 39 execuções corretas de uma instrução errada. Antes de
   atribuir um padrão repetido de descuido a quem executa, ler a instrução que
   essa pessoa estava seguindo.

   Na prática: item aberto que descreve comportamento de código carrega o
   arquivo e a linha de onde a afirmação foi **lida**, ou é escrito como
   suspeita explícita ("a conferir") em vez de fato. E toda change que
   consome um item aberto reconfere as afirmações dele contra a árvore
   antes de decidir qualquer coisa — a verificação da convenção 6 vale
   para o registro interno, não só para dependência externa.
7. **Frontend**: página busca dados e repassa como prop; componente
   apresentacional nunca importa hook de query/mutation diretamente.
   Cada feature mantém seu próprio `request<T>`/`ApiError` fino — sem
   cliente HTTP compartilhado entre features. Preocupação transversal
   (token) entra como módulo fino que cada `request<T>` importa, não como
   cliente comum. Feature se organiza por conceito de domínio, não por
   origem do dado — uma rota `/channels/{id}/algo` pode pertencer a outra
   feature que não `channels`.
8. **Checagem de integridade no startup** é o padrão para todo registro
   que possa ficar incompleto em silêncio — não documentação em prosa. Já
   aplicado a cinco casos de natureza diferente: DI keyed para pontos de
   extensão tipo-plugin (contrato de canal), classificação de rotas
   anônimas (autenticação), configuração de fuso horário do sistema
   (`apps/workers` — valor resolvido vs. valor declarado em `TZ`, não só
   presença da variável), e extratores de conteúdo por `SourceType`
   (`apps/api`). A checagem vale nos **dois sentidos** quando houver lista
   esperada e realidade mapeada: item declarado sem contraparte real é tão
   problema quanto o inverso. DI keyed continua sendo o padrão para pontos
   de extensão tipo-plugin.

   **Quinto caso, e de forma nova: a primeira checagem desta base que faz
   I/O no boot.** `ValidateEmbeddingIndexConsistency` (`apps/workers`)
   compara o provedor, o modelo e a dimensão de embedding **declarados na
   configuração** com os **gravados nos fragmentos** (`SELECT DISTINCT`
   sobre as três colunas de proveniência). Índice vazio sobe; uma
   combinação igual à declarada sobe; **qualquer outra coisa falha o
   boot** — inclusive mais de uma combinação distinta, que é corrupção por
   troca anterior não detectada e reprova ainda que uma delas seja a
   declarada.

   O motivo de falhar em vez de tolerar: vetores de modelos diferentes são
   **incomparáveis**, e a busca continuaria devolvendo resultados — errados,
   sem erro nenhum. Não há modo de tolerância nem bypass.

   **O que sustenta o custo do I/O no boot foi verificado, não suposto:**
   no compose, `apps/workers` declara `depends_on: migrator:
   service_completed_successfully`, e o `migrator` declara `postgres:
   service_healthy` — transitivamente, em produção o banco está saudável e
   migrado antes daquele processo subir. **Consequência local a declarar:**
   quem roda `dotnet run` fora do compose passa a não subir com o Postgres
   parado, onde antes subia e falhava por mensagem. `apps/workers` não faz
   nada sem banco e sem fila, então a diferença prática é o momento e a
   clareza da falha — mas é mudança que se nota antes de entender.

   **E esta forma tem um modo de reprovar que as outras não têm: a
   vacuidade.** `SELECT DISTINCT` sobre tabela vazia devolve conjunto
   vazio, e conjunto vazio sobe — então um guarda exercitado só contra
   **estado limpo** fica verde com e sem a implementação, porque nunca
   chega à comparação. É a quinta forma da convenção 15 aplicada a uma
   checagem de boot, e a saída é a mesma: todo cenário de divergência roda
   sobre **índice povoado**, e há um teste que afirma essa precondição.

   **Existem duas formas do padrão, e a escolha entre elas tem
   consequência de teste.** Três das quatro checagens rodam sobre o **host
   construído** (`IHost`/`WebApplication`, depois do `Build()`) e derrubam
   o boot de verdade. A quarta — e qualquer outra que inspecione
   **descritores de DI keyed** — precisa rodar sobre a
   `IServiceCollection`, **antes** do `Build()`, porque é ali que as
   chaves registradas são enumeráveis; sobre o host seria preciso resolver
   os serviços para descobri-las. `ValidateChannelAdapterRegistrations` já
   tinha essa forma, sem que a distinção estivesse escrita.

   O custo da forma `IServiceCollection` é que testar a checagem
   diretamente verifica **a extensão**, não o caminho de boot: mover ou
   remover a chamada do `Program.cs` deixa esses testes verdes e a
   aplicação sobe com registro divergente. Quem usar essa forma paga um
   teste a mais, que captura a `IServiceCollection` **real** pelo
   `ConfigureServices` da `WebApplicationFactory` (que roda depois de
   todos os registros do `Program.cs`) e afirma a coerência ali, sobre a
   composição de produção. É barato — sem container, sub-segundo — e é o
   único que pega o caso "chamada removida **e** registro divergente":
   verificado reintroduzindo exatamente esse defeito, com os testes da
   extensão ficando verdes e só esse reprovando.
9. **Toda vez que a implementação diverge do `design.md` aprovado**
   (um achado técnico durante a implementação muda a decisão), o
   `design.md` é corrigido pra refletir a causa real — nunca fica só
   registrado no resumo do chat.
10. **Todo risco nomeado na seção de Risks tem contraparte verificável**
    — cenário na spec e teste, ou uma justificativa explícita de por que
    não é testável. Risco listado e não coberto é o padrão de falha mais
    caro desta base, porque parece cuidado sem ser.
11. **Teste de acordo entre dois lados usa o artefato real produzido por
    um contra o outro** — nunca um artefato forjado no teste com a mesma
    configuração dos dois lados. Fixture montado pelo próprio teste passa
    igual com o comportamento certo e com o errado, e por isso não prova
    nada: já aconteceu com a chave de assinatura de token entre `apps/api`
    e `apps/inbox`, e com o formato de fio dos enums de `Message` entre
    `apps/inbox` e `apps/frontend`. Na prática: token emitido pela outra
    API de verdade, JSON bruto lido da resposta real em vez de round-trip
    pelo mesmo tipo, e — quando o ponto é provar independência — a outra
    ponta deliberadamente inalcançável durante o teste.
12. **Contrato entre apps inclui o formato de fio, não só os campos** —
    nome, tipo e forma de serialização (enum como string, nunca ordinal)
    fazem parte do requisito. Enum sem formato declarado já saiu como
    inteiro por omissão e obrigou o consumidor a decodificar por índice.
    Defeito de formato pertence a quem expõe, e se corrige lá, em change
    própria sequenciada antes — não se contorna no consumidor. Vale
    também para as opções de serialização em si (encoder de escaping,
    naming policy, tratamento de null), não só a representação de um
    campo específico: dois sites que serializam o mesmo tipo com opções
    diferentes produzem payloads estruturalmente diferentes mesmo com os
    campos certos. Já aconteceu com um codec de `apps/workers`
    serializando payload A2A sem as opções (`A2AJsonUtilities.DefaultOptions`)
    que o resto do pipeline usa, divergindo em encoding de aspas de um
    jeito que só um teste de acordo real entre `apps/api` e
    `apps/workers` pegou (`crossapp-session-codec-encoder`).
    A cláusula de naming policy voltou a morder em `agente-enderecos-a2a`,
    de forma mais sutil: a política camelCase minúscula apenas a primeira
    letra, então uma propriedade `A2A` vai para o fio como `a2A` e o
    consumidor que lê `a2a` recebe campo ausente. **Teste que desserializa a
    resposta para o mesmo tipo é cego a isso** — a chave passa pela mesma
    política na ida e na volta e sempre casa. O teste que pega inspeciona o
    texto do JSON. Sempre que um nome de propriedade tiver sigla, número ou
    maiúsculas consecutivas, fixar o nome no fio explicitamente.
13. **A UI nunca afirma mais do que o sistema sabe** — se o dado não é
    coletado, a interface não o insinua. Um único indicador de envio,
    jamais dois checks, porque entrega e leitura são recibos que o desenho
    escolhido não coleta; valor de enum desconhecido renderiza indicador
    neutro, nunca reaproveita o indicador de sucesso. A asserção que
    protege isso é a **negativa** (afirmar a ausência do segundo
    indicador), porque é ela que impede a regressão bem-intencionada de
    "deixar parecido com o WhatsApp".

    **E a pergunta que a convenção mais custa a responder não é "exibo ou não",
    é "este zero pode ser exibido".** Dois zeros do mesmo domínio, com o mesmo
    valor na tela e significados opostos, decidiram duas etapas em direções
    contrárias:

    | zero | o que é | pode exibir? |
    |---|---|---|
    | `documentCount` do resumo de indexação | contagem **medida** — a agregação percorreu os documentos daquela base e não achou nenhum; a projeção final é sobre as BASES, não sobre os grupos do `GroupBy`, exatamente para que a base vazia apareça | **sim** — `Nenhum` na tela é verdade |
    | `fragmentCount` de documento nunca indexado | **default de uma coluna que ninguém escreveu** | **não** — exibir afirmaria que a indexação rodou e não achou nada |

    A régua é **a proveniência do zero, nunca o tipo do campo**: um veio de uma
    medição que aconteceu, o outro de uma medição que não aconteceu. E há um
    terceiro caso que os dois primeiros ensinam a não confundir com nenhum deles
    — **a ausência de linha**: quando o consumidor faz duas requisições
    independentes e uma delas não trouxe aquele registro, isso não é zero nem é
    vazio, é **desconhecido**, e zerar ali afirma uma contagem que ninguém fez.
    Três estados, três textos: valor, vazio, travessão.

    **E existe um QUARTO estado, que é o mais fácil de colapsar no travessão:
    "sei que não existe".** Achado na aba de diagnóstico do índice (5c), onde os
    quatro convivem na mesma tela:

    | estado | exemplo na aba de diagnóstico | como se diz |
    |---|---|---|
    | valor | o índice tem uma combinação gravada | provedor, modelo, dimensão |
    | vazio | a base não tem documento, e a contagem mediu zero | `0 de 0` |
    | travessão | **não sei** — a consulta não respondeu, ou o registro não veio | `—`, ou o aviso de indisponibilidade |
    | **sei que não existe** | o índice **inteiro** está vazio: a rota respondeu `200` com `[]` | uma **explicação**, e nenhuma linha |

    A diferença entre os dois últimos é o que decide a tela. `—` afirma *"houve
    uma pergunta sem resposta"*; índice vazio é uma resposta que **chegou**, e
    dizê-la com travessão perde a única informação que ela carrega. Pior: a mesma
    aba tem um caminho de falha de leitura, que é o travessão de verdade — usar o
    símbolo nos dois apaga a distinção **onde ela existe**.

    O protótipo colapsava os dois, e o custo era o inverso do esperado: não
    exibia nada falso, mas gastava o vocabulário de "não sei" num fato conhecido.
    A régua continua sendo **a proveniência do dado, nunca o tipo do campo** — e
    ela agora separa quatro origens, não três: medição que deu zero, medição que
    não aconteceu, registro que não veio, e **resposta que chegou dizendo que não
    há**.

    Quem acrescentar contagem a uma tela **escreve a proveniência do zero junto
    com o campo**, e não só a regra de exibição — foi assim que a etapa das
    colunas do catálogo de bases pôde reusar a distinção em vez de reabrir a
    decisão, e é a frase que impede a etapa seguinte de "consertar" a exibição.

    **E existe uma forma da convenção que NENHUMA revisão da tela pega, porque a
    tela não mudou: a afirmação que era verdadeira quando foi escrita e que outra
    etapa tornou falsa.** As formas acima decidem *o que exibir agora*, a partir
    da proveniência do dado. Esta é sobre o **momento**: a tela continua correta
    contra o sistema que existia no dia em que foi implementada, e passa a mentir
    sem ninguém a tocar.

    Medido na 5a-4. O formulário de base tinha um bloco intitulado *"Como o agente
    vê esta base"* exibindo nome e descrição, e o detalhe uma nota dizendo que a
    descrição *"é a descrição da ferramenta que o agente vê"*. As duas eram
    verdade na 5a-1. A etapa 4 criou `KnowledgeToolDescription.Build`, que monta a
    descrição da tool com um prefixo, o texto cadastrado e um bloco fixo de como
    ler o resultado — e as duas viraram afirmação de identidade onde passou a
    haver **parte**. **A frase estava em três superfícies**, e a terceira é a que
    mostra o alcance: o `CHANGELOG` da própria etapa 4 registrou *"com a descrição
    cadastrada da base como descrição da tool"*, ou seja **nasceu falsa**, escrita
    pela change que a tornou falsa.

    **Por que engana mais que as outras formas:** não há nada de errado no código
    da tela, nenhum dado novo chegou, nenhum teste reprova — as asserções da suíte
    estavam escritas contra a metade estável da frase e passaram verdes com o
    defeito presente. É a convenção 15 aplicada a cópia, e o único modo de detecção
    que funciona é **ler o que a tela afirma contra o código que ela descreve**,
    periodicamente e sobretudo depois de uma etapa que mexa no mecanismo descrito.

    **Régua:** afirmação de tela sobre o que outro app faz carrega, no comentário,
    **o arquivo e a linha de onde foi lida, mais o gatilho** — a condição sob a
    qual ela deixa de valer. E a afirmação é **estrutural** ("o sistema envolve
    este texto em instruções fixas"), nunca a reprodução do texto do outro app, que
    seria segunda fonte de verdade e envelheceria a cada edição de prosa lá.

14. **Mudança visual só é verificada por olho humano** — a suíte roda em
    jsdom, que não enxerga cor, contraste nem layout. Uma mudança de tema
    ou de composição pode deixar a suíte inteira verde e o painel
    ilegível, e isso aconteceu: três etapas do redesenho passaram verde
    com defeitos que só a comparação com o protótipo pegou. Change que
    mexe em aparência traz conferência manual como tarefa própria, tela a
    tela, nos dois esquemas de cor — e a conferência é **iterativa**,
    porque cada correção muda o que fica visível (migrar o card revelou o
    divisor recuado, corrigir o divisor revelou a faixa desalinhada). O
    que a suíte pode cobrir é contrato: que o token vale o que a spec diz,
    que o componente recebe o que promete, que o link aponta para a rota
    certa.
15. **Um guarda só vale depois de ter falhado contra o defeito real** —
    escrever o teste, reintroduzir o defeito de propósito, ver reprovar, e
    só então manter a correção. **Quatro** guardas desta base passaram verde
    **com o defeito presente** antes de serem consertados: o que varre
    tons fixos de superfície (a expressão não cobria valor dentro de
    ternário, que era justamente a forma do caso real), o que prende a
    altura da faixa de cabeçalho, o que afirma o nome do campo no fio, e o de
    "Distinção de tools com nomes iguais entre servidores diferentes"
    (`mcp-tool-execution`), que cobria só servidores de nomes **diferentes**
    enquanto o requisito já estava violado por dois `McpServer.Name` que
    sanitizam para a mesma cadeia (`dedupe-global-nome-de-tool`).
    Guarda não verificado é pior que nenhum, porque dá impressão de
    cobertura sem ter.

    **E o erro tem uma segunda forma, que só aparece na implementação: guarda
    no lugar errado.** Um teste pode reprovar antes e depois da correção — e
    passar a impressão de estar funcionando na primeira metade — quando ele
    afirma a garantia no componente errado. Em `dedupe-global-nome-de-tool`
    três guardas reprovaram contra o defeito **e continuaram reprovando depois
    da correção**, porque afirmavam unicidade dentro de cada resolvedor,
    enquanto a correção é global no ponto que une os dois conjuntos. Ao escrever
    o guarda, checar não só que ele reprova, mas que ele reprova **no
    componente que a correção vai tocar**.
    **E a quinta forma, achada em `ordenacao-desempate-listas-vinculo`: guarda
    cujo critério é uma ordem que o banco às vezes já produz sozinho.** O guarda
    de desempate afirma "ordem crescente de `id`" — e, sem o `ThenBy`, o
    PostgreSQL **às vezes já devolve nessa ordem**, porque um index scan pela
    chave primária emite exatamente assim. *"Ordenado por `id` porque o `ThenBy`
    existe"* e *"ordenado por `id` porque o plano calhou"* são a mesma
    observação, e nenhum arranjo de teste as separa. Medido nos dois sentidos: o
    guarda novo de `mcpServers` passou com o defeito presente em **1 de 3**
    execuções da sua classe (e reprovou 5/5 rodando isolado, que é o jeito
    enganoso de "conferir"), enquanto o guarda entregue pela etapa 3 reprovou
    **4/4** na sua forma de consulta. Ou seja: a confiabilidade depende do plano,
    que varia por consulta e por povoamento da tabela, então **"reprovou quando
    eu conferi" não é propriedade durável** — e conferir o guarda isolado é
    justamente o que esconde o problema.

    A saída não é abandonar o guarda comportamental, que é a expressão do
    requisito: é **pareá-lo com uma asserção determinística sobre o artefato que
    a correção produz**. Ali o artefato é o SQL emitido — capturado do log de
    `Executed DbCommand` do EF Core durante a requisição real, nunca uma consulta
    remontada no teste (convenção 11) —, e a asserção de que o `ORDER BY` termina
    no desempate reprova em 100% das execuções. A régua geral: quando o efeito
    observável do defeito **coincide às vezes com o efeito do acerto**, o guarda
    comportamental sozinho é insuficiente por construção, e o par determinístico
    é obrigatório.

16. **Papel visual que troca de ponta da escala precisa de variável
    declarada por esquema** — `gray[n]` é claro nos dois esquemas e
    `dark[n]` é escuro nos dois, então um tom fixo usado como fundo de
    superfície funciona num tema e quebra no outro. O mesmo defeito
    apareceu três vezes no redesenho: fundo da página, faixa de cabeçalho
    de card e de tabela, e linha selecionada no histórico de sessões. A
    causa é conceitual — no tema claro a superfície sutil é *mais clara*
    que o card, no escuro é *mais escura* —, e a saída é uma variável
    declarada nos dois esquemas pelo resolver, ou um token da biblioteca
    que já troque sozinho. Fechado por guarda estático (ver 15).

    **E o inverso também vale, medido em `frontend-knowledge-base-documentos`: a
    variável nova só nasce quando não há token que já troque.** A faixa de falha
    por linha troca de ponta da escala entre os esquemas — gatilho desta
    convenção — e **não** ganhou variável, porque `Alert` do Mantine tem variante
    default `light` e resolve por `theme.variantColorResolver`, que é ciente do
    esquema (conferido no pacote instalado, não suposto). O precedente da casa
    para esse papel já existia em `AgentKnowledgeTab.tsx:176`. Inventar
    `--buteco-danger-band` ali seria variável sem consumidor exclusivo
    (convenção 2) e um segundo lugar onde a cor de perigo pode divergir.

17. **Contrariar o protótipo é resultado legítimo, e vira registro** —
    handoff de design feito sem acesso ao código diverge da realidade, e a
    divergência se resolve com decisão explícita, não com implementação
    silenciosa nem com fidelidade cega. No redesenho isso aconteceu cinco
    vezes: campo que a API não devolve, progresso que ninguém mede,
    contagem que exigiria segunda requisição, identidade de operador que o
    login não fornece, e um tom de rótulo que reprova no contraste mínimo
    que a própria identidade visual exige. Regra que o sistema já
    escreveu vence protótipo; a recusa vai para o `design.md` com o
    número que a sustenta.

18. **Estimar tamanho de change por diffstat de commit anterior engana de
    duas formas conhecidas**, e as duas foram medidas. Primeira: o headline de
    um commit inclui os artefatos OpenSpec — `b5df504` tem 30 arquivos / 1593
    linhas, mas 8 arquivos / 787 linhas são `openspec/`, então o código real
    foram 21 / 592. Comparar trabalho de código contra esse número subestima
    por construção. Segunda: cobertura de teste varia por uma ordem de
    grandeza entre changes (`b5df504` tem 1 arquivo de teste;
    `knowledge-base-catalogo-documentos` tem 11, com 42% de todo o trabalho
    manual), e diffstat não distingue.

    **E projetar só depois que a verificação fecha.** Uma projeção por componente
    feita antes do fim da verificação erra para baixo por construção, porque os
    componentes que a verificação ainda vai descobrir não estão nela para serem
    contados. Medido em `dedupe-global-nome-de-tool`: a projeção subiu ~15% entre
    a primeira redação do `design.md` e a revisão, sem nenhuma mudança de escopo
    — três itens (uma subclasse de wrapper do SDK, um cenário de comparação de
    caixa, um cenário de histórico persistido) só existiram depois de decompilar
    o SDK e consultar o banco. O número **não** é um fator a somar em projeções
    futuras; a lição é o momento de contar. Projeção feita durante a verificação
    é rascunho, não estimativa.

    **Projetar produção por "linhas de lógica" subestima por construção em change
    cujo entregável inclui registro de decisão ou de mecanismo — nessas, o
    comentário é o produto.** Promovida na terceira ocorrência, e a terceira é de
    **sinal contrário**: é a que mostra que a regra funciona quando aplicada,
    como a quinta evidência da convenção 22.

    | # | change | lógica : comentário, em produção | o que a proporção era |
    |---|---|---|---|
    | 1 | `dedupe-global-nome-de-tool` | `ToolOrigin`/`RenamedAIFunction` | documentação de decisão |
    | 2 | `lock-de-contexto-falha-terminal` | **48 : 111** (2,3:1), em `apps/workers/src/Buteco.Workers/Agents/` | três registros de mecanismo |
    | 3 | `delegacao-ciclo-no-cadastro` | **92 : 121** (1,3:1), em `apps/api/src/Buteco.Api/AgentDelegations/` | um registro de mecanismo, e a proporção foi **projetada em 1,4:1 antes de escrever o código** |

    Na 3 a regra foi aplicada preventivamente e **a dimensão de comentário veio
    certa** — 1,32:1 entregue contra 1,4:1 projetado. O volume total ainda errou
    para cima (~136 projetadas contra 213 entregues), mas **por outro motivo**, e
    é esse contraste que dá a regra:

    - **a mistura comentário:lógica é projetável**, desde que se pergunte quantos
      registros de mecanismo a change entrega — a 2 entregava três, a 3 entrega
      um, e as proporções seguem essa contagem;
    - **a contagem de componentes não é**, e foi ela que errou: uma função pura
      saiu do handler para poder ser testada isolada, e virou a segunda função
      pública de um tipo projetado com uma só.

    **E as duas direções de erro nomeadas de antemão no `design.md` da 3 estavam
    as duas erradas** — nenhuma das duas aconteceu, e o desvio veio de um terceiro
    lugar que não estava na lista. Nomear direções de erro não substitui contar
    componentes; é a contagem que carrega a projeção.

    **E as duas dimensões erram por motivos diferentes — registrar a direção
    sozinha não serve de nada.** A projeção por componente conta os componentes
    que a etapa parece precisar; a verificação pode tanto **acrescentar** um que
    ela não tinha (e aí a projeção errou para baixo) quanto **eliminar** um que
    ela previa, ao descobrir que o repositório já resolveu aquilo de outro jeito
    (e aí errou para cima). A causa que vale carregar é estrutural, não
    direcional: **projeção por operação CQRS conta arquivos criados, não
    modificados**, e o blast radius de um record compartilhado é todo em
    modificação. Isso vale muito além do caso que o mostrou: qualquer change que
    acrescente um campo a um response usado por N handlers tem contagem de
    arquivo dominada por modificação — em `knowledge-base-vinculo-agente`, 12
    arquivos modificados, 11 deles por uma a três linhas. O dado já estava à
    vista na decomposição acima e ninguém o usou como custo unitário: a linha
    "Modificados" é 5 arquivos / 301 linhas, e nenhum custo por operação CQRS a
    produz. **Projetar "modificados" separado, a partir do blast radius lido no
    código**, não a partir do número de operações.

    **E esse método foi medido e acertou — o que fecha a convenção pelos dois
    lados.** `knowledge-base-vinculo-agente` projetou 25 arquivos à mão e
    entregou **25**, o primeiro acerto de contagem de arquivo da série, depois
    de duas medições que erraram. Uma convenção construída só de casos em que se
    errou diz o que evitar, não o que fazer; esta agora diz as duas coisas:

    - **o que errava** — projetar por operação CQRS, que conta arquivos
      *criados* e é cega aos *modificados*;
    - **o que acertou** — contar criados e modificados **separadamente**, com o
      blast radius verificado no código **antes** de projetar. Na medição, os 8
      sites de construção de `AgentResponse` saíram com 1 a 3 linhas cada
      (3,3,3,3,3,3,2,1), exatamente o perfil previsto.

    **Refinamento do custo unitário, com a causa e não só o número**: o custo de
    ~19-21 linhas por cenário de teste **subestima cenário que precisa de
    arranjo próprio**. Os três mais caros daquela change — desempate com ordem
    de inserção invertida, listagem com três agentes e vínculos cruzados, e dois
    `PUT` vizinhos com catálogo MCP montado — custaram 25-40 linhas cada. É
    ajuste de custo unitário, não erro de método: a projeção de linhas ficou
    7,5% acima do topo da faixa, e a diferença estava inteira nos testes.

    **E a razão de headline chegou a 3,1x** (39 arquivos / 2922 linhas de
    commit contra 25 / 935 à mão) — a mais extrema já medida aqui. A causa é
    específica e vale saber: o `.Designer.cs` de uma migração carrega o
    snapshot **inteiro** do modelo, não só a tabela nova, então 6 arquivos
    gerados somaram 860 linhas contra 150-200 projetadas. É por isso que
    contagem de headline não serve para nada nesta base, e o diffstat
    decomposto é a única medição útil.

    E a régua que mais corrige a intuição: **contagem de arquivo é dirigida
    pelo número de operações CQRS, não por complexidade.** Numa change medida,
    24 arquivos de comando/handler/result somaram 450 linhas — média de 19
    linhas por arquivo. Uma etapa com muitas operações simples produz muitos
    arquivos minúsculos; uma com poucas operações e lógica pesada produz
    poucos arquivos longos. Projetar as duas com o mesmo fator é o erro.
    Estimar por componente (custo por operação CQRS, por cenário de teste, por
    grupo de endpoints), só sobre código, e citar a âncora **decomposta**, não
    o headline dela.

    **Quarta medição (`frontend-knowledge-base-catalogo`), e ela obriga a separar
    três causas que somadas mentem.** Entregue 52 arquivos / 3078 linhas contra 23
    projetados. Ler isso como "a projeção errou 2x" produziria um fator de
    correção inventado na quinta medição — o erro exato que esta convenção existe
    para não repetir. Decomposto:

    - **Os 21 criados projetados bateram exatos** (21 / 2364 contra 21 / ~2310;
      linhas +2,3%). **Segundo acerto seguido** do método de contar criados e
      modificados separadamente a partir do blast radius lido no código, depois
      dos 25 contra 25 de `knowledge-base-vinculo-agente`. Essa metade está
      confirmada.
    - **21 arquivos modificados a mais, e a causa é reutilizável**: o blast radius
      de um **campo obrigatório acrescentado a um tipo de domínio compartilhado no
      frontend** é a contagem de **fixtures de teste**, não a de componentes.
      Acrescentar `knowledgeBases` ao tipo `Agent` não obrigou a mudar componente
      nenhum e obrigou a tocar 21 arquivos de teste, com **uma linha cada**
      (`knowledgeBases: []`). Quem projetar isso lendo o código de produção erra
      por fator de 20. É a mesma forma do caso já registrado acima do lado do
      backend (12 modificados, 11 deles por uma a três linhas, ao acrescentar
      campo a um response usado por N handlers) — a régua vale nos dois apps.
      **O `tsc` enumera de graça**: tornar o campo obrigatório e ler a lista de
      erros custa menos que qualquer leitura manual.
    - **Escopo acrescentado durante a implementação**, que a projeção não podia
      conter: duas decisões tomadas na conferência manual (card de agentes e
      coluna de uso derivada) valeram +4 criados e +21 modificados. Não é erro de
      projeção; é escopo que não existia quando ela foi feita. **Medição de
      método só compara o escopo que estava projetado.**

    **Quinta medição (`frontend-knowledge-base-resumo-indexacao`), e ela acrescenta
    uma TERCEIRA dimensão de blast radius que a projeção não sabia varrer.** 11
    projetados, **12** entregues — criados exatos (2×2), modificados 10 contra 9.
    O arquivo a mais é `src/app/router.test.tsx`, de **outra feature**, e nenhuma
    leitura do código de produção o apontaria: ele entrou porque mocka
    `knowledgeBasesApi` **parcialmente**, e a função nova precisava entrar no
    override — sem isso a chamada escapava para a rede e derrubava outro teste.

    É a mesma forma das duas já registradas — fixtures de teste ao tornar um campo
    obrigatório; os N handlers que constroem um response compartilhado —, e as
    três dizem a mesma coisa: **o blast radius não está onde se está olhando, e
    cada dimensão dele tem um comando que a enumera de graça.**

    | o que se acrescenta | quem é arrastado | como enumerar |
    |---|---|---|
    | campo obrigatório a um tipo compartilhado | fixtures de teste | `tsc` |
    | campo a um response construído em N lugares | os N handlers | compilação |
    | **função exportada a um módulo de API** | **todo arquivo que o mocke, inclusive em outra feature** | `grep -rl "vi.mock(.*<modulo>"` |

    E um refinamento menor da régua de modificados: **todo arquivo modificado
    arrasta o teste dele.** A projeção de modificados desta change (2 arquivos)
    contou os de produção e foi cega aos testes deles; os três testes modificados
    somaram 60 das 99 linhas do escopo original. Projetar modificados em pares.

    **Quarta causa, e ela não é sobre contagem de arquivo: a unidade de projeção
    precisa casar com a de entrega.** Numa tela cujo valor está no que ela se
    **recusa** a afirmar, cada negativa é um `it()` próprio, nunca uma cláusula
    dentro de um teste positivo — senão a negativa some na primeira refatoração
    que "limpar" o teste. Quem projetar uma tela dessas conta **as negativas**
    antes de projetar linhas.

    **E a 5a-4, primeira a usar essa contagem para PROJETAR em vez de explicar,
    achou o degrau seguinte: a unidade tem de casar com a da SPEC, não só com a de
    entrega.** Projetadas 6 negativas, entregues **8**, com o total de testes
    acertando exato (861 contra ~861 projetados, sobre baseline de 850). A causa
    não foi escopo novo nem negativa imprevista — **as oito estavam nos cenários do
    delta de spec**. A projeção contou negativas *por afirmação recusada*; a spec
    as escreveu *por estado observável*, e duas afirmações têm dois estados cada:
    "o contador não emite veredito de qualidade" vira **abaixo do piso** e **acima
    do piso**; "o preview não se declara o texto completo" vira **o rótulo** e
    **não reproduzir o texto fixo**. Contar afirmações subestima; contar estados
    acerta. **Régua: projetar negativas lendo os cenários da delta, não a lista de
    coisas que a tela se recusa a dizer.**

    **E o que a projeção de modificados não sabe prever, medido na mesma change:**
    ela previu 4 asserções existentes a editar e **nenhuma** foi necessária — as
    quatro estavam escritas contra a metade estável de cada frase, e a change
    editava a outra metade. A única edição que apareceu veio da **conferência
    manual**, não de leitura de código: a tela na frente revelou uma contradição
    entre duas linhas vizinhas que nenhuma varredura apontaria. Blast radius se
    varre; consequência de cópia, não.

    **E a causa estrutural do desvio de LINHAS da 5a-4, que é reusável: numa change
    cujo entregável é a RECUSA, o artefato é a razão registrada — e razão
    registrada mora em comentário, não em código.** Dos +93 de produção, **+80 são
    comentário e +13 são código**; um dos dois arquivos entregou *menos* código do
    que tinha. A projeção leu "duas telas de cópia" e contou JSX. **Régua: quando o
    entregável é uma recusa ou uma decisão registrada, projetar as linhas de
    comentário como item próprio, com custo por razão citada.** Custo unitário
    medido: ~26 linhas para uma recusa de três razões com arquivo e linha.

19. **"Pré-existente" e "ambiental" são conclusões que exigem a baseline, e a
    baseline não fecha sozinha.** Três vezes nesta base uma falha de teste foi
    classificada como pré-existente ou ambiental e a classificação estava errada,
    cada vez por um motivo diferente:

    - `TimeoutException` do `InboxOrchestratorRoundTrip` lido como "latência do
      `podman`", porque o tempo (~21s) parecia próximo do limite. O número não era
      evidência de nada: era o próprio timeout de 20s do teste cortando a espera.
    - `ObjectDisposedException` de `apps/inbox` lida como corrida de disposal do
      `WebApplicationFactory` entre classes. O mecanismo real era outro
      (`InboxFactoryFixture` construindo o host antes de migrar).
    - Em `dedupe-global-nome-de-tool`, `TaskJobConsumerTests` reprovando em bloco
      lido como "limite pré-existente de contenção de containers". **Desmentido
      por baseline**: `git worktree` limpo em `6956d79` passa 132/132 em paralelo.
      A 13ª classe de host era da própria change.

    Daí a regra: **antes de classificar uma falha como pré-existente ou ambiental,
    rodar a suíte contra a baseline num `git worktree` limpo** — é barato, não
    mexe na árvore de trabalho, e é a única coisa que separa "já estava assim" de
    "eu quebrei".

    **E a metade que a baseline não resolve:** uma baseline que *também* falha
    remove a hipótese de regressão, e **só** ela. Não promove o sintoma a
    "ambiental" nem dispensa achar a causa. Foi exatamente esse o erro da segunda
    leitura do `TimeoutException` (`inbox-instante-mensagem`): o bisect em
    worktree comparou base e HEAD, viu falha idêntica, e registrou "causa
    ambiental confirmada por evidência direta" — e a causa real só apareceu na
    terceira leitura. Baseline verde acusa regressão; baseline vermelha não
    absolve ninguém.

    **E uma terceira coisa, que `knowledge-base-indexacao` aprendeu do jeito
    caro: baseline e fechamento GUARDAM A SAÍDA COMPLETA, em arquivo, fora do
    diretório de sessão.** Não filtrada, não resumida.

    Ali a suíte de fechamento reprovou 1 de 212, e a saída tinha passado por um
    `grep` que só capturava a linha de resumo — o **nome do teste se perdeu**.
    Sem o nome não há como separar "contenção" de "teste intermitente meu", e a
    dúvida ficou registrada como residual em vez de resolvida. Rodar de novo não
    recupera: o evento não se repetiu.

    Duas armadilhas concretas, as duas já encontradas nesta base:

    - **`| grep ... | head -N` fecha o cano e mata o produtor por `SIGPIPE`.** A
      rodada parece ter terminado e não produz nem sucesso nem falha.
    - **Filtrar antes de saber se há saída** deixa o caso de erro sem rastro
      justamente quando ele é o que interessa.

    O custo de guardar é um arquivo de ~1 MB. O custo de não guardar é uma
    pergunta que não tem mais resposta. **Isto pertence ao `tasks.md` da change,
    na tarefa de fechamento, e não à disciplina de quem roda** — escrito como
    tarefa, deixa de depender de alguém lembrar.

    **E a variável ambiental mais provável desta base é a própria ferramenta da
    conferência de protótipo, que disputa a máquina com a suíte.** Medido em
    `frontend-knowledge-base-resumo-indexacao`, no mesmo commit e na mesma
    árvore limpa:

    | | `load` na largada | resultado |
    |---|---|---|
    | com o Chrome headless da conferência vivo | **23,95** | **6 reprovações** em 4 arquivos, todas `Test timed out in 15000ms` |
    | com ele encerrado, esperando a carga cair | **3,57** | **722/722** |

    Uma variável mudou, e só uma. A leitura fácil — "timeout de 15 s, deve ser
    flake" — teria fechado como "ambiental" um resultado que some ao encerrar um
    processo.

    **E uma quarta leitura errada, na mesma change, na direção oposta — vale mais
    que as três acima.** Uma reprovação isolada de `router.test.tsx` com `load`
    29,63 foi classificada como contenção pelo mesmo raciocínio, e **não era**:
    era regressão da própria change. O que desmentiu foi o passo (1) da
    discriminação já escrita abaixo — **rodar o arquivo isolado** —, que reprovou
    ~1 em 3 com a máquina descarregada, enquanto a baseline dava 6/6.

    **"A suíte inteira passou depois" é evidência fraca para absolver um teste
    intermitente**, porque a intermitência é exatamente o que uma execução verde
    não distingue. Carga alta explica reprovação; não explica **por que este
    arquivo**. Quando a reprovação é de **um** teste, o arquivo isolado repetido
    decide — e é barato.

    A consequência é de **método, não de disciplina**: a conferência da convenção
    14 é iterativa e mantém o navegador aberto entre rodadas, e a tentação é
    rodar a suíte no meio. **Encerrar o navegador e esperar a carga cair antes de
    CADA execução da suíte**, não só da primeira, e escrever isso como passo nas
    tarefas de baseline e de fechamento. Encerrar por **porta**
    (`lsof -ti :9222 | xargs kill`), não por padrão de linha de comando: na mesma
    change, `pkill -f "<caminho>/server.mjs"` não casou com nada, porque o
    processo tinha sido lançado de dentro do diretório e a linha de comando era
    só `node server.mjs` — o processo velho continuou vivo e a medição seguinte
    saiu falsa.

20. **A UI de acompanhamento de processo assíncrono usa polling CONDICIONAL, com
    a condição em função pura** — `refetchInterval` do TanStack Query aceita
    `(query) => number | false | undefined`, e a primeira consulta do painel a
    usar isso é a listagem de documentos de uma base: devolve o intervalo
    enquanto houver item não-terminal e `false` quando todos chegam a um estado
    terminal. `useSessionMessagesQuery` já fazia polling antes, mas com intervalo
    **constante** — a condição lá é "a tela está aberta", não "os dados ainda
    mudam", e as duas coisas são diferentes.

    A condição mora numa **função pura exportada**, não numa expressão embutida
    no hook, por dois motivos medidos: é testável sem timer, e a mesma pergunta é
    feita por outra parte da tela (a faixa de resumo de itens não-terminais).

    **O guarda é pareado** (convenção 15, quinta forma): uma asserção
    determinística que resolve a opção real contra a query real do cache, e uma
    comportamental com timers falsos. A determinística sozinha não prova que a
    requisição se repete; a comportamental sozinha depende de agendamento.

    **Armadilha que custou uma leitura errada ao escrever isso:** o react-query
    usa `notifyOnChangeProps: 'tracked'` por padrão, e o objeto devolvido é um
    **proxy que registra quais props foram LIDAS**. O componente de `renderHook`
    não lê nada — quem lê é o teste, por `result.current`. Se a primeira espera
    tocar só `isSuccess`, `data` nunca entra no conjunto rastreado e uma mudança
    posterior só em `data` **não provoca re-render**: `result.current.data` fica
    preso no primeiro valor e o `waitFor` seguinte estoura o prazo, com a
    requisição tendo acontecido e devolvido o dado novo.

    A primeira reprodução isolada disso mudou **duas** variáveis de uma vez —
    acrescentou o `refetchInterval` em forma de função *e* deixou de tocar
    `.data` — e creditou o efeito ao `refetchInterval`, quase custando abandonar
    o recurso da biblioteca por um artefato do teste. **Isolamento só vale
    mudando uma variável**, e é a forma da convenção 6 que mais engana: medição
    correta respondendo à pergunta errada.

21. **Comando de varredura que falha em silêncio produz a mesma saída que
    ausência real.** Acréscimo à convenção 6, e cometido *dentro* da change que
    passou a conferência inteira nomeando essa família:
    `grep --include=*.cs` sem aspas vira glob do `zsh`, que devolve
    *"no matches found"* e **nenhuma linha**. A saída vazia foi lida como "a
    entidade não existe em `apps/api`", e a afirmação foi para o `design.md` como
    evidência de uma exclusão de escopo. A conclusão não mudou — nenhuma rota
    projeta aquela entidade, e a etapa dependente continua bloqueada —, mas a
    evidência estava errada e foi corrigida no próprio `design.md` (convenção 9).

    Na prática: varredura que devolve zero resultados só vale como evidência de
    ausência depois de conferir que **o comando rodou**. Um `echo $?`, um caso de
    controle que deveria casar, ou repetir a busca por outro caminho.

22. **Referência medida vale sobre o estado em que foi medida — então ela nasce
    com o estado escrito ao lado e com gatilho de recalibração.** Promovida na
    **quarta** ocorrência, como o item aberto previa, e o que decidiu a promoção
    não foi a contagem: foi a **mesma referência ter quebrado duas vezes, do
    mesmo jeito**, porque ninguém tinha pendurado um gatilho nela.

    O mecanismo: um número medido é citado depois com a autoridade de medição,
    sobre um sistema que já não é o que foi medido. Ninguém mente e ninguém
    erra a conta — o número continua correto sobre o estado antigo.

    As quatro:

    | # | referência | medida sobre | citada sobre | resultado |
    |---|---|---|---|---|
    | 1 | limiar de carga da suíte (load < 5,0) | `WorkerHostCollection` com **7** classes | **11** classes | reprovou 1/212 com a carga **dentro** do limiar |
    | 2 | bar de recall de `0c` (R@1 ≥ 70%) | os 75% de `0b`, num benchmark **saturado** de 44 fragmentos | corpus **discriminante** de 110 | o bar reprovou a própria rodada que ele deveria calibrar |
    | 3 | 7.500 fragmentos | **bytes em disco** | **microssegundos de busca** | o volume de referência de armazenamento já estava acima do teto de latência, e ninguém tinha notado |
    | 4 | limiar de carga da suíte, **de novo** | 7 classes (nunca recalibrado) | **12** classes | segunda quebra da mesma referência |

    **A ocorrência 3 é a variante que vale nomear junto:** ali o sistema **não**
    mudou — mudou a **pergunta** feita ao número. Dois números sobre a mesma
    grandeza aparente (fragmentos) respondendo a perguntas diferentes convivem
    sem se contradizer até alguém citar um no lugar do outro. O sintoma é
    idêntico, e por isso a regra é uma só.

    **Na prática, ao escrever qualquer número medido:**

    - **Escrever o estado ao lado do número**, na mesma frase. Não "load < 5,0",
      e sim "load < 5,0, medido com 7 classes de host". Não "7.500 fragmentos", e
      sim "7.500 fragmentos como volume de disco".
    - **Escrever o gatilho de recalibração**, e ele aponta para uma condição
      observável: "recalibrar quando entrar classe nova na coleção", não
      "recalibrar quando fizer sentido".
    - **Recalibrar é tarefa da change que muda o estado**, não descoberta da
      change seguinte. Quem acrescenta a 12ª classe de host recalibra o limiar.

    **A quinta evidência é de sinal contrário, e é ela que mostra que a regra
    funciona quando aplicada:** o bar de `0d` **recusou** ancorar em número de
    `0b` ou de `0c` — precisamente por causa da ocorrência 2, que já estava
    registrada — e derivou os cortes do mecanismo do próprio desenho (um erro de
    roteamento é irrecuperável; a alternativa tem roteamento perfeito por
    construção). O bar reprovou a hipótese testada, que é o que um bar deve poder
    fazer, e **nenhuma parte dele precisou ser defendida depois**. É o primeiro
    caso desta base em que a regra foi aplicada preventivamente, e ela foi
    aplicada porque a ocorrência 2 estava escrita.

    Parente da convenção 18 (*projeção feita antes de a verificação fechar é
    rascunho*) e da 19 (*"pré-existente" exige a baseline*): as três são sobre
    número citado com mais autoridade do que ele tem.

    **A forma curta, para poder ser citada numa linha de revisão: _régua citada
    sem escopo não é régua._** Ela entrou porque o custo de não a ter já estava
    contado no `02`: a régua de custo de dependência em `AgentExecutionService`
    circulou como **12**, **13**, **15** e **16** — quatro valores, em quatro
    documentos, cada um somando um conjunto diferente e **nenhum com o escopo
    colado**. Nenhum estava errado; nenhum dizia sobre o quê. A versão com escopo
    é **13 sítios de instanciação** (12 em `apps/workers/tests/` mais 1 em
    `tests/InboxOrchestratorRoundTrip.Tests/`) e **13 definições de `BuildHost`
    em `apps/workers/tests/`** — conjuntos distintos que coincidem por acaso, que
    é justamente o tipo de coincidência que faz um número migrar de pergunta sem
    ninguém notar.
