## ADDED Requirements

### Requirement: Descoberta de tools de um servidor MCP cadastrado
O sistema SHALL permitir, via `apps/api`, consultar a lista atual de tools
oferecidas por um `McpServer` cadastrado, executando um handshake real do
protocolo MCP (`tools/list`) contra a URL, tipo de autenticação e credencial
persistidos do servidor. O resultado SHALL refletir o estado atual do
servidor no momento da chamada, sem nenhum catálogo de tools persistido ou
cacheado em `apps/api`.

#### Scenario: Descoberta de tools com handshake bem-sucedido
- **WHEN** um cliente envia `GET /mcp-servers/{id}/tools` para um
  `McpServer` cadastrado, acessível, cujo servidor MCP responde ao
  `tools/list`
- **THEN** a API responde com HTTP 200 e a lista de tools atualmente
  oferecidas pelo servidor (nome e descrição de cada uma)

#### Scenario: Descoberta de tools de servidor MCP inexistente retorna 404
- **WHEN** um cliente envia `GET /mcp-servers/{id}/tools` para um `id` que
  não corresponde a nenhum `McpServer` cadastrado
- **THEN** a API responde com HTTP 404

#### Scenario: Descoberta de tools com host inalcançável
- **WHEN** um cliente envia `GET /mcp-servers/{id}/tools` para um
  `McpServer` cuja URL persistida não responde (host inalcançável, conexão
  recusada ou timeout)
- **THEN** a API responde com um resultado indicando falha, com um motivo
  que identifica problema de conectividade, sem expor nenhuma lista de
  tools

#### Scenario: Descoberta de tools com credencial rejeitada
- **WHEN** um cliente envia `GET /mcp-servers/{id}/tools` para um
  `McpServer` cujo servidor MCP rejeita a credencial persistida (HTTP 401 ou
  403 durante o handshake)
- **THEN** a API responde com um resultado indicando falha, com um motivo
  que identifica rejeição de credencial, sem expor nenhuma lista de tools

#### Scenario: Descoberta de tools de servidor MCP inativo é permitida
- **WHEN** um cliente envia `GET /mcp-servers/{id}/tools` para um
  `McpServer` com `isActive: false`
- **THEN** a API executa o handshake normalmente e responde com a lista de
  tools (ou o motivo de falha, conforme os cenários acima), sem bloquear a
  operação por causa do estado inativo
