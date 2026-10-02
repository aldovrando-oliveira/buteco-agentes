**Issue:** #98

## Why

Hoje não existe registro do que mudou nos documentos de uma base: quando um
documento foi incluído, atualizado ou excluído. Na base manual isso já falta para
auditoria, e na base sincronizada (linha do Google Drive, #102 e seguintes) vai ser
a única forma de o operador saber o que aconteceu sem ter feito nada.

O histórico não depende da sincronização e entrega valor sozinho. Por isso vem
primeiro na linha: a #101 (aba Histórico) e a #102 (catálogo sincronizado, que
grava com autor próprio) dependem dele.

## What Changes

- **`apps/api`**: tabela nova `knowledge_document_events`, ligada à base por FK
  com **cascade** e **sem FK para o documento**. O evento de exclusão precisa
  sobreviver ao documento, então o título do documento é gravado como
  **snapshot**.
- **`apps/api`**: três tipos de evento, `Created`, `Updated` e `Deleted`.
  `Updated` só existe quando o texto extraído ou o título mudou de fato, e o
  evento diz qual dos dois mudou.
- **`apps/api`**: os handlers de cadastro, atualização e exclusão de documento
  gravam o evento **no mesmo `SaveChangesAsync`** da escrita do documento. Escrita
  recusada não gera evento, e evento sem escrita não existe.
- **`apps/api`**: reindexar **não** gera evento, porque não muda conteúdo nem
  título.
- **`apps/api`**: `KnowledgeDocument.Update` passa a devolver o que mudou
  (conteúdo, título, necessidade de indexar) em vez de um `bool` só. O critério de
  "conteúdo alterado" é o incremento de `ContentRevision`, **não** o
  `ContentHash`. A diferença aparece na linha legada com hash nulo: ali o hash
  manda reindexar conteúdo idêntico, e isso continua correto para a indexação,
  mas não pode virar evento de "conteúdo alterado".
- **`apps/api`**: o autor é uma string aberta, e nesta change ela é o subject do
  token que fez a escrita. Hoje só existe o operador (`operator`). A #102
  acrescenta o subject `service:connectors`, que a UI vai apresentar como
  "sincronização".
- **`apps/api`**: rota nova `GET /knowledge-bases/{knowledgeBaseId}/document-events`,
  paginada por cursor, do mais recente para o mais antigo, com eventos só da base
  pedida.
- **`apps/api`**: migração que cria a tabela **sem** eventos retroativos.
  Documentos já existentes não ganham evento de inclusão.
- **Documentação**: `docs/architecture.md` (a entidade nova e a exceção de FK) e
  `CHANGELOG.md`.

### O que esta change NÃO faz, e é decisão e não esquecimento

- **Nenhuma tarefa em `apps/frontend`.** A aba Histórico é a #101.
- **Sem retenção.** O histórico cresce sem limite nesta entrega. O gatilho
  observável para rever mora na #109 (rótulo `aguardando gatilho`), com o
  motivo na D9 do `design.md`.
- **Sem eventos de arquivo ignorado ou de falha de acesso.** Esses estados são da
  base sincronizada e pertencem ao estado atual dela, não ao histórico (#98,
  alternativas).
- **Nenhuma rota de exclusão de base.** A cascata da tabela nova é verificada
  diretamente no banco, porque a base continua sem `MapDelete`. A D3 explica por
  que a cascata nasce assim mesmo.

## Capabilities

### New Capabilities

- `knowledge-document-history`: quais escritas de documento geram evento e quais
  não geram, o que cada evento carrega, como ele sobrevive ao documento e morre
  com a base, e a rota de leitura paginada por base.

### Modified Capabilities

Nenhuma. `knowledge-document-catalog` continua dona do que cada escrita faz com o
documento, e nenhum requisito dela muda. A lista nova segue
`api-response-ordering` como ela já está escrita, com cenário próprio na spec
nova.

## Impact

- **`apps/api`**: entidade e mapeamento novos no `AppDbContext`, uma migração, os
  handlers `CreateKnowledgeDocument`, `UpdateKnowledgeDocument` e
  `DeleteKnowledgeDocument`, os três commands (ganham o autor), os endpoints de
  documento (leem o subject do token), `KnowledgeDocument.Update` (muda o retorno)
  e uma query nova com endpoint.
- **Testes de `apps/api`**: casos novos nas classes de documento que já existem,
  e uma classe de migração nova que divide contêiner com
  `RejectionMetricsMigrationTests` numa collection. A classe existente só troca a
  fonte do contêiner, sem mudar o que afirma, e as fontes de contêiner continuam
  41 (D12 do `design.md`).
- **Contrato HTTP**: só adiciona uma rota. Nenhuma resposta existente muda de
  forma.
- **Sem dependência nova.**
- **Nenhum outro app é tocado.** `apps/workers` escreve em `knowledge_documents`
  (estado de indexação), mas não escreve título nem conteúdo, então não gera
  evento e não precisa espelhar a tabela.
