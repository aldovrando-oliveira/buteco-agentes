**Issue:** #102

## Why

Hoje toda base de conhecimento é manual: o operador carrega cada documento. A
linha de bases sincronizadas (#103, #104, #105, #107) vai manter uma base a partir
de uma pasta do Google Drive, e o `apps/api` continua sendo o único dono da escrita
de documentos. Para isso ele precisa saber de que tipo é a base, qual pasta ela
acompanha, guardar o estado da sincronização e aceitar escritas idempotentes de um
subject de serviço próprio. Esta change é o catálogo: nenhuma sincronização roda
ainda, e nenhuma rota do operador cria base sincronizada.

## What Changes

- **`apps/api`, base:** `ContentMode` (`Manual` ou `Synced`), imutável, `Manual`
  na migração e quando omitido no cadastro. Provedor e id da pasta, imutáveis, e os
  snapshots de nome e URL da pasta, que o operador não altera e que só a gravação
  de um ciclo bem-sucedido atualiza.
- **`apps/api`, estado da sincronização:** última sincronização concluída, último
  ciclo terminado, último erro (código e detalhe), desde quando está falhando e a
  lista de arquivos ignorados (referência, nome, código e detalhe). Vai na resposta
  da base, em `syncSource` e `syncState`, nulos em base manual.
- **`apps/api`, unicidade:** índice único parcial sobre provedor e id da pasta,
  valendo também para base inativa, com o id comparado como veio.
- **`apps/api`, documento:** `ExternalRef` anulável e único por base, e
  `ExternalVersion`, marcador opaco do provedor. `ExternalRef` preenchido se e
  somente se a base é `Synced`, garantido pelo banco.
- **`apps/api`, escrita do operador:** `409` ao criar, editar ou excluir documento
  em base `Synced`. Reindexar, editar nome e descrição e ativar ou desativar
  continuam liberados. O cadastro de base com `contentMode: "Synced"` é recusado
  com `400` até a #104.
- **`apps/api`, subject `service:connectors`:** escopo próprio, e rotas de serviço
  sob `/sync/knowledge-bases`: listar bases `Synced` (inclusive inativas), listar as
  referências e versões dos documentos de uma base, upsert e exclusão de documento
  por `ExternalRef`, e gravação do resultado de um ciclo. As escritas entram no
  histórico (#98) com autor `service:connectors`. Upsert com texto e título iguais
  não gera evento, não reindexa e só grava o marcador novo.
- **`apps/api`, autorização:** subject que não seja `operator` nem um subject de
  serviço conhecido passa a receber `403` em toda rota. Hoje qualquer subject
  diferente de `service:inbox` tem o acesso do operador.
- **Documentação:** `docs/architecture.md`, `01`, `02` e `CHANGELOG.md`. O `01` e
  o `docs/architecture.md` também são corrigidos onde dizem que o `apps/api` emite
  o token de serviço: o `apps/inbox` assina o próprio.

### O que esta change NÃO faz, e é decisão

- **Criar base `Synced` pela rota do operador:** #104, que valida a pasta no
  `apps/connectors`. Nesta change a base sincronizada só nasce por inserção direta,
  nos testes.
- **Conectores e sincronização:** #103 e #105. Nenhum código fora de `apps/api`.
- **Telas:** #107. Nenhuma tarefa em `apps/frontend`.
- **Exclusão de base:** #108. A mensagem de pasta em uso que a #104 vai devolver
  não pode mandar excluir a base enquanto a #108 não existir (D5 do `design.md`).
- **Texto de erro:** o `apps/api` grava códigos e não traduz nenhum (D1).

## Capabilities

### New Capabilities

- `knowledge-sync-service-api`: o subject `service:connectors`, o escopo dele e as
  rotas de serviço: listagem de bases sincronizadas e das referências dos
  documentos, upsert e exclusão por `ExternalRef`, e a gravação do resultado de um
  ciclo com as regras de "falhando desde" e de preservação da última concluída.

### Modified Capabilities

- `knowledge-base-catalog`: tipo de conteúdo, origem e estado da sincronização na
  resposta; imutabilidade de tipo, provedor e pasta; unicidade da pasta; cadastro
  com `contentMode` omitido ou `Manual`, e `Synced` recusado.
- `knowledge-document-catalog`: escrita do operador recusada em base `Synced`;
  `ExternalRef` acompanha o tipo da base.
- `knowledge-document-history`: o autor pode ser `service:connectors`; upsert sem
  mudança de texto nem de título não registra evento.
- `route-authentication`: subject desconhecido recebe `403`; a lista de rotas de
  cada subject de serviço é conferida no boot.

## Impact

- **`apps/api`:** entidades `KnowledgeBase` e `KnowledgeDocument`, mapeamento no
  `AppDbContext`, uma migração, handlers de documento e de base, respostas de base,
  `ServiceScopeAuthorizationHandler`, endpoints novos sob `/sync`, e testes.
- **Contrato HTTP:** `KnowledgeBaseResponse` ganha `contentMode`, `syncSource` e
  `syncState` (aditivo). Rotas novas sob `/sync/knowledge-bases`. As rotas de
  escrita de documento passam a responder `409` em base `Synced`, caso que não
  existia. Sob contenção, quando a escrita perde a corrida também na releitura, a
  atualização, a exclusão e a reindexação de documento do operador, e o upsert e a
  exclusão por referência de `/sync`, podem responder `503` com `Retry-After`, sem
  gravar nada (D10).
- **Banco:** colunas novas em `knowledge_bases` e `knowledge_documents`, a FK de
  documento para base passa a ser composta com o tipo, e três índices novos. O
  `apps/workers` mapeia as duas tabelas sem as colunas novas e não insere em
  nenhuma delas, então não muda.
- **Sem dependência nova.**
- **Testes:** casos novos em classes que já existem (`partial`) e uma classe de
  migração na `MigrationPostgresCollection`. Nenhuma fonte de contêiner nova
  (D12 do `design.md`).
