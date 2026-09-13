## Why

A aba **Diagnóstico do índice** é a última tela prevista da linha de bases de
conhecimento (etapa **5c**), e ela existe para um estado específico: o operador
abre o painel porque `apps/workers` **não sobe**. A checagem de boot
`ValidateEmbeddingIndexConsistency` reprova quando o índice tem mais de uma
combinação de provedor/modelo/dimensão, `apps/api` continua de pé, e não há
hoje nenhuma tela que diga **o que está gravado no índice**. Sem ela, a única
forma de responder "com que modelo isso foi indexado?" é `psql`.

A dependência está no ar desde **13/09/2026**: `GET /knowledge-index/diagnostics`,
entregue por `openspec/changes/archive/2026-09-13-knowledge-index-diagnostics/`,
com os **três corpos reais** capturados à mão contra o app de pé — índice vazio,
uma combinação, duas combinações. Esta change consome esse contrato. Nada de
backend.

## What Changes

Tudo em **`apps/frontend`**. Nenhuma tarefa toca `apps/api`, `apps/workers` ou
`apps/inbox`, e nenhuma dependência nova é adicionada — logo nenhuma versão de
runtime, framework ou biblioteca é fixada por esta change.

- **A barra de abas nasce no detalhe da base**, com duas abas de verdade:
  `Documentos` (canônica, sem parâmetro) e `Diagnóstico do índice`
  (`?tab=diagnostico`). É a D3 da 5a-1 sendo cumprida, no desenho já estabelecido
  por `frontend-agente-detalhe-abas`: aba ativa na URL, `parseTab`,
  `keepMounted={false}`, valor desconhecido caindo na primeira.
- **Aba de diagnóstico com dois grupos, em duas escalas diferentes e ditas como
  tais**: *Como o índice foi construído (sistema)*, alimentado por
  `GET /knowledge-index/diagnostics`, e *Volume desta base*, derivado da listagem
  de documentos que a página já carrega — nenhuma requisição a mais.
- **O gate do vazio passa a ser a vacuidade do índice inteiro**, não
  `indexados > 0` da base. A proveniência é global: a rota não aceita
  identificador de base e o schema torna impossível bases com proveniências
  diferentes.
- **Mais de uma combinação é nomeada como corrupção**, com `fragmentCount` de
  cada uma — é o número que torna a reindexação decidível. Não é erro de
  requisição: HTTP 200 com dois itens.
- **A soma de fragmentos usa `indexedAt != null`**, nunca o estado. O protótipo
  soma só `status === 'indexed'` e **subconta** o índice, pela garantia 3 de D9 da
  etapa 1 (falha preserva os fragmentos que já respondiam).
- **A aba busca a proveniência só quando está ativa**, e acompanha a **única**
  transição que a muda — o primeiro documento a terminar de indexar — reusando a
  condição pura que a 5a-2 extraiu para o `refetchInterval` da listagem.
- **Contrariando o protótipo** (convenção 17), com o motivo registrado: a lista de
  documentos em falha **não é repetida** na aba — ela já existe na tabela de
  documentos, com o motivo completo e o botão de reindexar; e o estado vazio
  **não** renderiza três linhas de travessão, porque nesta área `—` significa
  "não sei", e índice vazio é fato conhecido.

## Capabilities

### New Capabilities

- `knowledge-index-diagnostics-ui`: a aba de diagnóstico do índice no painel do
  operador — proveniência **global** gravada (provedor, modelo, dimensão e
  fragmentos por combinação), volume **desta base** derivado da listagem de
  documentos, a corrupção de mais de uma combinação nomeada na tela, e as
  asserções negativas que impedem a interface de afirmar configuração, de
  confundir falha de leitura com índice vazio e de eleger uma combinação como "a
  certa". Consome `GET /knowledge-index/diagnostics`, cujo requisito de backend
  vive em `knowledge-document-indexing`.

### Modified Capabilities

- `knowledge-base-catalog-ui`: o requisito *Detalhe da base de conhecimento* hoje
  diz `SHALL NOT apresentar estrutura de abas`, com cenário afirmando a ausência.
  Passa a exigir a barra de duas abas com a aba ativa no endereço, no padrão do
  detalhe do agente. É exatamente o gatilho que aquele requisito registrou.

## Impact

**Código (`apps/frontend`)** — 9 arquivos criados e 4 modificados, projetados por
componente no `design.md` **depois** da verificação:

- Criados: tipo do fio (`types/knowledgeIndex.ts`), acesso
  (`api/knowledgeIndexApi.ts` + hook `api/useKnowledgeIndex.ts`), regras puras
  (`utils/indexDiagnostics.ts`), o componente da aba
  (`components/KnowledgeIndexDiagnosticsTab.tsx`) e os testes de cada um.
- Modificados, em pares com o teste: `pages/KnowledgeBaseDetailPage.tsx` (abas) e
  `utils/documentIndexing.ts` (a soma por `indexedAt`, onde a regra já mora).

**Blast radius conferido antes de projetar** (convenção 18, terceira dimensão):
`grep -rl "vi.mock(.*knowledgeBasesApi"` enumera **6** arquivos que mockam o
módulo parcialmente, um deles em outra feature. A rota nova **não** entra em
`knowledgeBasesApi.ts` — ela é de outro recurso (`/knowledge-index`) e ganha
módulo próprio, o que deixa esses 6 arquivos intocados.

**Rotas consumidas:** `GET /knowledge-index/diagnostics` (nova para o cliente) e
`GET /knowledge-bases/{id}/documents` (já consumida pela página). **Nenhuma rota
nova de backend, nenhuma mudança de contrato.**

**Documentação:** `README.md`, `docs/architecture.md`, `CHANGELOG.md`,
`01-ARQUITETURA_E_CONVENCOES.md` e `02-HISTORICO_E_STATUS.md` — um artefato por
tarefa, dizendo "conferido, nada a mudar" onde não houver o que mudar, com a
lista viva de correções de protótipo passando de **onze** para **treze**.
