## Context

A etapa **5c — UI do diagnóstico do índice** está bloqueada por um passo de
backend que nunca foi proposto. A aba do protótipo mostra cinco linhas; a
exploração de 12/09/2026 mediu que **três** precisam de rota nova e **duas** já
estão no fio.

O que existe hoje, conferido contra a árvore:

| dado | onde está | estado |
|---|---|---|
| provedor, modelo e dimensão por fragmento | `KnowledgeFragment.cs:56-69`, colunas desde `20260911004712_AddKnowledgeFragmentIndex` | gravado, **não servido** |
| estado e `fragmentCount` por documento | `GET /knowledge-bases/{id}/documents` (etapa 2a) | servido |
| contagens agregadas por base | `GET /knowledge-bases/indexing-summary` (etapa 2b) | servido |
| motivo de falha + reindexar | `failureReason` na listagem + `POST .../{id}/reindex` | servido |
| **proveniência do índice** | — | **ausente** |

`KnowledgeFragments/` em `apps/api` tem exatamente um arquivo: a entidade. Não há
query, handler, response nem endpoint que a projete. `apps/api` **nunca escreve**
nessa tabela — a tabela nasce ali por razão de deploy (o `migrator` do
`docker-compose.prod.yml` só empacota bundles de `apps/api` e `apps/inbox`), e
quem escreve é `apps/workers`. Esta change não muda isso: ela adiciona **leitura**.

A restrição que molda o desenho inteiro está em `AppDbContext.cs:250`: a coluna é
`vector(4096)`. Medido contra `pgvector/pgvector:pg18`, gravar um vetor de outra
dimensão não é uma divergência a detectar depois — é um erro na hora:

```
ERROR:  expected 4096 dimensions, not 1536
```

## Goals / Non-Goals

**Goals:**

- Servir a proveniência **gravada** do índice de conhecimento por uma rota de
  leitura de `apps/api`, com a contagem de fragmentos de cada combinação.
- Representar com honestidade os três estados do índice: vazio, coerente, e
  **corrompido com mais de uma combinação** — este último é o estado em que
  `apps/workers` está no chão e o operador vem buscar explicação.
- Deixar o custo medido e o gatilho de otimização registrados, para que a decisão
  de não indexar agora seja revisitável com número, não com memória.
- Desbloquear a 5c sem adiantar nada dela.

**Non-Goals:**

- **Nenhuma mudança em `apps/workers`.** A checagem de boot
  (`EmbeddingIndexConsistencyValidation`) continua exatamente como está; esta
  change é um segundo leitor das mesmas colunas, não uma refatoração para
  compartilhar código entre apps — o que seria referência entre apps, proibida.
- **Nenhuma tela.** A 5c é change própria; aqui só entram as correções que ela
  herda, escritas para não precisarem ser redescobertas.
- **Nenhum volume por base nesta rota.** Já está no fio (ver D11).
- **Nenhuma migração, nenhum índice de banco, nenhuma configuração nova.** Nenhuma
  dependência nova é adicionada, então nenhuma versão de runtime, framework ou
  biblioteca é fixada por esta change.
- **Nenhum modo de tolerância.** A rota *relata* corrupção; ela não a conserta nem
  a esconde, e não existe parâmetro que mude isso.

## Árvore de pastas proposta

```
apps/api/src/Buteco.Api/
├── KnowledgeFragments/
│   ├── Endpoints/
│   │   └── KnowledgeIndexEndpoints.cs                      (novo)
│   ├── Entities/
│   │   └── KnowledgeFragment.cs                            (existe, não muda)
│   ├── Queries/
│   │   └── GetKnowledgeIndexDiagnostics/
│   │       ├── GetKnowledgeIndexDiagnosticsQuery.cs        (novo)
│   │       └── GetKnowledgeIndexDiagnosticsQueryHandler.cs (novo)
│   └── Responses/
│       └── KnowledgeIndexProvenanceResponse.cs             (novo)
└── Program.cs                                              (modificado: 1 linha)

apps/api/tests/Buteco.Api.Tests/
├── Knowledge/
│   └── KnowledgeIndexDiagnosticsTests.cs                   (novo)
└── Support/
    ├── ApiFactoryFixture.cs                                (existe, não muda)
    └── EmittedSqlCapture.cs                                (existe, não muda)
```

