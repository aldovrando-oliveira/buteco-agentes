## Context

A etapa 1 entregou `KnowledgeBase` e `KnowledgeDocument` e declarou, em spec, que
todo documento fica `Pending` para sempre porque não há consumidor. Esta change é
o consumidor. Ela é a **2a**: o índice. A superfície de operação em `apps/api`
(rota de reindexação, resumo por base) é a **2b**.

Duas rodadas de medição precedem esta proposta e estão em
`02-HISTORICO_E_STATUS.md`. O que elas fecharam entra aqui **implementado, não
reavaliado** — e o que elas explicitamente não certificaram não vira spec.

### Verificações feitas antes das decisões (convenção 6)

Nenhuma delas é raciocínio; todas foram conferidas na árvore de hoje ou em spike
contra Postgres real.

- **V1 — Testcontainers: 15 sítios de `new PostgreSqlBuilder(...)`, não 14.**
  *(Ver a correção logo abaixo: esta medição está certa e responde à pergunta
  errada.)*
  Onze trocam para `pgvector/pgvector:pg18`: oito em `apps/api/tests`, um em
  `apps/workers/tests` (`WorkerInfrastructureFixture`) e dois em `tests/`
  (`PostgresTaskStoreCompatibilityTests` e `RoundTripFixture`, campo
  `_apiAndWorkersPostgres`). Quatro ficam em `postgres:18`: os três de
  `apps/inbox/tests` e `RoundTripFixture._inboxPostgres`. Há ainda uma 16ª
  ocorrência da string, em `AgentMcpBindingEndpointsTests.cs:280`, que é
  **comentário** sobre collation e não muda.

  > **Correção (convenção 9), registrada durante a implementação:** a V1 mediu
  > `PostgreSqlBuilder` — a **imagem** — e concluiu "11 sítios a mudar". O número
  > está certo para a pergunta que ela fez, e a pergunta estava errada. **O que a
  > mudança alcança é `UseNpgsql` — o provider.** A imagem decide se a extensão
  > existe no servidor; o provider decide se o EF sabe **mapear o tipo**. Nenhuma
  > implica a outra.
  >
  > Os sítios reais são **~38**, não 11: todo lugar que constrói o `DbContext`
  > sem passar por `AddInfrastructure` precisa de `UseVector()`. Distribuição
  > medida: 27 em `apps/workers/tests`, 8 em `apps/api/tests`, 3 em `tests/`
  > (`apps/inbox` fica de fora — banco próprio, sem a entidade).
  >
  > **O modo de falha é o que torna isto caro:** sem `UseVector()`, o EF valida o
  > modelo **inteiro** na primeira materialização e recusa a propriedade `Vector`
  > com uma mensagem que fala em *"database provider does not support mapping"* —
  > parece defeito do modelo, não configuração faltando. E como a validação é do
  > modelo inteiro e não por entidade, **todo** teste que toca o banco reprova,
  > não só os de fragmento: `KnowledgeSchemaMirrorTests` reprovou 21 de 21.
  >
  > Pelo mesmo motivo, o caminho de produção **não** passou por acidente:
  > `AddInfrastructure` dos dois apps declara `UseVector()`, e se não declarasse
  > `apps/api` falharia em qualquer operação de banco — não só no dia em que a 2b
  > fizesse a primeira leitura de fragmento.
- **V2 — os dois fixtures de `tests/` aplicam migração de `apps/api`**
  (`ApiDbContext.MigrateAsync`), que é onde a tabela e a extensão nascem. É por
  isso que eles entram nos onze, e não por precaução.
- **V3 — `apps/workers` já depende do banco pronto no compose.** O serviço
  declara `depends_on: migrator: condition: service_completed_successfully`, e o
  `migrator` declara `postgres: condition: service_healthy`. Transitivamente, em
  produção o Postgres está saudável e migrado antes de `apps/workers` subir.
  **Esta verificação mudou a Decisão 6.**
- **V4 — o delete de documento é real** (`DeleteKnowledgeDocumentCommandHandler`
  faz `Remove` + `SaveChanges`), e `KnowledgeDocument → KnowledgeBase` usa
  `DeleteBehavior.Restrict`, enquanto as tabelas de vínculo usam `Cascade`.
  **Isto corrige D7 da etapa 1**, ver Decisão 5.
