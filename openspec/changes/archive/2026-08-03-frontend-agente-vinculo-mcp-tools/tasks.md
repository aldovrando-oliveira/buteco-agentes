## 1. Tipos e API client (apps/frontend)

- [x] 1.1 (apps/frontend) Estender `features/agents/types/agent.ts`: `Agent` ganha `mcpServers: { id: string; name: string; allowedTools: string[] }[]`.
- [x] 1.2 (apps/frontend) Estender `ValidationProblemDetails` em `features/agents/api/agentsApi.ts` para incluir `detail?: string`, capturando o motivo retornado no `ProblemDetails` do erro 502.
- [x] 1.3 (apps/frontend) Adicionar `replaceAgentMcpServers(agentId, bindings)` em `features/agents/api/agentsApi.ts`, enviando `PUT /agents/{id}/mcp-servers` com body `{ mcpServers: bindings }` (nunca array solto, nunca `null`).
- [x] 1.4 (apps/frontend) Adicionar `useReplaceAgentMcpServersMutation(agentId)` em `features/agents/api/useAgents.ts`, invalidando/atualizando o cache de `['agents', agentId]` e `['agents']` em caso de sucesso, mesmo padrão das demais mutations do arquivo.
- [x] 1.5 (apps/frontend) Adicionar `listMcpServerTools(id)` em `features/mcp-servers/api/mcpServersApi.ts`, envolvendo `GET /mcp-servers/{id}/tools`.
- [x] 1.6 (apps/frontend) Adicionar `useMcpServerToolsQuery(mcpServerId, { enabled })` em `features/mcp-servers/api/useMcpServers.ts`, com `staleTime: Infinity` (Decision 3 — garante que recolher/reabrir o mesmo servidor reutiliza o cache em vez de refazer a chamada), expondo o resultado (`{ success, tools, failureReason, message }`) como dado normal — sem tratar `success: false` como `isError`.

## 2. AgentDetailPage: resumo e link (apps/frontend)

- [x] 2.1 (apps/frontend) Estender `AgentDetailCard` para exibir o resumo dos servidores MCP vinculados: nomes de `agent.mcpServers` quando não vazio, ou uma indicação de "nenhum servidor MCP vinculado" quando vazio.
- [x] 2.2 (apps/frontend) Adicionar, no `Group` de ações do topo de `AgentDetailPage` (ao lado de "Editar"), um link para `/agents/{id}/mcp-servers` ("Gerenciar servidores MCP"), sempre visível independentemente de haver vínculo.
- [x] 2.3 (apps/frontend) Atualizar `AgentDetailPage.test.tsx`/`AgentDetailCard.test.tsx` cobrindo: resumo com servidores vinculados, resumo sem vínculo, presença do link de gestão.

## 3. AgentMcpServersPage (apps/frontend)

- [x] 3.1 (apps/frontend) Criar `features/agents/pages/AgentMcpServersPage.tsx`: carrega `useAgentQuery(id)` e `useMcpServersQuery()`, exibindo loader até ambos resolverem, e os estados de erro (agente inexistente → 404, falha de carregamento, catálogo de servidores MCP vazio) antes de montar a UI de seleção.
- [x] 3.2 (apps/frontend) Inicializar o estado local de seleção (servidores marcados + `allowedTools` por servidor) a partir de `agent.mcpServers`, via `useState` com inicializador preguiçoso (sem `useEffect` de sincronização), conforme Decision 4 do design.
- [x] 3.3 (apps/frontend) Criar componente de linha por servidor MCP (ex. `AgentMcpServerRow`) em `features/agents/components/`: checkbox de seleção do servidor, indicador visual de servidor inativo (`isActive: false`) sem bloquear a seleção, e área expansível com o estado de tools.
- [x] 3.4 (apps/frontend) Na linha do servidor, ligar a expansão/seleção ao `useMcpServerToolsQuery(mcpServerId, { enabled })` (busca lazy, Decision 3), com estado de carregamento.
- [x] 3.5 (apps/frontend) Ao receber a lista de tools com sucesso (`success: true`), renderizar checkboxes de tools marcados conforme o `allowedTools` local, e reconciliar o estado local removendo qualquer tool que não conste mais na lista viva (drift, Decision 4).
- [x] 3.6 (apps/frontend) Ao receber falha na descoberta (`success: false`), exibir `message` e um botão "Tentar novamente" que chama `refetch()`, sem bloquear a seleção do servidor (Decision 6).
- [x] 3.7 (apps/frontend) Implementar marcar/desmarcar tool individual, atualizando o `allowedTools` do servidor correspondente sem afetar os demais.
- [x] 3.8 (apps/frontend) Implementar o submit: monta os bindings a partir do estado local (só servidores selecionados) e chama `useReplaceAgentMcpServersMutation(agentId)`; em sucesso, exibe notificação e navega para `/agents/{id}`.
- [x] 3.9 (apps/frontend) Tratar o erro 502 do submit (Decision 5): exibir `error.problem?.title` + `error.problem?.detail` identificando o servidor culpado, sem navegar e sem limpar a seleção local.
- [x] 3.10 (apps/frontend) Implementar a ação "Cancelar", navegando para `/agents/{id}` sem enviar requisição nem exigir confirmação.

