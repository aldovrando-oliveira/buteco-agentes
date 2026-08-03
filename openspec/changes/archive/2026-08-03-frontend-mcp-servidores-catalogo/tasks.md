## 1. Tipos e cliente HTTP (apps/frontend)

- [x] 1.1 Criar `features/mcp-servers/types/mcpServer.ts` com `McpServer`,
  `McpServerAuthType` (`"None" | "BearerToken"`), `CreateMcpServerInput`,
  `UpdateMcpServerInput` e `McpConnectionTestResult` (`{ success: boolean;
  failureReason: string | null; message: string | null }`), espelhando os
  contratos reais de `apps/api` (`McpServerResponse`,
  `McpConnectionTestResult`).
- [x] 1.2 Criar `features/mcp-servers/api/mcpServersApi.ts` reaproveitando o
  padrão de `request<T>` de `features/agents/api/agentsApi.ts` (mesma
  `API_BASE_URL`, mesmo `ApiError`): `listMcpServers`, `getMcpServer(id)`,
  `createMcpServer(input)`, `updateMcpServer(id, input)`,
  `activateMcpServer(id)`, `deactivateMcpServer(id)`,
  `testUnsavedMcpServerConnection(config)`,
  `testSavedMcpServerConnection(id)`.

## 2. Hooks de dados (apps/frontend)

- [x] 2.1 Criar `features/mcp-servers/api/useMcpServers.ts` com
  `useMcpServersQuery`, `useMcpServerQuery(id)`,
  `useCreateMcpServerMutation`, `useUpdateMcpServerMutation`,
  `useActivateMcpServerMutation` e `useDeactivateMcpServerMutation`,
  seguindo o padrão de `useAgents.ts` (chave `['mcp-servers']` /
  `['mcp-servers', id]`, `setQueryData` + `invalidateQueries` no
  `onSuccess`).
- [x] 2.2 Adicionar, no mesmo arquivo, `useTestUnsavedMcpServerConnectionMutation`
  e `useTestSavedMcpServerConnectionMutation` — sem tocar o cache do
  `QueryClient` (resultado de teste é efêmero, conforme Decision 4 do
  design.md).
- [x] 2.3 Escrever `features/mcp-servers/api/useMcpServers.test.ts` cobrindo
  as oito hooks (sucesso e propagação de erro de cada mutation/query),
  mockando `mcpServersApi.ts`.

## 3. Componente de resultado de teste de conexão (apps/frontend)

- [x] 3.1 Criar `features/mcp-servers/components/ConnectionTestResultAlert.tsx`
  — componente de apresentação puro que recebe
  `{ success: boolean; message: string | null } | undefined` e renderiza
  um `Alert` do Mantine (verde/sucesso ou vermelho/falha), sem renderizar
  nada quando `undefined`.
- [x] 3.2 Escrever `ConnectionTestResultAlert.test.tsx` cobrindo os três
  estados (ausente, sucesso, falha com mensagem).

## 4. Formulário de servidor MCP (apps/frontend)

- [x] 4.1 Criar `features/mcp-servers/components/McpServerForm.tsx` com
  `@mantine/form`: campos Nome, Descrição, Url, AuthType (`Select`:
  None/BearerToken) e Credential — condicional e obrigatório apenas quando
  `AuthType != "None"` (cascata igual a Provider→Model em `AgentForm`),
  com placeholder "Deixe em branco para manter a credencial atual" quando
  em modo de edição. Incluir botão de submit, botão Cancelar (`onCancel`)
  e botão "Testar conexão".
- [x] 4.2 Implementar a ação "Testar conexão" usando
  `useTestUnsavedMcpServerConnectionMutation` com os valores atuais de
  `url`/`authType`/`credential` do formulário; normalizar erro de
  validação (400) para o mesmo formato de
  `ConnectionTestResultAlert` (Decision 1 do design.md); limpar o
  resultado exibido sempre que `url`, `authType` ou `credential` mudarem
  depois de um teste.
- [x] 4.3 Renderizar `ConnectionTestResultAlert` inline no próprio
  formulário com o resultado do teste.
- [x] 4.4 Escrever `McpServerForm.test.tsx`: validação de campos
  obrigatórios, cascata AuthType→Credential (aparecer/desaparecer,
  obrigatoriedade), submit em criação e edição, cancelar, teste de
  conexão com sucesso e com falha (incluindo erro de validação 400
  normalizado), limpeza do resultado do teste ao editar um campo
  relevante depois de testar. Incluir uma asserção explícita de que o
  resultado do teste de conexão (sucesso ou falha) nunca é exibido via
  `notifications.show` (toast) nem via `Modal`/`role="dialog"` — cobre o
  Scenario "Nenhuma notificação nem diálogo modal é usado para o
  resultado do teste" do spec.md.

## 5. Tabela e cartão de detalhe (apps/frontend)

- [x] 5.1 Criar `features/mcp-servers/components/McpServerTable.tsx` com 4
  colunas — Nome (link para o detalhe), Url, AuthType, Estado (badge
  `isActive`) — espelhando `AgentTable.tsx` (Decision 3 do design.md).