- **V5 — `KnowledgeWireFormatTests` hoje só consegue afirmar `"Pending"`**, com
  a limitação declarada no próprio arquivo. D10 da etapa 1 pede nominalmente que
  esta etapa o estenda; é tarefa, não descoberta.
- **V6 — `Pgvector.EntityFrameworkCore` 0.3.0 funciona nesta stack.** O pacote
  declara `targetFramework net8.0` e `Npgsql.EntityFrameworkCore.PostgreSQL >=
  9.0.1`; o repositório é `net10.0`, EF Core 10.0.10, Npgsql EFCore 10.0.3.
  Verificado end-to-end contra `pgvector/pgvector:pg18` real: `INSERT` de
  `vector(4096)`, `SELECT` de volta com norma L2 preservada em 1,000000, e
  `CosineDistance` traduzido para `<=>` no SQL emitido.
- **V7 — HNSW recusa a coluna de verdade.** Medido na extensão instalada
  (pgvector 0.8.6): `vector(4096)` → *"column cannot have more than 2000
  dimensions for hnsw index"*; `halfvec(4096)` → *"...more than 4000..."*. A
  coluna gerada `halfvec(3072)` a partir de `l2_normalize(subvector(...))` é
  criável, indexável, e o plano usa o índice.
- **V8 — a extensão exige superusuário.** `pg_available_extension_versions`
  responde `trusted = f`, `superuser = t` para `vector`.
- **V9 — não existe configuração de embedding no repositório.**
  `apps/workers/Options/` tem `ChatClientOptions`, `AnthropicOptions`,
  `GeminiOptions`, `RabbitMqOptions`, `McpCryptoOptions` e
  `AgentDelegationToolOptions`. Esta change **cria** a seção; não troca default
  de nada.
- **V10 — o molde do `try/catch` da convenção 4 já está certo** em
  `TaskJobConsumer.cs:47-61`: o `try` abre **antes** do `JsonSerializer.
  Deserialize` e envolve a execução inteira. Copiar a forma, não reinventar.
- **V11 — documento com `ExtractedText` em branco é inalcançável pela API.**
  `MarkdownSourceExtractor.Extract` recusa `string.IsNullOrWhiteSpace` depois de
  normalizar (linhas 42-45), `KnowledgeContentProcessor.Process` devolve
  `Invalid` quando a extração falha (linhas 37-40), e **os dois** handlers
  passam por ele: `CreateKnowledgeDocumentCommandHandler:27` e
  `UpdateKnowledgeDocumentCommandHandler:30`. Há ainda uma pré-checagem no
  endpoint (`KnowledgeDocumentEndpoints:160`). `ExtractedText` só é escrito pelo
  construtor e por `Update`, ambos alimentados pela saída do processador. **Ver
  Decisão 11.**
- **V12 — exercer o gatilho da coluna derivada reescreve a tabela sob `ACCESS
  EXCLUSIVE`.** Medido contra `pgvector/pgvector:pg18` real, tabela com 300
  linhas de `vector(4096)`: `ADD COLUMN ... GENERATED ALWAYS AS ... STORED` muda
  o `relfilenode` (16714 → 17024) — é reescrita —, e durante o `ALTER` um
  `SELECT count(*)` concorrente **bloqueia e estoura o `lock_timeout`**. Os
  locks observados são `AccessExclusiveLock` e `ShareLock`. Para comparação,
  `ADD COLUMN` de coluna **comum** anulável mantém o `relfilenode` (17630 →
  17630): é metadado, sem reescrita. **Ver Decisão 1.**

### Baseline da convenção 19 — medida antes de qualquer edição

Tomada em `0f7c9c1`, árvore limpa, **com a máquina dentro do limiar registrado**
(load de 1 min < 5,0 e nenhum processo alheio ≥ 100%), medido **antes de cada
alvo** e não só antes do primeiro. É contra estes números que o fechamento
(tarefa 14.1) compara, e por isso eles moram aqui e não no resumo da sessão.

| alvo | resultado | duração | load1 na largada |
|---|---|---|---|
| `libs/ProviderCatalog.Tests` | 6/6 | 4 s | 1,73 |
| `apps/api/Api.sln` | **275/276** | 53 s | 1,67 |
| `apps/workers/Workers.sln` | 174/174 | 5 m 24 s | 4,61 |
| `apps/inbox/Inbox.sln` | 164/164 | 20 s | 2,01 |
| `tests/CrossAppTaskStoreCompatibility` | 2/2 | 15 s | 2,79 |
| `tests/InboxOrchestratorRoundTrip` | 4/4 | 27 s | 2,33 |
| `apps/frontend` | 617/617 (69 arquivos) | 53 s | 2,78 |

