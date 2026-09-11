Todas as tarefas rodam em **`apps/api`**. Nenhuma toca `apps/workers`,
`apps/inbox` ou `apps/frontend`.

## 1. Reindexação — entidade

- [x] 1.1 `apps/api`: acrescentar `KnowledgeDocument.RequestReindex()` em
  `KnowledgeDocuments/Entities/KnowledgeDocument.cs`, ao lado de `Update()`.
  Numa operação: `IndexingStatus = Pending`, `FailureReason = null`,
  `IndexingAttempts = 0`, `LastAttemptAt = null`, `UpdatedAt` atualizado.
  **Não** tocar `ContentHash`, `ContentRevision`, `IndexedAt` nem
  `FragmentCount`.
- [x] 1.2 `apps/api`: escrever no comentário do método as **duas exceções como
  exceções** (design.md, D4) — o bypass de "conteúdo idêntico não é
  reindexado" com o escopo da regra que ele contorna, e o refinamento de
  `IndexingAttempts` para "rodada", com o motivo real do reset (a janela até o
  consumidor pegar a mensagem), **não** o motivo que o enunciado supunha.
  Registrar também por que anular `ContentHash` seria armadilha.

## 2. Reindexação — operação e rota

- [x] 2.1 `apps/api`: criar `ReindexKnowledgeDocumentCommand(Guid
  KnowledgeBaseId, Guid Id)` em
  `KnowledgeDocuments/Commands/ReindexKnowledgeDocument/`.
- [x] 2.2 `apps/api`: criar `ReindexKnowledgeDocumentCommandHandler`,
  devolvendo `KnowledgeDocumentResponse?` (nulo = 404), no molde de
  `ActivateKnowledgeBaseCommandHandler` — **sem** record de `Result`, porque
  não há caso de validação. Filtrar por `KnowledgeBaseId` **e** `Id`.
- [x] 2.3 `apps/api`: publicar com `IKnowledgeIndexingJobPublisher.PublishAsync(new
  KnowledgeIndexingJobMessage(document.Id, document.ContentRevision), ct)`
  **depois** do `SaveChangesAsync`, na forma exata de
  `UpdateKnowledgeDocumentCommandHandler` (design.md, V4). Não inventar
  mensagem nem sobrecarga nova; `Attempt` fica no default `1`.
- [x] 2.4 `apps/api`: registrar `MapPost("/{id:guid}/reindex", ...)` em
  `KnowledgeDocuments/Endpoints/KnowledgeDocumentEndpoints.cs`, respondendo
  `Results<Ok<KnowledgeDocumentResponse>, NotFound>`.

## 3. Resumo de indexação — consulta e rota

- [x] 3.1 `apps/api`: criar `KnowledgeBaseIndexingSummaryResponse(Guid
  KnowledgeBaseId, int DocumentCount, int IndexedCount, int FailedCount)` em
  `KnowledgeBases/Responses/`. Documentar no record **por que o zero daqui é
  exibível** e o de `fragmentCount` não (design.md, D2).
- [x] 3.2 `apps/api`: criar `GetKnowledgeBaseIndexingSummaryQuery` e o handler
  em `KnowledgeBases/Queries/GetKnowledgeBaseIndexingSummary/`, no molde em
  lote de `ListAgentsQueryHandler` (design.md, V2): bases ordenadas por
  `CreatedAt` com `ThenBy(Id)`; **um** `GroupBy` sobre `KnowledgeDocuments` com
  as contagens condicionais; `ToDictionary` por `KnowledgeBaseId`; projeção
  final sobre as **bases** com `GetValueOrDefault`. **Nunca** contagem dentro do
  `Select`.
- [x] 3.3 `apps/api`: registrar `MapGet("/indexing-summary", ...)` em
  `KnowledgeBases/Endpoints/KnowledgeBaseEndpoints.cs`, respondendo
  `Ok<IReadOnlyList<KnowledgeBaseIndexingSummaryResponse>>`. Deixar comentada a
  razão de a rota existir em vez de o campo entrar em `KnowledgeBaseResponse`
  (design.md, D1) — é a decisão que alguém tentaria "corrigir".

## 4. Testes de reindexação

- [x] 4.1 `apps/api`: criar `Knowledge/KnowledgeDocumentReindexTests.cs` com os
  oito cenários da spec — falhou volta a `Pending` e enfileira; preserva
  `indexedAt`/`fragmentCount`; não altera revisão nem hash; a atualização
  seguinte com o mesmo conteúdo **não** enfileira; nunca indexado; base
  inativa; documento de outra base dá 404; documento inexistente dá 404.
