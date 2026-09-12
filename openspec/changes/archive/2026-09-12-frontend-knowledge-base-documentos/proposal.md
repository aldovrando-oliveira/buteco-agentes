## Why

A etapa 5a-1 entregou o catálogo de bases e o detalhe de cada base, e deixou no
lugar da área de documentos uma **nota de sequenciamento** — não um estado vazio
—, porque naquele momento nada da indexação existia para ser exibido. Desde
então a `2a` (`knowledge-base-indexacao`) entregou o consumidor, a máquina de
estados e os campos por documento, e a `2b`
(`knowledge-base-indexacao-operacao`) entregou a rota de reindexação. Todas as
dependências desta tela estão implantadas e conferidas no código.

Hoje um operador **não tem como carregar um documento numa base** pelo painel: a
única entrada é `curl`. A base existe, a descrição que o modelo lê existe, o
vínculo com o agente existe, o índice existe — e o conteúdo, que é a razão de
tudo isso, só entra por linha de comando. É a última lacuna entre "a base está
cadastrada" e "o agente responde com ela".

Esta é a **segunda metade da etapa 5a**, dividida por tamanho durante a
reprojeção da etapa 1: a UI de catálogo MCP sozinha, sem upload nenhum, custou 29
arquivos / 3.088 linhas, e a 5a inteira somaria a isso o modal de dois modos, a
leitura de arquivo no cliente, a orquestração de N chamadas com estado por item e
a confirmação de exclusão.

## What Changes

Tudo em **`apps/frontend`**. Nenhuma rota, nenhum campo e nenhum comportamento de
`apps/api` ou `apps/workers` muda — as seis rotas de documento e a de
reindexação já estão no ar.

- **Tabela de documentos no detalhe da base**, substituindo
  `KnowledgeBaseDocumentsPlaceholder`, com os quatro estados de
  `KnowledgeIndexingStatus` (`Pending`, `Indexing`, `Indexed`, `Failed`) e a
  contagem de fragmentos governada por `indexedAt`, não pelo estado.
- **Faixa de falha** de largura total sob a linha que falhou, com
  `failureReason` **completo, sem truncar**, e o botão **Reindexar documento**
  chamando `POST /knowledge-bases/{id}/documents/{docId}/reindex`.
- **Polling condicional** da listagem enquanto houver documento em `Pending` ou
  `Indexing`, parando quando todos estiverem terminais. Sem barra de progresso
  percentual.
- **Modal de adicionar documento**, dois modos: subir arquivos
  (`.md`/`.markdown`/`.txt`, vários por vez, com título pré-preenchido do nome do
  arquivo e editável, e remoção individual por linha) e escrever manualmente.
- **Modal de atualizar documento**: substituir por arquivo (um só, título
  preservado) ou escrever manualmente com o conteúdo atual carregado.
- **Exclusão de documento** com confirmação, nomeando os agentes afetados a
  partir da derivação que a 5a-1 já entregou (`agentsConsultingBase`).
- **Faixa de resumo** acima da tabela quando houver documento não-terminal,
  derivada da própria listagem — sem requisição adicional.

**Três recusas ao protótipo herdadas da 5a-1 e confirmadas ao percorrê-lo**, mais
duas descobertas neste percurso, estão em `design.md` com o número que as
sustenta (convenção 17). A mais consequente: `0 fragmentos` num documento que
falhou é defeito do protótipo, e a regra correta é mais forte do que "omitir" —
documento que já foi indexado e falhou depois **continua exibindo a contagem
anterior**, porque os fragmentos antigos continuam respondendo.

Fora de escopo, com o motivo:

- **Diagnóstico do índice** (provedor, modelo, dimensão, fragmentos no índice) —
  é a etapa **5c**, e depende de dados que nenhuma rota de `apps/api` serve hoje.
- **Abas no detalhe da base** — a 5a-1 decidiu (D3) que a barra de abas nasce na
  5c, quando existirem duas de verdade. Uma aba só afirmaria uma estrutura que a
  tela não tem.
- **Colunas `Documentos`/`Indexação` e filtro `Com falha` no catálogo de bases** —
  a 2b entregou `GET /knowledge-bases/indexing-summary`, que destrava as três de
  uma vez; a decisão de sequenciamento está registrada em `design.md` (D1) e vai
  para uma change própria, com **gatilho imediato**.

## Capabilities

### New Capabilities

- `knowledge-document-catalog-ui`: a gestão de documentos de uma base de
  conhecimento no painel do operador — listar com estado de indexação, adicionar
  (por arquivo ou manualmente), atualizar, excluir e reindexar, consumindo as
  rotas de `knowledge-document-catalog` e a de reindexação de
  `knowledge-document-indexing` em `apps/api`. Inclui as asserções **negativas**
  que sustentam a convenção 13 nesta tela: contagem de fragmentos omitida e nunca
  zerada, ausência de progresso percentual, e nenhuma afirmação sobre o sistema
  ter verificado tamanho de documento.

### Modified Capabilities

- `knowledge-base-catalog-ui`: o requisito que hoje afirma que o detalhe da base
  **não consulta documentos** e exibe uma nota de sequenciamento no lugar deles
  deixa de valer — a área de documentos passa a existir. É o único requisito
  daquela capability que muda; as asserções negativas sobre as colunas do
  **catálogo** (`Documentos`, `Indexação`) continuam valendo e **não** são
  tocadas por esta change.

## Impact

**App afetado: `apps/frontend`, e só ele.**

- `src/features/knowledge-bases/` — feature existente, ampliada. Os documentos
  entram nela por conceito de domínio (convenção 7: feature se organiza por
  conceito, não por origem do dado), reusando o `request<T>`/`ApiError` da
  própria feature (D12 da 5a-1) em vez de um cliente compartilhado.
- `KnowledgeBaseDetailPage.tsx` — passa a buscar a listagem de documentos e a
  repassá-la como prop (convenção 7).
- `KnowledgeBaseDocumentsPlaceholder.tsx` + teste — **removidos**. O componente
  existia para dizer que a etapa seguinte traria os documentos; a etapa seguinte
  é esta.
- **Nenhuma dependência nova.** `@mantine/dropzone` **não está** no
  `package.json` (conferido) e **não será acrescentado**: a área de arrastar-e-
  soltar do protótipo é um `<input type="file">` com handlers de `dragover`/
  `drop`, o mesmo que o próprio protótipo faz. Ver `design.md`, D6.
- **Nenhuma rota nova no roteador.** Os dois modais vivem sobre o detalhe, como
  o protótipo propõe e como o modal de desativação da 5a-1 já faz.
- `openspec/specs/knowledge-base-catalog-ui/` — um requisito modificado.
- Documentação: `README.md`, `docs/architecture.md`, `CHANGELOG.md` e
  `02-HISTORICO_E_STATUS.md`, **cada um com tarefa própria** em `tasks.md` — a
  2a deixou três afirmações falsas nesses arquivos justamente por não ter essa
  tarefa, e `scripts/check-docs.py` não pega frase que virou mentira.
