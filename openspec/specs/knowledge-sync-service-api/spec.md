# knowledge-sync-service-api Specification

## Purpose

Cobre a **API de serviço que `apps/api` expõe para o app que sincroniza uma base
de conhecimento a partir de uma pasta de um provedor**. Esse app não é operador:
autentica-se com o subject de serviço `service:connectors`, cujo escopo é
restrito às rotas sob `/sync/knowledge-bases` — ele recebe `403` em qualquer
outra rota, e `service:inbox` recebe `403` em todas estas.

As rotas são o contrato inteiro entre a sincronização e o catálogo:

- listar as bases `Synced` (inclusive as inativas) e, por base, as referências
  dos documentos gravados (`externalRef`, `externalVersion`, id), sem o texto;
- gravar um documento por upsert na sua `ExternalRef` e excluí-lo pela mesma
  referência;
- registrar o resultado de um ciclo de sincronização (`Succeeded` ou `Failed`),
  que alimenta o estado de sincronização exposto na resposta da base.

A capability afirma que **toda escrita de documento de base sincronizada passa
pelo `apps/api`**, com as mesmas regras de normalização, indexação e histórico
das escritas do operador; o app de sincronização decide *o que* escrever, nunca
grava direto em `knowledge_documents`. O que pertence às vizinhas:
`knowledge-base-catalog` é dona do tipo da base e do estado de sincronização que
ela mostra, `knowledge-document-catalog` das escritas do operador (recusadas em
base `Synced`), e `route-authentication` da validação de tokens e subjects.

## Requirements

### Requirement: Subject de serviço da sincronização com escopo próprio
`apps/api` SHALL aceitar tokens com `sub` igual a `service:connectors`, assinados
pela mesma chave e validados pelo mesmo esquema dos demais tokens. Esse subject
SHALL ser autorizado **apenas** nas rotas sob `/sync/knowledge-bases` definidas
nesta capability, e SHALL receber `403 Forbidden` em qualquer outra rota, mesmo com
token estruturalmente válido e não expirado. O subject `service:inbox` SHALL
receber `403 Forbidden` em todas as rotas sob `/sync/knowledge-bases`.

#### Scenario: service:connectors recusado em rota do operador
- **WHEN** uma requisição `GET /knowledge-bases` chega com token válido de
  `service:connectors`
- **THEN** a API responde `403 Forbidden`

#### Scenario: service:connectors recusado em escrita de documento do operador
- **WHEN** uma requisição `POST /knowledge-bases/{id}/documents` chega com token
  válido de `service:connectors`
- **THEN** a API responde `403 Forbidden` e nenhum documento é criado

#### Scenario: service:connectors autorizado nas rotas de sincronização
- **WHEN** uma requisição `GET /sync/knowledge-bases` chega com token válido de
  `service:connectors`
- **THEN** a API responde `200`

#### Scenario: service:inbox recusado nas rotas de sincronização
- **WHEN** uma requisição para cada uma das rotas sob `/sync/knowledge-bases` chega
  com token válido de `service:inbox`
- **THEN** a API responde `403 Forbidden` em todas, sem executar nenhuma escrita

### Requirement: Listagem das bases sincronizadas
`GET /sync/knowledge-bases` SHALL devolver todas as bases `Synced`, **inclusive as
inativas**, com id, provedor, id da pasta, nome e URL da pasta e `isActive`. Bases
`Manual` NÃO SHALL aparecer. A ordem SHALL ser por data de cadastro com desempate
por id.

#### Scenario: Base sincronizada inativa aparece
- **WHEN** existem uma base `Synced` ativa, uma `Synced` inativa e uma `Manual`
- **THEN** a resposta traz as duas `Synced`, a inativa com `isActive: false`, e não
  traz a `Manual`

#### Scenario: Nenhuma base sincronizada
- **WHEN** só existem bases `Manual`
- **THEN** a API responde `200` com lista vazia