A **única** reprovação é
`AgentDeactivationTests.SendMessage_WithPushNotificationConfig_ForInactiveAgent_NeverPublishesJobOrCallsWebhook`,
em 62 ms — o flake de ordem de execução já registrado como item em aberto, com
causa conhecida (`TaskJobPublisher` é instância única da classe e
`PublishedMessages` acumula; passa isolado). **Qualquer outra reprovação no
fechamento é desta change.**

*Como esta baseline foi obtida importa, e está na seção própria do
`02-HISTORICO_E_STATUS.md`:* a primeira tentativa foi **invalidada** por carga
prévia de 26,04 contra o limiar de 5,0, e reportava 7 reprovações em
`apps/workers` e 12 em `apps/frontend` que **não existem** com a máquina
descarregada.

## Goals / Non-Goals

**Goals:**

- Fragmentar documento sem perder conteúdo, com os três invariantes medidos
  afirmados em spec e reprovando por teste.
- Indexar de forma assíncrona, sem bloquear execução de agente.
- Nunca deixar o documento sem fragmento com aparência de normal: as três
  garantias de D9 da etapa 1, agora com cenário verificável.
- Detectar divergência entre o modelo declarado e o índice gravado antes de
  escrever qualquer fragmento.
- Dar ao operador motivo de falha legível, contagem de tentativas e data da
  última.

**Non-Goals:**

- Rota de reindexação e resumo de indexação por base — **2b**.
- Tool de busca, resolvedor por agente, formatação de resultado — **etapa 4**.
- Qualquer UI — **5a-2**.
- Índice ANN, busca híbrida, reescrita de query, limiar de distância,
  reranker — decididos contra por medição, ou registrados como item em aberto
  com gatilho.
- **Certificar recall.** `0c` reprovou no próprio bar e o bar não foi movido.
  Nenhum número de recall entra em spec.

## Decisions

### D1 — `vector(4096)` como coluna de verdade, derivada indexável só quando doer

A coluna é `vector(4096)`, a dimensão nativa do modelo. **Não** truncamos no
cliente, e **não** passamos `defaultModelDimensions` ao construir o gerador: o
gateway aceita `dimensions` e o ignora silenciosamente (medido em `0b`), então
passar o parâmetro produziria metadata afirmando uma dimensão que o vetor não
tem.

Não há índice ANN nesta change. A busca da etapa 4 será exata, filtrada por
`KnowledgeBaseId` com índice btree. O gatilho para criar o índice é medido:
**p95 da consulta acima de 200 ms**.

Quando esse dia chegar, a coluna indexável nasce **por SQL, sem reembedar**:

```sql
ALTER TABLE knowledge_fragments
  ADD COLUMN embedding_idx halfvec(3072)
  GENERATED ALWAYS AS (l2_normalize(subvector("Embedding", 1, 3072))::halfvec(3072)) STORED;
CREATE INDEX ... USING hnsw (embedding_idx halfvec_cosine_ops);
```

*Alternativa recusada — truncar já na gravação:* seria irreversível sem
reembedar, e V7 mostra que a escolha de 3072 não é preferência: HNSW recusa mais
de 2000 dimensões em `vector` e mais de 4000 em `halfvec`, então **qualquer**
índice para este modelo exige truncar. Guardar 4096 cheio mantém a dimensão da
derivada revisável depois, com um benchmark que discrimine — e `0c` registrou
que o benchmark atual **não** discrimina nessa faixa.

*Custo aceito:* 123 MB por 7.500 fragmentos contra 60 MB truncado. Medido, não
estimado. Todo vetor vai para TOAST em qualquer dimensão (`attstorage = 'e'`),
então TOAST não distingue as opções.

**O que custa disparar o gatilho, para que o dia não seja surpresa (V12):** a
coluna gerada é **reescrita de tabela sob `ACCESS EXCLUSIVE`**, e durante ela
até leitura bloqueia — medido, não lido em documentação. Extrapolando das
medidas (17,1 kB por linha antes, 24,5 kB depois), 150 mil fragmentos são **~2,4
GB lidos e ~3,5 GB escritos com a tabela travada**.