Nada em `libs/`. Não há segundo consumidor: `apps/workers` lê as mesmas colunas
com o seu próprio `AppDbContext` e a sua própria entidade, e extrair uma lib para
um leitor HTTP e um validador de boot seria a abstração prematura que a convenção
2 proíbe.

## Decisions

### D1 — A rota é global e vive fora do grupo de bases

`GET /knowledge-index/diagnostics`, mapeada direto em `app`, no molde de
`ProviderEndpoints.cs:12` (`app.MapGet("/providers", ...)`) — o precedente do
repositório para recurso de leitura que não pertence a nenhuma entidade. É o único
que existe, e serve exatamente para isso.

Três evidências independentes de que a proveniência é do **sistema**:

1. **A dimensão é inescrevível de outro jeito.** `vector(4096)` em
   `AppDbContext.cs:250` e na migração; o erro medido acima. `EmbeddingDimensions`
   gravado só pode valer 4096 enquanto o schema disser isso — a coluna ecoa uma
   constante do schema, não um dado por base.
2. **A checagem de boot já trata o índice como um só.**
   `EmbeddingIndexConsistencyValidation.cs:52-60` faz `SELECT DISTINCT` sem filtro
   de base, e `:72-82` reprova com mais de uma combinação — no índice **inteiro**.
3. **O protótipo já modela assim.** `Buteco Agentes.dc.html:1487`:
   `embedding = { provider, model, dim }`, um objeto no estado raiz, não um campo
   de cada base. O texto da aba (`:776`) diz, com as palavras dele, que é
   *"configuração de processo, validada no boot do servidor"*.

**Alternativas consideradas:**

- `GET /knowledge-bases/{id}/index-diagnostics` — mais barata (filtra pelo índice
  `IX_knowledge_fragments_KnowledgeBaseId`) e casa com a tela. **Recusada**:
  afirmaria que bases diferentes podem ter proveniências diferentes, o que o
  schema torna impossível, e produziria a anomalia de base A mostrar travessão
  enquanto base B, ao lado, mostra o modelo. A convenção 13 corta nos dois
  sentidos — não afirmar o que não se sabe, e não negar o que se sabe.
- Campos em `KnowledgeBaseResponse` — **recusada** pelo mesmo motivo que a etapa
  2b recusou as contagens (D1 de lá): seis sítios de construção, quatro deles
  handlers de comando sem relação nenhuma com indexação.

### D2 — A resposta é o gravado, nunca o declarado

Conferido: a seção `Embedding` existe em **um** `appsettings` do repositório,
`apps/workers/src/Buteco.Workers/appsettings.Development.json:21`, e
`EmbeddingOptions` vive em `Buteco.Workers.Options`. `apps/api` não tem esse
valor.

Servir o declarado exigiria duplicar a seção em `apps/api` — a segunda fonte do
mesmo valor, que divergem no primeiro rodízio. Mostrar o gravado não é só a
recomendação da exploração da etapa 2: **é a única opção que não exige
configuração nova.** E é a opção certa de qualquer forma: a tela exibe a realidade
do índice, não a intenção do arquivo.

Corolário, e é requisito na spec: índice vazio **não** preenche os campos com a
configuração. Não há configuração para preencher.

### D3 — Lista de combinações com contagem, não três campos escalares

`{ provider, model, dimensions }` não tem como representar o estado de duas
combinações, e esse é precisamente o estado em que a tela é aberta: `apps/workers`
reprova o boot, `apps/api` sobe, o operador vem entender por quê. Medido, com 500
dos 150.000 fragmentos trocados:

| provedor | modelo | dimensão | fragmentos |
|---|---|---|---|
| `openai` | `modelo-a` | 4096 | 149.500 |
| `openai` | `modelo-b` | 4096 | 500 |

