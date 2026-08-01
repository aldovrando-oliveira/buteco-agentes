# agent-catalog Specification

## Purpose

TBD - defined by change backend-agente-a2a-mvp. Update Purpose after archive.

## Requirements

### Requirement: Cadastro de agente
O sistema SHALL permitir, via `apps/api`, cadastrar um agente informando
nome, instruções (system prompt), `provider` e `model`, persistindo o
registro em PostgreSQL via EF Core. `provider` e `model` SHALL ser
validados contra o catálogo de provedores/modelos atualmente disponível
(a mesma fonte usada por `GET /providers`).

#### Scenario: Criar agente com sucesso
- **WHEN** um cliente envia `POST /agents` com nome, instruções, `provider`
  e `model` válidos, sendo a combinação `provider`+`model` atualmente
  disponível
- **THEN** a API cria o registro no banco e responde com o agente criado,
  incluindo um identificador único gerado pelo sistema e os valores de
  `provider`/`model` persistidos

#### Scenario: Criar agente sem nome ou sem instruções é rejeitado
- **WHEN** um cliente envia `POST /agents` sem nome ou sem instruções
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

#### Scenario: Criar agente sem provider ou sem model é rejeitado
- **WHEN** um cliente envia `POST /agents` sem `provider` ou sem `model`
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

#### Scenario: Criar agente com provider ou model indisponíveis é rejeitado
- **WHEN** um cliente envia `POST /agents` com `provider` não configurado
  no ambiente, ou com `model` que não consta no catálogo do `provider`
  informado
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

### Requirement: Listagem de agentes
O sistema SHALL permitir, via `apps/api`, listar todos os agentes
cadastrados, incluindo agentes inativos e agentes sem `provider`/`model`
configurados.

#### Scenario: Lista retorna todos os agentes cadastrados
- **WHEN** um cliente envia `GET /agents` e existem agentes cadastrados
- **THEN** a API responde com a lista de todos os agentes, incluindo id,
  nome, instruções, `provider`, `model` e o campo `isActive` de cada um

#### Scenario: Lista inclui agentes inativos
- **WHEN** um cliente envia `GET /agents` e existe pelo menos um agente
  desativado (`isActive = false`)
- **THEN** a API inclui esse agente na resposta normalmente, com
  `isActive` refletindo `false` — sem filtro escondendo agentes inativos

#### Scenario: Lista inclui agentes sem provider/model configurados
- **WHEN** um cliente envia `GET /agents` e existe pelo menos um agente
  cadastrado antes desta capacidade existir, sem `provider`/`model`
  definidos
- **THEN** a API inclui esse agente na resposta normalmente, com
  `provider` e `model` retornados como nulos, sem filtro escondendo esse
  agente

### Requirement: Consulta de agente por id
O sistema SHALL permitir, via `apps/api`, consultar um agente específico
pelo seu identificador, incluindo agentes inativos e agentes sem
`provider`/`model` configurados.

#### Scenario: Consulta de agente existente retorna dados completos
- **WHEN** um cliente envia `GET /agents/{id}` para um id existente
- **THEN** a API responde com HTTP 200 e os dados completos do agente,
  incluindo os campos `isActive`, `provider` e `model`

#### Scenario: Consulta de agente inexistente retorna 404
- **WHEN** um cliente envia `GET /agents/{id}` para um id que não existe
- **THEN** a API responde com HTTP 404

#### Scenario: Consulta de agente inativo retorna normalmente
- **WHEN** um cliente envia `GET /agents/{id}` para um id de agente
  desativado
- **THEN** a API responde com HTTP 200 e `isActive: false`, sem tratar
  isso como não encontrado

#### Scenario: Consulta de agente sem provider/model retorna esses campos como nulos
- **WHEN** um cliente envia `GET /agents/{id}` para um agente cadastrado
  antes desta capacidade existir, sem `provider`/`model` definidos
- **THEN** a API responde com HTTP 200 e `provider`/`model` como nulos, sem
  tratar isso como não encontrado ou como erro

### Requirement: Atualização de agente
O sistema SHALL permitir, via `apps/api`, atualizar nome, instruções,
`provider` e `model` de um agente já cadastrado, com a mesma validação
usada no cadastro (incluindo a validação de `provider`/`model` contra o
catálogo disponível). Atualizar um agente que estava sem `provider`/`model`
definidos com valores válidos e disponíveis SHALL retirá-lo do estado
implícito de "precisa de reconfiguração".

#### Scenario: Atualizar agente com sucesso
- **WHEN** um cliente envia `PUT /agents/{id}` com nome, instruções,
  `provider` e `model` válidos para um id existente, sendo a combinação
  `provider`+`model` atualmente disponível
- **THEN** a API atualiza o registro no banco e responde com HTTP 200 e o
  agente atualizado, incluindo `isActive` inalterado

#### Scenario: Atualizar agente sem nome ou sem instruções é rejeitado
- **WHEN** um cliente envia `PUT /agents/{id}` sem nome ou sem instruções
- **THEN** a API responde com erro de validação (HTTP 400) e não altera o
  registro existente

#### Scenario: Atualizar agente sem provider ou sem model é rejeitado
- **WHEN** um cliente envia `PUT /agents/{id}` sem `provider` ou sem
  `model`
- **THEN** a API responde com erro de validação (HTTP 400) e não altera o
  registro existente

#### Scenario: Atualizar agente com provider ou model indisponíveis é rejeitado
- **WHEN** um cliente envia `PUT /agents/{id}` com `provider` não
  configurado no ambiente, ou com `model` que não consta no catálogo do
  `provider` informado
- **THEN** a API responde com erro de validação (HTTP 400) e não altera o
  registro existente

#### Scenario: Atualizar agente inexistente retorna 404
- **WHEN** um cliente envia `PUT /agents/{id}` para um id que não existe
- **THEN** a API responde com HTTP 404 e não cria nenhum registro

#### Scenario: Atualizar agente legado com provider/model válidos sai do estado de reconfiguração
- **WHEN** um cliente envia `PUT /agents/{id}` com `provider`/`model`
  válidos e disponíveis para um agente que estava com `provider`/`model`
  nulos
- **THEN** a API atualiza o registro com os novos `provider`/`model` e o
  agente deixa de estar no estado implícito de "precisa de reconfiguração"

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

### Requirement: Persistência durável do catálogo de agentes
O sistema SHALL persistir o catálogo de agentes em PostgreSQL, sem uso de
armazenamento em memória, garantindo que os dados sobrevivam a reinícios da
aplicação.

#### Scenario: Agente cadastrado sobrevive a restart da API
- **WHEN** um agente é cadastrado e o processo de `apps/api` é reiniciado
- **THEN** uma consulta subsequente a `GET /agents/{id}` continua
  retornando o agente com os mesmos dados
