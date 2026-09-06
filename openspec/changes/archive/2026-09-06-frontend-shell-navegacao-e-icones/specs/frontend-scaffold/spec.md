## MODIFIED Requirements

### Requirement: Mantine configurado com AppShell inicial
O sistema SHALL incluir, em `apps/frontend`, as bibliotecas Mantine `core`,
`hooks`, `form` e `notifications`, configuradas com os providers necessários
(`MantineProvider`, `Notifications`), e um `AppShell` do Mantine que renderiza
o conteúdo roteado da aplicação na área principal (via `<Outlet/>`).

A composição da casca — as regiões da barra lateral, a navegação e os controles
do operador — é especificada pela capability `frontend-app-shell`, para onde
migra também a regra de que a navegação só lista itens com página real, sem
mudança de comportamento.

#### Scenario: Aplicação renderiza o AppShell inicial
- **WHEN** a aplicação frontend é iniciada em modo de desenvolvimento e
  acessada no navegador
- **THEN** a página renderiza a casca da aplicação exibindo, na área principal,
  a página correspondente à rota atual, sem erros no console

### Requirement: Roteamento client-side configurado
O sistema SHALL prover, em `apps/frontend`, roteamento client-side via
`react-router` em data mode (`createBrowserRouter` combinado com
`RouterProvider`), com a casca da aplicação atuando como layout compartilhado
das rotas e a rota raiz (`/`) redirecionando para a primeira feature
disponível. A árvore de rotas SHALL ser definida em um módulo próprio, sem
efeito colateral no histórico do browser, de forma que a mesma árvore
usada pela aplicação possa ser montada em um router de memória fora do
browser. O data mode é exigido porque as APIs de interceptação de
navegação do `react-router` — usadas para avisar o operador sobre
alterações não salvas — só funcionam nesse modo.

#### Scenario: Rota raiz redireciona para uma feature existente
- **WHEN** o usuário acessa a aplicação pela URL raiz (`/`)
- **THEN** a aplicação redireciona automaticamente para a rota de uma
  feature existente, sem exibir uma página vazia

#### Scenario: Navegação entre rotas preserva o layout
- **WHEN** o usuário navega entre páginas diferentes da aplicação
- **THEN** a casca compartilhada permanece visível e não é recarregada,
  apenas o conteúdo da área principal muda

#### Scenario: Árvore de rotas montável fora do browser
- **WHEN** a mesma árvore de rotas usada pela aplicação é montada em um
  router de memória, a partir de uma rota inicial qualquer
- **THEN** a página correspondente a essa rota é renderizada, com o mesmo
  layout compartilhado e as mesmas regras de proteção de rota da
  aplicação real