### Requirement: Listagem das referências dos documentos de uma base sincronizada
`GET /sync/knowledge-bases/{id}/documents` SHALL devolver, para cada documento da
base, `externalRef`, `externalVersion` e `documentId`, sem o texto. SHALL responder
`404` para base inexistente e `409` para base `Manual`. A ordem SHALL ser por
`externalRef` com desempate por id.

#### Scenario: Referências e versões gravadas
- **WHEN** a base `Synced` tem dois documentos gravados por upsert
- **THEN** a resposta traz as duas referências com as versões gravadas no último
  upsert de cada uma

#### Scenario: Base manual
- **WHEN** a rota é chamada para uma base `Manual`
- **THEN** a API responde `409`

### Requirement: Upsert de documento por referência externa
`PUT /sync/knowledge-bases/{id}/documents` SHALL receber `externalRef`,
`externalVersion`, `title`, `sourceType` e `content`, todos obrigatórios e não
vazios, e SHALL criar o documento quando a base não tem documento com aquela
referência, ou atualizá-lo quando tem. O conteúdo SHALL passar pela mesma extração,
normalização e teto de tamanho do cadastro do operador. A resposta SHALL ser `200`
com `documentId` e `outcome` (`Created`, `Updated` ou `Unchanged`, como string).

SHALL responder `404` para base inexistente, `409` para base `Manual` e `400` para
payload inválido. Base inativa SHALL aceitar o upsert normalmente.

`externalVersion` SHALL ser gravado como veio e NÃO SHALL participar de nenhuma
decisão: se o documento mudou é decidido pelo texto extraído e pelo título, pelas
mesmas regras da atualização do operador.

#### Scenario: Primeira gravação cria o documento
- **WHEN** o upsert chega para uma referência que a base não tem
- **THEN** a API responde `200` com `outcome: "Created"`, o documento é criado com
  `externalRef` e `externalVersion` enviados, um evento `Created` é registrado e a
  indexação é enfileirada

#### Scenario: Marcador diferente com texto e título iguais não tem efeito
- **WHEN** o upsert chega com `externalVersion` diferente do gravado e o mesmo
  título e o mesmo texto extraído
- **THEN** a API responde `outcome: "Unchanged"`, grava o `externalVersion` novo,
  não registra evento, não enfileira indexação, e `contentRevision`,
  `indexingStatus` e `updatedAt` do documento continuam como estavam

#### Scenario: Texto diferente atualiza e reindexa
- **WHEN** o upsert chega com texto extraído diferente do gravado
- **THEN** a API responde `outcome: "Updated"`, `contentRevision` é incrementado,
  um evento `Updated` com `contentChanged: true` é registrado e a indexação é
  enfileirada

#### Scenario: Só o título mudou
- **WHEN** o upsert chega com o mesmo texto e título diferente
- **THEN** a API responde `outcome: "Updated"`, registra evento `Updated` com
  `titleChanged: true` e `contentChanged: false`, e não enfileira indexação

#### Scenario: Upsert em base manual é recusado
- **WHEN** o upsert chega para uma base `Manual`
- **THEN** a API responde `409` e nenhum documento é criado

#### Scenario: Upsert sem referência externa é recusado
- **WHEN** o upsert chega sem `externalRef`, ou com `externalRef` vazio
- **THEN** a API responde `400` em `externalRef` e nenhum documento é criado

#### Scenario: Conteúdo acima do teto é recusado
- **WHEN** o upsert chega com texto extraído acima de 1 MiB
- **THEN** a API responde `400` e o documento existente, se houver, continua como
  estava, inclusive o `externalVersion`

#### Scenario: Referência comparada como veio
- **WHEN** a base tem um documento com `externalRef` `AbC` e chega um upsert com
  `externalRef` `abc`
- **THEN** a API cria um segundo documento, com `outcome: "Created"`