Os nomes de modelo acima são **sintéticos**, da carga de medição — o que a medição
estabelece são as contagens e o custo, não o nome. Os corpos reais da rota, com o
modelo de verdade (`qwen-qwen3-embedding-8b`), estão mais abaixo em
"Corpos reais da rota".

`fragmentCount` entra por combinação. Ele **não** é campo sem cenário (convenção
2): o cenário dele é o caso anormal, onde saber que são 500 contra 149.500 decide
se a saída é reindexar 500 documentos ou o acervo inteiro. E ele sai de graça —
medido: `GROUP BY` com `count(*)` custa o mesmo que `DISTINCT` puro.

**A rota espelha a checagem de boot em vez de reinterpretá-la**: mesma agregação,
mesma semântica de "quantas combinações existem", mais um consumidor.

### D4 — Array nu na raiz, não objeto envelope

`Ok<IReadOnlyList<KnowledgeIndexProvenanceResponse>>`. Precedentes: `/providers` e
`/knowledge-bases/indexing-summary` — rotas de leitura cujo último segmento é um
substantivo e que devolvem array nu. Um envelope `{ "combinations": [...] }`
acomodaria campos de sistema futuros, e é exatamente por isso que fica fora:
acomodação para consumidor que não existe é o que a convenção 2 proíbe. Quando
houver o segundo dado de sistema a servir, o envelope nasce junto com ele.

### D5 — Agregação e ordenação no banco; contagem como `long`

Uma consulta: `GroupBy` nas três colunas, `Select` com `Count()`, `OrderBy` nas
três colunas, `ToListAsync`. Sem projeção em memória, sem segunda ida ao banco.

**A ordenação é da consulta, não do processo**, porque a spec viva
`api-response-ordering` exige o comparador do banco como canônico — decisão
medida lá, não presumida: a collation do PostgreSQL e o comparador de `string` do
.NET discordam de fato, e a cultura do processo não é fixada em lugar nenhum deste
repositório.

**Não há quarto critério de desempate, e isso é deliberado.** A regra "toda lista
ordenada termina por desempate no identificador" está satisfeita pelo critério
primário: as três colunas **são** o identificador da combinação — são a própria
chave de agrupamento, únicas por construção. Escrito aqui porque é o tipo de coisa
que alguém "conserta" depois sem ver a causa.

`count(*)` do PostgreSQL é `bigint`; a contagem vai para a resposta como `long`,
sem cast para `int`. Com 150.000 fragmentos cabe em `int`, mas o cast seria uma
conversão sem motivo num caminho que só cresce.

**Nota de escopo, sobre um desacordo que esta change deixa de pé de propósito:**
a mensagem de erro da checagem de boot ordena as combinações em memória com
`Order(StringComparer.Ordinal)` (`EmbeddingIndexConsistencyValidation.cs:76`),
comparador diferente do que esta rota usará. Não é o caso que
`api-response-ordering` governa — aquele requisito é sobre **duas rotas que servem
a mesma lista lógica**, e ali é uma mensagem de exceção em outro app. Mexer nisso
seria mudança em `apps/workers` sem cenário que a peça.

### D6 — Sem índice agora, com gatilho medido

A consulta é `Seq Scan` + `HashAggregate` sobre o heap. **Não existe índice nas
três colunas, e esta change não cria um.** O precedente é da própria base: o
índice HNSW também não existe, e `AppDbContext.cs:237-243` declara o gatilho
medido em vez de criá-lo por precaução.

**Gatilho registrado: p95 da rota acima de 500 ms, ou heap de `knowledge_fragments`
acima de 500 MB.** A saída é `CREATE INDEX CONCURRENTLY` nas três colunas —
medido em **1,08 MB** para 150.000 linhas, 2,2 s para construir, sem reescrita de
tabela e sem reembedar. O índice é minúsculo por um motivo que vale registrar: a
deduplicação do btree colapsa valores idênticos em *posting lists*, e **o
invariante que torna a varredura cara é o mesmo que torna o índice barato**.

### D7 — Onde a pasta vive

