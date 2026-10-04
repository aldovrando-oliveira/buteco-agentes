> **Conflito previsto com a #105.** A #105 (`feat/105-ciclo-de-sincronizacao`) edita
> em paralelo `01-ARQUITETURA_E_CONVENCOES.md`, `02-HISTORICO_E_STATUS.md`,
> `CHANGELOG.md` e `docs/architecture.md`. Nesta change esses arquivos recebem só
> **seções acrescentadas** e edições de uma ou duas linhas nos pontos indicados;
> nenhum trecho é reescrito. Quem fizer o merge depois da outra resolve os
> conflitos nesses quatro arquivos à mão. `openspec/specs/knowledge-sync-service-api`
> também pode conflitar no sync, se a #105 mexer nele.

> **Push.** A branch `feat/108-exclusao-base` está **sem upstream** (o
> `git worktree add` tinha configurado `origin/main`, e foi desfeito com
> `git branch --unset-upstream`). O primeiro push, depois do archive, é
> `git push -u origin feat/108-exclusao-base` — nunca `git push` sem argumentos.

## 1. Baseline (apps/api)

- [x] 1.1 (apps/api) Conferir o `load average` e rodar `dotnet test apps/api/Api.sln` antes de qualquer mudança, com `DOCKER_HOST` do Podman; registrar o número de testes aprovados e o commit. Com a máquina carregada pela suíte da #105, esperar e refazer antes de classificar falha.
  **Medido:** `0499161`, `dotnet test apps/api/Api.sln` **717/717** em 1 min 47 s, `load average` 7,77 no início e 15,60 no fim (a suíte da #105 rodava em paralelo).
- [x] 1.2 (apps/api) Medir a contagem de classes com contêiner pelo critério do `02` (mesmo `git grep` da #102) e registrar; a change não pode mudar o número de fontes de contêiner.
  **Medido** (critério do `02`, em todas as subpastas, excluída `Support/`): 35 usos de `IClassFixture` de contêiner (incluindo os dois tipos aninhados, `AgentInsightsEndpointsTests.AgentInsightsFixture` e `InsightsEndpointsTests.InsightsFixture`), 3 pela `MigrationPostgresCollection`, 4 que constroem o próprio. Somando as duas declarações aninhadas, como a #104 registrou: **37 + 3 + 4 = 44 classes**, igual ao fechamento da #104.

## 2. Rota de exclusão (apps/api)

- [x] 2.1 (apps/api) Criar `DeleteKnowledgeBaseCommand`, `DeleteKnowledgeBaseResult` (`Deleted`, `NotFound`, `Active`, `ConcurrentWrite`) e `DeleteKnowledgeBaseCommandHandler`: transação explícita, `FOR UPDATE` na linha da base, conferência de `IsActive` sob o bloqueio, `ExecuteDeleteAsync` dos documentos da base, exclusão da base, commit (D2, D3).
- [x] 2.2 (apps/api) Repetir a transação uma vez em `40P01`; segunda falha → `ConcurrentWrite` (D6, D11).
- [x] 2.3 (apps/api) Log informativo `1025` (`KnowledgeBaseDeleted`) com id, tipo e contagem de documentos apagados (D11).
- [x] 2.4 (apps/api) `MapDelete("/{id:guid}")` em `KnowledgeBaseEndpoints`: `204`, `404`, `409` `knowledge-base-active` em `ProblemDetails`, `503` com `Retry-After`. Reescrever o comentário das linhas 41-45 com o motivo da exceção (D1).
- [x] 2.5 (apps/api) Confirmar que a rota não entra em `ServiceScopeAuthorizationHandler.ServiceRoutes` (o subject de serviço recebe `403`).
  **Conferido:** `ServiceScopeAuthorizationHandler.ServiceRoutes` não mudou; o teste da 5.12 afirma o `403`.

## 3. Base sumida no meio da escrita (apps/api)

- [x] 3.1 (apps/api) Medir com `EmittedSqlCapture` a ordem dos comandos que o EF emite no upsert de atualização com evento (`UPDATE` do documento × `INSERT` do evento) e registrar o resultado na D6 do `design.md`, dizendo se o impasse com a exclusão é alcançável.
  **Medido** (`EmittedSqlCapture`, EF Core 10.0.10): nos três caminhos — upsert de inclusão, upsert de atualização, exclusão por referência — o EF emite UM `DbCommand` em lote com o `INSERT INTO knowledge_document_events` **antes** do comando sobre o documento. A escrita trava a base (`FOR KEY SHARE`) antes da linha do documento, na mesma ordem da exclusão: **impasse não alcançável**. Registrado na D6, e a ordem passou a ser afirmada por teste (`SyncDocumentWrites_EmitTheEventInsertBeforeTouchingTheDocumentRow`).
- [x] 3.2 (apps/api) Criar, para as rotas de `/sync`, o ponto único que reconhece violação das FKs **para a base** pelo nome da constraint — a do documento e a do evento, as duas que o upsert de inclusão grava — e relê a existência da base (D6). Conferir os nomes reais das constraints no banco migrado, não de memória.
  **Lido do banco** (`pg_constraint`, Postgres migrado por `dotnet ef database update` num contêiner próprio na porta 55108): `FK_knowledge_documents_knowledge_bases_KnowledgeBaseId_Knowled~` (63 caracteres, truncado pelo EF com `~`) e `FK_knowledge_document_events_knowledge_bases_KnowledgeBaseId`. Arquivo: `KnowledgeSync/KnowledgeBaseWriteFailures.cs` (o design dizia `KnowledgeBaseForeignKeys.cs`; nome corrigido lá).
- [x] 3.3 (apps/api) `UpsertSyncedDocumentCommandHandler`: inclusão com FK violada, atualização com releitura vazia e `40P01` → relê a base → `404` se sumiu; sem publicar indexação.
- [x] 3.4 (apps/api) `DeleteSyncedDocumentCommandHandler`: concorrência seguida de releitura vazia ou `40P01` → relê a base → `404` se sumiu, em vez de `204`.
  Além da falha de gravação, a referência ausente relê a base antes do `204`.
- [x] 3.5 (apps/api) `RecordSyncResultCommandHandler`: `DbUpdateConcurrencyException` (base apagada entre a leitura e o `UPDATE`) e `40P01` → relê a base → `404`.
- [x] 3.6 (apps/api) `KnowledgeSyncEndpoints`: mapear o novo desfecho para `404` nas três rotas de escrita.
  **Sem mudança necessária:** os endpoints já mapeavam `SyncedKnowledgeBaseLookup.NotFound` para `404`; os handlers passaram a devolver esse desfecho.

## 4. Comentário do 409 de pasta em uso (apps/api)

- [x] 4.1 (apps/api) Corrigir **só o comentário** de `FolderInUse` em `KnowledgeBaseEndpoints.cs:81-86`, que diz que não existe rota de exclusão de base até a #108: a rota passa a existir, e a mensagem continua neutra até o botão da #136 (D8). O `detail`, o requisito e o teste da #104 que proíbe `exclu`, `remov` e `apag` ficam como estão.

## 5. Testes (apps/api)

- [x] 5.1 (apps/api) Remover `DeleteKnowledgeBase_IsNotAllowedAndBaseSurvives` de `KnowledgeBaseCatalogTests.cs` (o `405` deixa de valer).
- [x] 5.2 (apps/api) Criar `Knowledge/KnowledgeBaseCatalogTests.Deletion.cs` (`partial`): exclusão de base inativa com documentos indexados, fragmentos, eventos e agente vinculado → `204`, e contagem zero da base em `knowledge_documents`, `knowledge_fragments`, `knowledge_document_events` e `agent_knowledge_bases`, lidas por SQL; o agente continua, sem a base.
- [x] 5.3 (apps/api) Mesmo arquivo: base ativa → `409` `knowledge-base-active`, e as quatro contagens iguais às de antes (asserção negativa).
- [x] 5.4 (apps/api) Mesmo arquivo: id inexistente → `404`; outra base com documentos, fragmentos, eventos e vínculos intacta nas mesmas contagens; linhas de `knowledge_indexing_attempts` da base excluída continuam existindo.
- [x] 5.5 (apps/api) Mesmo arquivo: base `Synced` inativa; cadastro de outra base na mesma pasta → `409` `folder-in-use`; exclusão; o mesmo cadastro → `201` (D5).
- [x] 5.6 (apps/api) Mesmo arquivo: par determinístico (D12) — SQL emitido na requisição real com `FOR UPDATE` na base antes do `DELETE` dos documentos, na mesma transação.
  O SQL e as transações vêm de um log próprio do teste (categorias de comando e de transação do EF), porque o `EmittedSqlCapture` da fixture só vê comandos.
- [x] 5.7 (apps/api) **Guarda (convenção 15):** retirar a conferência de `IsActive` do handler, rodar o teste de 5.3 e ver reprovar (`204`, base sumida); retirar o `FOR UPDATE` e ver 5.6 reprovar; devolver os dois. Registrar aqui as duas reprovações.
  **Guarda 1:** `if (knowledgeBase.IsActive)` trocado por `if (false && ...)` → `DeleteActiveBase_Returns409AndDeletesNothing` reprovou com `Expected: Conflict / Actual: NoContent`. **Guarda 2:** `FOR UPDATE` retirado do `FromSql` → `DeleteBase_LocksTheBaseRowBeforeDeletingDocuments_InOneTransaction` reprovou com `Nada contendo [FOR UPDATE] foi registrado`. Os dois desfeitos copiando o arquivo original de volta (`diff` idêntico) e conferidos por `grep -rn GUARDA-TEMPORARIA apps/` sem resultado.
- [x] 5.8 (apps/api) Criar `Knowledge/KnowledgeDocumentCatalogTests.BaseDeletion.cs` (`partial`), com um `SaveChangesInterceptor` que chama a rota real `DELETE /knowledge-bases/{id}` no primeiro `SaveChanges` da escrita observada e conta o disparo (molde de `DeleteThenConflictOnceInterceptor`): upsert de inclusão, upsert de atualização, exclusão por referência e resultado de ciclo → `404`, nenhum `500`, interceptor disparou, nenhuma indexação publicada.
  **Visto reprovando pelo motivo certo** com os três handlers de `/sync` revertidos (`git apply -R` do diff, reaplicado depois): upsert de inclusão `500`, upsert de atualização `500`, upsert sem mudança `503`, exclusão por referência `500`, resultado de ciclo `500` — o interceptor disparou **1 vez em cada um dos cinco caminhos**, e a exclusão respondeu `204` em todos. Depois da correção, os cinco respondem `404`. O caminho "sem mudança" foi acrescentado ao teste por ser o único que dava `503`.
- [x] 5.9 (apps/api) Mesmo arquivo: as quatro rotas de `/sync` depois da exclusão → `404`.
  Passava já com os handlers revertidos: a leitura inicial de cada rota já respondia `404` para base inexistente. Fica como cobertura do requisito.
- [x] 5.10 (apps/api) Se 3.1 mostrar que o impasse é alcançável: teste que o provoca (interceptor depois do `UPDATE` do documento, esperando a exclusão bloquear em `pg_stat_activity`) e afirma que nenhum dos dois lados responde `500`.
  **Não se aplica** (3.1): com a ordem medida não há interleaving que produza o impasse. O tratamento de `40P01` do design é exercitado com a exceção injetada: `Upsert_AfterOneDeadlockWithTheBaseStillThere_RetriesAndSucceeds` (era `500` antes do código), `DeleteBase_AfterOneDeadlock_RetriesAndDeletes` e `DeleteBase_AfterTwoDeadlocks_Returns503AndDeletesNothing`.
- [x] 5.11 (apps/api) Cenário pela rota no histórico (`knowledge-document-history`): base com documentos e eventos excluída → nenhum evento dela, nenhum evento novo em outra base.
- [x] 5.12 (apps/api) `ServiceScopeAuthorizationTests`: `service:connectors` em `DELETE /knowledge-bases/{id}` → `403`, base continua.
  Passava já antes da rota existir (respondia `403`, e não `405`); a causa desse `403` anterior não foi investigada. Com a rota, continua `403`, agora contra o endpoint real, e reprova se alguém incluir a rota na lista de `service:connectors`.
- [x] 5.13 (apps/api) Medir o tempo de exclusão de uma base com volume de referência (fragmentos de `vector(4096)`) e registrar para o `02` (Risks).
  **Medido** num Postgres próprio (`pgvector/pgvector:pg18`, porta 55108, removido depois), com os três comandos que o handler emite: base inativa com 100 documentos e **7.500 fragmentos `vector(4096)` (133 MB)**, buffers quentes — `FOR UPDATE` 0,7 ms, `DELETE` dos documentos com a cascata dos fragmentos **353 ms**, `DELETE` da base 1,5 ms, commit 7 ms: **~362 ms** no total.

## 6. Conferências sem mudança de código (apps/workers)

- [x] 6.1 (apps/workers) Reler `KnowledgeIndexingService.cs` no commit da implementação e confirmar que as linhas citadas na D7 continuam as mesmas; corrigir a citação se mudaram.
  **Conferido:** linhas 71-83, 318-337 e 366-382 de `KnowledgeIndexingService.cs` e 106-109 de `KnowledgeIndexingConsumer.cs` inalteradas no commit de base.
- [x] 6.2 (apps/workers) Confirmar no código da busca de conhecimento que uma consulta a base apagada devolve zero fragmentos sem exceção, e citar arquivo e linha na D7.
  **Conferido:** `KnowledgeToolSetResolver.cs:167-177` consulta fragmentos por `KnowledgeBaseId` sem tocar `knowledge_bases`; zero linhas caem em `:179-197` (resultado de "base sem conteúdo indexado"), sem exceção. Citado na D7.

## 7. Documentação (raiz e docs/)

- [x] 7.1 (docs) `01-ARQUITETURA_E_CONVENCOES.md`: acrescentar, ao fim de "Exclusão: catálogo × conteúdo", a subseção da exceção da base (D1), e trocar só as frases que afirmam que a base não tem exclusão (`KnowledgeBase` "sem exclusão", "Sem rota de exclusão de base (#108)" — esta reescrita para dizer que a rota existe e que a mensagem de pasta em uso continua neutra até o botão da #136 (D8), "Hoje as duas cascatas são inertes", "Hoje é inerte"). Pode conflitar com a #105.
- [x] 7.2 (docs) `docs/architecture.md`: acrescentar a exceção em "Exclusão: catálogo × conteúdo" e trocar as frases inertes em `KnowledgeDocumentEvent` e na FK `Restrict`. Pode conflitar com a #105.
- [x] 7.3 (docs) `CHANGELOG.md`, `[Unreleased]`: `Added` (rota de exclusão) e `Changed` (`405` que deixa de valer, e `404` nas escritas de `/sync` que encontram a base excluída). A frase do `409` de pasta em uso **não** muda nesta change (D8, #136) e não entra. Linhas acrescentadas. Pode conflitar com a #105.
- [x] 7.4 (docs) `02-HISTORICO_E_STATUS.md`: entrada da change com baseline, números finais remedidos (não de memória), guardas, medição de 3.1 e 5.13, e a #136. Seção acrescentada. Pode conflitar com a #105.
- [x] 7.5 (openspec) No sync, ajustar o Purpose de `openspec/specs/knowledge-base-catalog/spec.md` ("não existe rota de exclusão"), que o delta não alcança.
  **Feito no sync do archive (03/10/2026):** o Purpose passa a listar a exclusão e diz que ela é a exceção da #108, exige base inativa e leva o conteúdo junto.

## 8. Verificação

- [x] 8.1 (apps/api) `dotnet test apps/api/Api.sln` completo, com `load average` conferido; comparar com 1.1.
  **Medido:** **730/730** em 3 min 1 s, `load average` 8,27 no início e 22,40 no fim (a suíte da #105 rodava junto). Contra a baseline de 1.1 (717): +14 testes novos, −1 removido (5.1).
- [x] 8.2 (apps/workers) `dotnet test apps/workers/Workers.sln`, para confirmar que nada mudou no lado que lê o schema.
  **Medido:** **391/391** em 7 min 29 s, `load average` 22,40 → 3,36. Também rodou `tests/ApiConnectorsRoundTrip.Tests`: **4/4**, `load average` 3,36 → 4,40.
- [x] 8.3 (apps/api) Remedir a contagem de classes com contêiner (1.2): mesmo número de fontes.
  **Medido:** igual a 1.2 — 35 `IClassFixture`, 3 pela collection, 4 que constroem o próprio; os dois arquivos novos são `partial` sem fixture.
- [x] 8.4 (docs) `python3 scripts/check-docs.py` e `openspec validate exclusao-base-conhecimento --strict`.
  **Medido:** `Integridade da documentação: OK` e `Change 'exclusao-base-conhecimento' is valid`.