A saída existe e também está medida: `ADD COLUMN` de coluna **comum** anulável
não reescreve (o `relfilenode` não muda), então o caminho sem trava longa é
coluna comum + `UPDATE` em lotes + índice depois. Custa perder a garantia de que
a derivada nasce preenchida — passa a ser responsabilidade de quem migra, não do
banco.

Isto **não muda a decisão de adiar**: a alternativa de truncar desde já é pior,
porque é irreversível sem reembedar. Muda o que a change que exercer o gatilho
precisa saber antes de começar.

### D2 — Fila própria, e o motivo não é lentidão

`knowledge-indexing`, distinta de `agent-tasks`. `TaskJobConsumer` roda com
`BasicQosAsync(0, prefetchCount: 1, global: false)`, e
`AgentDelegationConcurrencyTests` existe justamente para provar que isso
serializa o consumo dentro de uma instância — a ponto de uma delegação que
espera a task do alvo ser autodeadlock estrutural.

Indexação de minutos na mesma fila não é "fila mais lenta": é a mesma classe de
bloqueio, com execução de agente atrás.

*Alternativa recusada — subir o `prefetchCount` de `agent-tasks`:* mudaria o
comportamento de concorrência de um caminho já em produção, com um teste
dedicado a afirmar a propriedade atual, para resolver um problema de outra
feature.

### D3 — Retry por fila de espera com TTL e dead-letter, três execuções

O consumidor atual descarta com `BasicNackAsync(requeue: false)`. Não há molde
de retry no repositório; ele nasce aqui, e nasce porque a tela da 5a-2 mostra
*"429 nas três tentativas, a última em 01/09/2026 às 03:14"* — contagem e
carimbo persistidos são requisito, não enfeite.

**Três execuções, espaçadas por 1 minuto e 5 minutos.** Uma execução é uma
tentativa: `IndexingAttempts` conta execuções, não chamadas HTTP, e é isso que
faz o texto da tela ("três tentativas, a última às 03:14") ser verdadeiro.

O mecanismo são **duas filas de espera**, `knowledge-indexing-wait-60s` e
`knowledge-indexing-wait-300s`, cada uma com `x-message-ttl` fixo e
`x-dead-letter-exchange` de volta para a fila principal. Falhou a execução, o
consumidor publica na fila de espera correspondente ao número da tentativa;
esgotadas as três, grava `Failed` com motivo legível.

*Alternativa recusada — TTL por mensagem numa fila de espera só:* RabbitMQ expira
mensagem apenas na cabeça da fila, então uma mensagem de 300 s na frente segura
uma de 60 s atrás. É bloqueio de cabeça de fila, conhecido e silencioso. Duas
filas de TTL fixo custam uma declaração a mais e não têm o problema.

*Alternativa recusada — retry só dentro da execução, em segundos:* seria mais
simples, mas três tentativas em poucos segundos não sobrevivem a um limite de
taxa de janela de minuto, que é o caso real, e tornaria o texto da tela falso.

*Não há retry da chamada ao provedor dentro da execução.* Uma camada só, um
contador só, um significado só.

### D4 — Uma unidade de trabalho por documento, transação única na gravação

O consumidor lê o documento, fragmenta, gera embedding em lote (a interface é
`GenerateAsync(IEnumerable<TInput>, ...)`, batch-nativa, o que casa com
indexação e desalinha com a consulta da etapa 4), e grava.

A gravação é **uma transação**: apaga todos os fragmentos do documento, insere
os novos, atualiza o documento para `Indexed` com `IndexedAt` e `FragmentCount`.
Tudo ou nada — é a garantia 2 de D9 da etapa 1.

O `try/catch` segue o molde de `TaskJobConsumer` da convenção 4: **abre antes da
desserialização e envolve a leitura do banco e a chamada ao provedor**. Essa
cláusula já mordeu duas vezes nesta base, e o motivo de repeti-la é que a
chamada ao provedor é justamente a que falha.

### D5 — `ContentRevision` cobre a atualização; o **delete** é coberto pela FK, e D7 estava impreciso

A gravação é condicionada: `UPDATE ... WHERE "Id" = @id AND "ContentRevision" =
@revisaoLida`, e o consumidor checa **linhas afetadas**. Zero linhas significa
"a revisão mudou **ou** o documento sumiu", e nos dois casos o resultado inteiro
é descartado sem gravar nada.

