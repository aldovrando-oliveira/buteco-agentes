## Why

Hoje, vincular um agente a um `McpServer` (change `backend-mcp-catalogo-vinculo`)
é tudo-ou-nada: o vínculo dá acesso a todas as tools que o servidor expõe, sem
nenhuma forma de restringir quais delas um agente específico pode usar. Um
mesmo `McpServer` que expõe tools sensíveis (ex. escrita, exclusão) ao lado de
tools somente-leitura força qualquer agente vinculado a herdar o conjunto
inteiro. Esta change introduz seleção granular: ao vincular, o cliente escolhe
quais tools específicas aquele agente pode usar daquele servidor, e agentes
diferentes vinculados ao mesmo `McpServer` podem ter conjuntos de tools
permitidas diferentes entre si.

Como pré-requisito, não existe hoje nenhuma forma de descobrir quais tools um
`McpServer` oferece — diferente de LLM Provider/Model, que têm catálogo
estático em `Buteco.ProviderCatalog`, tools de um MCP só são conhecidas
conectando de verdade ao servidor.

## What Changes

- Novo endpoint `GET /mcp-servers/{id}/tools`: executa um handshake MCP real
  (`tools/list`) contra o servidor e retorna a lista atual de tools (nome +
  descrição). Reaproveita a infraestrutura de conexão já construída para o
  endpoint de teste (`IMcpConnectionTester`), sem duplicar lógica de
  transporte/handshake.
- **BREAKING**: `PUT /agents/{id}/mcp-servers` muda de shape — de uma lista de
  `mcpServerId` (`Guid[]`) para uma lista de objetos
  `{ mcpServerId: Guid, allowedTools: string[] }`. Continua substituindo o
  conjunto inteiro de vínculos do agente (mesmo comportamento herdado da
  change anterior).
- Cada `allowedTools` submetida é validada contra um `tools/list` ao vivo do
  `McpServer` correspondente; tool inexistente no servidor é rejeitada com
  HTTP 400, e a operação inteira é rejeitada (nenhum vínculo do payload é
  aplicado) se o handshake de validação falhar para qualquer `McpServer`
  referenciado.
- `AgentResponse.McpServers` (e qualquer leitura do vínculo de um agente)
  passa a incluir as tools permitidas por servidor vinculado, além de
  id + name.
- Armazenamento do conjunto de tools permitidas por vínculo: nova coluna jsonb
  em `AgentMcpServer` (não uma tabela filha — ver design.md, Decision 1).

## Capabilities

### New Capabilities

_Nenhuma — esta change estende capacidades já existentes, não introduz uma nova._

### Modified Capabilities

- `mcp-server-catalog`: novo requirement de descoberta de tools de um
  `McpServer` cadastrado (`GET /mcp-servers/{id}/tools`).
- `agent-mcp-binding`: `PUT /agents/{id}/mcp-servers` muda de shape para
  incluir `allowedTools` por vínculo (**BREAKING**), com validação contra o
  servidor real; leitura do vínculo passa a incluir as tools permitidas.

## Impact

- **apps/api** (único app afetado):
  - `McpServers/Connectivity/IMcpConnectionTester.cs` e
    `McpConnectionTester.cs`: novo método de listagem de tools, reaproveitando
    a construção de transporte já existente.
  - `McpServers/Endpoints`, `Queries`: novo endpoint `GET /mcp-servers/{id}/tools`.
  - `AgentMcpBindings/Entities/AgentMcpServer.cs`: nova coluna `AllowedTools`.
  - `AgentMcpBindings/Requests/ReplaceAgentMcpServersRequest.cs`,
    `Commands/ReplaceAgentMcpServers/*`: shape novo, validação contra o
    servidor real.
  - `Agents/Responses/AgentResponse.cs`,
    `AgentMcpBindings/AgentMcpServerLookup.cs`,
    `McpServers/Responses/McpServerSummaryResponse.cs` (ou tipo irmão): passam
    a incluir `AllowedTools`.
  - Nova migration EF Core aditiva (coluna `allowed_tools` jsonb em
    `agent_mcp_servers`, default `[]`).
- **apps/workers**: não tocado nesta fatia (ver Non-Goals em design.md) — a
  execução real (filtrar tools entregues ao LLM pelo allow-list) fica para uma
  change futura (`apps-workers-execucao-mcp`).
- **apps/frontend**: não tocado.
- **Consumidores existentes de `PUT /agents/{id}/mcp-servers`**: precisam
  migrar do shape antigo (`mcpServerId[]`) para o novo
  (`{ mcpServerId, allowedTools }[]`) — mudança de contrato incompatível.
