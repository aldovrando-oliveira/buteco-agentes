## MODIFIED Requirements

### Requirement: Substituição do conjunto de servidores MCP vinculados a um agente
O sistema SHALL permitir, via `apps/api`, definir o conjunto completo de
servidores MCP vinculados a um agente, substituindo qualquer vínculo
anterior pelo conjunto informado. Cada item do conjunto SHALL incluir o
`mcpServerId` e o conjunto de tools que o agente pode usar daquele servidor
(`allowedTools`, lista de strings, podendo ser vazia). A operação SHALL ser
idempotente quando o mesmo conjunto for enviado repetidamente.

Cada `allowedTools` submetida SHALL ser validada contra a lista de tools
atualmente oferecida pelo `McpServer` correspondente, obtida por um
handshake MCP real (`tools/list`) executado no momento da chamada. A
operação SHALL ser rejeitada por completo (nenhum vínculo do payload
aplicado) quando qualquer tool de `allowedTools` não existir no servidor
correspondente, ou quando o handshake de validação falhar para qualquer
`McpServer` referenciado no payload (host inalcançável, credencial
rejeitada, ou qualquer outra falha de conexão).

#### Scenario: Vincular servidores MCP a um agente sem vínculo prévio
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` com uma lista de
  `{ mcpServerId, allowedTools }` cujos `mcpServerId` são válidos e cujas
  `allowedTools` existem nos respectivos servidores, para um agente que
  ainda não tem nenhum servidor MCP vinculado
- **THEN** a API responde com HTTP 200 e o agente passa a ter todos os
  servidores informados como vinculados, cada um com o `allowedTools`
  correspondente

#### Scenario: Substituir o conjunto vinculado por um conjunto diferente
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` com uma lista de
  `{ mcpServerId, allowedTools }` diferente da atualmente vinculada a um
  agente
- **THEN** a API remove os vínculos que não estão na nova lista, adiciona ou
  atualiza os que estão presentes (incluindo o `allowedTools` de cada um), e
  responde com HTTP 200 e o conjunto atualizado

#### Scenario: Remover todos os vínculos de um agente
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` com uma lista
  vazia para um agente que tem servidores MCP vinculados
- **THEN** a API remove todos os vínculos existentes e responde com HTTP 200
  e o agente sem nenhum servidor MCP vinculado

#### Scenario: Reenviar o mesmo conjunto é idempotente
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` duas vezes
  seguidas com a mesma lista de `{ mcpServerId, allowedTools }`
- **THEN** ambas as chamadas respondem com HTTP 200 e o mesmo conjunto
  vinculado (mesmos `allowedTools` por servidor), sem erro na segunda
  chamada

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

#### Scenario: allowedTools vazio é um vínculo válido
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` com um
  `mcpServerId` válido e `allowedTools: []`
- **THEN** a API cria o vínculo normalmente, sem exigir nenhuma tool
  selecionada, e o vínculo passa a existir com nenhuma tool permitida

#### Scenario: Tool inexistente no servidor é rejeitada
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` com um
  `mcpServerId` válido cujo `allowedTools` inclui o nome de uma tool que o
  `tools/list` do servidor correspondente não retorna
- **THEN** a API responde com erro de validação (HTTP 400) identificando a
  tool rejeitada, e não altera nenhum vínculo existente do agente

#### Scenario: Falha de handshake durante validação rejeita a operação inteira
- **WHEN** um cliente envia `PUT /agents/{id}/mcp-servers` referenciando um
  ou mais `McpServer`s, e o handshake de validação (`tools/list`) falha para
  ao menos um deles (host inalcançável, credencial rejeitada, ou qualquer
  outra falha de conexão)
- **THEN** a API responde com HTTP 502, identificando o `McpServerId` e o
  motivo da falha, e não aplica nenhum vínculo do payload, mesmo os
  referentes a `McpServer`s cujo handshake teve sucesso

### Requirement: Mesmo servidor MCP vinculado a múltiplos agentes
O sistema SHALL permitir que um mesmo servidor MCP esteja vinculado a mais
de um agente simultaneamente, cada vínculo com seu próprio `allowedTools`
independente.

#### Scenario: Dois agentes vinculados ao mesmo servidor MCP
- **WHEN** um cliente vincula o mesmo `mcpServerId` a dois agentes
  diferentes, um de cada vez via `PUT /agents/{id}/mcp-servers`
- **THEN** ambos os agentes passam a ter esse servidor MCP no seu conjunto
  vinculado, sem que vincular ao segundo agente afete o vínculo do primeiro

#### Scenario: Dois agentes vinculados ao mesmo servidor com allowedTools diferentes
- **WHEN** um cliente vincula o mesmo `mcpServerId` a dois agentes
  diferentes, cada um com um `allowedTools` distinto (ex.: agente A com
  `["read"]`, agente B com `["read", "write"]`)
- **THEN** cada agente reflete exatamente o `allowedTools` que lhe foi
  atribuído, sem que a atualização de um agente altere o `allowedTools` do
  outro para o mesmo servidor