**Correção à D7 da etapa 1 (convenção 9):** ela afirma que `ContentRevision`
"cobre o delete de graça". Cobre o *descarte*, sim — zero linhas afetadas —, mas
não é `ContentRevision` que impede fragmento órfão: é a **chave estrangeira**.
V4 confirmou que o delete de documento é real (`Remove` + `SaveChanges`), então
a FK de `KnowledgeFragment → KnowledgeDocument` é **`Cascade`**, e não `Restrict`
como a FK de documento para base. Com `Restrict`, a exclusão de documento
indexado passaria a falhar — regressão direta do comportamento que a etapa 1
entregou como decisão central (D6, o primeiro `MapDelete` do repositório).

A escolha de `Cascade` aqui e `Restrict` lá não é inconsistência: fragmento é
conteúdo derivado do documento, e documento é conteúdo referenciado pela base.

*Consequência a testar, não a supor:* se a gravação dos fragmentos acontecer
depois do delete, o `INSERT` viola a FK. É falha ruidosa, que é o desejável, e
está coberta pelo descarte por zero linhas afetadas — que roda antes.

### D6 — Checagem de integridade no boot, e a verificação V3 é o que decide

`apps/workers` verifica no boot que provedor, modelo e dimensão declarados
coincidem com os gravados: `SELECT DISTINCT` sobre as três colunas da tabela de
fragmentos. Vazio sobe; uma linha igual à declarada sobe; **qualquer outra coisa
falha o boot**, inclusive duas combinações distintas, que é corrupção por troca
anterior não detectada.

Seria a primeira checagem desta base a fazer I/O no boot — as três existentes
varrem `IServiceCollection` ou leem configuração. **A V3 é o que decide:** o
compose já garante que o Postgres está saudável e migrado antes de
`apps/workers` subir, então o custo alegado ("não subir com Postgres lento") não
se materializa em produção. Em desenvolvimento, `dotnet run` sem Postgres passa a
falhar no boot em vez de falhar por mensagem — que é feedback melhor, não pior.

*Mudança de comportamento local, declarada:* a V3 vale para o compose. Quem roda
`apps/workers` direto em desenvolvimento (`dotnet run`), fora dele, passa a
**não subir** com o Postgres parado, onde hoje sobe e falha por mensagem. É
provavelmente irrelevante — `apps/workers` não faz nada sem banco e sem fila —,
mas é mudança que alguém vai notar antes de entender, e por isso está escrita
aqui e não descoberta na primeira vez.

*Alternativa recusada — checagem preguiçosa no consumidor*, uma vez por processo
antes da primeira gravação: daria a mesma garantia (nada é escrito antes de
verificar) e preservaria o boot sem I/O, mas foge do padrão das outras três sem
que o motivo sobreviva à V3, e move a falha para mais tarde e menos visível.

*A convenção 15 vale integralmente, com a quinta forma:* `SELECT DISTINCT` sobre
tabela vazia passa por **vacuidade**. O guarda tem de ser pareado com asserção
sobre linha semeada — ver a divergência reintroduzida nos dois sentidos, com o
índice povoado, e só então manter.

### D7 — Chunker: invariantes na spec, parâmetros no código

A spec afirma I1, I2 e I3. `TARGET_MIN = 900` e `HARD_MAX = 1600` são
**constantes nomeadas em `apps/workers`**, fora da spec, no idioma de
`AgentDelegationToolOptions` (defaults que são constantes de produto, sem seção
de configuração).

O motivo está medido: `0c` provou que o chunker corrigido não perde conteúdo;
**não** provou que 900/1600 é o ótimo — não houve braço com outra configuração de
tamanho. Afirmar em spec um número não medido é o padrão que a convenção 10
nomeia: requisito que passa verde sem provar nada. Gatilho para remedir: corpus
real de operador com volume; pela convenção 2, a opção de configuração nasce
quando alguém precisar de outro valor.

O chunker faz, e a spec cobre: captura de preâmbulo, fronteira em múltiplos
níveis de cabeçalho, fallback por tamanho quando não há cabeçalho, teto aplicado
ao **texto emitido** com quebra intra-parágrafo (parágrafo → linha → sentença →
corte), e repetição do cabeçalho de tabela em cada pedaço.

