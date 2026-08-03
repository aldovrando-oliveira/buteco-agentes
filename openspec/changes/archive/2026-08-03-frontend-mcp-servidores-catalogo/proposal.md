## Why

O backend já expõe o catálogo de servidores MCP (`mcp-server-catalog`,
change `backend-mcp-catalogo-vinculo`, já aplicada) — cadastro, edição,
ativação/desativação e teste de conexão — mas não existe nenhuma interface
em `apps/frontend` para operar esse catálogo. Hoje a única forma de
cadastrar um servidor MCP é chamando a API diretamente. Sem uma interface,
ninguém consegue cadastrar, revisar ou testar a conexão de um servidor MCP
pelo produto, o que bloqueia qualquer uso real do catálogo.

## What Changes

- Nova feature `features/mcp-servers/` em `apps/frontend`, seguindo a
  mesma estrutura de `features/agents/` (api/, components/, pages/,
  types/).
- Página de listagem (`McpServerListPage`) consumindo `GET /mcp-servers`,
  com tabela mostrando Nome, Url, tipo de autenticação e estado
  (`isActive`) de cada servidor, e botão para cadastrar um novo.
- Formulário de cadastro e edição (`McpServerForm`) reutilizado pelas
  páginas `McpServerCreatePage` e `McpServerEditPage`, com campos Nome,
  Descrição, Url, tipo de autenticação (`None`/`BearerToken`) e credencial
  (exibida apenas quando o tipo de autenticação exige credencial, nunca
  pré-preenchida), consumindo `POST /mcp-servers` e `PUT /mcp-servers/{id}`,
  e um botão de "Testar conexão" que usa `POST /mcp-servers/test` com os
  valores atuais do formulário.
- Página de detalhe (`McpServerDetailPage`) exibindo os dados completos do
  servidor (sem a credencial), com link para editar, ação de
  ativar/desativar (`POST /mcp-servers/{id}/activate` e
  `.../deactivate`, com confirmação apenas na desativação) e um botão de
  "Testar conexão" que usa `POST /mcp-servers/{id}/test`.
- Resultado do teste de conexão (salvo ou não salvo) exibido inline na
  própria tela (não como notificação nem modal), permitindo ajustar os
  dados e testar novamente sem perder o contexto.
- Nova rota `/mcp-servers` (mais `/new`, `/:id`, `/:id/edit`) registrada
  no `AppRouter`, e novo item de navegação no `AppShell` ao lado de
  "Agentes".

## Capabilities

### New Capabilities
- `mcp-server-catalog-ui`: interface em `apps/frontend` para listar,
  cadastrar, editar, ativar/desativar e testar a conexão de servidores MCP
  cadastrados no catálogo já existente em `apps/api`.

### Modified Capabilities
(nenhuma — esta change não altera requisitos de `mcp-server-catalog`,
`agent-catalog-ui` nem de nenhuma outra capability já existente; apenas
consome endpoints já especificados em `mcp-server-catalog`.)

## Impact

- **apps/frontend**: novo diretório `src/features/mcp-servers/` completo;
  `src/app/router.tsx` ganha as rotas `/mcp-servers*`;
  `src/components/layout/AppShell.tsx` ganha um novo `NavLink`. Nenhum
  arquivo de `features/agents/` é modificado.
- **apps/api**: nenhuma mudança — esta change só consome endpoints já
  aplicados (`POST/GET/PUT /mcp-servers`,
  `POST /mcp-servers/{id}/activate|deactivate`, `POST /mcp-servers/test`,
  `POST /mcp-servers/{id}/test`).
- **apps/workers**: nenhuma mudança.
- Vínculo agente↔MCP (seleção de tools por agente, `PUT
  /agents/{id}/mcp-servers`) fica fora de escopo — será uma change
  seguinte que também tocará `AgentForm`/`AgentDetailPage`.
