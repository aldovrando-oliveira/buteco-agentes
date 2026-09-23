## 0. Conferência de escopo de arquivo

**Lista fechada de caminhos permitidos.** Qualquer arquivo fora desta lista que
apareça no `git status` ao fim é achado a reportar, não a commitar. **`apps/api`
entra só para a migração** — entidades de mapeamento, `AppDbContext`, os
arquivos gerados pelo `dotnet ef` e o teste da migração. Nenhuma rota, nenhum
handler, nenhum endpoint. **`provider_calls` não é tocada**: se
`ProviderCall.cs`, o bloco dele no `AppDbContext` ou o `Purpose` de conversa
aparecerem em diff, é achado (D1).

```
# apps/api — só migração
apps/api/src/Buteco.Api/EmbeddingMetrics/Entities/EmbeddingCall.cs                        (novo)
apps/api/src/Buteco.Api/EmbeddingMetrics/Entities/KnowledgeIndexingAttempt.cs             (novo)
apps/api/src/Buteco.Api/Infrastructure/AppDbContext.cs
apps/api/src/Buteco.Api/Infrastructure/Migrations/<ts>_AddEmbeddingMetrics.cs              (gerado)
apps/api/src/Buteco.Api/Infrastructure/Migrations/<ts>_AddEmbeddingMetrics.Designer.cs     (gerado)
apps/api/src/Buteco.Api/Infrastructure/Migrations/AppDbContextModelSnapshot.cs             (gerado)
apps/api/tests/Buteco.Api.Tests/EmbeddingMetricsMigrationTests.cs                          (novo)

# apps/workers — produção
apps/workers/src/Buteco.Workers/EmbeddingMetrics/Entities/EmbeddingCall.cs                 (novo)
apps/workers/src/Buteco.Workers/EmbeddingMetrics/Entities/KnowledgeIndexingAttempt.cs      (novo)
apps/workers/src/Buteco.Workers/EmbeddingMetrics/EmbeddingMetricsValues.cs                 (novo)
apps/workers/src/Buteco.Workers/EmbeddingMetrics/EmbeddingMetricsWriter.cs                 (novo)
apps/workers/src/Buteco.Workers/EmbeddingMetrics/MeasuredEmbeddingGenerator.cs             (novo)
apps/workers/src/Buteco.Workers/EmbeddingMetrics/KnowledgeIndexingAttemptContext.cs        (novo)
apps/workers/src/Buteco.Workers/ExecutionMetrics/ExecutionMetricsScope.cs
apps/workers/src/Buteco.Workers/ExecutionMetrics/ExecutionMetricsWriter.cs
apps/workers/src/Buteco.Workers/Knowledge/Indexing/KnowledgeIndexingService.cs
apps/workers/src/Buteco.Workers/Knowledge/Indexing/KnowledgeIndexingFailure.cs
apps/workers/src/Buteco.Workers/Knowledge/Execution/KnowledgeToolSetResolver.cs
apps/workers/src/Buteco.Workers/Infrastructure/AppDbContext.cs
apps/workers/src/Buteco.Workers/Infrastructure/Migrations/<ts>_AddEmbeddingMetrics.cs          (gerado)
apps/workers/src/Buteco.Workers/Infrastructure/Migrations/<ts>_AddEmbeddingMetrics.Designer.cs (gerado)
apps/workers/src/Buteco.Workers/Infrastructure/Migrations/AppDbContextModelSnapshot.cs         (gerado)

# apps/workers — teste
apps/workers/tests/Buteco.Workers.Tests/EmbeddingMetrics/EmbeddingMetricsSchemaMirrorTests.cs  (novo)
apps/workers/tests/Buteco.Workers.Tests/Support/EmbeddingMetricsReader.cs                      (novo)
apps/workers/tests/Buteco.Workers.Tests/Knowledge/Support/KnowledgeIndexingHarness.cs
apps/workers/tests/Buteco.Workers.Tests/Knowledge/Support/FakeEmbeddingGeneratorResolver.cs
apps/workers/tests/Buteco.Workers.Tests/Knowledge/KnowledgeIndexingTests.cs
apps/workers/tests/Buteco.Workers.Tests/Knowledge/KnowledgeIndexingZeroFragmentGuardTests.cs
apps/workers/tests/Buteco.Workers.Tests/Knowledge/KnowledgeIndexingConcurrencyTests.cs
apps/workers/tests/Buteco.Workers.Tests/Knowledge/KnowledgeToolExecutionEndToEndTests.cs
apps/workers/tests/Buteco.Workers.Tests/Knowledge/KnowledgeToolSetResolverTests.cs   (acrescentado no apply — ver abaixo)

# registro
02-HISTORICO_E_STATUS.md
CHANGELOG.md
openspec/changes/metricas-embedding-coleta/**
```

