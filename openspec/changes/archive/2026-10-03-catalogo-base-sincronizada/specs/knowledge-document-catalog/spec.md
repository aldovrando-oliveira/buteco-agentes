## ADDED Requirements

### Requirement: Operador não escreve documento em base sincronizada
O operador SHALL receber `409 Conflict` ao escrever documento em base `Synced`.
Em base `Synced`, as rotas do operador de cadastro
(`POST /knowledge-bases/{kbId}/documents`), atualização
(`PUT /knowledge-bases/{kbId}/documents/{id}`) e exclusão
(`DELETE /knowledge-bases/{kbId}/documents/{id}`) SHALL responder `409 Conflict`
com `ProblemDetails`, sem alterar documento nem registrar evento. Base inexistente
continua respondendo `404`.

A reindexação (`POST /knowledge-bases/{kbId}/documents/{id}/reindex`), a listagem
e a consulta de documento SHALL continuar funcionando em base `Synced`.

#### Scenario: Criar documento em base sincronizada
- **WHEN** o operador envia `POST /knowledge-bases/{kbId}/documents` com payload
  válido para uma base `Synced`
- **THEN** a API responde `409`, nenhum documento é criado, nenhum evento é
  registrado e nenhuma indexação é enfileirada

#### Scenario: Editar documento em base sincronizada
- **WHEN** o operador envia `PUT /knowledge-bases/{kbId}/documents/{id}` com payload
  válido para um documento de base `Synced`
- **THEN** a API responde `409` e o título, o texto, `contentRevision` e
  `updatedAt` do documento continuam os mesmos

#### Scenario: Excluir documento em base sincronizada
- **WHEN** o operador envia `DELETE /knowledge-bases/{kbId}/documents/{id}` para um
  documento de base `Synced`
- **THEN** a API responde `409` e o documento continua existindo

#### Scenario: Reindexar documento em base sincronizada
- **WHEN** o operador envia `POST /knowledge-bases/{kbId}/documents/{id}/reindex`
  para um documento de base `Synced`
- **THEN** a API responde `200` com o documento em `Pending` e a indexação é
  enfileirada

#### Scenario: Escrita em base manual continua igual
- **WHEN** o operador cria, edita e exclui documento numa base `Manual`
- **THEN** as três operações respondem como antes (`201`, `200`, `204`)

### Requirement: Referência externa acompanha o tipo da base
Documento de base `Synced` SHALL ter `ExternalRef` e `ExternalVersion` não nulos,
e documento de base `Manual` SHALL ter os dois nulos. `ExternalRef` SHALL ser único
por base. O banco SHALL garantir a regra nos dois sentidos, além da recusa feita
pela aplicação.

#### Scenario: Documento com referência externa em base manual é recusado
- **WHEN** um documento com `ExternalRef` é inserido direto no banco numa base
  `Manual`
- **THEN** o banco recusa a inserção

#### Scenario: Documento sem referência externa em base sincronizada é recusado
- **WHEN** um documento sem `ExternalRef` é inserido direto no banco numa base
  `Synced`
- **THEN** o banco recusa a inserção

#### Scenario: Cópia do tipo divergente da base é recusada
- **WHEN** um documento que declara a base como `Synced` é inserido direto no banco
  numa base `Manual`
- **THEN** o banco recusa a inserção pela chave estrangeira

#### Scenario: Operador não grava referência externa
- **WHEN** o operador cria documento numa base `Manual` enviando também um campo
  `externalRef` no corpo
- **THEN** o documento é criado com `ExternalRef` nulo

### Requirement: Atualizações simultâneas do operador recebem revisões distintas
O sistema SHALL dar revisões distintas a duas atualizações simultâneas do mesmo
documento pelo operador (`PUT /knowledge-bases/{kbId}/documents/{id}`) com textos
diferentes. A revisão SHALL funcionar como token de
concorrência: a escrita que perder a corrida SHALL ser reaplicada sobre o estado
gravado pela outra, ficando com a revisão seguinte, e cada escrita SHALL publicar a
indexação da própria revisão. Nenhuma das duas SHALL responder `500`.

#### Scenario: Dois PUT simultâneos com textos diferentes
- **WHEN** o operador envia ao mesmo tempo dois `PUT` com textos diferentes para um
  documento na revisão N
- **THEN** os dois respondem `200`, um com `contentRevision` N+1 e outro com N+2, o
  documento gravado tem a revisão N+2 e o texto da resposta que ficou com N+2, e as
  duas publicações de indexação carregam N+1 e N+2

### Requirement: Escrita do operador que perde a corrida também na releitura responde 503
O sistema SHALL responder `503` com `Retry-After` à escrita do operador que perde a
corrida também na releitura. Quando uma escrita do operador sobre um documento perde
para outra escrita do mesmo documento e perde **de novo** na única releitura, as rotas
`PUT /knowledge-bases/{kbId}/documents/{id}`,
`DELETE /knowledge-bases/{kbId}/documents/{id}` e
`POST /knowledge-bases/{kbId}/documents/{id}/reindex` SHALL responder
`503 Service Unavailable` com o cabeçalho `Retry-After`, indicando que a operação pode
ser repetida. Nesse caso a rota NÃO SHALL gravar o documento, NÃO SHALL registrar
evento no histórico e NÃO SHALL publicar indexação, e NÃO SHALL responder `500`.

O cadastro (`POST /knowledge-bases/{kbId}/documents`) não tem esse desfecho: ele só
insere, e a inclusão não confere a revisão.

#### Scenario: Segunda falha seguida nas rotas do operador
- **WHEN** o operador atualiza, exclui ou reindexa um documento, e a gravação perde
  para uma escrita concorrente na primeira tentativa e na releitura
- **THEN** cada rota responde `503` com `Retry-After`, o documento continua com a
  mesma revisão e o mesmo texto, nenhum evento é registrado e nenhuma indexação é
  publicada