- [x] 5.2 Escrever `McpServerTable.test.tsx` cobrindo renderização das 4
  colunas e o indicador visual de `isActive`.
- [x] 5.3 Criar `features/mcp-servers/components/McpServerDetailCard.tsx`
  exibindo nome, descrição, URL, AuthType, badge `isActive`, datas de
  criação/atualização — nunca nenhum campo de credencial.
- [x] 5.4 Escrever `McpServerDetailCard.test.tsx`.

## 6. Páginas (apps/frontend)

- [x] 6.1 Criar `features/mcp-servers/pages/McpServerListPage.tsx`
  (`useMcpServersQuery` + `McpServerTable`, estados de loading/erro/vazio,
  botão "Novo servidor MCP"), espelhando `AgentListPage.tsx`.
- [x] 6.2 Escrever `McpServerListPage.test.tsx`.
- [x] 6.3 Criar `features/mcp-servers/pages/McpServerCreatePage.tsx`
  (`useCreateMcpServerMutation` + `McpServerForm`, notificação de sucesso,
  navegação para o detalhe do servidor criado, `fieldErrorsFrom` para
  erros 400), espelhando `AgentCreatePage.tsx`.
- [x] 6.4 Escrever `McpServerCreatePage.test.tsx`.
- [x] 6.5 Criar `features/mcp-servers/pages/McpServerEditPage.tsx`
  (`useMcpServerQuery` + `useUpdateMcpServerMutation` + `McpServerForm`
  pré-preenchido, exceto credencial), espelhando `AgentEditPage.tsx`.
- [x] 6.6 Escrever `McpServerEditPage.test.tsx` cobrindo edição mantendo a
  credencial atual (campo em branco) e edição trocando a credencial.
  Incluir um teste dedicado para a regra de negócio específica do domínio
  MCP citada na Decision 2 do design.md — `AuthType != "None"` sem
  nenhuma credencial jamais persistida para o servidor (ex.: servidor
  criado com `AuthType: None` e editado para `BearerToken` sem informar
  credencial) — mockando a API retornando 400 com `{"credential": [...]}`
  e confirmando que a mensagem aparece no campo Credential (via o mesmo
  `fieldErrorsFrom`/`ApiError.problem.errors` herdado de `AgentForm`), sem
  navegar para outra página. Não deixar esse cenário coberto apenas
  implicitamente pelo teste genérico de "edição rejeitada por validação
  do servidor".
- [x] 6.7 Criar `features/mcp-servers/pages/McpServerDetailPage.tsx`
  (`useMcpServerQuery` + `McpServerDetailCard`, link Editar, ação
  ativar/desativar com `Modal` de confirmação apenas na desativação,
  botão "Testar conexão" via `useTestSavedMcpServerConnectionMutation` com
  `ConnectionTestResultAlert` inline), espelhando `AgentDetailPage.tsx`.
  Incluir o tratamento de servidor MCP inexistente: quando
  `GET /mcp-servers/{id}` responde 404 (`ApiError` com `status === 404`),
  exibir um estado de "servidor não encontrado" em vez do restante da
  página, sem quebrar a navegação do restante da aplicação — mesmo
  padrão já usado em `AgentDetailPage.tsx`.
- [x] 6.8 Escrever `McpServerDetailPage.test.tsx` cobrindo estado
  ativo/inativo, confirmação de desativação, ativação sem confirmação,
  teste de conexão salvo com sucesso e com falha, e o Scenario "Servidor
  MCP inexistente" do spec.md (mock de `getMcpServer` rejeitando com
  `ApiError(404, ...)`, verificando que a página exibe o estado de "não
  encontrado" em vez de quebrar). Incluir também uma asserção explícita
  de que o resultado do teste de conexão nunca é exibido via
  `notifications.show` (toast) nem via `Modal`/`role="dialog"` — cobre o
  Scenario "Nenhuma notificação nem diálogo modal é usado para o
  resultado do teste" do spec.md.

## 7. Roteamento e navegação (apps/frontend)

- [x] 7.1 Registrar em `src/app/router.tsx` as rotas `mcp-servers` (index,
  `new`, `:id`, `:id/edit`) dentro do `AppShell`, seguindo o mesmo padrão
  aninhado usado para `agents`.
- [x] 7.2 Adicionar em `src/components/layout/AppShell.tsx` um novo
  `NavLink` para `/mcp-servers` ao lado do já existente para `/agents`.
- [x] 7.3 Atualizar `AppShell.test.tsx` (se necessário) para cobrir o novo
  item de navegação.

## 8. Verificação final (apps/frontend)

- [x] 8.1 Rodar toda a suíte de testes (`npm test` ou equivalente em
  `apps/frontend`) e garantir que passa sem quebrar nenhum teste
  existente de `features/agents/`.
- [x] 8.2 Rodar o app localmente e percorrer o fluxo completo (listar,
  cadastrar sem autenticação, cadastrar com BearerToken, testar conexão
  não salva, editar mantendo credencial, editar trocando credencial,
  ativar/desativar, testar conexão salva) para confirmar visualmente
  antes de considerar a change pronta para `/opsx:apply`.
