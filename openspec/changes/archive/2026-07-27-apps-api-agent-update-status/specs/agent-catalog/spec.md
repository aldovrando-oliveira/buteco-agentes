## MODIFIED Requirements

### Requirement: Listagem de agentes
O sistema SHALL permitir, via `apps/api`, listar todos os agentes
cadastrados, incluindo agentes inativos.

#### Scenario: Lista retorna todos os agentes cadastrados
- **WHEN** um cliente envia `GET /agents` e existem agentes cadastrados
- **THEN** a API responde com a lista de todos os agentes, incluindo id,
  nome, instruções e o campo `isActive` de cada um

#### Scenario: Lista inclui agentes inativos
- **WHEN** um cliente envia `GET /agents` e existe pelo menos um agente
  desativado (`isActive = false`)
- **THEN** a API inclui esse agente na resposta normalmente, com
  `isActive` refletindo `false` — sem filtro escondendo agentes inativos

### Requirement: Consulta de agente por id
O sistema SHALL permitir, via `apps/api`, consultar um agente específico
pelo seu identificador, incluindo agentes inativos.

#### Scenario: Consulta de agente existente retorna dados completos
- **WHEN** um cliente envia `GET /agents/{id}` para um id existente
- **THEN** a API responde com HTTP 200 e os dados completos do agente,
  incluindo o campo `isActive`

#### Scenario: Consulta de agente inexistente retorna 404
- **WHEN** um cliente envia `GET /agents/{id}` para um id que não existe
- **THEN** a API responde com HTTP 404

#### Scenario: Consulta de agente inativo retorna normalmente
- **WHEN** um cliente envia `GET /agents/{id}` para um id de agente
  desativado
- **THEN** a API responde com HTTP 200 e `isActive: false`, sem tratar
  isso como não encontrado

## ADDED Requirements

### Requirement: Atualização de agente
O sistema SHALL permitir, via `apps/api`, atualizar nome e instruções de um
agente já cadastrado, com a mesma validação usada no cadastro.

#### Scenario: Atualizar agente com sucesso
- **WHEN** um cliente envia `PUT /agents/{id}` com nome e instruções
  válidos para um id existente
- **THEN** a API atualiza o registro no banco e responde com HTTP 200 e o
  agente atualizado, incluindo `isActive` inalterado

#### Scenario: Atualizar agente sem nome ou sem instruções é rejeitado
- **WHEN** um cliente envia `PUT /agents/{id}` sem nome ou sem instruções
- **THEN** a API responde com erro de validação (HTTP 400) e não altera o
  registro existente

#### Scenario: Atualizar agente inexistente retorna 404
- **WHEN** um cliente envia `PUT /agents/{id}` para um id que não existe
- **THEN** a API responde com HTTP 404 e não cria nenhum registro

### Requirement: Ativação e desativação de agente
O sistema SHALL permitir, via `apps/api`, ativar e desativar um agente
cadastrado por meio de um campo de estado (`isActive`), sem excluir o
registro do agente (nem exclusão definitiva, nem soft delete). As
operações SHALL ser idempotentes.

#### Scenario: Desativar agente ativo
- **WHEN** um cliente envia `POST /agents/{id}/deactivate` para um agente
  atualmente ativo
- **THEN** a API responde com HTTP 200 e o agente atualizado com
  `isActive: false`

#### Scenario: Desativar agente já inativo é idempotente
- **WHEN** um cliente envia `POST /agents/{id}/deactivate` para um agente
  que já está `isActive: false`
- **THEN** a API responde com HTTP 200 e o agente com `isActive: false`,
  sem erro

#### Scenario: Ativar agente inativo
- **WHEN** um cliente envia `POST /agents/{id}/activate` para um agente
  atualmente inativo
- **THEN** a API responde com HTTP 200 e o agente atualizado com
  `isActive: true`

#### Scenario: Ativar agente já ativo é idempotente
- **WHEN** um cliente envia `POST /agents/{id}/activate` para um agente
  que já está `isActive: true`
- **THEN** a API responde com HTTP 200 e o agente com `isActive: true`,
  sem erro

#### Scenario: Ativar ou desativar agente inexistente retorna 404
- **WHEN** um cliente envia `POST /agents/{id}/activate` ou
  `POST /agents/{id}/deactivate` para um id que não existe
- **THEN** a API responde com HTTP 404