**Overlap é zero**, e é resultado, não parcimônia: `0c` mediu perda de 20 a 23
pontos de R@1 nas duas direções e nas duas magnitudes, depois de remover duas
armadilhas de implementação.

### D8 — Provedor de embedding: seção própria, `ProviderCatalog` não serve

`EmbeddingOptions` (`Embedding:Provider`, `Embedding:Model`,
`Embedding:Dimensions`) e `IEmbeddingGeneratorResolver` no molde de
`IChatClientResolver` — interface existe para ser testável isolando "qual client
é construído para qual provedor" sem rede.

A credencial é reusada de `ChatClientOptions` (seção `OpenAI`), que é onde o
endpoint no formato OpenAI já está configurado. V9 confirmou que não existe
configuração de embedding hoje.

*`LlmProviders`/`ProviderCatalog` não serve*, e o motivo é concreto:
`LlmProviders.All` declararia `anthropic` como provedor de embedding, e o
assembly não tem nenhum tipo de embedding. O catálogo é de provedores de chat.

O gerador é construído por
`new OpenAIClient(...).GetEmbeddingClient(modelo).AsIEmbeddingGenerator()`,
**sem** o segundo parâmetro — ver D1.

### D9 — `ContentHash` com um propósito só

`ContentHash` (SHA-256 do texto extraído, hex) existe para uma coisa: atualização
cujo conteúdo seja idêntico não volta a `Pending`, não enfileira e não gasta
embedding. É a evolução que a etapa 1 registrou em D9 para não parecer regressão
depois — lá **toda** atualização voltava a `Pending`, inclusive a que só trocava
o título, e isso era conservador de propósito.

Não é chave de deduplicação entre documentos, não é validação de integridade e
não participa de nenhuma decisão de busca.

### D10 — A tabela nasce na migração de `apps/api`, que não escreve nela

Contraintuitivo o bastante para alguém "consertar", então fica escrito: a
migração que cria `knowledge_fragments` e a extensão `vector` sai de
`apps/api`, porque o `migrator` do `docker-compose.prod.yml` só empacota bundles
de `apps/api` e `apps/inbox`, e `apps/workers` nunca aplica migração a banco
real. `apps/workers` ganha o espelho de EF Core e a migração equivalente de
design-time, verificados por `KnowledgeSchemaMirrorTests` contra Testcontainer —
mesmo tratamento que a etapa 1 deu às duas entidades dela.

`CREATE EXTENSION IF NOT EXISTS vector` entra via `HasPostgresExtension("vector")`
no `AppDbContext` de `apps/api`. V8 registra a restrição: exige superusuário.

### D11 — Documento em branco: o cenário sai da spec, e o motivo fica aqui

A primeira redação da spec tinha um cenário *"Documento vazio não produz
fragmento e não é erro"*, cujo `THEN` mandava terminar em `Failed`. **Duas
coisas erradas de uma vez:** o título contradiz o próprio `THEN` (`Failed` é o
estado de erro), e a premissa é inalcançável.

V11 conferiu no código, não por suposição: `MarkdownSourceExtractor` recusa
conteúdo em branco depois de normalizar, `KnowledgeContentProcessor` transforma
isso em erro de validação, e **os dois** handlers passam por ele. `ExtractedText`
só é escrito pelo construtor e por `Update`, ambos alimentados pela saída do
processador. Nenhum `POST` ou `PUT` produz documento com conteúdo em branco.

É o mesmo padrão que a D7 da 5a-1 pegou com a descrição vazia, e a conclusão é a
mesma: **o estado não existe**, não é raro.

**O que ficou no lugar** não é o mesmo requisito com o título consertado — é
outro requisito, sobre outra coisa: *"Sucesso com zero fragmentos é recusado"*.
A guarda que importa nunca foi contra documento vazio; é contra **defeito de
fragmentação**, que é alcançável por regressão no chunker e produziria
exatamente a contagem zerada com aparência de sucesso que a convenção 13 nomeia
como pior caso. O texto do requisito diz isso, para que a distinção sobreviva ao
archive.

*Por que isto está registrado como decisão e não corrigido em silêncio:* a spec
vira spec viva no arquivamento, e esta linha de trabalho já corrigiu três vezes o
mesmo padrão — decisão certa no `design.md`, texto errado sobrevivendo na spec.

### D12 — `Pgvector.EntityFrameworkCore` nos dois apps, contra a recomendação anterior

