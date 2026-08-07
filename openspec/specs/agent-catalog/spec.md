# agent-catalog Specification

## Purpose

TBD - defined by change backend-agente-a2a-mvp. Update Purpose after archive.

## Requirements

### Requirement: Cadastro de agente
O sistema SHALL permitir, via `apps/api`, cadastrar um agente informando
nome, instruções (system prompt), `provider`, `model`, `description`
(opcional) e `skills` (opcional), persistindo o registro em PostgreSQL
via EF Core. `provider` e `model` SHALL ser validados contra o catálogo
de provedores/modelos atualmente disponível (a mesma fonte usada por
`GET /providers`). Cada item de `skills`, se informado, SHALL ter `name`
não vazio.

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

#### Scenario: Criar agente com description e skills
- **WHEN** um cliente envia `POST /agents` com `description` e uma lista
  de `skills` (cada uma com `name` e, opcionalmente, `description`),
  além dos demais campos válidos
- **THEN** a API cria o registro e responde com o agente criado,
  incluindo `description` e `skills` exatamente como enviados

#### Scenario: Criar agente sem description e sem skills usa os defaults
- **WHEN** um cliente envia `POST /agents` sem `description` e sem
  `skills`, com os demais campos válidos
- **THEN** a API cria o registro e responde com `description` nulo e
  `skills` como uma lista vazia

#### Scenario: Criar agente com skill sem nome é rejeitado
- **WHEN** um cliente envia `POST /agents` com pelo menos um item em
  `skills` cujo `name` é vazio ou ausente
- **THEN** a API responde com erro de validação (HTTP 400) identificando
  o item inválido e não cria nenhum registro

### Requirement: Listagem de agentes
O sistema SHALL permitir, via `apps/api`, listar todos os agentes
cadastrados, incluindo agentes inativos e agentes sem `provider`/`model`
configurados. Cada agente na lista SHALL incluir o conjunto de servidores
MCP atualmente vinculados a ele, o conjunto de agentes para os quais ele
delega (`delegatesTo`), além de `description` e `skills`.

#### Scenario: Lista retorna todos os agentes cadastrados
- **WHEN** um cliente envia `GET /agents` e existem agentes cadastrados
- **THEN** a API responde com a lista de todos os agentes, incluindo id,
  nome, instruções, `provider`, `model`, `description`, `skills`, o campo
  `isActive`, o conjunto de servidores MCP vinculados (`mcpServers`, cada
  item com id e nome) e o conjunto de agentes para os quais delega
  (`delegatesTo`, cada item com id e nome) de cada um

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

#### Scenario: Lista inclui agente sem nenhum servidor MCP vinculado
- **WHEN** um cliente envia `GET /agents` e existe pelo menos um agente sem
  nenhum servidor MCP vinculado
- **THEN** a API inclui esse agente na resposta com `mcpServers` como uma
  lista vazia, não nula

#### Scenario: Lista inclui agente legado sem description e sem skills
- **WHEN** um cliente envia `GET /agents` e existe pelo menos um agente
  cadastrado antes desta capacidade existir, sem `description`/`skills`
  definidos
- **THEN** a API inclui esse agente na resposta normalmente, com
  `description` nulo e `skills` como uma lista vazia, sem filtro
  escondendo esse agente

#### Scenario: Lista inclui agente sem nenhuma delegação de saída
- **WHEN** um cliente envia `GET /agents` e existe pelo menos um agente
  que não delega para nenhum outro agente
- **THEN** a API inclui esse agente na resposta com `delegatesTo` como uma
  lista vazia, não nula

### Requirement: Consulta de agente por id
O sistema SHALL permitir, via `apps/api`, consultar um agente específico
pelo seu identificador, incluindo agentes inativos e agentes sem
`provider`/`model` configurados. A resposta SHALL incluir o conjunto de
servidores MCP atualmente vinculados a esse agente, o conjunto de agentes
para os quais ele delega (`delegatesTo`), além de `description` e
`skills`.

#### Scenario: Consulta de agente existente retorna dados completos
- **WHEN** um cliente envia `GET /agents/{id}` para um id existente
- **THEN** a API responde com HTTP 200 e os dados completos do agente,
  incluindo os campos `isActive`, `provider`, `model`, `description`,
  `skills`, `mcpServers` (cada item com id e nome dos servidores MCP
  vinculados) e `delegatesTo` (cada item com id e nome dos agentes para os
  quais delega)

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

#### Scenario: Consulta de agente sem nenhum servidor MCP vinculado
- **WHEN** um cliente envia `GET /agents/{id}` para um agente sem nenhum
  servidor MCP vinculado
- **THEN** a API responde com HTTP 200 e `mcpServers` como uma lista vazia,
  não nula

#### Scenario: Consulta de agente legado sem description e sem skills
- **WHEN** um cliente envia `GET /agents/{id}` para um agente cadastrado
  antes desta capacidade existir, sem `description`/`skills` definidos
- **THEN** a API responde com HTTP 200, `description` nulo e `skills`
  como uma lista vazia, sem tratar isso como não encontrado ou como erro

#### Scenario: Consulta de agente sem nenhuma delegação de saída
- **WHEN** um cliente envia `GET /agents/{id}` para um agente que não
  delega para nenhum outro agente
- **THEN** a API responde com HTTP 200 e `delegatesTo` como uma lista
  vazia, não nula

### Requirement: Atualização de agente
O sistema SHALL permitir, via `apps/api`, atualizar nome, instruções,
`provider`, `model`, `description` e `skills` de um agente já cadastrado,
com a mesma validação usada no cadastro (incluindo a validação de
`provider`/`model` contra o catálogo disponível e de `name` não vazio em
cada item de `skills`). Atualizar um agente que estava sem
`provider`/`model` definidos com valores válidos e disponíveis SHALL
retirá-lo do estado implícito de "precisa de reconfiguração". A
atualização de `skills` SHALL substituir o conjunto inteiro anteriormente
persistido (replace completo, sem merge).

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

#### Scenario: Atualizar agente substituindo o conjunto inteiro de skills
- **WHEN** um cliente envia `PUT /agents/{id}` para um agente que já
  tinha `skills` cadastradas, com uma nova lista de `skills` diferente da
  anterior
- **THEN** a API responde com HTTP 200 e `skills` refletindo exatamente a
  nova lista enviada, sem nenhum item da lista anterior remanescente

#### Scenario: Atualizar agente com skill sem nome é rejeitado
- **WHEN** um cliente envia `PUT /agents/{id}` com pelo menos um item em
  `skills` cujo `name` é vazio ou ausente
- **THEN** a API responde com erro de validação (HTTP 400) identificando
  o item inválido e não altera o registro existente

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
