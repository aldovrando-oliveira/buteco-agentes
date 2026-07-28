## 1. apps/api — CORS mínimo

- [x] 1.1 (apps/api) Criar `Options/CorsOptions.cs` (`SectionName = "Cors"`, `string[] AllowedOrigins = []`), seguindo o padrão de `Options/RabbitMqOptions.cs`.
- [x] 1.2 (apps/api) Em `Program.cs`, registrar `builder.Services.Configure<CorsOptions>(...)`, `builder.Services.AddCors(...)` lendo `CorsOptions.AllowedOrigins`, e chamar `app.UseCors()` após `UseHttpsRedirection()` e antes do mapeamento dos endpoints.
- [x] 1.3 (apps/api) Adicionar `"Cors": { "AllowedOrigins": ["http://localhost:5173"] }` em `appsettings.Development.json` e `"Cors": { "AllowedOrigins": [] }` em `appsettings.json`.
- [x] 1.4 (apps/api) Teste de integração cobrindo que uma requisição com `Origin: http://localhost:5173` recebe os headers de CORS esperados (usar `ApiFactoryFixture` existente).

## 2. apps/frontend — dependências e infraestrutura de build/teste

- [x] 2.1 (apps/frontend) Adicionar `react-router@^8.3.0` e `@tanstack/react-query@^5.101.4` às dependencies.
- [x] 2.2 (apps/frontend) Adicionar `vitest@^4.1.10`, `@testing-library/react@^16.3.2`, `@testing-library/jest-dom@^7.0.0`, `@testing-library/user-event@^14.6.1`, `jsdom@^29.1.1` às devDependencies.
- [x] 2.3 (apps/frontend) Adicionar script `"test": "vitest run"` (e opcionalmente `"test:watch": "vitest"`) ao `package.json`.
- [x] 2.4 (apps/frontend) Atualizar `vite.config.ts` para usar `defineConfig` de `vitest/config` com `test: { environment: 'jsdom', setupFiles: ['./src/test/setup.ts'], globals: false }`.
- [x] 2.5 (apps/frontend) Criar `src/test/setup.ts` importando `@testing-library/jest-dom/vitest`.
- [x] 2.6 (apps/frontend) Criar `tsconfig.vitest.json` (`include: ["src/**/*.test.ts", "src/**/*.test.tsx", "src/test/**"]`), adicionar `exclude` correspondente em `tsconfig.app.json`, e referenciar o novo projeto em `tsconfig.json`.
- [x] 2.7 (apps/frontend) Criar `.env.example` documentando `VITE_API_BASE_URL=http://localhost:5017`.

## 3. apps/frontend — tema claro e AppShell

- [x] 3.1 (apps/frontend) Criar `src/theme.ts` com `createTheme({})`.
- [x] 3.2 (apps/frontend) Substituir o script manual de color-scheme em `index.html` pelo `<ColorSchemeScript defaultColorScheme="light" />` oficial do Mantine.
- [x] 3.3 (apps/frontend) Atualizar `main.tsx`: `MantineProvider` com `theme={theme}` e `defaultColorScheme="light"`.
- [x] 3.4 (apps/frontend) Atualizar `AppShell.tsx`: trocar `children: PropsWithChildren` por `<Outlet/>` no `Main`; remover `Dashboard`/`Inboxes` do array de navegação, manter só `Agents` com `component={Link} to="/agents"`.
- [x] 3.5 (apps/frontend) Adicionar toggle de tema (2 estados, light/dark) via `ActionIcon` no `Header`, usando `useMantineColorScheme()` e um SVG inline (sol/lua) — sem adicionar biblioteca de ícones.
- [x] 3.6 (apps/frontend) `AppShell.test.tsx`: cobre o Scenario "Navbar não lista itens sem página correspondente" (só "Agents" aparece) e os 2 Scenario do Requirement "Tema claro com alternância manual":
  - Abre em light por padrão — sem nenhuma preferência salva (`localStorage` vazio), a interface renderiza no esquema `light`.
  - Alternância persiste entre sessões — acionar o toggle e remontar o componente (simulando reload) mantém o esquema escolhido, sem voltar para `light`.

## 4. apps/frontend — roteamento

- [x] 4.1 (apps/frontend) Criar `src/app/queryClient.ts` com a instância única de `QueryClient`.
- [x] 4.2 (apps/frontend) Criar `src/app/router.tsx` com `BrowserRouter`/`Routes`/`Route` em modo declarativo: rota raiz (`/`) redireciona para `/agents`; `AppShell` como layout (`element`) da rota pai; rotas filhas `/agents`, `/agents/new`, `/agents/:id`.
- [x] 4.3 (apps/frontend) Atualizar `main.tsx`: envolver a árvore com `QueryClientProvider` e o router de `app/router.tsx`; remover a renderização de `App.tsx`.
- [x] 4.4 (apps/frontend) Remover `src/App.tsx` (substituído pela árvore de rotas).
- [x] 4.5 (apps/frontend) `app/router.test.tsx`: cobre os 2 Scenario do Requirement "Roteamento client-side configurado" (renderiza a árvore de `router.tsx` completa, não só `AppShell` isolado, já que o redirect é definido em `router.tsx`):
  - Rota raiz redireciona — acessar `/` renderiza o conteúdo de `/agents`, sem página vazia.
  - Navegação preserva o layout — navegar de `/agents` para `/agents/:id` (ou `/agents/new`) mantém header e navbar do `AppShell` montados, só o conteúdo da área principal muda.