A exploração que precedeu a etapa 1 recomendou o **oposto**: sem plugin, coluna
declarada por SQL cru. Registrado aqui porque a decisão que prevaleceu é a outra,
e decisão revertida sem o argumento anterior à vista reaparece como se nunca
tivesse existido (convenção 9).

**O argumento de lá não era viabilidade** — o spike daquela exploração já provava
que o plugin funciona. Era **colocação de dependência**, em duas partes:

1. `apps/api` nunca faz matemática vetorial. Com SQL cru ele declararia a coluna
   em duas linhas e não precisaria aprender o que é um vetor.
2. O pacote alvo `net8.0`/EF 9 entra no caminho de compilação de **dois** apps, e
   passa a pesar num bump futuro de EF Core.

**O que se pode conceder de imediato:** a segunda parte continua inteira, e não é
respondida — é aceita. Está como R8, com o gatilho de que o pacote levou 9 meses
entre 0.2.2 e 0.3.0. E há um agravante que a exploração da etapa 2 não pesou:
**nesta change ninguém consulta por distância.** O pipeline só escreve. O
benefício mais forte do plugin — a tradução de `<=>` em LINQ, que o spike
demonstrou — só é exercido na etapa 4.

**Por que a primeira parte não vence mesmo assim**, e é aqui que a conta muda:

- *"`apps/api` só declara a coluna"* está certo, mas o custo de declarar sem o
  plugin **não** são duas linhas de SQL numa migração — é `migrationBuilder.Sql`,
  e a etapa 1 fixou o oposto depois de spike: `ContentLengthBytes` foi mapeado
  com `HasComputedColumnSql` justamente para que **o EF gerasse o DDL
  nativamente, sem SQL cru na migração** (D14 da etapa 1). Usar SQL cru aqui
  desfaria, na primeira oportunidade, a propriedade que aquela change verificou.
- A saída de não mapear a entidade em `apps/api` é pior: o modelo do EF deixaria
  de conhecer uma tabela que existe, e migração seguinte poderia tentar removê-la.
- E `apps/workers` **escreve** vetor em todo fragmento inserido, dentro da
  transação da Decisão 4. Ali não é declaração: é caminho de escrita quente, e
  sem o plugin seria SQL cru com literal de 4.096 floats por linha.

**Conclusão, e ela concede metade:** o argumento de colocação estava certo sobre
`apps/api` em isolado, e erra ao supor que a alternativa custa duas linhas — ela
custa reintroduzir SQL cru em migração, contra um precedente medido da etapa 1.
Fica o plugin nos dois apps, com o custo do item 2 aceito e registrado em R8, e
com o reconhecimento de que **na 2a o plugin paga menos do que vai pagar na 4**.

## Risks / Trade-offs