### Requirement: Upsert concorrente da mesma referência termina com um documento
Dois upserts simultâneos da mesma referência na mesma base NÃO SHALL criar dois
documentos e NÃO SHALL responder `500`. A unicidade de referência por base SHALL
ser garantida por índice único no banco.

#### Scenario: Corrida de dois upserts idênticos
- **WHEN** dois upserts com o mesmo payload, para uma referência que a base não
  tem, são enviados ao mesmo tempo
- **THEN** os dois respondem `200`, um com `outcome: "Created"` e outro com
  `outcome: "Unchanged"`, a base tem um documento com aquela referência, há um
  evento `Created` e uma publicação de indexação

#### Scenario: Corrida de dois upserts sobre documento existente
- **WHEN** dois upserts com textos diferentes, para uma referência que a base já
  tem na revisão N, são enviados ao mesmo tempo
- **THEN** os dois respondem `200`, a base continua com um documento com aquela
  referência, o texto e o `contentHash` gravados são os de um dos dois payloads,
  as duas publicações de indexação carregam revisões diferentes (N+1 e N+2), e a
  revisão final é N+2, refletindo as duas escritas

#### Scenario: Índice recusa referência duplicada inserida direto no banco
- **WHEN** um segundo documento com a mesma base e o mesmo `ExternalRef` é inserido
  direto no banco
- **THEN** o banco recusa a inserção por violação de unicidade

### Requirement: Escrita da sincronização que perde a corrida também na releitura responde 503
O sistema SHALL responder `503` com `Retry-After` à escrita da sincronização que perde
a corrida também na releitura. Quando um upsert sobre documento existente
(`PUT /sync/knowledge-bases/{id}/documents`) ou uma exclusão por referência
(`DELETE /sync/knowledge-bases/{id}/documents?externalRef=<ref>`) perde a corrida
para outra escrita do mesmo documento e perde **de novo** na única releitura, a rota
SHALL responder `503 Service Unavailable` com o cabeçalho `Retry-After`, indicando que
a operação pode ser repetida. O upsert SHALL responder o mesmo quando o documento
deixa de existir entre a primeira tentativa e a releitura. Nesses casos a rota NÃO
SHALL gravar o documento, NÃO SHALL registrar evento no histórico e NÃO SHALL publicar
indexação, e NÃO SHALL responder `500`.

O `409` dessas rotas continua significando base `Manual`, que repetir não resolve; o
`503` é o único desfecho de contenção.

#### Scenario: Segunda falha seguida no upsert e na exclusão por referência
- **WHEN** o subject `service:connectors` faz upsert com texto novo sobre um documento
  existente, e exclui um documento por referência, e as duas gravações perdem para
  uma escrita concorrente na primeira tentativa e na releitura
- **THEN** as duas rotas respondem `503` com `Retry-After`, o documento continua
  existindo, nenhum evento é registrado e nenhuma indexação é publicada

### Requirement: Exclusão de documento por referência externa
`DELETE /sync/knowledge-bases/{id}/documents?externalRef=<ref>` SHALL excluir o
documento da base com aquela referência e registrar o evento `Deleted`. Quando a
base não tem documento com a referência, SHALL responder `204` sem registrar
evento. SHALL responder `404` para base inexistente, `409` para base `Manual` e
`400` sem `externalRef`.

#### Scenario: Excluir documento sincronizado
- **WHEN** a exclusão chega para uma referência existente
- **THEN** a API responde `204`, o documento deixa de existir e um evento `Deleted`
  com `author` `service:connectors` é registrado

#### Scenario: Excluir referência inexistente é idempotente
- **WHEN** a exclusão chega para uma referência que a base não tem
- **THEN** a API responde `204` e nenhum evento é registrado

#### Scenario: Excluir só na base pedida
- **WHEN** duas bases `Synced` têm documento com a mesma referência e a exclusão
  chega para uma delas
- **THEN** só o documento da base pedida é excluído

