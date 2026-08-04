## Why

O backend já suporta vincular um agente a servidores MCP com seleção
granular de tools (`PUT /agents/{id}/mcp-servers`, change
backend-mcp-selecao-tools) e descoberta ao vivo das tools de um servidor
(`GET /mcp-servers/{id}/tools`), mas não existe nenhuma interface para
usar essa capacidade — hoje só é possível via chamada direta à API. Esse
Non-Goal ficou pendente em três changes de frontend anteriores
(frontend-cadastro-agentes, frontend-mcp-servidores-catalogo e a mais
recente que criou o catálogo de servidores MCP na interface). Sem essa
tela, a seleção de tools por agente — o motivo de existir do backend —
permanece inacessível para quem opera a plataforma pela UI.

## What Changes

- Nova página `AgentMcpServersPage` (`/agents/:id/mcp-servers`) para
  vincular um agente a servidores MCP e, por servidor selecionado,
  escolher quais tools ele pode usar — consumindo o catálogo de
  servidores MCP (`GET /mcp-servers`), a descoberta ao vivo de tools de
  um servidor (`GET /mcp-servers/{id}/tools`) e o submit via
  `PUT /agents/{id}/mcp-servers`.
- `AgentDetailPage` ganha um resumo dos servidores MCP vinculados ao
  agente (nomes) e um link para a nova página de gestão.
- `features/agents/types/agent.ts`: `Agent` ganha o campo `mcpServers`
  (espelhando `AgentResponse.McpServers`).
- `features/agents/api`: nova função `replaceAgentMcpServers` e hook
  `useReplaceAgentMcpServersMutation`.
- `features/mcp-servers/api`: novo hook `useMcpServerToolsQuery` para a
  descoberta ao vivo de tools de um servidor específico.
- Nova rota `/agents/:id/mcp-servers` em `app/router.tsx`.

## Capabilities

### New Capabilities
- `agent-mcp-binding-ui`: página de gestão do vínculo agente↔servidores
  MCP na interface — seleção de servidores, descoberta e seleção de
  tools por servidor, submit e tratamento de erro.

### Modified Capabilities
- `agent-catalog-ui`: a página de detalhe do agente passa a exibir um
  resumo dos servidores MCP vinculados e um link para a página de
  gestão do vínculo.

## Impact

- Afeta somente `apps/frontend`. Nenhuma mudança em `apps/api` ou
  `apps/workers` — ambos os endpoints consumidos já existem e estão
  aplicados (change backend-mcp-selecao-tools).
- Novos arquivos: `features/agents/pages/AgentMcpServersPage.tsx` (+
  teste), possivelmente um componente de seleção de tools por servidor.
- Arquivos modificados: `features/agents/types/agent.ts`,
  `features/agents/api/agentsApi.ts`, `features/agents/api/useAgents.ts`,
  `features/agents/pages/AgentDetailPage.tsx` (ou
  `components/AgentDetailCard.tsx`), `features/mcp-servers/api/*`,
  `app/router.tsx`.
- Nenhuma biblioteca nova; reutiliza Mantine, React Query e o padrão de
  API client (`fetch` + `ApiError`) já estabelecido em ambas as
  features.
