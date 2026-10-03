# Tarefas — `codigo-recusa-conteudo-upsert` (#120)

**Branch:** `feat/120-codigo-recusa-conteudo`, criada de `2cb1f5c` (`main`
atualizada, sem commits à frente).

**Apps afetados:** só `apps/api` (código e testes). Nenhuma tarefa toca
`apps/connectors`, `apps/workers`, `apps/inbox` ou `apps/frontend`. O efeito no
frontend está na D4 e foi para a #131 (`Refs #131` no PR, não `Closes`).

---

## 1. Antes do código

- [x] 1.1 [`apps/api`] Medir a baseline da suíte de `apps/api` na `2cb1f5c`, pelo
  critério do `02` (comando, total, passados, falhos), nunca de memória.
  Registrar inline aqui.

  **Medido em 03/10/2026 18:18 -03, sobre `2cb1f5c`** (árvore só com a change não
  rastreada): `dotnet test apps/api/Api.sln` → **697/697**, 0 falhas, 1m35s de
  execução (1m48s com o build), com `load average` entre 7 e 13 por outra carga na
  máquina.
- [x] 1.2 [`apps/api`] Ler o texto real da recusa de hoje: um teste descartável
  que faz o upsert acima do teto e imprime o corpo cru. Conferir o `title`
  padrão, a ausência de `code` e a chave `errors.content` que a D3 assume.
  Divergência corrige a D3 antes de seguir.

  **Lido, sem divergência; a D3 não muda.** Corpo real das três recusas no upsert
  (teste descartável, apagado depois):
  `{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"content":["O conteúdo do documento tem 1048577 bytes e excede o limite de 1048576 bytes."]}}`.
  O conteúdo em branco e o com NUL têm a mesma forma, com a frase deles em
  `errors.content`. Nenhum tem `code`, `detail` nem `traceId`.

## 2. Código da falha no extrator e no processador (`apps/api`)

- [x] 2.1 [`apps/api`] Criar `KnowledgeContentRefusalCodes` com `too-large`,
  `unsupported-source-type`, `null-character` e `empty-content` (D1).
- [x] 2.2 [`apps/api`] `ExtractionResult.Failure` passa a exigir o código junto
  com a mensagem; `MarkdownSourceExtractor` usa `null-character` e
  `empty-content` (D5). Atualizar `MarkdownSourceExtractorTests` para afirmar o
  código de cada falha, inclusive o conteúdo só de BOM.
- [x] 2.3 [`apps/api`] Criar `KnowledgeContentRefusal` (chave, mensagem, código,
  e os números do teto quando é o teto) e trocar o dicionário de
  `KnowledgeContentResult` por ela. `KnowledgeContentProcessor` usa
  `unsupported-source-type` e `too-large`, com `maxContentBytes` lido de
  `KnowledgeDocumentLimits.MaxContentBytes` (D5). A mensagem de cada recusa fica
  como está hoje.
- [x] 2.4 [`apps/api`] Teste unitário `KnowledgeContentRefusalCodesTests`, sem
  contêiner: toda constante de `KnowledgeContentRefusalCodes` passa em
  `SyncCode.IsValid`, e a lista lida por reflexão tem as quatro.

## 3. Resultados, endpoints e validação de forma (`apps/api`)

- [x] 3.1 [`apps/api`] `CreateKnowledgeDocumentResult`,
  `UpdateKnowledgeDocumentResult` e `UpsertSyncedDocumentResult` carregam a
  `KnowledgeContentRefusal` em vez do dicionário; os três handlers a repassam.
- [x] 3.2 [`apps/api`] `KnowledgeDocumentEndpoints.ContentRefused(refusal)`:
  `TypedResults.ValidationProblem` com o mesmo `errors` de hoje, `title` padrão,
  extensão `code`, e no `too-large` o `detail` e as extensões `contentBytes` e
  `maxContentBytes` (D3). As duas rotas do operador e o upsert de `/sync` passam a
  usá-lo (D4).
- [x] 3.3 [`apps/api`] `ValidateShape` passa a recusar só `content` ausente
  (`null`); `""` e só de espaços seguem para o extrator (D2). Conferir que a
  mensagem do extrator para vazio é a mesma de hoje.

## 4. Testes de aceite sobre o texto da resposta (`apps/api`)

Todos leem o corpo com `JsonDocument` a partir de `ReadAsStringAsync`, nunca
desserializando para um tipo do `apps/api` (convenção 12). Ficam em
`KnowledgeDocumentCatalogTests.ContentRefusal.cs`, arquivo novo da classe
`partial` existente; nenhuma classe nova com contêiner.

- [x] 4.1 [`apps/api`] Upsert acima do teto sobre documento existente: `400`,
  `code` `too-large`, `contentBytes` igual ao tamanho do texto extraído,
  `maxContentBytes` igual a `KnowledgeDocumentLimits.MaxContentBytes`; e o
  documento continua com o texto, a `contentRevision` e o `externalVersion` de
  antes. Atualizar `Upsert_AboveTheSizeCap_IsRefusedAndKeepsTheMarker` ou
  substituí-lo por este.
- [x] 4.2 [`apps/api`] Upsert com `sourceType` sem extrator, com U+0000 e com
  `content` `""`: `400` com `unsupported-source-type`, `null-character` e
  `empty-content`, e nenhum documento criado.
- [x] 4.3 [`apps/api`] Asserção negativa: upsert sem `externalRef` responde `400`
  com `errors.externalRef` e **sem** a propriedade `code`. Mesmo para o upsert sem
  o campo `content` (ausente), que continua forma.
- [x] 4.4 [`apps/api`] Upsert com texto extraído de exatamente
  `MaxContentBytes`: `200` com `outcome: "Created"`.