`KnowledgeFragments/`, não `KnowledgeBases/` nem `KnowledgeDocuments/`. A feature
se organiza por conceito de domínio: o recurso é o **índice de fragmentos**, e a
pasta da entidade já existe. `KnowledgeIndexEndpoints` fica em
`KnowledgeFragments/Endpoints/` mesmo servindo uma rota que não tem `fragments` no
caminho — a rota é `/knowledge-index/diagnostics` porque é o índice que ela
descreve, e o nome do recurso na URL não precisa repetir o nome da tabela.

### D8 — O guarda de autenticação é a `FallbackPolicy`, e **não** a checagem de classificação de rotas

Correção de premissa, conferida no código (convenção 6). Era esperado que
`ValidateRouteAuthenticationClassification` falhasse o boot se a rota nova não
fosse classificada. **Ela não faz isso**, e não é para fazer.
`RouteAuthenticationExtensions.cs:23-54` verifica duas coisas, as duas sobre o
caminho **anônimo**:

1. toda rota marcada `AllowAnonymous` tem uma `AnonymousRouteClassification` com o
   motivo;
2. todo padrão da allowlist esperada corresponde de fato a um endpoint anônimo
   mapeado (pega allowlist defasada).

Uma rota nova **autenticada** não aparece em nenhuma das duas varreduras, e não
precisa: ela é autenticada por **omissão**, pela `FallbackPolicy` de
`Program.cs:66-70`, que exige usuário autenticado mais `ServiceScopeRequirement`
em toda rota não marcada. O desenho é seguro por padrão — esquecer de declarar
algo **fecha** a rota, não abre.

Consequência prática: a rota nova não entra na lista de
`ValidateRouteAuthenticationClassification` (`Program.cs:107-110`) — colocá-la lá
**reprovaria** o boot, porque aquela lista é de rotas que precisam estar anônimas.
E o 403 para o token de serviço vem de graça de
`ServiceScopeAuthorizationHandler.cs:20-23`, que só libera duas rotas para
`service:inbox`.

Por isso os dois cenários de autenticação na spec são testes de verdade, não
redundância com um guarda de boot que não cobre este caso.

**E o `01` já estava certo — a imprecisão era do prompt desta change.** Conferido
durante a implementação: a convenção 8 descreve o quarto caso como
*"classificação de rotas **anônimas** (autenticação)"* (`01-ARQUITETURA_E_CONVENCOES.md:702-703`),
e a linha 534 fala de *"classificado explicitamente na allowlist de rotas
anônimas"*. Nenhuma das duas afirma que a checagem cobre rota autenticada. Então a
convenção **não foi alterada**: ela foi conferida e está correta. Registrado aqui
para que ninguém "conserte" o `01` com base na premissa errada.

### D9 — O que a 5c herda, escrito aqui para não ser redescoberto

Nenhuma é rota nova; as três são correções de tela, e a primeira é defeito de
protótipo contra requisito **já vivo**.

1. **O predicado da soma de fragmentos.** O protótipo soma `chunks` só de
   documentos `status === 'indexed'` (`:1500`). Isso **subconta o índice**, e pelo
   mecanismo que é garantia deliberada do backend: a falha **preserva os
   fragmentos anteriores** (`KnowledgeIndexingService.cs:193-196`, garantia 3 de
   D9 da etapa 1). Documento que indexou com 14 fragmentos e falhou ao reindexar
   continua com 14 fragmentos vivos, continua respondendo consultas, e sai da
   soma. O mesmo vale para `Indexing`: a remoção dos antigos acontece **dentro** da
   transação de commit (`:181-187`). O requisito vivo já diz o predicado certo —
   `knowledge-document-indexing`, "Contagem de fragmentos acompanha o índice":
   exibir *"sempre que `indexedAt` não for nulo, **qualquer que seja o estado**"*.
   Há um **segundo caminho** para o mesmo subcontar: no mock, `reindex` e a
   atualização de conteúdo gravam `chunks: 0`, enquanto o backend preserva.
