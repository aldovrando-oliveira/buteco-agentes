# Tarefas — `historico-documentos-base` (#98)

**Branch:** `feat/98-historico-documentos-base`, criada de `1cae600` (`main`
atualizada, sem commits à frente).

**App afetado: `apps/api` só.** Nenhuma tarefa toca `apps/workers`,
`apps/inbox` ou `apps/frontend`: a aba Histórico é a #101. Fora dos apps, só
`docs/architecture.md` e `CHANGELOG.md`.

---

## 1. Verificações antes do código

- [x] 1.1 [`apps/api`] Verificar como o Npgsql da versão fixada em
  `Directory.Packages.props` traduz a comparação do cursor
  `(OccurredAt, Id) < (cursorAt, cursorId)` sobre `timestamptz` e `uuid`, pela
  documentação oficial do provider ou por decompilação, nunca de memória. Usar a
  forma que gera comparação no SQL, e não avaliação no cliente. Se o resultado
  mudar a D7, corrigir o `design.md` antes de seguir.
- [x] 1.2 [`apps/api`] Remedir a contagem de classes de contêiner de `apps/api`
  pelo critério do `02` ("classes com fixture de contêiner mais classes que
  constroem o seu próprio, excluída `Support/`, contadas em todas as
  subpastas"), antes e depois da seção 8, e colar os dois números aqui. Medido na
  proposta: 41 em `1cae600`. Registrar no `02` a ressalva da D12: o critério
  precisa contar a collection uma vez.

  **Medido:** antes (`1cae600`) 41 = 36 com fixture + 5 próprios; depois 42
  pelo critério literal (38 com fixture, 2 delas pela collection, + 4 próprios),
  e 41 fontes de contêiner. Registrado no `02`.
## 2. Entidade, mapeamento e migração

- [x] 2.1 [`apps/api`] Criar `KnowledgeDocumentEventType` (`Created`, `Updated`,
  `Deleted`) com `JsonStringEnumConverter`, no molde de
  `KnowledgeIndexingStatus`.
- [x] 2.2 [`apps/api`] Criar `KnowledgeDocumentEvent` com as colunas da D1 e três
  fábricas (`Created`, `Updated`, `Deleted`) que só deixam montar o formato da D5:
  booleanos nulos em `Created` e `Deleted`, não nulos em `Updated`.
- [x] 2.3 [`apps/api`] Mapear no `AppDbContext`: tabela
  `knowledge_document_events`, `Type` como string, FK para `knowledge_bases` com
  `OnDelete(Cascade)`, **sem** FK para `knowledge_documents`, índice
  `(KnowledgeBaseId, OccurredAt, Id)` e a `CHECK` da D5. Comentar no mapeamento
  por que a cascata existe ao lado do `Restrict` do documento (D3).
- [x] 2.4 [`apps/api`] Gerar a migração `AddKnowledgeDocumentEvents` e conferir o
  `Up` à mão: só cria tabela, FK, índice e `CHECK`, sem nenhum `INSERT` nem
  `Sql(...)` de dados (D10).

## 3. Critério de "atualizado" na entidade

- [x] 3.1 [`apps/api`] Criar `KnowledgeDocumentUpdateOutcome(NeedsIndexing,
  ContentChanged, TitleChanged)` e fazer `KnowledgeDocument.Update` devolvê-lo.
  `ContentChanged` é a mesma condição que incrementa `ContentRevision`,
  `TitleChanged` é a comparação ordinal do título, e `NeedsIndexing` continua
  decidido por `ContentHash`, sem mudança (D4). Atualizar o comentário do método
  com a divergência da linha legada.
- [x] 3.2 [`apps/api`] Testes unitários em `KnowledgeDocumentUpdateOutcomeTests`
  (sem contêiner): só texto, só título, ambos, idênticos, só `sourceType`. Para a
  linha legada, forçar `ContentHash` nulo por reflexão e afirmar
  `NeedsIndexing = true` e `ContentChanged = false`.
- [x] 3.3 [`apps/api`] Guarda contra o defeito real: trocar provisoriamente
  `ContentChanged` para a comparação por `ContentHash`, ver o teste da linha
  legada reprovar, e desfazer.

## 4. Autor nas escritas

- [x] 4.1 [`apps/api`] Acrescentar `Author` a `CreateKnowledgeDocumentCommand`,
  `UpdateKnowledgeDocumentCommand` e `DeleteKnowledgeDocumentCommand`.
- [x] 4.2 [`apps/api`] Nos três endpoints de escrita de
  `KnowledgeDocumentEndpoints`, receber o `ClaimsPrincipal` e passar
  `user.FindFirst(ClaimTypes.NameIdentifier)?.Value` no command. Usar a mesma
  leitura de subject de `ServiceScopeAuthorizationHandler`, sem uma segunda forma
  de ler o claim. Claim ausente lança exceção, nunca grava autor vazio (D6).

## 5. Gravação dos eventos nos handlers

- [x] 5.1 [`apps/api`] `CreateKnowledgeDocumentCommandHandler`: adicionar o
  `Created` depois de todas as validações e antes do único `SaveChangesAsync`. A
  publicação na fila continua depois do `SaveChangesAsync`.
- [x] 5.2 [`apps/api`] `UpdateKnowledgeDocumentCommandHandler`: adicionar o
  `Updated` só quando `ContentChanged || TitleChanged`, antes do único
  `SaveChangesAsync`. `NeedsIndexing` continua decidindo a publicação.
- [x] 5.3 [`apps/api`] `DeleteKnowledgeDocumentCommandHandler`: adicionar o
  `Deleted` com o título do documento antes do `Remove` e do único
  `SaveChangesAsync`.
- [x] 5.4 [`apps/api`] Conferir que `ReindexKnowledgeDocumentCommandHandler` não
  foi tocado.

## 6. Rota de leitura

- [x] 6.1 [`apps/api`] Criar `KnowledgeDocumentEventCursor` (codifica e decodifica
  `(OccurredAt, Id)` em base64url, e devolve falha para entrada malformada em vez
  de lançar exceção).
- [x] 6.2 [`apps/api`] Criar `ListKnowledgeDocumentEventsQuery` e o handler:
  `null` para base inexistente; filtro **sempre** por `KnowledgeBaseId`; ordem
  `OccurredAt` desc, `Id` desc; busca 51 e devolve 50; `nextCursor` montado a
  partir da última linha **lida do banco**, e nulo quando a 51ª não veio (D7).
- [x] 6.3 [`apps/api`] Criar `KnowledgeDocumentEventResponse` e
  `KnowledgeDocumentEventPageResponse` com os campos da D8.
- [x] 6.4 [`apps/api`] Mapear `GET /knowledge-bases/{knowledgeBaseId:guid}/document-events`
  em `KnowledgeDocumentEndpoints`: cursor malformado vira `400` na chave
  `cursor`, base inexistente vira `404`. Sem entrada na allowlist anônima.

## 7. Testes de aceite (Testcontainers, em classes que já existem)

- [x] 7.1 [`apps/api`] Acrescentar a `KnowledgeTestClient` um helper que lê todas
  as páginas de eventos de uma base.
- [x] 7.2 [`apps/api`] `KnowledgeDocumentCatalogTests`: cadastro gera um
  `Created` com `author = operator` e booleanos nulos; cadastro recusado por
  conteúdo inválido, por teto e por base inexistente não gera evento.
- [x] 7.3 [`apps/api`] `KnowledgeDocumentUpdateTests`: só texto, só título,
  ambos (um evento só), idênticos (nenhum), recusada por conteúdo inválido, por
  teto, por documento inexistente e por documento de outra base (nenhum evento
  em nenhuma das duas bases); `INSERT` direto de `Updated` sem mudança é recusado
  pela `CHECK`.
- [x] 7.4 [`apps/api`] `KnowledgeDocumentIndexingContractTests`: linha legada com
  hash nulo e conteúdo idêntico é enfileirada e **não** gera evento.
- [x] 7.5 [`apps/api`] `KnowledgeDocumentDeleteTests`: o `Deleted` sobrevive ao
  documento junto com os eventos anteriores dele; exclusão inexistente ou de
  outra base não gera evento; apagar por SQL uma base sem documento remove os
  eventos dela e preserva os de outra base.
- [x] 7.6 [`apps/api`] `KnowledgeDocumentReindexTests`: reindexar não gera evento.
- [x] 7.7 [`apps/api`] `KnowledgeDocumentCatalogTests`, rota: isolamento por base;
  cursor da base A usado na base B; ordem `Deleted`, `Updated`, `Created`;
  desempate com `OccurredAt` empatado (gravado por SQL) em ordem de inserção
  oposta à de `Id`, atravessando a fronteira de página; evento novo entre
  páginas; 50 eventos (cursor nulo); 51 eventos (segunda página de um); base sem
  evento; base inexistente `404`; base inativa lida; cursor malformado `400`.
- [x] 7.8 [`apps/api`] Formato de fio afirmado sobre o **texto** da resposta HTTP
  real: `type` como string nos três valores e as chaves em camelCase.
- [x] 7.9 [`apps/api`] `KnowledgeRouteAuthenticationTests`: a rota nova sem token
  responde `401`, e com token de subject `service:inbox` responde `403` (D8),
  com o token de serviço emitido no molde de `ServiceScopeAuthorizationTests`.
- [x] 7.10 [`apps/api`] Guardas contra o defeito real, um de cada vez, vendo
  reprovar e desfazendo: (a) gravar o evento com um `SaveChangesAsync` próprio
  antes da validação no `Update` (forma corrigida, ver o risco na D2 do
  `design.md`); (b) remover o desempate por `Id`; (c) retirar o filtro por base da
  consulta; (d) remover a `CHECK`. Registrar no `02-HISTORICO_E_STATUS.md` qual
  teste reprovou em cada um.

## 8. Migração sem retroativos

- [x] 8.1 [`apps/api`] Rodar `RejectionMetricsMigrationTests` sozinha
  (`dotnet test --filter`) **antes** de qualquer mudança e colar o resultado.
  Medir também quantos contêineres ela sobe, pela saída do Testcontainers ou por
  `podman ps -a` durante a execução, como evidência para a #110 (um contêiner por
  teste). As outras 4 classes ficam para a #110, sem correção aqui.
  **Medido:** 7/7 verdes em 27 s, e **7** eventos `start` de
  `pgvector/pgvector:pg18` no `podman events` da janela: um contêiner por teste,
  confirmado. Depois da 8.3, as duas classes da collection: 10/10 em 5 s, **1**
  contêiner.