- [x] 4.2 `apps/api`: em cada cenário, afirmar o publicado por
  `FakeKnowledgeIndexingJobPublisher.PublishedFor(documentId)` — **exatamente
  uma** mensagem, com `Attempt == 1` e a `ContentRevision` inalterada. Nos dois
  cenários de 404, a asserção é **negativa**: nenhuma mensagem publicada.
- [x] 4.3 `apps/api`: acrescentar em `Knowledge/KnowledgeTestClient.cs` os
  atalhos de arranjo — reindexar um documento, e levar um documento a `Failed`
  com motivo e tentativas gravados. Só arranjo, nenhuma asserção.

## 5. Testes do resumo

- [x] 5.1 `apps/api`: criar `Knowledge/KnowledgeBaseIndexingSummaryTests.cs` com
  os cenários da spec — agregação de uma base com estados variados; **base sem
  documento nenhum presente com zeros** (o par obrigatório da convenção 5);
  base inativa com contagens reais; nenhuma base cadastrada devolve lista
  vazia; documento excluído sai da contagem.
- [x] 5.2 `apps/api`: guarda de custo (R1) — com `EmittedSqlCapture`, afirmar
  que a requisição emite um número de comandos **independente do número de
  bases**, com arranjo de três bases sendo uma delas vazia.
- [x] 5.3 `apps/api`: guarda de ordenação (R6), no **par** que a convenção 15
  exige: comportamental com `CreatedAtTie` forçando o empate, **mais**
  `EmittedSqlCapture.AssertOrderByEndsWithTieBreak` sobre o SQL de produção —
  molde idêntico ao de `KnowledgeBaseCatalogTests.cs:56`.

## 6. Contrato e autenticação

- [x] 6.1 `apps/api`: acrescentar as duas rotas novas a
  `Knowledge/KnowledgeRouteAuthenticationTests.cs` — sem token dá 401, com token
  passa.
- [x] 6.2 `apps/api`: acrescentar a `Knowledge/KnowledgeBaseCatalogTests.cs` os
  dois cenários negativos do requisito novo de `knowledge-base-catalog` — a
  listagem e a criação de base **não** trazem campo de contagem. Asserção
  negativa sobre o JSON, que é a forma que impede a regressão bem-intencionada
  de "só acrescentar o campo".
- [x] 6.3 `apps/api`: acrescentar a `Knowledge/KnowledgeWireFormatTests.cs` a
  asserção de formato de fio do resumo (convenção 12), **inspecionando o texto
  do JSON** e não desserializando para o mesmo tipo: os nomes
  `knowledgeBaseId`, `documentCount`, `indexedCount`, `failedCount` como
  camelCase no fio.

## 7. Verificação dos guardas (convenção 15)

- [x] 7.1 `apps/api`: reintroduzir o defeito de R1 — mover a contagem para
  dentro do `Select`, virando uma consulta por base — e ver 5.2 **reprovar**.
  Conferir que reprova no **handler que a correção toca**, e não noutro
  componente. Desfazer.
- [x] 7.2 `apps/api`: reintroduzir o defeito de R2 — projetar sobre os grupos em
  vez de sobre as bases — e ver o cenário "base sem documento nenhum"
  **reprovar**. Desfazer.
- [x] 7.3 `apps/api`: **o guarda mais delicado dos cinco.** Reintroduzir o
  defeito de R3 — `ContentHash = null` dentro de `RequestReindex()` — e ver o
  cenário "a atualização seguinte não reindexa" **reprovar**. O defeito é
  sedutor: força o reprocessamento, resolve o problema imediato e passa numa
  revisão apressada; o que ele quebra é a regra da 2a de que hash nulo significa
  "linha legada, nunca indexada sob esta regra", que é o que faz o caminho de
  documento pré-existente funcionar. Conferir que reprova **no componente
  certo** — o cenário de reindexação, e não junto dos cenários de "conteúdo
  idêntico não é reindexado" da 2a, que afirmam outra coisa e continuariam
  verdes. É a segunda forma da convenção 15 (guarda no lugar errado). Desfazer.
- [x] 7.4 `apps/api`: reintroduzir o defeito de R6 — remover o `ThenBy(Id)` do
  handler do resumo — e conferir que a asserção determinística sobre o SQL
  reprova **em todas** as execuções, enquanto a comportamental pode passar. É a
  quinta forma da convenção 15, e não rodar a classe isolada é parte da
  conferência. Desfazer.
- [x] 7.5 `apps/api`: reintroduzir o defeito de R5 — filtrar o resumo por
  `IsActive` — e ver o cenário de base inativa **reprovar**. Desfazer.

## 8. `Purpose` de `knowledge-base-catalog` (gatilho de 09/09)