| # | Risco | Contraparte verificável |
|---|---|---|
| R1 | **Documento sem cabeçalho indexa zero fragmentos e termina `Indexed`** — o pior caso da convenção 13, contagem zerada com aparência de sucesso | I1 na spec, com as quatro formas que `0c` mediu (`.txt` corrido, `#` sem `##`, `#` + `###`, controle). Já reprova contra o chunker de `0b` com 3 violações de 40 documentos |
| R2 | **Fragmento estoura o teto** e degrada precisão | I2 na spec, medido no texto emitido com prefixo. Já reprova com 2 violações, pior caso 2.858 caracteres |
| R3 | **Documento atualizado durante a indexação grava fragmento da revisão anterior**, silencioso | Teste de concorrência real com Testcontainers, no molde literal de `AgentDelegationConcurrencyTests`: duas instâncias, containers próprios, nada compartilhado além de Postgres e RabbitMQ. Não caminho feliz |
| R4 | **Documento excluído no meio da indexação** | Cenário do descarte por zero linhas afetadas (D5), mais a FK `Cascade` — e o teste afirma que excluir documento indexado **tem sucesso**, o que reprovaria se a FK fosse `Restrict` |
| R5 | **Drift de modelo de embedding** corrompe o índice em silêncio | Checagem bidirecional no boot (D6), com a convenção 15 nos dois sentidos **e a quinta forma**: pareada com asserção sobre índice povoado, porque sobre tabela vazia o guarda passa por vacuidade |
| R6 | **429 e falha parcial** em documento grande | D3: três execuções espaçadas, contador e carimbo persistidos, `Failed` com motivo legível ao esgotar. Cenários de falha transitória com sucesso na segunda, e de esgotamento |
| R7 | **Falha deixa o documento sem fragmento com aparência de normal** | Garantia 3 de D9 da etapa 1, agora com cenário: falha na reindexação preserva `indexedAt` e os fragmentos antigos, e a busca continua devolvendo o conteúdo anterior |
| R8 | **`Pgvector.EntityFrameworkCore` não acompanha o ciclo do EF Core** — pacote de terceiro que declara `net8.0` e Npgsql 9 | Mitigado hoje por V6 (verificado end-to-end) e pela suíte rodando contra `pgvector:pg18`. **Gatilho registrado:** o pacote levou 9 meses entre 0.2.2 e 0.3.0; num upgrade de EF Core, verificar antes de assumir |
| R9 | **`CREATE EXTENSION` sem superusuário** num ambiente que não seja o compose | Declarado como requisito de deploy. **Sem contraparte técnica**, e está dito: o Testcontainer roda como superusuário, então nenhum teste desta change exerce o caso |
| R10 | **Fragmento-atrator residual** — `0c` mediu que repetir o cabeçalho da tabela leva o top-1 espúrio de 38,6% para 12,0%, ainda mais de treze vezes o ~0,9% de um índice uniforme, e a causa raiz vale para qualquer bloco denso e heterogêneo sem cabeçalho para repetir | **Sem contraparte nesta change, e é aceitação consciente.** A spec afirma a repetição do cabeçalho, que é o que foi medido; o resíduo só é observável por medição de recall, que esta change não faz e cuja ausência é decisão registrada. Item em aberto com gatilho: base real com catálogo, glossário ou tabela de códigos, e o sintoma a procurar é o mesmo fragmento no topo de consultas sem relação |
| R11 | **Nenhum número de recall foi certificado** — `0c` reprovou no próprio bar (R@1 41,0% contra 70%) e o bar não foi movido | **Não é risco a mitigar nesta change, é escopo declarado.** A contraparte é negativa e deliberada: nenhum requisito de spec afirma recall, e os parâmetros de fragmentação ficam fora da spec (D7). Item em aberto com gatilho próprio |
| R12 | **Boot de `apps/workers` passa a depender do banco** (D6) | V3 mostra que o compose já ordena. Em desenvolvimento a falha vira falha de boot, que é mais visível. Trade-off aceito e declarado, não efeito colateral |

## Migration Plan

Uma migração em `apps/api` (aplicada) e a equivalente em `apps/workers`
(design-time, nunca aplicada). Aditivo: cria `knowledge_fragments` e a extensão
`vector`, e acrescenta quatro colunas a `knowledge_documents`
(`ContentHash`, `FragmentCount`, `IndexingAttempts`, `LastAttemptAt`). Nenhuma
coluna existente é alterada, nenhum dado migrado.

`ContentHash` nasce nulo para documentos existentes, e nulo significa "nunca
indexado sob esta regra" — a primeira atualização ou indexação o preenche. Não
há backfill: documentos parados em `Pending` desde a etapa 1 são enfileirados
pelo caminho normal quando alguém os tocar, ou pela rota da 2b.

*Rollback:* o `Down` das duas migrações derruba a tabela e as colunas. Como nada
consome fragmento ainda — a tool é a etapa 4 —, não há perda funcional em
reverter; perde-se o índice, que se refaz reindexando.

*Ordem de deploy, com a assimetria declarada:* o `migrator` roda antes de
`apps/api` e `apps/inbox`; `apps/workers` sobe depois do `migrator` (V3). Uma
coluna com dimensão errada passaria pelo migrator e seria pega por
`apps/workers` no boot, que é o único que checa — e agora, pela D6, ele reprova
em vez de subir.

## Open Questions

1. **Qual o volume real de documento por base em produção?** Decide se o lote de
   embedding precisa de janelamento além do que a interface já faz. Não bloqueia:
   o pipeline funciona documento a documento, e o custo por documento é
   proporcional ao número de fragmentos.
2. **A forma do resumo de indexação da 2b** — contagem em `KnowledgeBaseResponse`
   contra rota própria — precisa ser decidida com a tela na mão. A 5a-1 registrou
   que a coluna de documentos exigiria uma requisição por base, contra as 100+
   bases que o handoff declara. **Registrado aqui como handoff para a 2b**, não
   resolvido nesta change.