- [x] 4.5 [`apps/api`] Operador: `POST` acima do teto responde `code` `too-large`
  com `errors.content` e o `title` de antes; `PUT` com U+0000 responde
  `null-character` e o documento não muda; `POST` sem título responde sem `code`.
- [x] 4.6 [`apps/api`] Operador: `POST` com conteúdo só de espaços em base
  inexistente responde `404` (efeito da D2), e em base existente `400` com
  `empty-content`.
- [x] 4.7 [`apps/api`] Rodar sem mudança de asserção os testes atuais das rotas
  do operador e do upsert (`KnowledgeDocumentCatalogTests`,
  `KnowledgeDocumentUpdateTests`, `MarkdownSourceExtractorTests`). Listar aqui
  qualquer teste que tenha precisado mudar, com o motivo; a expectativa é nenhum
  além do 4.1.


  **Resultado:** os testes de `Knowledge` rodaram 403/403 com a implementação.
  **Nenhum teste existente mudou de asserção** — nem o do 4.1:
  `Upsert_AboveTheSizeCap_IsRefusedAndKeepsTheMarker` continua como estava e
  verde, e o caso com código entrou como teste novo
  (`Upsert_AboveTheSizeCap_RespondsTooLargeOnTheWireAndKeepsTheDocument`), em vez
  de substituí-lo. Em testes existentes houve só **acréscimo**, previsto no 2.2:
  `MarkdownSourceExtractorTests` ganhou a asserção do `FailureCode` em dois testes
  e o caso `"\uFEFF"` na teoria de conteúdo em branco. E um **comentário** ficou
  falso e foi corrigido: `KnowledgeDocumentCatalogTests.cs:351-354` dizia que
  "conteúdo vazio já para no endpoint", o que a D2 deixou de tornar verdade.

  **Antes da implementação**, o arquivo novo rodou com 13 reprovações, todas pelo
  motivo certo (`Sem a propriedade code no corpo`, e `400` em vez de `404` no caso
  da base inexistente). Os testes de forma sem código e o de exatamente no teto
  passaram já ali, como deviam: descrevem o que se mantém, e o 5.2 é que prova que
  pegam a regressão.
## 5. Guardas que precisam falhar contra o defeito (convenção 15)

- [x] 5.1 [`apps/api`] Reintroduzir a resposta só com frase no upsert (voltar
  `KnowledgeSyncEndpoints` a `TypedResults.ValidationProblem(errors)` sem
  extensão) e ver o 4.1 reprovar. Registrar a mensagem da reprovação aqui.

  **Reprovou:** 6 de 100 em `KnowledgeDocumentCatalogTests`, entre eles
  `Upsert_AboveTheSizeCap_RespondsTooLargeOnTheWireAndKeepsTheDocument`, com
  `Sem a propriedade code no corpo: {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"content":["O conteúdo do documento tem 1048577 bytes e excede o limite de 1048576 bytes."]}}`.
  Os outros cinco são os casos de `Upsert_OtherContentRefusals_RespondTheirCode`,
  com a mesma mensagem. Desfeito.
- [x] 5.2 [`apps/api`] Pôr um `code` na recusa de forma do upsert e ver o 4.3
  reprovar.

  **Reprovou:** com `code: "invalid-payload"` na recusa de forma, 2 de 100:
  `Upsert_WithoutExternalRef_IsShapeRefusalWithoutCode` e
  `Upsert_WithoutContentField_IsShapeRefusalWithoutCode`, com
  `Recusa de forma com code: {...,"errors":{"externalRef":["A referência externa do arquivo é obrigatória."]},"code":"invalid-payload"}`.
  Desfeito.
- [x] 5.3 [`apps/api`] Devolver o vazio a `ValidateShape` e ver o teste de
  `empty-content` do 4.2 reprovar.


  **Reprovou:** 5 de 100: os casos `""` e `"   \n\t  "` de
  `Upsert_OtherContentRefusals_RespondTheirCode` e de
  `CreateDocument_WithBlankContent_RespondsEmptyContent`, com
  `Sem a propriedade code no corpo: {...,"errors":{"content":["O conteúdo do documento é obrigatório e não pode ser vazio."]}}`,
  e `CreateDocument_WithBlankContentInMissingBase_Returns404` com
  `Expected: NotFound Actual: BadRequest`. O caso só de BOM continuou verde, como
  devia: ele nunca passou pela forma. Desfeito.

  Conferido por `grep` depois dos três: nenhum dos defeitos ficou no código.
## 6. Suíte e documentação

- [x] 6.1 [`apps/api`] `dotnet test apps/api/Api.sln` inteira, com o número
  comparado à baseline do 1.1.

  **Medido em 03/10/2026 18:36 -03:** **717/717**, 1m30s. Contra a baseline de
  697: **20 entradas novas e nenhuma saída**, por nome de teste nos `.trx`, igual à
  projeção (17 em `KnowledgeDocumentCatalogTests`, 2 em
  `KnowledgeContentRefusalCodesTests`, 1 caso em `MarkdownSourceExtractorTests`).
  `tests/ApiConnectorsRoundTrip.Tests`: 4/4.
- [x] 6.2 [`apps/api`] `01-ARQUITETURA_E_CONVENCOES.md`: no contrato de conector,
  os códigos de recusa de conteúdo do upsert e a regra de presença do `code`.
- [x] 6.3 [`apps/api`] `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md` (`[Unreleased]`),
  com o que a implementação mediu.
- [x] 6.4 `python3 scripts/check-docs.py` e
  `openspec validate codigo-recusa-conteudo-upsert --strict`.

  `Integridade da documentação: OK` e `Change 'codigo-recusa-conteudo-upsert' is valid`.