2. **O gate do vazio passa a ser global.** O protótipo condiciona a proveniência a
   `t.indexed > 0` da base (`:1632`), com o texto *"Nada indexado nesta base"*
   (`:781`). Com a proveniência global isso esconde um fato que o sistema sabe e
   insinua que o fato é da base. O gate é a vacuidade do índice inteiro — que é o
   que esta rota responde com lista vazia.
3. **A aba separa as duas escalas.** Hoje três linhas de sistema e duas de base
   ficam na mesma lista, sob um cabeçalho ("Como o índice foi construído") que só
   descreve as três primeiras. Com a proveniência global isso deixa de ser detalhe
   visual e passa a ser afirmação errada: dois grupos, "Como o índice foi
   construído (sistema)" e "Volume desta base".

E o frescor: a proveniência muda **exatamente uma vez**, quando o primeiro
documento termina de indexar. A condição que a 5a-2 extraiu como função pura para
o `refetchInterval` da listagem (convenção 20) é a mesma pergunta — reuso, não
invenção.

### D10 — O volume por base não entra nesta rota

`KnowledgeBaseDetailPage.tsx:44` já carrega a listagem de documentos, e
`ListKnowledgeDocumentsQueryHandler` **não pagina** (`:37-54`): a página tem na
mão `fragmentCount`, `indexingStatus`, `indexedAt` e `failureReason` de todos os
documentos da base. "Documentos indexados: X de Y", "Fragmentos no índice" e a
lista de falhas com botão de reindexar são derivações disso, e a soma é exata
porque `FragmentCount` é escrito na **mesma transação** que os fragmentos
(`KnowledgeIndexingService.cs:160-187`).

Acrescentar um total por base aqui seria um segundo caminho para o mesmo número —
e o caminho pior, porque a rota é global e teria de voltar a aceitar id de base,
desfazendo D1.

## Custo, medido

Postgres real, `pgvector/pgvector:pg18` (a imagem do `docker-compose`), tabela com
a forma exata de `knowledge_fragments` — mesmos índices, `vector(4096)`, texto de
~1.300 caracteres como `KnowledgeChunker` produz (alvo 900, teto 1600) —,
**150.000 fragmentos em 50 bases**.

O fato que decide o custo: `pg_attribute.attstorage` da coluna de vetor é `e`
(EXTERNAL). pgvector manda o vetor para TOAST sempre, sem compressão, e a tupla do
heap carrega um ponteiro de 18 bytes. **A varredura de proveniência não toca nos
2,4 GB de vetor.**

| | tamanho | páginas |
|---|---|---|
| heap (o que a varredura lê) | **234 MB** | 30.000 |
| TOAST + índices | 2.455 MB | — |
| total da tabela | 2.689 MB | — |

| consulta | tempo | páginas lidas | plano |
|---|---|---|---|
| `SELECT DISTINCT` das 3 colunas | **0,24 – 0,82 s** | 30.000 (só heap) | HashAggregate ← Seq Scan |
| `GROUP BY` + `count(*)` | 0,25 – 1,16 s | 30.000 | idem — a contagem é de graça |
| `LIMIT 1` | 1 – 53 ms | 3 | Limit ← Seq Scan |
| `count(*)` de uma base (3.000 frags) | 0,12 s | 3 | Index Only Scan, `Heap Fetches: 0` |
| `DISTINCT` com índice nas 3 colunas | 0,21 – 0,30 s | **135** | Parallel Index Only Scan |

Três resultados que mudaram a decisão:

- **A varredura não suja o cache.** Despejada a tabela de `shared_buffers`
  (`pg_buffercache_evict_relation`) e rodada a varredura de 30.000 páginas,
  sobraram **282 páginas (2,2 MB)** — o *ring buffer* de leitura em massa. Antes do
  despejo a tabela ocupava 15.906 páginas (124 MB dos 128 MB). Era o custo que mais
  preocupava, evictar o cache que a busca de conhecimento usa, **e ele não
  existe.**
