# agent-catalog Specification

## Purpose

TBD - defined by change backend-agente-a2a-mvp. Update Purpose after archive.

## Requirements

### Requirement: Cadastro de agente
O sistema SHALL permitir, via `apps/api`, cadastrar um agente informando ao
menos nome e instruções (system prompt), persistindo o registro em
PostgreSQL via EF Core.

#### Scenario: Criar agente com sucesso
- **WHEN** um cliente envia `POST /agents` com nome e instruções válidos
- **THEN** a API cria o registro no banco e responde com o agente criado,
  incluindo um identificador único gerado pelo sistema

#### Scenario: Criar agente sem nome ou sem instruções é rejeitado
- **WHEN** um cliente envia `POST /agents` sem nome ou sem instruções
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

### Requirement: Listagem de agentes
O sistema SHALL permitir, via `apps/api`, listar todos os agentes
cadastrados.

#### Scenario: Lista retorna todos os agentes cadastrados
- **WHEN** um cliente envia `GET /agents` e existem agentes cadastrados
- **THEN** a API responde com a lista de todos os agentes, incluindo id,
  nome e instruções de cada um

### Requirement: Consulta de agente por id
O sistema SHALL permitir, via `apps/api`, consultar um agente específico
pelo seu identificador.

#### Scenario: Consulta de agente existente retorna dados completos
- **WHEN** um cliente envia `GET /agents/{id}` para um id existente
- **THEN** a API responde com HTTP 200 e os dados completos do agente

#### Scenario: Consulta de agente inexistente retorna 404
- **WHEN** um cliente envia `GET /agents/{id}` para um id que não existe
- **THEN** a API responde com HTTP 404

### Requirement: Persistência durável do catálogo de agentes
O sistema SHALL persistir o catálogo de agentes em PostgreSQL, sem uso de
armazenamento em memória, garantindo que os dados sobrevivam a reinícios da
aplicação.

#### Scenario: Agente cadastrado sobrevive a restart da API
- **WHEN** um agente é cadastrado e o processo de `apps/api` é reiniciado
- **THEN** uma consulta subsequente a `GET /agents/{id}` continua
  retornando o agente com os mesmos dados
