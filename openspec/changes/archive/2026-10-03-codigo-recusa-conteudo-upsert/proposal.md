**Issue:** #120

## Why

O upsert de `/sync` recusa conteúdo com `400` e uma **frase** em `errors.content`,
sem código. A #105 precisa registrar cada arquivo recusado como ignorado com um
**código** (D1 da `catalogo-base-sincronizada`, #102: motivo é código estável, o
texto é do frontend), e hoje só conseguiria lendo a frase. Como o `apps/api` é a
autoridade única do teto de 1 MiB (D6 da `apps-connectors-google-drive`, #103), o
código tem de sair dele. O mesmo vale para as outras recusas de conteúdo que o
upsert pode devolver: o teto é uma de quatro, e a #105 recebe todas.

## What Changes

- Toda recusa de **conteúdo** do `KnowledgeContentProcessor` passa a carregar um
  código estável no formato da D1 da #102 (`^[a-z0-9]+(-[a-z0-9]+)*\z`):
  - `too-large`: texto extraído acima do teto, com o tamanho e o teto como
    extensões numéricas e no `detail`;
  - `unsupported-source-type`: `sourceType` sem extrator registrado;
  - `null-character`: conteúdo com U+0000;
  - `empty-content`: conteúdo vazio ou só de espaços depois da normalização.
- A resposta continua `400` com `ValidationProblemDetails` e o mesmo `errors`
  de hoje, e ganha a extensão **`code`** no padrão de erro com código do
  `apps/api` desde a #104. É aditiva: nada que já está no corpo sai ou muda.
- A recusa de **forma** (campo obrigatório ausente) continua
  `ValidationProblemDetails` **sem** `code`. A presença de `code` é o que separa
  as duas.
- O conteúdo **vazio ou só de espaços** deixa de ser recusado pela validação de
  forma do endpoint e passa a ser recusado pelo extrator, que já tinha a mesma
  regra e a mesma frase; a forma continua recusando o `content` **ausente**.
- As rotas do operador (`POST` e `PUT` de documento) passam a levar o mesmo
  código, porque a recusa nasce no processador compartilhado.
- O teto continua sendo uma constante só, `KnowledgeDocumentLimits.MaxContentBytes`.

Nenhuma mudança para quem envia dados válidos. O status, o `title` e o `errors`
de cada recusa continuam como estão. **O que muda é a ordem das recusas para
conteúdo em branco** (`""` ou só de espaços), que deixa de ser checado antes de
procurar a base: numa base inexistente a resposta passa de `400` a `404`, e numa
base `Synced` (pelas rotas do operador) passa de `400` a `409` (design.md, D2).

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `knowledge-sync-service-api`: o upsert por referência externa distingue a
  recusa de conteúdo, com código, da recusa de forma, sem código.
- `knowledge-document-catalog`: as recusas de conteúdo do cadastro e da
  atualização do operador carregam o código; o teto responde `too-large` com o
  tamanho e o teto; conteúdo vazio deixa de ser recusa de forma.
- `knowledge-source-extractor-plugin`: a falha de extração carrega um código, e o
  `sourceType` sem extrator responde `unsupported-source-type`.

## Impact

- **`apps/api`**: `KnowledgeContentProcessor`, `ExtractionResult`,
  `MarkdownSourceExtractor`, os três handlers que consomem o processador e seus
  tipos de resultado, `KnowledgeDocumentEndpoints` (validação de forma e a
  montagem da recusa) e `KnowledgeSyncEndpoints`. Testes de integração em
  `apps/api/tests`.
- **`apps/frontend`**: nenhuma mudança nesta change. O painel lê o `title` da
  resposta, que não muda. O achado de que o modal de documento mostra o título
  genérico do ASP.NET em vez do motivo foi aberto como #131 (design.md, D4).
- **`apps/connectors`**: nenhuma mudança. Quem passa a ler o `code` é a #105.
- **Documentação**: `01-ARQUITETURA_E_CONVENCOES.md`, `02-HISTORICO_E_STATUS.md` e
  `CHANGELOG.md`.