**Acréscimo à lista em 23/09/2026, com o motivo — e não violação silenciosa.**
`KnowledgeToolSetResolverTests.cs` **não estava** na lista fechada. Entrou porque
`KnowledgeToolSetResolver` precisou de `IOptions<EmbeddingOptions>` no
construtor: a linha de busca grava provedor, modelo e dimensão em **snapshot**
(D9), e o resolvedor não tinha essa informação — só `KnowledgeIndexingService`
tinha. **Este é o único sítio de construção manual** do tipo; todo o resto passa
pela interface registrada em DI, então o custo foi uma chamada. O arquivo
entraria no diff de qualquer jeito, e por isso o guarda de "busca fora de
execução não grava linha" passou a morar nele, que é a classe que **já tem o
arranjo** (D11).

A alternativa — resolver `IOptions<EmbeddingOptions>` do escopo de DI que
`SearchAsync` já abre — foi recusada: é service locator, e o idioma da casa é
injeção por construtor.

Isso contraria a projeção da convenção 18, que afirma *"nenhum construtor de
produção muda"* nomeando três tipos, e está **errada para um dos três**.
Comparado na tarefa 7.6, **não reprojetado**.

**Fora da lista, e por quê:** `apps/frontend` e `apps/inbox` (nenhuma tela nesta
etapa); `libs/` (sem segundo consumidor — design, *Árvore de pastas*);
`Options/EmbeddingOptions.cs` e `Knowledge/Indexing/KnowledgeIndexingQueues.cs`
(o `EMBEDDING_BATCH_SIZE` e a política de tentativas são Non-Goal);
`Knowledge/Embedding/EmbeddingGeneratorResolver.cs` (a medida entra no sítio de
chamada, **não** no resolvedor — D4);
`apps/api/src/Buteco.Api/ExecutionMetrics/**` e
`apps/workers/src/Buteco.Workers/ExecutionMetrics/Entities/**` (D1: nada de
`provider_calls`).

- [x] 0.1 Reler esta lista antes do primeiro commit e conferir `git status` contra ela; reportar qualquer caminho fora, em vez de commitar

## 1. Baseline medida, nunca herdada

- [x] 1.1 Com `podman ps` em **zero** containers e `DOCKER_HOST` apontando para o socket do Podman, medir as três suítes contra `HEAD` (`b95e3a6`) — `apps/workers`, `apps/api`, `apps/inbox` — e registrar os números **medidos** no `design.md`, na seção da projeção, ao lado da nota de que os últimos registrados (340/340, 346/346, 202/203) são da etapa 1 e a `compactacao-historico` entrou depois
- [x] 1.2 Registrar o flake conhecido de `apps/inbox` **pelo nome** na medição, e confirmar que reprova em suíte cheia e passa isolado — se não reproduzir, é achado a reportar
- [x] 1.3 Conferir que a projeção da convenção 18 no `design.md` está fechada antes de qualquer edição de código; o fechamento (7.x) **só compara**, nunca reescreve a projeção

## 2. Schema primeiro — `apps/api` migra, `apps/workers` espelha

O schema entra antes dos guardas, e é isso que permite vê-los reprovando **pela
propriedade** e não por compilação (D11).

