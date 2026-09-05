## Why

A change seguinte (detalhe do agente em abas) precisa avisar o operador
quando ele tenta sair de uma aba com rascunho não salvo do vínculo MCP ou
das delegações. O único mecanismo do `react-router` para interceptar uma
navegação é o hook `useBlocker`, e ele **exige um data router**: na versão
instalada (react-router 8.3.0) o hook resolve `useDataRouterContext` e
`useDataRouterState` logo na primeira linha, e estoura fora de um data
router.

Hoje `apps/frontend` monta as rotas em modo declarativo (`<BrowserRouter>`
envolvendo `<Routes>`), onde esse hook não funciona. Sem esta migração, a
change das abas teria que carregar junto uma mudança de infraestrutura de
roteamento — dois tipos de risco no mesmo diff, num arquivo que toda rota
da aplicação atravessa.

Esta change faz **só** a migração, sem nenhuma mudança visível. O critério
de sucesso é a suíte inteira continuar passando com o comportamento de
navegação idêntico.

## What Changes

- **`app/routes.tsx` (novo)**: a árvore de rotas hoje embutida dentro do
  componente `AppRouter` passa a viver em módulo próprio, exportada como
  `RouteObject[]` construída com `createRoutesFromElements`. A estrutura
  JSX aninhada é preservada literalmente; muda o invólucro, não as rotas.
- **`app/router.tsx`**: passa a criar o router com
  `createBrowserRouter(appRoutes)` em escopo de módulo e a renderizar
  `<RouterProvider router={...} />`. `AppRouter` continua exportado com a
  mesma assinatura, então `main.tsx` não muda.
- **`app/router.test.tsx`**: passa a montar a árvore com
  `createMemoryRouter(appRoutes, { initialEntries })` e a afirmar o
  conteúdo renderizado, em vez de `window.location.pathname`. Necessário
  porque um router de escopo de módulo é criado uma vez por import: o
  `window.history.pushState` que hoje reposiciona cada teste deixaria de
  ter efeito sobre ele (Decision 3).
- Nenhuma rota nova, removida ou renomeada. Nenhum `loader`, `action` ou
  `errorElement`. Nenhuma dependência nova (todas as APIs usadas já vêm do
  `react-router` 8.3.0 instalado).

## Capabilities

### New Capabilities
(nenhuma)

### Modified Capabilities
- `frontend-scaffold`: o requisito de roteamento deixa de fixar o modo
  declarativo, passa a exigir data mode e a árvore de rotas em módulo
  próprio reutilizável fora do browser.

## Impact

- **apps/frontend**: três arquivos — `app/routes.tsx` (novo),
  `app/router.tsx` e `app/router.test.tsx`. Nenhum outro. Os outros 18
  arquivos de teste que montam `MemoryRouter` renderizam páginas
  isoladas e não dependem do modo do router, então não mudam.
- **apps/api / apps/workers / apps/inbox**: nenhuma mudança.
- **Fora de escopo** (change seguinte, `frontend-agente-detalhe-abas`):
  abas no detalhe do agente, hook de guarda de rascunho sujo, barra fixa
  de salvamento e o redirect da rota `/agents/:id/mcp-servers`.