- **`LIMIT 2` não curto-circuita.** Verificado: `DISTINCT ... LIMIT 2` leu as
  30.130 páginas de qualquer jeito — o `HashAggregate` consome toda a entrada antes
  de emitir a primeira linha. Não há atalho "pare na segunda combinação", e por
  isso a agregação completa não é desperdício a otimizar.
- **`LIMIT 1` seria 15× mais rápido e cego.** Ele lê uma linha e afirma a
  proveniência do índice inteiro com base nela — a corrupção de duas combinações
  fica invisível. Recusado pela convenção 13: afirmaria mais do que mediu.

⚠️ **Aviso que precisa acompanhar qualquer número daqui.**
`EXPLAIN (ANALYZE, BUFFERS, TIMING ON)` reportou **5,78 s** para a varredura com
50.000 linhas, enquanto a consulta **sem instrumentação**, com 150.000, rodou em
0,65–0,82 s — inflação de 7 a 9 vezes nesta máquina. **A contagem de `BUFFERS` é
confiável; o `Execution Time` com `TIMING ON` não é.** Os números desta seção são
todos de consulta sem instrumentação. Se o 5,78 vazar para um item aberto, volta
como "a consulta leva 6 segundos".

⚠️ **Estado da máquina (convenção 22), porque a referência vale sobre ele:** VM do
podman, amd64, disco virtualizado, PostgreSQL 18.6, `shared_buffers` no default de
128 MB. **As páginas valem em qualquer lugar; os milissegundos valem para este
disco.** Escala linearmente com o heap: 1,56 KB por linha, logo 1 milhão de
fragmentos ≈ 1,5 GB de heap ≈ 4–5 s — território do gatilho de D6.

## Projeção de tamanho, por componente

Feita **depois** que a verificação fechou (convenção 18). Entrega **código**, não
decisão. Âncora: a metade `indexing-summary` de `77ec813`, o componente mais
parecido que existe — `Query` 11 + `Handler` 64 + `Response` 48 + wiring 21 =
**144 linhas de fonte** para esta mesma forma.

| componente | arquivos | linhas |
|---|---|---|
| `GetKnowledgeIndexDiagnosticsQuery` | 1 novo | 10–15 |
| Handler (o `GROUP BY`; carrega o comentário de D1) | 1 novo | 50–70 |
| `KnowledgeIndexProvenanceResponse` | 1 novo | 40–60 |
| `KnowledgeIndexEndpoints` (molde `ProviderEndpoints`) | 1 novo | 25–35 |
| `Program.cs` | 1 mod | 1–2 |
| Testes de contrato (10 cenários) | 1 novo | 260–360 |
| `01` / `02` / `CHANGELOG` / `docs/architecture` | 4 mod | 60–130 |
| OpenSpec (proposal, design, tasks, delta) | 4 novos | 700–1.100 |

**Código + testes: 6 arquivos, 390–540 linhas.** Com docs e OpenSpec: 14 arquivos,
1.150–1.770 linhas — cerca de metade de `77ec813` (28 arquivos / 2.450), que tinha
duas metades.

**Um risco de tamanho que não existe, e vale dizer porque era esperado:** semear
fragmentos em teste de `apps/api` já tem precedente pronto.
`KnowledgeDocumentIndexingContractTests.cs:198-206` insere por SQL cru com literal
de 4.096 dimensões, e `ApiFactoryFixture.cs:29` já roda `pgvector/pgvector:pg18`.
Conferido contra a árvore de hoje: **nenhum fixture novo, nenhuma imagem nova.**

## Risks / Trade-offs

Cada risco com contraparte verificável (convenção 10) — cenário e teste, ou a
justificativa explícita de por que não é testável.