- [x] 8.2 [`apps/api`] Criar `Support/MigrationPostgresFixture.cs` (um contêiner
  `pgvector/pgvector:pg18` e `CreateDatabaseAsync()`, que cria
  `migration_<guid>` e devolve a connection string). Para a imagem, usar a
  constante das fixtures de `apps/api` se existir uma. Se não existir, usar o
  literal e **não** criar a constante: a contagem de cópias é da #110. Conferido
  na abertura da #110: 11 cópias literais, nenhuma constante. Criar também
  `Support/MigrationPostgresCollection.cs` (`[CollectionDefinition]` com
  `ICollectionFixture<MigrationPostgresFixture>`).
- [x] 8.3 [`apps/api`] Mover `RejectionMetricsMigrationTests` para a collection:
  sai o campo do contêiner, entra `[Collection]` e a fixture no construtor, e
  `InitializeAsync` cria o banco do teste e migra. **Nenhum `[Fact]` ou `[Theory]`
  muda.** Conferir pelo diff que só mudaram campo, construtor,
  `InitializeAsync`, `DisposeAsync` e a fonte da connection string de
  `ScalarAsync`. Rodar a classe sozinha e ver verde com o mesmo número de casos
  da 8.1.
- [x] 8.4 [`apps/api`] Criar `KnowledgeDocumentEventsMigrationTests` na mesma
  collection: num banco próprio, migrar até a migração imediatamente anterior a
  `AddKnowledgeDocumentEvents`, resolvida pela lista ordenada de migrações do
  `AppDbContext`; inserir base e documentos por SQL; aplicar a migração nova e
  afirmar zero linhas em `knowledge_document_events`. O teste falha de forma
  explícita se `AddKnowledgeDocumentEvents` não estiver na lista ou for a
  primeira. Num segundo banco, migrado até o fim, afirmar a
  forma lida de `information_schema` e `pg_constraint`: FK em cascata para
  `knowledge_bases`, nenhuma FK para `knowledge_documents`, a `CHECK` da D5 e o
  índice da D1.