### Requirement: Gravação do resultado de um ciclo de sincronização
`POST /sync/knowledge-bases/{id}/sync-results` SHALL receber `outcome`
(`Succeeded` ou `Failed`) e aplicar ao estado da base:

- `Succeeded`, com `folderName`, `folderUrl` e `ignoredFiles` obrigatórios: grava a
  última concluída e o último ciclo terminado com o instante da gravação, limpa o
  último erro e o "falhando desde", substitui a lista de ignorados e atualiza nome
  e URL da pasta;
- `Failed`, com `error` obrigatório (`code` e `detail` opcional): grava o último
  ciclo terminado e o último erro, preenche "falhando desde" com o instante da
  gravação **somente se estava nulo**, e NÃO SHALL alterar a última concluída, a
  lista de ignorados, nem nome e URL da pasta.

O instante SHALL ser o relógio do `apps/api`. Códigos de erro e de arquivo ignorado
SHALL ser strings no formato `^[a-z0-9]+(-[a-z0-9]+)*$`, de até 64 caracteres, e
NÃO SHALL ser traduzidos. Campos do outro desfecho, código fora do formato, item de
ignorado sem `externalRef` ou sem `name`, ou `externalRef` repetido na lista SHALL
ser recusados com `400`. SHALL responder `404` para base inexistente e `409` para
base `Manual`. A resposta de sucesso SHALL ser `200` com a base.

Provedor e id da pasta NÃO SHALL ser alterados por esta rota.

#### Scenario: Registrar uma falha preserva a última sincronização concluída
- **WHEN** uma base com última concluída `T1` recebe um resultado `Failed`
- **THEN** `lastCompletedAt` continua `T1`, `lastError` traz o código enviado e
  `failingSince` é preenchido

#### Scenario: Falhas seguidas mantêm o início da falha
- **WHEN** uma base recebe dois resultados `Failed` seguidos
- **THEN** `failingSince` é o instante da primeira falha, e `lastFinishedAt` é o da
  segunda

#### Scenario: Sucesso limpa a falha
- **WHEN** uma base falhando recebe um resultado `Succeeded`
- **THEN** `failingSince` e `lastError` ficam nulos e `lastCompletedAt` passa a ser o
  instante da gravação

#### Scenario: Falha não altera a lista de ignorados nem o nome da pasta
- **WHEN** uma base com dois arquivos ignorados e nome de pasta `Atendimento`
  recebe um resultado `Failed`
- **THEN** a lista continua com os dois itens e o nome continua `Atendimento`

#### Scenario: Sucesso atualiza nome e URL da pasta
- **WHEN** um resultado `Succeeded` chega com nome de pasta diferente do gravado
- **THEN** o nome gravado passa a ser o enviado, e o provedor e o id da pasta
  continuam como estavam

#### Scenario: Frase no lugar de código é recusada
- **WHEN** um resultado `Failed` chega com `code` `"Sem acesso à pasta"`
- **THEN** a API responde `400` e o estado da base não muda

#### Scenario: Resultado em base manual é recusado
- **WHEN** um resultado chega para uma base `Manual`
- **THEN** a API responde `409` e a base continua sem estado de sincronização

#### Scenario: Gravação do resultado não toca documentos
- **WHEN** um resultado `Failed` chega para uma base com documentos
- **THEN** todos os documentos continuam existindo, sem evento novo no histórico

### Requirement: Escritas da sincronização são do apps/api
Toda escrita de documento de base `Synced` SHALL passar pelas rotas desta
capability, com as mesmas regras de normalização, indexação e histórico das
escritas do operador. Nenhum app além do `apps/api` SHALL escrever em
`knowledge_documents` conteúdo, título ou referência externa.

#### Scenario: Upsert usa a mesma normalização do cadastro
- **WHEN** o upsert chega com conteúdo com BOM e quebras `CRLF`, e depois com o
  mesmo conteúdo sem BOM e com `LF`
- **THEN** o segundo upsert responde `outcome: "Unchanged"`
