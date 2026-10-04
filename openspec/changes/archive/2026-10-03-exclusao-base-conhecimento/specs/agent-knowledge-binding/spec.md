## ADDED Requirements

### Requirement: Excluir a base remove os vínculos dela
A exclusão de uma base de conhecimento SHALL remover os vínculos de todos os
agentes com ela, pela FK em cascata, sem alterar os agentes nem os vínculos deles
com outras bases.

#### Scenario: Agente perde só o vínculo com a base excluída
- **WHEN** um agente está vinculado a duas bases e uma delas, inativa, é excluída
- **THEN** `GET /agents/{id}` responde com `knowledgeBases` contendo só a outra base
- **AND** o agente continua com o mesmo nome, estado e demais vínculos