- [x] 8.5 [`apps/api`] Guarda contra o defeito real: acrescentar provisoriamente
  um backfill de `Created` no `Up` da migração, ver o teste da 8.4 reprovar, e
  desfazer.

## 9. Documentação

- [x] 9.1 [docs] `docs/architecture.md`: a entidade `KnowledgeDocumentEvent` ao
  lado de `KnowledgeDocument`, com a exceção de FK (cascata para a base, nenhuma
  para o documento) e o autor como subject do token.
- [x] 9.2 [docs] `CHANGELOG.md`, em `[Unreleased]`: a rota nova e o histórico.
- [x] 9.3 [docs] `01-ARQUITETURA_E_CONVENCOES.md` e `02-HISTORICO_E_STATUS.md`:
  registrar a change e o que a implementação mediu (1.1, 1.2, 7.10).

## 10. Verificação

- [x] 10.1 [`apps/api`] `dotnet test apps/api/Api.sln` com Podman exposto em
  `DOCKER_HOST`, e o número de testes **medido** e colado aqui. Falha classificada
  como pré-existente só depois de rodar a baseline num `git worktree` limpo.
  **Medido:** 486/486, `Duração: 1 m 25 s` (91 s de relógio), `load average`
  2,3 em 12 núcleos, `postgres` e `rabbitmq` do compose de pé. A change acrescenta
  47 casos (contados no diff contra a `main`). Nenhuma falha, então nenhuma
  classificação dependeu de baseline. A tentativa de baseline num worktree limpo
  não executou teste nenhum (interrompida em 15 min antes do build), e está
  registrada no `02`.
- [x] 10.2 [docs] `python3 scripts/check-docs.py` sem violação.
  **Medido:** `Integridade da documentação: OK`.
- [x] 10.3 [`apps/api`] `openspec validate historico-documentos-base`.
  **Medido:** `Change 'historico-documentos-base' is valid`.
