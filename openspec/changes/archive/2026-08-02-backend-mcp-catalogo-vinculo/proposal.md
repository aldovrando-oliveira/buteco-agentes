## Why

Agentes vão precisar de tools de servidores MCP remotos para agir além do que
o LLM sabe sozinho, mas hoje não existe onde cadastrar esses servidores nem
como associá-los a um agente. Esta change entrega só a base de dados —
catálogo de servidores MCP e o vínculo N:N com agentes — para que a change
seguinte (execução: o Worker descobrindo e chamando tools MCP numa execução
real) tenha o que consumir. Sem catálogo e vínculo persistidos, não há como
começar a fatia de execução.

## What Changes

- Novo modelo `McpServer` (nome, descrição, URL, tipo de autenticação
  `None`/`BearerToken`, credencial, `isActive`), com CRUD completo em
  `apps/api` seguindo o mesmo padrão CQRS (Commands/Queries + `Mediator`) já
  usado em `Agent`.
- Credencial do `McpServer` é criptografada antes de persistir (AES-GCM, uma
  chave simétrica de 256 bits vinda de configuração/env var — nunca do
  banco, mesmo princípio já usado para chaves de provedor de LLM) e nunca é
  retornada em nenhuma resposta de leitura (campo write-only).
- Ativação/desativação de `McpServer` via `isActive`, mesmo padrão de
  `Agent.IsActive` — sem exclusão do registro.
- Endpoint de teste de configuração MCP (`POST /mcp-servers/test` para
  configuração ainda não salva, `POST /mcp-servers/{id}/test` para servidor
  já cadastrado) que executa o handshake real do protocolo MCP (mensagem
  `initialize`, via SDK oficial `ModelContextProtocol.Core`) contra o
  servidor remoto — única conexão de rede real desta change; resultado é
  efêmero, não é persistido.
- Novo vínculo N:N `AgentMcpServer` entre `Agent` e `McpServer`, sem filtro
  de tool individual, com endpoint(s) dedicados para gerenciar o conjunto de
  servidores MCP vinculados a um agente.
- `GET /agents` e `GET /agents/{id}` (via `AgentResponse`) passam a incluir
  os servidores MCP vinculados ao agente.
- **BREAKING**: `AgentResponse` ganha um novo campo obrigatório
  (`mcpServers`) — consumidores existentes que fazem parsing estrito do
  shape da resposta precisam ser atualizados.

Fora de escopo desta change (ver design.md para detalhamento e Non-Goals):
MCP local via stdio (fora de escopo permanente), descoberta/uso real de
tools numa execução de agente, filtro de tool individual dentro de um MCP
vinculado, qualquer UI, e qualquer mudança em `apps/workers`.

## Capabilities

### New Capabilities
- `mcp-server-catalog`: cadastro, consulta, atualização, ativação/desativação
  e teste de configuração de servidores MCP remotos em `apps/api`.
- `agent-mcp-binding`: vínculo N:N entre agentes e servidores MCP cadastrados
  (associar/desassociar, consultar o conjunto vinculado).

### Modified Capabilities
- `agent-catalog`: `AgentResponse` (retornado por `POST /agents`,
  `GET /agents`, `GET /agents/{id}`, `PUT /agents/{id}`,
  `POST /agents/{id}/activate`, `POST /agents/{id}/deactivate`) passa a
  incluir os servidores MCP vinculados ao agente.

## Impact

- **apps/api**: novo módulo `McpServers` (entidade, commands, queries,
  endpoints, requests/responses) espelhando a estrutura de `Agents`; novo
  módulo (ou submódulo) para o vínculo `AgentMcpServer`; nova migration EF
  Core (tabelas `mcp_servers` e `agent_mcp_servers`); `AgentResponse` alterado;
  nova dependência NuGet `ModelContextProtocol.Core`; nova configuração
  (`Mcp:CredentialEncryptionKey` ou equivalente) documentada em
  `.env.example`.
- **apps/workers**: nenhuma mudança nesta fatia.
- **apps/frontend**: nenhuma mudança nesta fatia (sem UI).
- Consumidores de `GET /agents`/`GET /agents/{id}` precisam tolerar o novo
  campo `mcpServers` no shape de `AgentResponse`.
