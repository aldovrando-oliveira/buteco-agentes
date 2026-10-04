## ADDED Requirements

### Requirement: Base excluída responde 404 em toda rota de sincronização, inclusive no meio da escrita
As rotas de `/sync/knowledge-bases/{id}` SHALL responder `404` para base excluída,
e NUNCA `500`. Isso SHALL valer também para a escrita que encontrou a base ao
começar e a encontra excluída ao gravar: upsert (inclusão ou atualização), exclusão
por referência e gravação do resultado de ciclo. Quando a gravação falhar porque a
base deixou de existir — violação da FK para a base, atualização ou exclusão que não
acha mais a linha, ou impasse com a exclusão —, a rota SHALL reler a existência da
base e, sem ela, responder `404`.

Nesses casos a rota NÃO SHALL gravar documento, NÃO SHALL registrar evento e NÃO
SHALL publicar indexação. A exclusão por referência NÃO SHALL responder `204` para
base excluída: o `204` de referência inexistente vale só com a base existindo.

#### Scenario: Rotas de sincronização depois da exclusão
- **WHEN** uma base `Synced` é excluída e o subject `service:connectors` chama, para
  ela, a listagem de referências, o upsert, a exclusão por referência e a gravação do
  resultado de ciclo
- **THEN** as quatro rotas respondem HTTP 404

#### Scenario: Upsert de inclusão que começa antes da exclusão e grava depois
- **WHEN** um upsert de referência nova passa pela leitura da base, e a base é
  excluída pela rota do operador antes do `SaveChanges` do upsert
- **THEN** o upsert responde HTTP 404, nenhum documento ou evento existe para a base
  e nenhuma indexação é publicada

#### Scenario: Upsert de atualização que começa antes da exclusão e grava depois
- **WHEN** um upsert com texto novo sobre um documento existente passa pela leitura,
  e a base é excluída antes do `SaveChanges`
- **THEN** o upsert responde HTTP 404, e não 503, e nenhuma indexação é publicada

#### Scenario: Exclusão por referência que começa antes da exclusão da base
- **WHEN** uma exclusão por referência passa pela leitura da base e do documento, e
  a base é excluída antes do `SaveChanges`
- **THEN** a rota responde HTTP 404, e não 204

#### Scenario: Resultado de ciclo que começa antes da exclusão e grava depois
- **WHEN** a gravação de um resultado `Failed` lê a base, e a base é excluída antes
  do `SaveChanges`
- **THEN** a rota responde HTTP 404, e não 500

## MODIFIED Requirements

### Requirement: Escrita da sincronização que perde a corrida também na releitura responde 503
O sistema SHALL responder `503` com `Retry-After` à escrita da sincronização que perde
a corrida também na releitura. Quando um upsert sobre documento existente
(`PUT /sync/knowledge-bases/{id}/documents`) ou uma exclusão por referência
(`DELETE /sync/knowledge-bases/{id}/documents?externalRef=<ref>`) perde a corrida
para outra escrita do mesmo documento e perde **de novo** na única releitura, a rota
SHALL responder `503 Service Unavailable` com o cabeçalho `Retry-After`, indicando que
a operação pode ser repetida. O upsert SHALL responder o mesmo quando o documento
deixa de existir entre a primeira tentativa e a releitura **com a base existindo**;
se a base deixou de existir, a resposta SHALL ser `404` (requisito "Base excluída
responde 404 em toda rota de sincronização, inclusive no meio da escrita"). Nesses
casos a rota NÃO SHALL gravar o documento, NÃO SHALL registrar evento no histórico e
NÃO SHALL publicar indexação, e NÃO SHALL responder `500`.

O `409` dessas rotas continua significando base `Manual`, que repetir não resolve; o
`503` é o único desfecho de contenção, e o `404` o de base que não existe mais.

#### Scenario: Segunda falha seguida no upsert e na exclusão por referência
- **WHEN** o subject `service:connectors` faz upsert com texto novo sobre um documento
  existente, e exclui um documento por referência, e as duas gravações perdem para
  uma escrita concorrente na primeira tentativa e na releitura
- **THEN** as duas rotas respondem `503` com `Retry-After`, o documento continua
  existindo, nenhum evento é registrado e nenhuma indexação é publicada
