## Why

A aba **Diagnóstico do índice** do protótipo (detalhe da base de conhecimento,
etapa 5c) mostra com que provedor, modelo e dimensão de embedding o índice foi
construído. Nenhuma rota expõe esse dado: `KnowledgeFragments/` em `apps/api` tem
**um arquivo** — a entidade —, e nenhuma query, handler, response ou endpoint
projeta a proveniência gravada. A 5c está bloqueada por este passo, que a
exploração de 12/09/2026 mediu e fechou.

O dado existe desde a etapa 2a, gravado por fragmento para a checagem
bidirecional do boot de `apps/workers`
(`EmbeddingIndexConsistencyValidation.cs:52-60`). Falta servi-lo — e servi-lo
pelo lado que importa: **quando o índice tem mais de uma combinação de
provedor/modelo/dimensão, `apps/workers` não sobe**, `apps/api` sobe normalmente,
e o operador abre o painel **exatamente nesse estado** para descobrir por quê.

## What Changes

- **Nova rota de leitura em `apps/api`**: `GET /knowledge-index/diagnostics`,
  devolvendo a **lista das combinações de proveniência gravadas no índice**, cada
  uma com provedor, modelo, dimensão e a contagem de fragmentos que a usam.
- A rota é **global, não por base** — ela não aceita identificador de base e
  agrega o índice inteiro. A dimensão é inescrevível de outro jeito
  (`vector(4096)` no schema: gravar 1536 devolve
  `expected 4096 dimensions, not 1536`, medido), e a checagem de boot exige
  combinação única no índice todo. Provedor, modelo e dimensão são propriedade do
  **sistema**.
- A rota devolve o **gravado**, nunca o declarado: `apps/api` não tem a seção
  `Embedding` de configuração, e servir o declarado exigiria duplicá-la, criando
  a segunda fonte do mesmo valor.
- **Lista vazia é resposta válida** (HTTP 200), e significa índice vazio. Nenhum
  valor de configuração entra na resposta para preencher o espaço.
- Registro do **gatilho medido** para o índice de cobertura das três colunas, que
  esta change deliberadamente **não** cria: p95 da rota acima de 500 ms ou heap da
  tabela de fragmentos acima de 500 MB.
- Sem mudança em `apps/workers`, `apps/inbox` ou `apps/frontend`. Sem migração de
  banco. Sem configuração nova. Nada de **BREAKING**: é rota nova, nenhum
  contrato existente muda.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `knowledge-document-indexing`: requisito `ADDED` para a rota de proveniência do
  índice. A capability já hospeda uma rota de leitura servida por `apps/api`
  ("Resumo de indexação por base") e já hospeda a checagem de integridade que lê
  **as mesmas três colunas** ("Integridade entre o modelo declarado e o índice
  gravado"). A rota é o segundo consumidor dessa propriedade, não uma propriedade
  nova.

## Impact

**`apps/api` — único app afetado.**

- Novo: `KnowledgeFragments/Queries/GetKnowledgeIndexDiagnostics/` (query e
  handler), `KnowledgeFragments/Responses/` (a resposta e o item de combinação),
  `KnowledgeFragments/Endpoints/KnowledgeIndexEndpoints.cs`.
- Modificado: `Program.cs`, uma linha de `Map*`.
- Testes: uma classe de contrato em `apps/api/tests`, semeando fragmentos por SQL
  cru no molde já existente de `KnowledgeDocumentIndexingContractTests` —
  `ApiFactoryFixture` já roda `pgvector/pgvector:pg18`, então nenhum fixture novo
  e nenhuma imagem nova.
- Documentação: `docs/architecture.md`, `CHANGELOG.md`, `01-ARQUITETURA_E_CONVENCOES.md`
  e `02-HISTORICO_E_STATUS.md`.

**Não afetado, e verificado:** nenhuma migração (as três colunas existem desde
`20260911004712_AddKnowledgeFragmentIndex`), nenhuma alteração de autenticação (a
`FallbackPolicy` de `Program.cs:65-69` cobre a rota nova por omissão, e
`ServiceScopeAuthorizationHandler` já nega token de serviço em toda rota fora das
duas que `apps/inbox` consome), nenhum índice de banco.

**Desbloqueia:** a etapa **5c — UI do diagnóstico do índice** (`apps/frontend`),
que além desta rota herda três correções de protótipo já registradas no
`design.md` desta change.