- [x] 2.1 `apps/api`: criar `EmbeddingMetrics/Entities/KnowledgeIndexingAttempt.cs` e `EmbeddingCall.cs` — mapeamento, sem escritor
- [x] 2.2 `apps/api`: mapear as duas em `AppDbContext` — tabelas `knowledge_indexing_attempts` e `embedding_calls`, vocabulário como **texto** (convenção 12), as duas FKs anuláveis para `knowledge_indexing_attempts` e `task_executions` com `Restrict`, **nenhuma** FK para `knowledge_bases`, `knowledge_documents` ou `agents` (D9), sem coluna de tokens de saída (D10)
- [x] 2.3 `apps/api`: gerar a migração `AddEmbeddingMetrics` com `dotnet ef` e conferir que o `Up` é **só `CREATE TABLE`** — nenhuma coluna existente muda de tipo ou nulabilidade, e `provider_calls` não aparece no diff
- [x] 2.4 `apps/workers`: criar as duas entidades espelho e mapeá-las no `AppDbContext` do worker; gerar a migração espelho (que nunca roda contra banco real)
- [x] 2.5 `apps/api`: `EmbeddingMetricsMigrationTests` — as duas tabelas existem, as FKs são as duas esperadas, e **não** existe FK para o catálogo
- [x] 2.6 `apps/workers`: `EmbeddingMetrics/EmbeddingMetricsSchemaMirrorTests` — aplica as migrações do worker num Postgres limpo do Testcontainers e afirma tabelas, tipos e **nulabilidade** coluna a coluna; classe **fora** da `WorkerHostCollection`, no molde de `KnowledgeSchemaMirrorTests` (`IClassFixture<WorkerInfrastructureFixture>`)
- [x] 2.7 Confirmar que a `WorkerHostCollection` continua com **14** classes, por busca ancorada em `^\[Collection(`

## 3. Guardas vermelhos (convenção 15), contra `HEAD` + schema

Escritos **antes** do código de coleta, e vistos reprovando com as tabelas
**vazias** — a propriedade que falta, não a compilação. Cada guarda mora na
classe que já tem o arranjo (D11). Nenhuma classe nova entra na coleção.

- [x] 3.1 `Support/EmbeddingMetricsReader` — leitor das duas tabelas, infraestrutura de duplo, **projetado como item separado** na convenção 18
- [x] 3.2 Estender `FakeEmbeddingGenerator` (em `KnowledgeIndexingHarness`) para reportar `Usage` de forma controlável — ausente, com contagem, e com **zero** — e para lançar exceção tipada do SDK (`ClientResultException` com `Status`); estender `FakeEmbeddingGeneratorResolver` do mesmo jeito para a busca
- [x] 3.3 **Guarda de lote** (`KnowledgeIndexingTests`): documento com mais fragmentos que o lote gera **mais de uma** linha, nenhuma com `InputCount` maior que o lote, e a soma dos `InputCount` igual ao número de fragmentos — com a contagem de fragmentos **afirmada como precondição**; e o par: documento menor que o lote gera **exatamente uma**
- [x] 3.4 **Guarda do `502`** (`KnowledgeIndexingTests`): a chamada lança a exceção tipada com status `502` → linha com `Failed = true` e `HttpStatus = 502`, e a indexação falha exatamente como falharia sem coleta. É o defeito medido de 20/09 virando consulta
- [x] 3.5 **Guarda da falha no segundo lote**: a primeira chamada devolve vetores e a segunda lança → uma linha `Failed = false` e uma `Failed = true`, e **nenhum** fragmento gravado
- [x] 3.6 **Guarda negativo de nulo** (`KnowledgeIndexingTests`): sem `Usage`, `InputTokens` é nulo **e não é zero** — a asserção afirma a ausência do zero; com `InputTokenCount = 0`, grava `0`
- [x] 3.7 **Par "sem item"** (convenção 5, `KnowledgeIndexingZeroFragmentGuardTests`): documento **com conteúdo** cuja fragmentação devolve zero fragmentos → o gerador **não** é chamado, **nenhuma** linha em `embedding_calls`, e a linha de tentativa tem `Outcome = Failed` e `FailurePhase = Chunking` — com a precondição afirmada, para não passar por vacuidade
- [x] 3.8 **Guardas de descarte** (`KnowledgeIndexingConcurrencyTests`): descarte **antes de começar** não grava linha nenhuma; descarte **tardio** grava `Outcome = Discarded` e preserva as linhas de chamada (D8)
- [x] 3.9 **Guarda das três tentativas** (`KnowledgeIndexingTests`): três linhas com `Attempt` 1, 2 e 3, as duas primeiras `RetryScheduled` e a última `Failed` — é M30
- [x] 3.10 **Guarda de fase** (`[Theory]`): `ProviderResolution`, `EmbeddingGateway`, `VectorCountMismatch` e `DimensionMismatch` saem do passo em que a falha aconteceu, nunca do texto da exceção
- [x] 3.11 **Guardas da busca** (`KnowledgeToolExecutionEndToEndTests`): busca dentro de execução → linha `Purpose = Search`, `TaskId` da execução e `KnowledgeIndexingAttemptId` nulo; busca **fora** de execução → nenhuma linha e nenhuma falha; busca que falha no gateway → linha `Failed = true` **e** a tool devolve resultado de falha ao modelo (degradação graciosa intacta)
- [x] 3.12 **Guarda de degradação** (`KnowledgeIndexingTests`): com as tabelas de métrica ausentes, o documento termina `Indexed` com a contagem completa e sai um `LogWarning` com o identificador do documento
- [x] 3.13 **Guarda de exclusão** (D9): excluir uma base com documentos indexados funciona, e as linhas de métrica continuam lá
- [x] 3.14 Rodar os guardas e **registrar o vermelho** — quais reprovam e por qual asserção. Guarda que já nasce verde é achado: ou a propriedade já existia, ou o guarda não afirma o que diz