## 5. apps/frontend — feature `agents`: camada de API

- [x] 5.1 (apps/frontend) Criar `src/features/agents/types/agent.ts` com os tipos `Agent`, `CreateAgentInput` espelhando `AgentResponse`/`CreateAgentRequest` de `apps/api` (`id`, `name`, `instructions`, `createdAt`, `updatedAt`).
- [x] 5.2 (apps/frontend) Criar `src/features/agents/api/agentsApi.ts`: `listAgents()`, `getAgent(id)`, `createAgent(input)` via `fetch`, lendo `import.meta.env.VITE_API_BASE_URL`; lançar `ApiError` tipada (status + `ValidationProblemDetails` parseado quando presente) em respostas não-OK.
- [x] 5.3 (apps/frontend) Criar `src/features/agents/api/useAgents.ts`: `useAgentsQuery()` (`queryKey: ['agents']`), `useAgentQuery(id)` (`queryKey: ['agents', id]`), `useCreateAgentMutation()` (em sucesso: `setQueryData` do detalhe + `invalidateQueries` da lista).
- [x] 5.4 (apps/frontend) `useAgents.test.ts`: cada hook com sucesso e erro, usando `fetch` mockado e um `QueryClientProvider` de teste.

## 6. apps/frontend — feature `agents`: componentes e páginas

- [x] 6.1 (apps/frontend) Criar `AgentTable.tsx` (apresentacional): recebe lista de agentes + estado de loading/erro via props, renderiza link para cada detalhe.
- [x] 6.2 (apps/frontend) Criar `AgentForm.tsx` (apresentacional): `@mantine/form` com validação client-side de nome/instruções obrigatórios, aceita `onSubmit`/erros de campo via props — sem chamar a API diretamente.
- [x] 6.3 (apps/frontend) `AgentForm.test.tsx`: submit válido chama `onSubmit`; submit vazio exibe erro de validação e não chama `onSubmit`.
- [x] 6.4 (apps/frontend) Criar `AgentDetailCard.tsx` (apresentacional): exibe nome, instruções, datas — somente leitura.
- [x] 6.5 (apps/frontend) Criar `AgentListPage.tsx`: usa `useAgentsQuery`, compõe `AgentTable`, trata loading/vazio/erro, botão para `/agents/new`.
- [x] 6.6 (apps/frontend) `AgentListPage.test.tsx`: cobre o estado de loading e os 3 Scenario do Requirement "Listagem de agentes na interface": lista carregada com sucesso, **lista vazia** (mensagem de "nenhum agente" com o botão de criar ainda visível) e erro ao carregar.
- [x] 6.7 (apps/frontend) Criar `AgentCreatePage.tsx`: usa `useCreateAgentMutation`, compõe `AgentForm`, mapeia erro 400 para os campos via `form.setFieldError`, notifica sucesso/erro via `@mantine/notifications`, navega para `/agents/:id` em sucesso.
- [x] 6.8 (apps/frontend) `AgentCreatePage.test.tsx`: cobre os 3 Scenario do Requirement "Cadastro de agente pela interface" — integração entre `useCreateAgentMutation` e `AgentForm` (única lógica desta fatia sem teste de integração, apesar das duas peças serem testadas isoladamente em 5.4 e 6.3):
  - Submit válido — mutation mockada com sucesso → `navigate` é chamado com `/agents/:id` do agente retornado, e a notificação de sucesso é exibida.
  - Submit rejeitado com 400 — mutation mockada rejeitando com `ApiError` (status 400 + `ValidationProblemDetails` simulado) → os erros aparecem nos campos certos do formulário via `form.setFieldError`, e `navigate` não é chamado.
  - Falha de rede ou do servidor — mutation mockada rejeitando com um erro que não é `ApiError` de status 400 (erro de rede ou 500) → interface exibe notificação de erro genérica, `navigate` não é chamado, e o formulário mantém os dados preenchidos (não é resetado).
- [x] 6.9 (apps/frontend) Criar `AgentDetailPage.tsx`: usa `useAgentQuery(id)` (via `useParams`), compõe `AgentDetailCard`, trata loading e 404 (`ApiError.status === 404`).
- [x] 6.10 (apps/frontend) `AgentDetailPage.test.tsx`: cobre os 2 Scenario do Requirement "Detalhe de agente somente leitura":
  - Detalhe carregado com sucesso — query mockada retornando um agente → nome, instruções e datas de criação/atualização são exibidos, sem nenhum controle de edição ou exclusão.
  - Agente inexistente — query mockada retornando `ApiError` com status 404 → interface exibe estado de "agente não encontrado", sem quebrar a navegação do restante da aplicação.

## 7. Validação final

- [x] 7.1 (apps/frontend) Rodar `npm run lint`, `npm run format:check`, `npm run test`, `npm run build` sem erros.
- [x] 7.2 (apps/api) Rodar a suíte de testes (`dotnet test`) incluindo o novo teste de CORS.
- [x] 7.3 (manual) Validar o fluxo ponta a ponta localmente: `docker compose up -d` → `dotnet run` em `apps/api` → `npm run dev` em `apps/frontend` → criar um agente pela UI, conferir que aparece na lista e no detalhe, e testar o toggle de tema.
