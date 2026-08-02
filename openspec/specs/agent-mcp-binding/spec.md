# agent-mcp-binding Specification

## Purpose

TBD - defined by change backend-mcp-catalogo-vinculo. Update Purpose after archive.

## Requirements

### Requirement: Substituição do conjunto de servidores MCP vinculados a um agente
O sistema SHALL permitir, via `apps/api`, definir o conjunto completo de
servidores MCP vinculados a um agente, substituindo qualquer vínculo
anterior pelo conjunto informado. A operação SHALL ser idempotente quando o
mesmo conjunto for enviado repetidamente.

#### Scenario: Vincular servidores MCP a um agente sem vínculo prévio
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` com uma lista de
  `mcpServerId` válidos para um agente que ainda não tem nenhum servidor MCP
  vinculado
- **THEN** a API responde com HTTP 200 e o agente passa a ter todos os
  servidores informados como vinculados

#### Scenario: Substituir o conjunto vinculado por um conjunto diferente
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` com uma lista de
  `mcpServerId` diferente da atualmente vinculada a um agente
- **THEN** a API remove os vínculos que não estão na nova lista, adiciona os
  que estão ausentes, e responde com HTTP 200 e o conjunto atualizado

#### Scenario: Remover todos os vínculos de um agente
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` com uma lista
  vazia para um agente que tem servidores MCP vinculados
- **THEN** a API remove todos os vínculos existentes e responde com HTTP 200
  e o agente sem nenhum servidor MCP vinculado

#### Scenario: Reenviar o mesmo conjunto é idempotente
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` duas vezes
  seguidas com a mesma lista de `mcpServerId`
- **THEN** ambas as chamadas respondem com HTTP 200 e o mesmo conjunto
  vinculado, sem erro na segunda chamada

#### Scenario: Vincular a um McpServer inexistente é rejeitado
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` incluindo um
  `mcpServerId` que não corresponde a nenhum servidor cadastrado
- **THEN** a API responde com erro de validação (HTTP 400) e não altera
  nenhum vínculo existente do agente

#### Scenario: Vincular a um agente inexistente retorna 404
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` para um `id` de
  agente que não existe
- **THEN** a API responde com HTTP 404

#### Scenario: Vincular a um McpServer inativo é permitido
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` incluindo o
  `mcpServerId` de um servidor com `isActive: false`
- **THEN** a API cria o vínculo normalmente, sem rejeitar por causa do
  estado inativo do servidor

### Requirement: Mesmo servidor MCP vinculado a múltiplos agentes
O sistema SHALL permitir que um mesmo servidor MCP esteja vinculado a mais
de um agente simultaneamente.

#### Scenario: Dois agentes vinculados ao mesmo servidor MCP
- **WHEN** um cliente vincula o mesmo `mcpServerId` a dois agentes
  diferentes, um de cada vez via `PUT /agents/{id}/mcp-servers`
- **THEN** ambos os agentes passam a ter esse servidor MCP no seu conjunto
  vinculado, sem que vincular ao segundo agente afete o vínculo do primeiro

### Requirement: Mesmo agente vinculado a múltiplos servidores MCP
O sistema SHALL permitir que um mesmo agente tenha mais de um servidor MCP
vinculado simultaneamente.

#### Scenario: Um agente vinculado a dois servidores MCP diferentes
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` com uma lista
  contendo dois `mcpServerId` diferentes
- **THEN** a API responde com HTTP 200 e o agente passa a ter os dois
  servidores MCP vinculados

### Requirement: Persistência durável do vínculo agente-MCP
O sistema SHALL persistir o vínculo entre agentes e servidores MCP em
PostgreSQL, sem uso de armazenamento em memória, garantindo que os dados
sobrevivam a reinícios da aplicação.

#### Scenario: Vínculo sobrevive a restart da API
- **WHEN** um agente é vinculado a um servidor MCP e o processo de
  `apps/api` é reiniciado
- **THEN** uma consulta subsequente que retorne os servidores MCP vinculados
  a esse agente continua refletindo o mesmo vínculo