## 4. Coleta da indexação (`apps/workers`)

- [x] 4.1 `EmbeddingMetricsValues` — `Purpose` (`Indexing`, `Search`), `Outcome` (`Indexed`, `RetryScheduled`, `Failed`, `Discarded`) e `FailurePhase` (`Chunking`, `ProviderResolution`, `EmbeddingGateway`, `VectorCountMismatch`, `DimensionMismatch`, `Persistence`), todos como texto
- [x] 4.2 `MeasuredEmbeddingGenerator` — cronometra `GenerateAsync`, lê `GeneratedEmbeddings<T>.Usage` **sem normalizar nulo**, chama `ExecutionMetricsScope.HttpStatusOf` na exceção e **relança**, e entrega a linha ao `Action<EmbeddingCall>` do construtor. Implementa `IEmbeddingGenerator` direto e `Dispose` é **no-op** (D4), com o motivo escrito ao lado
- [x] 4.3 `KnowledgeIndexingAttemptContext` — id da tentativa, se a tentativa **foi contada**, a fase corrente e a lista de `EmbeddingCall`
- [x] 4.4 `EmbeddingMetricsWriter` — a escrita única, em escopo de DI próprio, pai e filhas num `SaveChangesAsync` só; **nenhum método lança** (convenção 4), falha vira `LogWarning` com `KnowledgeDocumentId`
- [x] 4.5 `KnowledgeIndexingService`: extrair o corpo atual para `IndexCoreAsync` e deixar `IndexAsync` como criar contexto → `await` → gravar métrica → devolver o desfecho, **depois** do estado terminal do documento (D7); marcar `Counted` em `MarkAttemptStartedAsync` e avançar a fase passo a passo; embrulhar o gerador em `GenerateInBatchesAsync`
- [x] 4.6 Conferir que `KnowledgeIndexingOutcome` devolvido ao consumidor é **o mesmo** que seria sem coleta, em todos os quatro valores

## 5. Coleta da busca (`apps/workers`)

- [x] 5.1 `ExecutionMetricsScope`: acrescentar a lista de `EmbeddingCall` e `RecordEmbeddingCall`, no molde exato de `RecordProviderCall` — inclusive o **no-op fora de escopo** e o `lock`. **Nenhum `AsyncLocal` novo** (D4)
- [x] 5.2 `ExecutionMetricsWriter.CloseAsync`: acrescentar o `AddRange` das linhas de busca ao `SaveChangesAsync` que já existe — é o que faz a FK para `task_executions` valer por construção
- [x] 5.3 `KnowledgeToolSetResolver.SearchAsync`: embrulhar o gerador que `Resolve()` devolveu no `MeasuredEmbeddingGenerator`, com o sink apontando para `ExecutionMetricsScope.RecordEmbeddingCall`. Conferir que `GenerateVectorAsync` (método de **extensão**) desemboca em `GenerateAsync` e é medido por esse único braço
- [x] 5.4 Conferir que o `catch` de degradação graciosa da busca continua devolvendo ao modelo exatamente o mesmo resultado de antes

## 6. Registro, sem correção (D5)

