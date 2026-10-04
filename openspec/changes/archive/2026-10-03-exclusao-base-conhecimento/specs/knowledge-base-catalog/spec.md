## ADDED Requirements

### Requirement: Exclusão de base de conhecimento inativa
`DELETE /knowledge-bases/{id}` SHALL excluir a base, de forma atômica, junto com
tudo que é dela: os documentos, os fragmentos desses documentos, os eventos de
histórico da base e os vínculos de agentes com a base. A exclusão SHALL valer para
base `Manual` e `Synced`, e SHALL responder `204` sem corpo.

A exclusão SHALL exigir a base **inativa**. Base ativa SHALL ser recusada com `409`
em `ProblemDetails`, com a extensão `code` igual a `knowledge-base-active` e um
`detail` que diz para desativar a base antes; nesse caso NADA SHALL ser apagado. A
conferência de `IsActive` SHALL acontecer sob o bloqueio da linha da base, na mesma
transação que apaga.

Id inexistente SHALL responder `404`. A exclusão NÃO SHALL tocar documentos,
fragmentos, eventos ou vínculos de outra base. As métricas de indexação e de
embedding NÃO SHALL ser apagadas.

A rota é do operador: o subject `service:connectors` SHALL receber `403`.

#### Scenario: Excluir base inativa com conteúdo
- **WHEN** um cliente envia `DELETE /knowledge-bases/{id}` para uma base inativa
  com documentos indexados, eventos de histórico e um agente vinculado
- **THEN** a API responde HTTP 204
- **AND** `GET /knowledge-bases/{id}` responde 404
- **AND** não resta linha da base em `knowledge_documents`, `knowledge_fragments`,
  `knowledge_document_events` nem `agent_knowledge_bases`
- **AND** o agente continua existindo, sem a base entre as vinculadas

#### Scenario: Base ativa é recusada sem apagar nada
- **WHEN** um cliente envia `DELETE /knowledge-bases/{id}` para uma base ativa com
  documentos, fragmentos, eventos e um agente vinculado
- **THEN** a API responde HTTP 409 com `code: "knowledge-base-active"`
- **AND** a base, os documentos, os fragmentos, os eventos e o vínculo continuam
  existindo, nas mesmas contagens de antes

#### Scenario: Base inexistente
- **WHEN** um cliente envia `DELETE /knowledge-bases/{id}` para um id que não existe
- **THEN** a API responde HTTP 404

#### Scenario: Outra base não é tocada
- **WHEN** duas bases inativas têm documentos, fragmentos, eventos e vínculos, e
  uma delas é excluída
- **THEN** a outra mantém todos os documentos, fragmentos, eventos e vínculos, nas
  mesmas contagens de antes

#### Scenario: Métricas de indexação sobrevivem à base
- **WHEN** uma base com tentativas de indexação registradas é excluída
- **THEN** as linhas de `knowledge_indexing_attempts` daquela base continuam
  existindo

#### Scenario: A pasta de uma base sincronizada excluída fica livre
- **WHEN** uma base `Synced` inativa usa uma pasta, o cadastro de outra base com a
  mesma pasta responde 409 `folder-in-use`, e a base é excluída
- **THEN** o mesmo cadastro, repetido, responde HTTP 201 e a base nova usa a pasta

#### Scenario: O bloqueio vem antes de apagar os documentos
- **WHEN** a exclusão de uma base inativa é executada
- **THEN** o SQL emitido na requisição trava a linha da base com `FOR UPDATE` antes
  do `DELETE` dos documentos, na mesma transação

#### Scenario: Subject de serviço não exclui base
- **WHEN** o subject `service:connectors` envia `DELETE /knowledge-bases/{id}`
- **THEN** a API responde HTTP 403 e a base continua existindo

## MODIFIED Requirements

### Requirement: Ativação e desativação de base de conhecimento
O sistema SHALL permitir ativar e desativar uma base, sem excluí-la. Desativar NÃO
SHALL apagar nada: a base continua existindo, com seus documentos intactos. A
exclusão é uma operação separada, que exige a base inativa (requisito "Exclusão de
base de conhecimento inativa").

#### Scenario: Desativar base ativa
- **WHEN** um cliente envia `POST /knowledge-bases/{id}/deactivate` para uma
  base ativa
- **THEN** a API responde HTTP 200 com `isActive: false` e a base continua
  existindo, com seus documentos intactos

#### Scenario: Ativar base inativa
- **WHEN** um cliente envia `POST /knowledge-bases/{id}/activate` para uma base
  desativada
- **THEN** a API responde HTTP 200 com `isActive: true`

#### Scenario: Desativar base já inativa é idempotente
- **WHEN** um cliente envia `POST /knowledge-bases/{id}/deactivate` para uma
  base que já está inativa
- **THEN** a API responde HTTP 200 com `isActive: false`, sem erro

#### Scenario: Ativar ou desativar base inexistente retorna 404
- **WHEN** um cliente envia `POST /knowledge-bases/{id}/activate` ou
  `POST /knowledge-bases/{id}/deactivate` para um id que não existe
- **THEN** a API responde HTTP 404
