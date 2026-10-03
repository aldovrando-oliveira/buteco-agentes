# Tarefas — `catalogo-base-sincronizada` (#102)

**Branch:** `feat/102-catalogo-base-sincronizada`, criada de `9cd2a93` (`main`
atualizada, sem commits à frente).

**App afetado: `apps/api` só.** Nenhuma tarefa toca `apps/workers`, `apps/inbox`,
`apps/frontend` ou o futuro `apps/connectors`. Fora dos apps, só documentação.

---

## 1. Verificações antes do código

- [x] 1.1 [`apps/api`] Verificar, com o EF Core e o Npgsql das versões fixadas em
  `Directory.Packages.props`, nunca de memória (convenção 6), que: a chave
  alternativa `("Id", "ContentMode")` com `HasConversion<string>()` é aceita; a FK
  composta `(KnowledgeBaseId, KnowledgeBaseContentMode)` → `(Id, ContentMode)` com
  `Restrict` sai no SQL de `dotnet ef migrations script` como descrita na D4; o
  índice parcial sai com o `WHERE` da D5 e da D10; e o change tracker recusa
  modificar `ContentMode` depois de anexado. Se algum ponto não sustentar,
  corrigir a D4 no `design.md` com a causa antes de seguir.

  **Medido (03/10/2026):** console no scratchpad com `Microsoft.EntityFrameworkCore`
  e `.Design` 10.0.10 e `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, modelo mínimo
  com as duas tabelas, `dotnet ef migrations script`. Saiu
  `CONSTRAINT "AK_knowledge_bases_Id_ContentMode" UNIQUE ("Id", "ContentMode")`;
  `FOREIGN KEY ("KnowledgeBaseId", "KnowledgeBaseContentMode") REFERENCES
  knowledge_bases ("Id", "ContentMode") ON DELETE RESTRICT`;
  `CREATE UNIQUE INDEX ... ("SyncProvider", "SyncFolderId") WHERE "ContentMode" =
  'Synced'`; `CREATE UNIQUE INDEX ... ("KnowledgeBaseId", "ExternalRef") WHERE
  "ExternalRef" IS NOT NULL`; `"ContentMode" text NOT NULL DEFAULT 'Manual'`. O
  change tracker, com a entidade anexada e `ContentMode` alterado, lançou no
  `DetectChanges`: `InvalidOperationException: The property 'Kb.ContentMode' is part
  of a key and so cannot be modified or marked as modified.` **As quatro coisas
  saíram como a D4, a D5 e a D10 descrevem; nenhuma decisão mudou.** Dois detalhes
  de forma, sem mudança de decisão: o EF cria sozinho um índice
  `("KnowledgeBaseId", "KnowledgeBaseContentMode")` para a FK composta, ao lado do
  índice simples que já existe; e o enum com `HasDefaultValue(Manual)` precisa de
  `HasSentinel` fora do domínio, senão o EF omite `Manual` no `INSERT` por ser o
  valor default do CLR (inofensivo aqui, porque o default do banco é o mesmo, mas
  esconderia o valor escrito).
- [x] 1.2 [`apps/api`] Medir a baseline da suíte num `git worktree` limpo de
  `9cd2a93` (convenção 19), com Podman exposto e `TZ=America/Sao_Paulo`, com o log
  inteiro em arquivo e o `load average` anotado. Colar o número aqui.

  **Medido:** `git worktree add --detach` em `9cd2a93`, `DOCKER_HOST` do Podman,
  `TESTCONTAINERS_RYUK_DISABLED=true`, `TZ=America/Sao_Paulo`; `restore`, `build` e
  `dotnet test apps/api/Api.sln --no-build` com o log inteiro no scratchpad
  (`baseline.log`). **501/501 em 2 min 41 s** (167 s de relógio). `load average`
  6,37 no início e 9,17 no fim, em 12 núcleos (`podman machine` `applehv`, 6 CPUs).
  O worktree foi removido depois.
- [x] 1.3 [`apps/api`] Remedir a contagem de classes de contêiner pelo comando da
  D12 e confirmar 43 classes e 42 fontes em `9cd2a93`. Colar aqui.

  **Medido:** `git grep` em `9cd2a93`, critério do `02` (`IClassFixture<...>` em
  `apps/api/tests/Buteco.Api.Tests/` excluída `Support/`, mais arquivos com
  `[Collection(MigrationPostgresCollection...)]`, mais arquivos fora de `Support/`
  com `PostgreSqlBuilder`/`RabbitMqBuilder`): 37 + 2 + 4 = **43 classes**; fontes de
  contêiner 37 + 1 (a collection, uma vez) + 4 = **42**.

## 2. Entidades e mapeamento

- [x] 2.1 [`apps/api`] Criar `KnowledgeBaseContentMode` (`Manual`, `Synced`) com
  `JsonStringEnumConverter`, no molde de `KnowledgeIndexingStatus`.
- [x] 2.2 [`apps/api`] Criar o record `KnowledgeBaseSyncIgnoredFile(ExternalRef,
  Name, Code, Detail?)`.
- [x] 2.3 [`apps/api`] `KnowledgeBase`: propriedades de origem e de estado da D2,
  todas com setter privado; `ContentMode` atribuído só nos construtores; construtor
  manual mantém `Manual`; fábrica `CreateSynced(name, description, provider,
  folderId, folderName, folderUrl)`; métodos `RecordSyncSuccess(now, folderName,
  folderUrl, ignoredFiles)` e `RecordSyncFailure(now, code, detail)` com as regras
  da tabela da D2. `UpdateDetails` não muda.
- [x] 2.4 [`apps/api`] `KnowledgeDocument`: `KnowledgeBaseContentMode`,
  `ExternalRef`, `ExternalVersion`; o construtor recebe o tipo da base; fábrica
  para documento sincronizado que exige referência e versão; método
  `ApplyExternalRevision(title, sourceType, extractedText, externalVersion)` da D3,
  que devolve o desfecho (`Unchanged` ou o `KnowledgeDocumentUpdateOutcome` de
  `Update`) e não toca `UpdatedAt` quando título, tipo de origem e texto são iguais.

  **Feito de forma diferente da redação, sem mudar decisão:** o construtor público
  **não** recebe o tipo da base; ele cria sempre documento de base `Manual`, e o de
  base `Synced` nasce só por `KnowledgeDocument.CreateSynced`, que exige referência
  e versão. O handler de cadastro do operador não tem por que passar o tipo, porque
  recusa base `Synced` antes (D7); se essa recusa sumir, a FK composta recusa a
  linha, que é o guarda 6.13(g) no banco. `ApplyExternalRevision` devolve um
  `KnowledgeDocumentUpdateOutcome` com os três valores falsos no caso sem efeito.
- [x] 2.5 [`apps/api`] Testes unitários sem contêiner: `RecordSyncFailure`
  preserva `LastSyncCompletedAt`, `SyncIgnoredFiles` e nome e URL da pasta, e só
  preenche `SyncFailingSince` quando nulo; `RecordSyncSuccess` limpa erro e "falhando
  desde"; `ApplyExternalRevision` com texto e título iguais muda só
  `ExternalVersion`, sem tocar `UpdatedAt`, `ContentRevision` nem
  `IndexingStatus`. No molde de `KnowledgeDocumentUpdateOutcomeTests`.
- [x] 2.6 [`apps/api`] `AppDbContext`: colunas da base e do documento; `CHECK`s da
  base (valores de `ContentMode`, combinação de origem por tipo, base `Manual` sem
  estado, as duas da D2); chave alternativa `("Id", "ContentMode")`; índice único
  parcial da pasta (D5); `CHECK`s do documento (D4); índice único parcial de
  `("KnowledgeBaseId", "ExternalRef")` (D10); a FK composta substituindo a simples,
  com `Restrict`; `SyncIgnoredFiles` como `jsonb` com `HasConversion` e
  `ValueComparer`, no molde de `Agent.Skills`. Cada bloco com o comentário da
  decisão que o justifica.
- [x] 2.7 [`apps/api`] Gerar a migração `AddKnowledgeBaseSync` e conferir o SQL
  contra o "Migration Plan" do `design.md`: defaults `'Manual'`, nenhum backfill
  além deles, e o `Down` recriando a FK simples.

## 3. Autorização

- [x] 3.1 [`apps/api`] `ServiceScopeAuthorizationHandler`: tabela de subjects da
  D6, com `OperatorSubject`, `InboxSubject` (renomeia `ServiceSubject`) e
  `ConnectorsSubject`; `operator` passa em tudo, cada serviço só na sua lista, e
  qualquer outro subject não é autorizado. Atualizar o comentário do topo.
- [x] 3.2 [`apps/api`] `AuthEndpoints` emite o token do operador por
  `OperatorSubject`; trocar `ServiceSubject` por `InboxSubject` em
  `ServiceScopeAuthorizationTests` e `KnowledgeRouteAuthenticationTests`.
- [x] 3.3 [`apps/api`] Criar a checagem de boot da D6 (cada par da lista existe
  entre os endpoints mapeados), chamada no fim do `Program.cs` ao lado de
  `ValidateRouteAuthenticationClassification`. Testes sem contêiner em
  `ServiceScopeRouteValidationTests`: a extensão falha nomeando subject, método e
  padrão para uma entrada inexistente, e a composição real passa.

  **A checagem pegou um defeito real na primeira execução.** A lista dizia
  `GET /sync/knowledge-bases`, e o `RawText` que `MapGroup("/sync/knowledge-bases")`
  gera para `MapGet("/")` é `/sync/knowledge-bases/`, com barra final (medido num
  console à parte: `MapGet("")` dá o mesmo). O boot de toda fixture caiu com
  `'service:connectors' lista GET '/sync/knowledge-bases', que não corresponde a
  nenhum endpoint mapeado`. Sem a checagem, o conector receberia `403` nessa rota em
  produção. A lista passou a usar o padrão com barra. A composição real ficou em
  `ServiceScopeAuthorizationTests.ProductionServiceRouteLists_MatchTheBuiltHostEndpoints`
  (endpoints do host construído pela fixture, não rotas montadas no teste), e não em
  `ServiceScopeRouteValidationTests`: a primeira redação desse teste mapeava as rotas
  a partir da própria lista, o que é tautológico, e foi trocada.

## 4. Rotas do operador

- [x] 4.1 [`apps/api`] `KnowledgeBaseResponse` com `contentMode`, `syncSource` e
  `syncState` (D13), e os dois records de resposta novos; `ignoredFiles` nulo
  enquanto não houve sucesso.
- [x] 4.2 [`apps/api`] `CreateKnowledgeBaseRequest` com `contentMode` opcional, e
  validação no endpoint pela D11: omitido ou `Manual` cria `Manual`; `Synced` e
  valor desconhecido respondem `400` em `contentMode`. `UpdateKnowledgeBaseRequest`
  não muda.

  `CreateKnowledgeBaseCommand` não mudou (a árvore da D12 o listava como alterado):
  só `Manual` chega ao handler, e o construtor de base já é manual.

- [x] 4.3 [`apps/api`] Handlers de cadastro, edição e exclusão de documento: base
  inexistente `404`, base `Synced` `409` (D7), antes de processar conteúdo; o
  cadastro passa o tipo da base ao construtor do documento. Os `Result` ganham o
  caso de conflito, e os endpoints respondem `TypedResults.Problem(statusCode:
  409)` com título em português.

  Na edição, o documento é procurado primeiro e o `409` sai da cópia do tipo no
  próprio documento (`KnowledgeBaseContentMode`, que a FK composta mantém igual ao
  da base); documento inexistente em base `Synced` responde `404`. Na exclusão, como
  a D7 pede, o `409` vem antes de procurar o documento. A exclusão passou a devolver
  `DeleteKnowledgeDocumentResult` (três desfechos) no lugar do `bool`.

- [x] 4.4 [`apps/api`] Conferir que reindexação, edição de base e
  ativação/desativação não consultam o tipo da base, e deixar comentário curto
  nesses handlers dizendo que continuam liberados em base `Synced`, com a
  referência à D7.

## 5. Rotas de serviço

- [x] 5.1 [`apps/api`] Criar `SyncCode` com a forma da D1 (regex e 64
  caracteres), e testes unitários com códigos aceitos e recusados, inclusive frase
  com espaço e acento, string vazia e 65 caracteres.

  A regex usa `\z`, não `$`: em .NET o `$` casa antes de um `\n` final, e
  `"access-denied\n"` passaria. Há um caso de teste para isso.

- [x] 5.2 [`apps/api`] `ListSyncedKnowledgeBases`: query, handler e resposta, com
  inativas, sem `Manual`, ordem por `CreatedAt` e `Id`.
- [x] 5.3 [`apps/api`] `ListSyncedDocumentRefs`: `404`, `409` em `Manual`, ordem
  por `ExternalRef` e `Id`, sem o texto.
- [x] 5.4 [`apps/api`] `UpsertSyncedDocument`: validação de forma no endpoint
  (os cinco campos), handler com `404`/`409`, `KnowledgeContentProcessor`,
  `ApplyExternalRevision` ou criação, evento pelas fábricas da #98 com o autor lido
  do token, publicação depois do `SaveChangesAsync`, e o tratamento de
  `UniqueViolation` da D10 (detach das entidades `Added`, uma releitura, aplicar como
  atualização). Resposta `{ documentId, outcome }`.

  O `catch` só aceita a violação do índice `IX_knowledge_documents_KnowledgeBaseId_ExternalRef`
  (pelo `ConstraintName`); qualquer outra sobe. Ele registra um evento de log
  (`ConcurrentSyncedDocumentUpsertRecovered`, id 1021), que é o que o teste de
  corrida conta.

- [x] 5.5 [`apps/api`] `DeleteSyncedDocument`: `externalRef` obrigatório na query,
  `404`/`409`, `204` com evento `Deleted` quando existe, `204` sem evento quando não.
- [x] 5.6 [`apps/api`] `RecordSyncResult`: validação de forma por desfecho (campos
  do outro desfecho recusados, códigos por `SyncCode`, itens sem `externalRef` ou
  `name` e referências repetidas recusados), `404`/`409`, instante pelo
  `TimeProvider`, `200` com a base.
- [x] 5.7 [`apps/api`] `KnowledgeSyncEndpoints` mapeando as cinco rotas da D8,
  `MapKnowledgeSyncEndpoints` no `Program.cs`, e as cinco entradas na lista de
  `service:connectors`.

## 6. Testes de aceite (Testcontainers, nas classes que já existem)

Os arquivos novos são `partial` das classes existentes (D12). Base `Synced` é
semeada por `KnowledgeSyncTestSeed`, pelo `DbContext` com `CreateSynced`.

- [x] 6.1 [`apps/api`] Declarar `partial` em `KnowledgeBaseCatalogTests` e
  `KnowledgeDocumentCatalogTests`, e criar `KnowledgeSyncTestSeed` e um helper de
  cliente com token de `service:connectors`.
- [x] 6.2 [`apps/api`] `KnowledgeBaseCatalogTests.Synced`: cadastro com
  `contentMode` omitido, `Manual`, `Synced` (`400`, nenhum registro) e desconhecido;
  `PUT` com tipo, provedor, pasta, nome e URL diferentes não altera os valores,
  afirmado pelo banco; `PUT` em base `Manual` com `contentMode: "Synced"` não a
  torna sincronizada; edição, ativação e desativação de base `Synced` preservam
  origem e estado.
- [x] 6.3 [`apps/api`] `KnowledgeBaseCatalogTests.Synced`, banco: as `CHECK`s da
  base por inserção direta (`Synced` sem pasta, `Manual` com pasta, falhando sem
  erro); índice da pasta com base ativa e com base inativa; `AbC` e `abc`
  coexistem; corrida de duas conexões inserindo a mesma pasta ao mesmo tempo,
  exatamente uma vencedora e a outra com `23505`.

  **Corrida da pasta:** 10 iterações, cada uma com duas conexões em transação que
  inserem a mesma pasta depois de uma `Barrier`. O teste afirma uma vencedora por
  iteração **e 10 violações `23505` no total**: a segunda inserção fica bloqueada na
  entrada não confirmada do índice e recebe a violação quando a primeira confirma,
  então a corrida acontece em toda iteração, e não por sorte de escalonamento.

- [x] 6.4 [`apps/api`] `KnowledgeBaseCatalogTests.Synced`, resultado de ciclo:
  falha preserva `lastCompletedAt`; duas falhas mantêm `failingSince` da primeira e
  avançam `lastFinishedAt`; sucesso limpa a falha e atualiza nome e URL sem tocar
  provedor e pasta; falha não altera ignorados nem nome; frase no lugar de código,
  campos do outro desfecho e referência repetida respondem `400` sem mudar o estado;
  base `Manual` `409`; falha não toca documentos nem histórico.
- [x] 6.5 [`apps/api`] `KnowledgeBaseCatalogTests.Synced`, formato de fio sobre o
  **texto** da resposta HTTP real: chaves em camelCase, nenhuma em PascalCase,
  `contentMode` como `"Synced"`, códigos como as strings gravadas; base `Manual` com
  `syncSource` e `syncState` `null`; base nunca sincronizada com `ignoredFiles:
  null`; base inativa com falha na listagem.
- [x] 6.6 [`apps/api`] `KnowledgeDocumentCatalogTests.Synced`, operador: `409` em
  criar, editar e excluir em base `Synced`, sem documento novo, sem alteração, sem
  evento e sem publicação; reindexar responde `200` e publica; o mesmo fluxo em base
  `Manual` continua `201`/`200`/`204`; `externalRef` no corpo do operador é
  ignorado.
- [x] 6.7 [`apps/api`] `KnowledgeDocumentCatalogTests.Synced`, invariantes no
  banco: documento com `ExternalRef` em base `Manual` e documento sem `ExternalRef`
  em base `Synced` recusados pela `CHECK`; documento com a cópia `Synced` em base
  `Manual` recusado pela FK; referência duplicada na mesma base recusada pelo índice.
- [x] 6.8 [`apps/api`] `KnowledgeDocumentCatalogTests.Synced`, upsert: criação,
  `Unchanged` com marcador novo (sem evento, sem publicação, `contentRevision`,
  `indexingStatus` e `updatedAt` iguais, marcador gravado), texto novo, só título,
  normalização de BOM e `CRLF` dá `Unchanged`, acima do teto `400` sem mudar o
  marcador, referência com caixa diferente cria outro documento, base inativa
  aceita, `Manual` `409`, base inexistente `404`.
- [x] 6.9 [`apps/api`] `KnowledgeDocumentCatalogTests.Synced`, corridas: dois
  upserts idênticos simultâneos de referência nova (um `Created`, um `Unchanged`, um
  documento, um evento, uma publicação, nenhum `500`); dois upserts com textos
  diferentes sobre documento existente (um documento, nenhum `500`, texto e
  `contentHash` coerentes com um dos payloads). Disparo com `Task.WhenAll` sobre dois
  clientes, repetido em laço curto para não depender de uma intercalação de sorte.

  **Corrida do upsert idêntico:** 30 iterações. O contador de log
  (`ConcurrentUpsertLogCounter`, sobre o evento 1021) mediu **29 de 30** iterações
  passando pelo `catch` de `UniqueViolation` na execução da classe; o teste exige
  pelo menos uma e imprime o número. Na outra, a segunda requisição leu o documento
  já confirmado e foi direto para a atualização. **Corrida de duas atualizações:** 15
  iterações; ela não passa pelo `catch` (o documento já existe), e o que afirma é um
  documento só, nenhum `500`, e texto e `contentHash` coerentes com um dos dois
  payloads.

  > **Insuficiente, corrigido na seção 10.** Esse teste passava com o defeito: as
  > duas escritas gravavam a mesma revisão, e ele não olhava as revisões. Foi
  > substituído por
  > `ConcurrentUpserts_WithDifferentTexts_OnAnExistingDocument_GetDistinctRevisions`.

- [x] 6.10 [`apps/api`] `KnowledgeDocumentCatalogTests.Synced`, exclusão e
  listagem: exclusão com evento `Deleted` de autor `service:connectors`; referência
  inexistente `204` sem evento; mesma referência em duas bases exclui só na pedida;
  listagem de referências com as versões do último upsert, `409` em `Manual`;
  listagem de bases sincronizadas com inativas e sem `Manual`.
- [x] 6.11 [`apps/api`] Histórico: os três eventos do subject de serviço com
  `author` `service:connectors`; contagem de eventos inalterada no `Unchanged` e na
  exclusão de referência inexistente.
- [x] 6.12 [`apps/api`] `ServiceScopeAuthorizationTests`: `service:connectors`
  autorizado nas cinco rotas de `/sync` e `403` em `GET /knowledge-bases` e
  `POST /knowledge-bases/{id}/documents`; `service:inbox` `403` nas cinco rotas de
  `/sync`, sem escrita; subject `service:desconhecido` `403` em
  `GET /knowledge-bases`; operador `200` em `GET /sync/knowledge-bases`.
- [x] 6.13 [`apps/api`] Guardas contra o defeito real (convenção 15), um de cada
  vez, vendo reprovar e desfazendo, com o nome do teste que reprovou colado aqui:
  (a) a regra antiga "quem não é `service:inbox` passa"; (b) `RecordSyncFailure`
  sobrescrevendo `SyncFailingSince`; (c) `RecordSyncFailure` limpando
  `LastSyncCompletedAt`; (d) `ApplyExternalRevision` delegando sempre a `Update`;
  (e) sem a `CHECK` de `ExternalRef` no documento; (f) sem o `catch` de
  `UniqueViolation` no upsert; (g) o `409` do operador removido do handler de
  exclusão; (h) índice da pasta com filtro por `IsActive`.

  **Medido, cada um reintroduzido, visto reprovar e desfeito** (script no
  scratchpad, filtro nas cinco classes tocadas; conferido por `grep` que nenhum
  ficou):

  | guarda | defeito | o que reprovou |
  |---|---|---|
  | (a) | `if (subject != InboxSubject) Succeed` no lugar da tabela | `ServiceScopeAuthorizationTests.ConnectorsToken_OnOperatorRoutes_ReturnsForbidden`, `ServiceScopeAuthorizationTests.UnknownSubject_OnOperatorRoute_ReturnsForbidden` |
  | (b) | `SyncFailingSince = now` | `KnowledgeBaseCatalogTests.TwoFailures_KeepTheFirstFailingSinceAndAdvanceLastFinished`, `KnowledgeSyncEntityTests.RecordSyncFailure_Twice_KeepsTheFirstFailingSince` |
  | (c) | `LastSyncCompletedAt = null` na falha | `KnowledgeBaseCatalogTests.RecordingAFailure_PreservesTheLastCompletedSync`, `KnowledgeBaseCatalogTests.SyncedBaseResponse_UsesCamelCaseAndStringCodesOnTheWire`, `KnowledgeSyncEntityTests.RecordSyncFailure_PreservesLastCompletedIgnoredFilesAndFolderSnapshot` |
  | (d) | `ApplyExternalRevision` delegando sempre a `Update` | `KnowledgeDocumentCatalogTests.Upsert_NewMarkerWithSameTitleAndText_HasNoEffectBesidesTheMarker`, `KnowledgeSyncEntityTests.ApplyExternalRevision_WithSameTitleTypeAndText_ChangesOnlyTheExternalVersion` |
  | (e) | sem a `CHECK` `CK_knowledge_documents_external_ref` na migração | `KnowledgeDocumentCatalogTests.Database_RefusesExternalRefInManualBase`, `KnowledgeDocumentCatalogTests.Database_RefusesMissingExternalRefInSyncedBase` |
  | (f) | sem o `catch` de `UniqueViolation` no upsert | `KnowledgeDocumentCatalogTests.ConcurrentIdenticalUpserts_OfANewReference_EndWithOneDocument` (só ele) |
  | (g) | sem o `409` no handler de exclusão do operador | `KnowledgeDocumentCatalogTests.Operator_DeletingDocumentInSyncedBase_IsConflictAndTheDocumentSurvives` (só ele) |
  | (h) | índice da pasta com `AND "IsActive"` no filtro | `KnowledgeBaseCatalogTests.FolderIndex_InactiveBaseStillHoldsTheFolder` (só ele) |

  **Um a mais, fora da lista:** (g2) sem o `409` no handler de **cadastro** do
  operador. Reprovou
  `KnowledgeDocumentCatalogTests.Operator_CreatingDocumentInSyncedBase_IsConflictAndWritesNothing`
  com `Expected: Conflict, Actual: InternalServerError`: sem a recusa, o
  `SaveChanges` falha, porque o construtor cria documento de base manual e a FK
  composta não aceita a cópia `Manual` numa base `Synced`. É a D4 segurando o
  defeito no banco; o `500` é a forma ruim, e o `409` do handler é a resposta.

## 7. Migração

- [x] 7.1 [`apps/api`] Criar `KnowledgeBaseContentModeMigrationTests` na
  `MigrationPostgresCollection` (D12), resolvendo a migração anterior pela lista
  ordenada, como `KnowledgeDocumentEventsMigrationTests`: banco parado na anterior
  com uma base ativa, uma inativa e documentos; depois de migrar, as duas bases
  `Manual` com nome, descrição, `isActive`, `createdAt` e `updatedAt` iguais, colunas
  de sincronização nulas, documentos com `ExternalRef` nulo e o resto igual, e
  nenhum evento criado.
- [x] 7.2 [`apps/api`] Guarda: um backfill provisório que preencha `UpdatedAt` das
  bases no `Up` precisa reprovar o 7.1. Desfazer e colar o nome do teste aqui.

  **Medido:** `UPDATE knowledge_bases SET "UpdatedAt" = now()` no fim do `Up`
  reprovou `KnowledgeBaseContentModeMigrationTests.Migration_TurnsExistingBasesIntoManualAndChangesNothingElse`
  (`Expected: ...,"UpdatedAt":"2026-09-02T10:00:00+00:00"` contra
  `Actual: ...,"UpdatedAt":"2026-10-03T05:16:33.910318"`). Desfeito.

## 8. Documentação

- [x] 8.1 [docs] `docs/architecture.md`: `KnowledgeBase` com tipo, origem e estado
  da sincronização; `KnowledgeDocument` com `ExternalRef`, `ExternalVersion` e a FK
  composta; a seção de autenticação com `service:connectors`, a recusa de subject
  desconhecido e como o `apps/connectors` vai assinar o próprio token (D6).
  **Correção (decisão do mantenedor, 03/10/2026):** a frase "`apps/api` emite
  (login do operador e token de serviço)" (`docs/architecture.md:585`) é falsa para
  o token de serviço. Reescrever dizendo que o `apps/api` emite só o token do
  operador, e que cada serviço assina o próprio token a cada requisição de saída com
  a chave compartilhada, citando onde o `apps/inbox` assina:
  `apps/inbox/src/Buteco.Inbox/Auth/ServiceTokenDelegatingHandler.cs:24` (subject na
  linha 12, TTL fixo de 5 min). Ajustar no mesmo sentido o item "Token de serviço"
  (linha 595).
- [x] 8.2 [docs] `01-ARQUITETURA_E_CONVENCOES.md`: modelo de domínio de
  `KnowledgeBase` e `KnowledgeDocument`, e a seção "Autenticação" com o subject novo
  e a regra de subject desconhecido. **Mesma correção da 8.1** na seção
  "Autenticação": a frase de `01-ARQUITETURA_E_CONVENCOES.md:405` passa a dizer que
  o `apps/api` emite só o token do operador e que o `apps/inbox` assina o próprio
  token de serviço em
  `apps/inbox/src/Buteco.Inbox/Auth/ServiceTokenDelegatingHandler.cs:24`; o item
  "Token de serviço" (linha 415) acompanha. Sem issue própria.
- [x] 8.3 [docs] `02-HISTORICO_E_STATUS.md`: seção da change; na seção da etapa 0,
  nota junto de "O que as issues herdam" dizendo que o comentário da etapa 0 na #102
  substituiu "hash de conteúdo" por "marcador do provedor", com o motivo; contagem de
  contêineres da tarefa 1.3 com a diferença de instrumento da D12; e as issues
  abertas na revisão dos artefatos (convenção 23): #116 (o `apps/inbox` autoriza
  qualquer subject) e #117 (chave de assinatura por serviço, `aguardando
  gatilho`).
- [x] 8.4 [docs] `CHANGELOG.md`, em `[Unreleased]`: tipo e origem da base, estado
  da sincronização na resposta, `409` do operador em base sincronizada, rotas de
  serviço e a recusa de subject desconhecido.

## 9. Verificação

- [x] 9.1 [`apps/api`] `dotnet test apps/api/Api.sln` com Podman exposto e
  `TZ=America/Sao_Paulo`; colar o total contra a baseline da 1.2 e conferir que a
  diferença fecha com os casos novos contados no diff.

  **Medido:** 603/603 em 2,25 min (138 s de relógio), `load average` 10,30 no
  início e 15,49 no fim; log inteiro no scratchpad (`final.log`). Contra a baseline
  da 1.2 (501/501): +102, e o diff contra a `main` conta 72 `[Fact]`, 21
  `[InlineData]` e 9 linhas de `MemberData` em 5 `[Theory]`, sem nenhum caso
  removido; 72 + 21 + 9 = 102.

- [x] 9.2 [`apps/api`] Remedir a contagem de contêineres pelo comando da D12 e
  confirmar 44 classes e 42 fontes.

  **Medido:** mesmo critério da 1.3, sobre a árvore de trabalho: 37 `IClassFixture` +
  3 pela collection + 4 que constroem o próprio = **44 classes**; **42 fontes**. Os
  arquivos `partial` novos não declaram `IClassFixture` e não somam.

- [x] 9.3 [docs] `python3 scripts/check-docs.py` sem violação.

  **Medido:** `Integridade da documentação: OK`.

- [x] 9.4 [`apps/api`] `openspec validate catalogo-base-sincronizada`.

  **Medido:** `openspec validate catalogo-base-sincronizada --strict` →
  `Change 'catalogo-base-sincronizada' is valid`.

## 10. Ajuste de revisão: corrida de escritas sobre documento existente

Achado na revisão da implementação: duas escritas que leem a revisão N gravavam as
duas N+1, e o indexador, que descarta pela revisão, podia gravar os fragmentos de um
texto sobre o outro (D10). O caminho do operador tinha o mesmo defeito antes desta
change.

- [x] 10.1 [`apps/workers`, só leitura] Confirmar como o consumidor usa
  `ContentRevision`. **Medido:** lê o texto do banco no início, sem rastreamento
  (`KnowledgeIndexingService.cs:71-73`), compara a revisão antes de começar (linha
  78), e grava os fragmentos sob `UPDATE ... WHERE "ContentRevision" = <mensagem>`
  (linha 321) na mesma transação que apaga e insere (linhas 318-345). Não grava
  `ContentRevision` em lugar nenhum (só filtra, linhas 291, 321 e 369). A análise do
  achado se confirmou. Nenhuma mudança em `apps/workers`.
- [x] 10.2 [`apps/api`] Escrever os testes de corrida das duas escritas (upsert e
  `PUT` do operador) afirmando revisões distintas, e rodá-los **contra o código
  anterior**. **Medido:** os dois reprovaram com as publicações `[2, 2]` (esperado
  `[2, 3]`); no `PUT`, as duas respostas com `contentRevision` 2.
- [x] 10.3 [`apps/api`] `ContentRevision` com `IsConcurrencyToken()`. **SQL emitido,
  capturado do caminho da requisição:** `UPDATE knowledge_documents SET ... WHERE "Id"
  = @p13 AND "ContentRevision" = @p14` e `DELETE FROM knowledge_documents WHERE "Id" =
  @p9 AND "ContentRevision" = @p10`. **Sem migração:**
  `dotnet ef migrations has-pending-model-changes` → "No changes have been made to the
  model since the last migration".
- [x] 10.4 [`apps/api`] Upsert do `service:connectors`: `DbUpdateConcurrencyException`
  → limpa o change tracker, relê, reaplica UMA vez; evento de log 1022.
- [x] 10.5 [`apps/api`] Atualização do operador: a mesma regra; evento de log 1023.
- [x] 10.6 [`apps/api`] Segunda falha seguida responde `503` com `Retry-After: 1`,
  nunca `500` (D10, com o `409` descartado).
- [x] 10.7 [`apps/api`] Os outros caminhos que gravam `KnowledgeDocument` com
  rastreamento: a reindexação e as duas exclusões passaram a receber a exceção
  (o `UPDATE` e o `DELETE` levam a revisão no `WHERE`) e ganharam a mesma releitura e
  o mesmo `503`. O cadastro e a inclusão do upsert são só `INSERT`, que não confere
  token. Varredura: `grep 'dbContext.KnowledgeDocuments'` em `apps/api/src`, seis
  arquivos de escrita.
- [x] 10.8 [`apps/api`] Testes:
  `ConcurrentUpserts_WithDifferentTexts_OnAnExistingDocument_GetDistinctRevisions` e
  `ConcurrentOperatorUpdates_WithDifferentTexts_GetDistinctRevisions` (20 iterações
  cada; revisões N+1 e N+2 nas publicações, N+2 no final; no `PUT`, o texto gravado é
  o da resposta com N+2); `DocumentUpdateAndDelete_CarryTheReadRevisionInTheWhereClause`
  (SQL emitido); `SecondConsecutiveConcurrencyFailure_Returns503WithRetryAfter_OnEveryWritePath`
  (os cinco caminhos, com um interceptor que lança a exceção em todo `SaveChanges`
  que modifica ou exclui documento). **Corridas medidas: 20 de 20 iterações pela
  releitura em cada caminho**, contadas pelo log.
- [x] 10.9 [`apps/api`] Guardas, cada um visto reprovar e desfeito: (t) sem
  `IsConcurrencyToken()` → reprovaram
  `ConcurrentOperatorUpdates_WithDifferentTexts_GetDistinctRevisions`,
  `ConcurrentUpserts_WithDifferentTexts_OnAnExistingDocument_GetDistinctRevisions` e
  `DocumentUpdateAndDelete_CarryTheReadRevisionInTheWhereClause`; (t2) sem o segundo
  `catch` do upsert → reprovou
  `SecondConsecutiveConcurrencyFailure_Returns503WithRetryAfter_OnEveryWritePath`.
- [x] 10.10 [docs] `design.md` (D10, risco, árvore), specs autorizadas
  (`knowledge-sync-service-api`, cenário da corrida sobre documento existente;
  `knowledge-document-catalog`, requisito dos dois `PUT`), `02` e `CHANGELOG.md`.
- [x] 10.11 [`apps/api`] `dotnet test apps/api/Api.sln`. **Medido:** 606/606 em 2,31
  min (142 s de relógio), `load average` 7,86 → 16,46. Contra 603: +3 (diff com 80
  atributos `[Fact]`/`[Theory]` novos contra 77 antes do ajuste, 21 `[InlineData]` e
  9 linhas de `MemberData`, nenhum removido: 105 casos sobre a baseline de 501). Na
  mesma execução, as corridas de concorrência passaram pela releitura em 19 de 20
  iterações cada. `check-docs.py` OK; `openspec validate --strict` válido.

## 11. Ajuste de revisão: cobertura dos cenários de `503`

Os cenários de `503` das specs afirmavam mais do que o teste provava (convenção 10).
Só teste; nenhum código de produção mudou.

- [x] 11.1 [`apps/api`] Em
  `SecondConsecutiveConcurrencyFailure_Returns503WithRetryAfter_OnEveryWritePath`,
  medido **caminho a caminho**, antes e depois de cada requisição, nos cinco (`PUT`,
  reindexação e `DELETE` do operador; upsert e `DELETE` por referência): a contagem de
  eventos da base não muda (`KnowledgeTestClient.CountDocumentEventsInDatabaseAsync`)
  e nenhuma publicação de indexação acontece (`factory.IndexingPublisher.PublishedFor`).
  No upsert, o texto, o `contentHash`, o `contentRevision` e o `externalVersion` do
  documento sincronizado continuam os anteriores. O teste também afirma que o host
  derivado publica no mesmo duplo da fixture (`Assert.Same`), senão a asserção de
  publicação passaria por vacuidade.
- [x] 11.2 [`apps/api`] Teste novo
  `Upsert_WhenTheDocumentIsDeletedBeforeTheReread_Returns503AndARetryCreatesIt`. A
  exclusão no meio é **determinística**: um interceptor de `SaveChanges`, na primeira
  gravação que modifica o documento, o exclui por SQL numa conexão própria e lança
  `DbUpdateConcurrencyException`; o handler relê, não acha, e responde `503` com
  `Retry-After`. Afirma que o interceptor disparou uma vez, nenhum documento com a
  referência, nenhum evento, nenhuma publicação; e que o mesmo upsert repetido sem o
  interceptor responde `outcome: "Created"`.
- [x] 11.3 [`apps/api`] Guardas, cada um visto reprovar e desfeito (conferido por
  `grep`):
  - (p1) publicar antes de confirmar a gravação, na atualização do operador →
    reprovou `SecondConsecutiveConcurrencyFailure_Returns503WithRetryAfter_OnEveryWritePath`
    com `PUT operador: publicou indexação` (e também
    `ConcurrentOperatorUpdates_WithDifferentTexts_GetDistinctRevisions`, com as
    publicações `[2, 2, 3]`);
  - (p2) gravar o evento antes de confirmar a exclusão, no `DELETE` do operador →
    reprovou `SecondConsecutiveConcurrencyFailure_Returns503WithRetryAfter_OnEveryWritePath`
    com `DELETE operador: registrou evento no histórico`;
  - (p3) recriar o documento na releitura do upsert em vez de responder `503` →
    reprovou `Upsert_WhenTheDocumentIsDeletedBeforeTheReread_Returns503AndARetryCreatesIt`
    com `Expected: ServiceUnavailable, Actual: OK`.
- [x] 11.4 [`apps/api`] `dotnet test apps/api/Api.sln`. **Medido:** 607/607 em 1,34
  min (82 s de relógio), `load average` 7,52 no início e 7,92 no fim. Contra 606: +1,
  o teste novo da 11.2; o da 11.1 só ganhou asserções. `openspec validate --strict`
  válido; `check-docs.py` OK.