| risco | contraparte |
|---|---|
| A varredura cresce com o índice e a rota fica lenta | Gatilho medido em D6, com os números e o estado da máquina. Em CI: `EmittedSqlCapture` provando que a rota emite **uma** consulta, sem laço por base nem por documento (cenário "O custo não cresce..."). **O tempo não é assertável em CI** — a suíte roda em container com disco virtualizado, e um limite em ms viraria teste intermitente. Está escrito em vez de fingido. |
| A tela afirmar que provedor/modelo são da base | Cenário "Bases diferentes com a mesma combinação devolvem um item só" **mais** a asserção negativa de que a rota não aceita id de base. A negativa é a que importa: é ela que impede a regressão bem-intencionada de "deixar a rota aceitar o id, já que a tela é por base". |
| Índice vazio renderizar algo que pareça configuração | Cenário "Índice vazio devolve lista vazia, sem afirmar proveniência", com asserção negativa de que nenhum nome de provedor ou modelo aparece. Garantido por construção — `apps/api` não tem a seção `Embedding` —, e o teste guarda a construção. |
| Corrupção ficar invisível | Cenário "Duas combinações devolvem dois itens". É o cenário que mede a escolha de D3: com três campos escalares ele não passa. |
| Nome no fio divergir do que o consumidor lê (convenção 12) | Cenário "Os nomes dos campos no fio são os declarados", asserção sobre o **texto** do JSON, nunca desserializando de volta para o mesmo tipo — a ida e a volta passam pela mesma política e sempre casam. Foi o `a2A` que ensinou. |
| Rota escapar da autenticação | Testes de 401 sem token e 403 com token de serviço. **Não** delegado ao guarda de boot, que comprovadamente não cobre este caso (D8). |
| O nome do modelo voltar errado nos artefatos | O nome é `qwen-qwen3-embedding-8b`. Conferido na árvore: `.env.example:71`, `appsettings.Development.json:23` e os testes de `apps/workers` usam essa forma; a única ocorrência da forma curta no repositório é a linha do `02` que **registra a correção anterior**. Tarefa de fechamento varre os artefatos desta change. |
| Guarda que nunca falhou contra o defeito real (convenção 15) | Cada asserção negativa é exercida contra o defeito antes de valer: a de "um item só" rodando primeiro contra uma agregação por base, a de nomes no fio contra um nome trocado. Tarefa explícita em `tasks.md`. |

**Trade-off assumido:** a rota paga uma varredura de heap onde um `LIMIT 1` pagaria
três páginas. Comprado de propósito: a diferença é de 0,8 s para 50 ms numa tela
que o operador abre para investigar, e o que se compra é a única informação que
torna a tela útil no estado em que ela é aberta.

## Guardas exercidos contra o defeito real

Convenção 15: um guarda só vale depois de ter falhado contra o defeito que ele
guarda. Cada inversão abaixo foi aplicada, confirmada **vermelha** e desfeita
durante a implementação.

| inversão aplicada | cenário que reprovou |
|---|---|
| `GroupBy` incluindo `KnowledgeBaseId` (a agregação por base) | `TwoBasesWithSameCombination_ReturnOneItemWithTheSum` |
| `ORDER BY` removido da consulta, ordenação em memória com `StringComparer.Ordinal` | `Items_AreOrderedByDatabaseCollation_NotByDotNetComparer` |
| `fragmentCount` renomeado no fio para `fragmentsCount` | `WireFieldNames_AreTheDeclaredOnes` |
| `Take(1)` depois da agregação (a alternativa `LIMIT 1`) | `TwoCombinations_ReturnsBothWithTheirOwnCounts` |

**A terceira inversão provou a cegueira da convenção 12 na mesma rodada**, que é o
resultado mais útil das quatro: com `fragmentsCount` no fio, a asserção sobre o
**texto** do JSON reprovou enquanto os dois testes que **desserializam para o
mesmo tipo** continuaram **verdes** — a chave passa pela mesma política na ida e na
volta e sempre casa. Um teste de round-trip aqui teria dado falsa confiança.

**A segunda confirmou que o par de nomes separa os comparadores neste ambiente de
teste**, e não por sorte: `suporte-alfa` / `Suporte Alfa` é o par **medido** em
`openspec/changes/archive/2026-09-09-ordenacao-desempate-listas-vinculo/design.md:59-64`,
reusado em vez de inventado, e já exercido nesta suíte por
`AgentKnowledgeBindingEndpointsTests`. Se a inversão tivesse passado, o problema
seria dos nomes do arranjo — não do teste.