## 4. Rota (apps/frontend)

- [x] 4.1 (apps/frontend) Adicionar a rota `agents/:id/mcp-servers` em `app/router.tsx`, como sibling de `:id` e `:id/edit`, apontando para `AgentMcpServersPage`.

## 5. Testes (apps/frontend)

- [x] 5.1 (apps/frontend) `AgentMcpServersPage.test.tsx`: pré-seleção a partir de vínculos existentes, incluindo o caso de tool com drift (permitida mas não mais oferecida pelo servidor).
- [x] 5.2 (apps/frontend) `AgentMcpServersPage.test.tsx`: busca de tools só é disparada ao selecionar/expandir um servidor, nunca no carregamento inicial da página.
- [x] 5.3 (apps/frontend) `AgentMcpServersPage.test.tsx`: seleção e desseleção de servidor, e de tool individual, atualizam o estado local corretamente.
- [x] 5.4 (apps/frontend) `AgentMcpServersPage.test.tsx`: submit completo, verificando o body envelopado (`{ mcpServers: [...] }`) enviado a `PUT /agents/{id}/mcp-servers`, incluindo o caso de enviar `[]` quando todos os servidores são desmarcados.
- [x] 5.5 (apps/frontend) `AgentMcpServersPage.test.tsx`: erro atômico 502 identifica o servidor específico que falhou, sem perder a seleção dos demais servidores/tools.
- [x] 5.6 (apps/frontend) `AgentMcpServersPage.test.tsx`: falha de descoberta de tools de um servidor (`success: false`) exibe o motivo e permite tentar novamente, sem bloquear a seleção dos demais servidores.
- [x] 5.7 (apps/frontend) `useMcpServers.test.ts`: cobrir `useMcpServerToolsQuery` respeitando `enabled` e expondo `success: false` como dado (não como `isError`).
- [x] 5.8 (apps/frontend) `useAgents.test.ts`: cobrir `useReplaceAgentMcpServersMutation` e o envelope enviado por `replaceAgentMcpServers`.
- [x] 5.9 (apps/frontend) `useAgents.test.ts`: mockar um `PUT /agents/{id}/mcp-servers` respondendo 502 com `{ title, detail }` no corpo e verificar que `useReplaceAgentMcpServersMutation` expõe um `ApiError` com `error.problem.title` e `error.problem.detail` populados — trava a suposição da Decision 5 (parsing de `ProblemDetails` em `request<T>` não é restrito a 400).
- [x] 5.10 (apps/frontend) `AgentMcpServersPage.test.tsx`: `GET /mcp-servers` retornando lista vazia exibe indicação de que não há servidor MCP cadastrado, sem exibir a lista de seleção nem permitir submit (Scenario "Catálogo de servidores MCP vazio").
- [x] 5.11 (apps/frontend) `AgentMcpServersPage.test.tsx`: `GET /agents/{id}` retornando 404 exibe estado de "agente não encontrado", sem quebrar a navegação do restante da aplicação (Scenario "Agente inexistente").
- [x] 5.12 (apps/frontend) `AgentMcpServersPage.test.tsx`: expandir um servidor, recolher e expandir novamente não dispara uma segunda chamada a `GET /mcp-servers/{id}/tools` — depende do `staleTime: Infinity` da task 1.6 (Scenario "Recolher e reabrir um servidor não repete a busca desnecessariamente").
- [x] 5.13 (apps/frontend) `AgentMcpServersPage.test.tsx`: clicar no checkbox de seleção de um servidor com `isActive: false` e confirmar que ele não está desabilitado — o clique marca o servidor normalmente, entrando na seleção (Scenario "Indicador distingue servidor MCP inativo na lista de seleção" — cobre o controle continuar habilitado, não só o indicador visual aparecer).
- [x] 5.14 (apps/frontend) `AgentMcpServersPage.test.tsx`: com um servidor cuja descoberta de tools retornou `success: false`, clicar no seu checkbox de seleção e confirmar que ele não está desabilitado — o clique inclui o servidor no estado local de seleção com `allowedTools: []` (Scenario "Servidor com falha de descoberta pode ser selecionado com allowedTools vazio").