- [x] 8.1 **Já escrito na delta** (`specs/knowledge-base-catalog/spec.md`), antes
  da implementação e não depois do archive — é o que o gatilho pede. Conferir
  que ele responde as três coisas do formato de `api-response-ordering` e
  `agent-knowledge-binding-ui`: o que a capability cobre, a pergunta de operação
  que a justifica, e o que a distingue das vizinhas.
- [x] 8.2 Depois do `/opsx:sync`, **reler o arquivo vivo**
  `openspec/specs/knowledge-base-catalog/spec.md` e confirmar que o `Purpose`
  real substituiu o `TBD - defined by change knowledge-base-catalogo-documentos`.
  `openspec validate --strict` **não pega isto** — placeholder é texto válido.
  Conferir junto: sem bloco de requisito duplicado, sem resíduo de
  `## ADDED Requirements`, e diff aditivo.
- [x] 8.3 Registrar em `02-HISTORICO_E_STATUS.md` que o estoque de `Purpose`
  placeholder caiu de **39 para 38**, e que a queda veio **pelo gatilho** — a
  change que modificou a spec escreveu o `Purpose` na mesma passada — e **não**
  por change de mutirão. A distinção é o dado: é ela que diz se o mecanismo
  fixado em 09/09 funciona. Atualizar a lista de capabilities com `Purpose`
  real, que passa de quatro para cinco.

## 9. Fechamento

- [x] 9.1 `apps/api`: rodar `dotnet test Api.sln` **com a saída completa e não
  filtrada redirecionada para arquivo** em
  `~/.cache/buteco-agents/2b-baseline/api-fechamento.txt`, fora do diretório de
  sessão (convenção 19, terceira parte). **Nunca** canalizar para `grep`/`head`
  antes de a rodada terminar: fecha o cano, mata o produtor por `SIGPIPE`, e a
  rodada parece ter terminado sem produzir sucesso nem falha. Filtrar o arquivo
  **depois**.
- [x] 9.2 `apps/api`: conferir a carga da máquina **antes** da rodada (load de
  1 min < 5,0, nenhum processo alheio ≥ 100%) e registrá-la junto da saída —
  uma rodada com a máquina carregada não é comparável à baseline e já invalidou
  uma medição desta base.
- [x] 9.3 Comparar contra a baseline de `design.md`: **287/288 em
  `79ad3f1`**, com a única reprovação sendo
  `AgentDeactivationTests.SendMessage_WithPushNotificationConfig_ForInactiveAgent_NeverPublishesJobOrCallsWebhook`
  (flake de ordem já registrado). **Qualquer outra reprovação é desta change** —
  e classificar uma como "pré-existente" ou "ambiental" exige o `git worktree`
  limpo, não a impressão. Baseline vermelha removeria a hipótese de regressão e
  **só** ela; não dispensaria achar a causa.
- [x] 9.4 Registrar em `02-HISTORICO_E_STATUS.md` a **nona medição** da convenção
  18: projetado 14 arquivos / ~680 linhas (7 criados / ~525, 7 modificados /
  ~155), contra o entregue, **decomposto** — criados e modificados separados, só
  código, artefatos OpenSpec fora, e a razão de headline citada apenas como
  curiosidade. Se errar, registrar a **causa estrutural**, nunca um fator de
  correção.
- [x] 9.5 Conferir a convenção 9: se alguma decisão de `design.md` mudou durante
  a implementação por achado técnico, corrigir o `design.md` com a causa real —
  não deixar só no resumo da sessão.

## 10. Documentação (`repository-documentation`)

Conferir os quatro artefatos **um a um**. Onde não houver o que mudar, dizer que
foi conferido — silêncio não distingue "nada a fazer" de "ninguém olhou".

- [x] 10.1 `README.md`: conferir. Duas rotas internas autenticadas não mudam a
  porta de entrada; declarar conferido se for o caso.
- [x] 10.2 `docs/`: conferir `docs/architecture.md` e `docs/api.md` (se houver
  superfície de rota documentada) contra as duas rotas novas.
- [x] 10.3 `01-ARQUITETURA_E_CONVENCOES.md`: conferir a seção "Filas de
  trabalho" — ela descreve quem publica em `knowledge-indexing`, e passa a haver
  um terceiro publicador. Conferir também "Exclusão: catálogo × conteúdo", que
  enumera as rotas de documento.
- [x] 10.4 `CHANGELOG.md`: entrada em `[Unreleased]`, no formato Keep a
  Changelog, para as duas rotas novas.
- [x] 10.5 Rodar `python3 scripts/check-docs.py` e ver passar — links relativos,
  links para change arquivada com o prefixo `archive/`, apps documentados e
  `[Unreleased]` presente.