## Baseline da suíte de `apps/api`

Medida em `git worktree` limpo no `HEAD` `35b7f01`, **antes** de qualquer edição
(convenção 19), com a saída inteira guardada em arquivo:

**308 aprovados, 1 reprovado, 309 total.**

A falha é `AgentDeactivationTests.SendMessage_WithPushNotificationConfig_ForInactiveAgent_NeverPublishesJobOrCallsWebhook`,
**pré-existente e sem relação com esta change** — e a classificação foi ganha nos
três passos, não presumida:

1. **Isolada** (só aquele teste): **passa**.
2. **Na classe** (`--filter ~AgentDeactivationTests`): **reprova**.
3. **Em worktree limpo no `HEAD`**: reprova igual, então não é desta change.

Mecanismo: `AgentDeactivationFixture.TaskJobPublisher` é uma instância única da
classe, registrada como Singleton, que **acumula** as mensagens publicadas sem
reset entre testes. `Assert.Empty(...)` depende, portanto, de
`SendMessage_AfterReactivation_PublishesNormally` não ter rodado antes — e o xUnit
não garante ordem dentro da classe. É estado compartilhado de fixture com asserção
dependente de ordem, o mesmo mecanismo que `KnowledgeIndexDiagnosticsTests` evita
limpando `knowledge_fragments` no início de cada teste, porque a rota nova é
global e cada teste precisa ser dono da tabela inteira.

**Não corrigido aqui**: é `apps/api`, mas outro domínio (desativação de agente e
push notification) e outra change. Fica como item a sequenciar.

## Corpos reais da rota (conferência manual, e o que a 5c consome)

Capturados com `apps/api` de pé contra um `pgvector/pgvector:pg18` real e
migrações aplicadas, via `GET /knowledge-index/diagnostics` com token de operador.
**Não é o que um teste construiu — é o que a rota devolve**, e é contra estes
corpos que o tipo do cliente da 5c deve ser escrito.

**1. Índice vazio** — `HTTP/1.1 200 OK`:

```json
[]
```

Array nu e vazio. Nenhum campo nulo, nenhum item com zeros, nenhum valor de
configuração. Sem token, a mesma rota responde `401`.

**2. Uma combinação** (1 documento, 3 fragmentos) — `200`:

```json
[
  {
    "provider": "openai",
    "model": "qwen-qwen3-embedding-8b",
    "dimensions": 4096,
    "fragmentCount": 3
  }
]
```

**3. Duas combinações** — o estado em que `apps/workers` reprova o boot e esta rota
responde normalmente, que é o estado em que o operador abre a tela — `200`:

```json
[
  {
    "provider": "openai",
    "model": "nomic-embed-text-v1.5",
    "dimensions": 4096,
    "fragmentCount": 1
  },
  {
    "provider": "openai",
    "model": "qwen-qwen3-embedding-8b",
    "dimensions": 4096,
    "fragmentCount": 3
  }
]
```

Note a ordem: `nomic-...` antes de `qwen-...`, crescente por modelo, produzida pela
collation do banco — e é ela que a 5c deve exibir sem reordenar no cliente.

`dimensions` e `fragmentCount` chegam como **números**, não strings. O tipo do
cliente é uma lista, nunca um objeto de três campos: é a lista que representa os
três estados com o mesmo formato.

## Migration Plan

Não há migração de banco — as três colunas existem desde
`20260911004712_AddKnowledgeFragmentIndex` e nada no schema muda.

Deploy: rota nova em `apps/api`, sem configuração nova, sem variável de ambiente
nova, sem dependência nova. Consumidor nenhum até a 5c, então a ordem de deploy é
livre.

Rollback: remover o `Map*` de `Program.cs`. Nenhum dado é escrito por esta change,
então não há estado a desfazer.

## Open Questions

Nenhuma. As duas que existiam foram fechadas pela exploração — proveniência global
× por base (D1) e `fragmentCount` por combinação × resposta só com as combinações
(D3) —, e a terceira, a forma do guarda de autenticação, foi fechada lendo o código
(D8).