- [x] 6.1 Comentário ao lado do `switch` de `KnowledgeIndexingFailure.Describe`, nomeando que os braços de `429`/`401`/`403` são **inalcançáveis** no caminho `openai` (a exceção é `ClientResultException`, que deriva de `Exception`), e apontando o item do `02` que tem o assunto. **Não corrigir aqui** — muda texto de tela
- [x] 6.2 **Absorver no item existente do `02`, não abrir item novo** (D5): em *Itens em aberto* → *Abertos por `indexacao-lote-de-fragmentos` (2026-09-20)*, o item *"O texto de falha da tela de documentos manda repetir sem dizer o que já foi repetido"* aponta para o **mesmo arquivo, o mesmo braço `_` e o mesmo texto**. Acrescentar a segunda causa (braços inalcançáveis, verificado por execução; todo erro HTTP no balde genérico; reindexar não resolve o `502`) e marcar **Gatilho: cumprido**
- [x] 6.3 **Recalibrar a posição daquele item** (convenção 22): ele diz *"Posição: change de `apps/frontend`"*, e com a segunda causa a correção também é de `apps/workers` (o braço que falta em `Describe`). Corrigir a linha **com o motivo**, no idioma das outras correções de posição do `02`
- [x] 6.4 Conferir que não ficaram **dois** registros sobre o mesmo texto: se por algum motivo o item próprio for aberto mesmo assim, os dois têm de se referir um ao outro — meia-correção com o assunto dado por encerrado é o defeito que a absorção evita

## 7. Verificação (convenção 6) e fechamento

- [x] 7.1 Compilar **todos os `.csproj`** (não há `.sln` na raiz) e conferir a projeção de que **nenhum construtor de produção muda** e **nenhum registro de DI novo** entra — por compilação, não por `grep`
- [x] 7.2 Guardas do grupo 3 **verdes**, e cada um comparado ao vermelho registrado em 3.14
- [x] 7.3 Rodar as três suítes com `podman ps` em zero e comparar com a baseline de 1.1 — regressão é achado a reportar, não a normalizar
> **7.4 e 7.5 ficam PENDENTES para o deploy, por decisão do dono em
> 23/09/2026 — e por impossibilidade material aqui.** O `.env.prod` desta cópia
> de trabalho **não define `EMBEDDING_MODEL` nem `EMBEDDING_DIMENSIONS`** (só o
> `.env.prod.example` define, com `changeme` no modelo), e aponta
> `OPENAI_BASE_URL` para `https://api.openai.com/v1`. Não é o ambiente que
> produziu a medição de 20/09: a OpenAI não serve modelo de embedding de **4.096
> dimensões**, que é a dimensão da coluna do índice. O gateway do piloto
> sobrescreve a base URL no servidor.
>
> Rodar contra `api.openai.com` responderia sobre **outro provedor** — e com
> dimensão incompatível a indexação pararia em `DimensionMismatch` —, então não
> fecha nenhuma das duas. **As duas ficam escritas como não verificadas**, e o
> que cada uma deixa em aberto está no `design.md` (D6 e D10) e no `02`.

- [ ] 7.4 **Execução real, NO DEPLOY** (convenção 6, segredo por variável de ambiente ou `dotnet user-secrets`, **nunca** `appsettings` versionado): indexar um documento contra o gateway do piloto e conferir **(a)** se `InputTokens` vem preenchido ou nulo — resposta da pergunta aberta de D10 — e **(b)** o número de linhas gravadas contra o número de lotes esperado. Rodar `apps/workers` com `--no-launch-profile`, senão o `launchSettings.json` força `Development` e a conferência testa outra coisa
- [ ] 7.5 **Execução real do `502`, NO DEPLOY** (D6): reproduzir a falha do gateway com um lote grande o bastante e conferir que a linha sai com `HttpStatus = 502`. É o ciclo aberto em 20/09 fechando — o status que não ficou gravado em lugar nenhum passa a sair por consulta. Se a exceção **não** chegar como `ClientResultException` com `Status`, é achado a reportar e a corrigir, porque é a premissa de D6
- [x] 7.6 **Convenção 18 — fechamento compara, não reescreve**: medir arquivos, tipos públicos, casos de xUnit e linhas (`git diff -w`), com **infraestrutura de duplo** e **código gerado** contados à parte, e registrar o erro de cada item contra a projeção do `design.md`
- [x] 7.7 `openspec validate --all` verde
- [x] 7.8 `git status` conferido contra a lista fechada da seção 0
- [x] 7.9 Atualizar `02-HISTORICO_E_STATUS.md` (o que a change provou, o que ela **não** prova, os achados de método e o resultado de 7.4/7.5) e `CHANGELOG.md`
- [ ] 7.10 No deploy, registrar no `02` a data e a hora em que a coleta de embedding começa em produção, com o fuso — é o **segundo** regime da linha, ao lado de *"medindo desde 22/09/2026 01:21, `America/Sao_Paulo`"* da etapa 1 (convenção 22)
