## 1. Extração da árvore de rotas (apps/frontend)

- [x] 1.1 Criar `src/app/routes.tsx` exportando `appRoutes: RouteObject[]`,
  construída com `createRoutesFromElements` a partir exatamente da mesma
  árvore JSX que hoje está dentro de `<Routes>` em `AppRouter`: rota
  `login`, e sob `ProtectedRoute` + `AppShell`, o `index` redirecionando
  para `/agents` e os grupos `agents`, `mcp-servers` e `channels` com
  todas as suas rotas filhas, na mesma ordem (Decision 1 do design.md).
- [x] 1.2 Garantir que `routes.tsx` não tenha nenhum efeito colateral de
  módulo — nada de `createBrowserRouter` nem de acesso a `window` no topo
  do arquivo (Decision 2).

## 2. Router em data mode (apps/frontend)

- [x] 2.1 Em `src/app/router.tsx`, criar o router com
  `createBrowserRouter(appRoutes)` em escopo de módulo (nunca dentro do
  corpo do componente) e reduzir `AppRouter` a renderizar
  `<RouterProvider router={router} />`, mantendo nome e assinatura do
  componente exportado (Decisions 2 e 5).
- [x] 2.2 Confirmar que `src/main.tsx` não precisa de nenhuma mudança e
  não tocá-lo.

## 3. Teste do roteamento (apps/frontend)

- [x] 3.1 Reescrever `src/app/router.test.tsx` para montar
  `createMemoryRouter(appRoutes, { initialEntries: [...] })` dentro de
  `<RouterProvider>`, preservando os quatro casos existentes em intenção:
  raiz redireciona para a listagem de agentes; sem token vai para o login
  sem renderizar conteúdo protegido; o `AppShell` permanece ao navegar
  para o cadastro de agente; o item "Canais" leva à listagem de canais
  (Decision 3).
- [x] 3.2 Trocar as asserções sobre `window.location.pathname` por
  asserções sobre o conteúdo renderizado, e remover o
  `window.history.pushState` do `beforeEach`, que deixa de ter efeito
  sobre um router de memória.
- [x] 3.3 Adicionar um caso que monta a árvore a partir de uma rota
  interna qualquer (ex.: `/channels`) e verifica que a página
  correspondente renderiza com o layout compartilhado — cobrindo o
  cenário "Árvore de rotas montável fora do browser" do spec.

## 4. Verificação (apps/frontend)

- [x] 4.1 Rodar a suíte completa em `apps/frontend` e confirmar que os
  240 testes existentes seguem passando, sem nenhuma mudança em arquivo
  de teste fora de `src/app/router.test.tsx`.
- [x] 4.2 Rodar lint e typecheck do frontend e confirmar que não há erros
  introduzidos.
- [x] 4.3 Subir a aplicação (`npm run dev`) e conferir manualmente o
  caminho crítico do roteamento: raiz redireciona, navegação pela navbar
  preserva o layout, refresh direto em `/agents/:id` e em `/channels`
  carrega a página certa, e o logout/401 continua levando ao login.
