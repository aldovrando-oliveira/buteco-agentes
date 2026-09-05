## MODIFIED Requirements

### Requirement: Roteamento client-side configurado
O sistema SHALL prover, em `apps/frontend`, roteamento client-side via
`react-router` em data mode (`createBrowserRouter` combinado com
`RouterProvider`), com o `AppShell` atuando como layout compartilhado das
rotas e a rota raiz (`/`) redirecionando para a primeira feature
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
- **THEN** o header e a navbar do `AppShell` permanecem visíveis e não são
  recarregados, apenas o conteúdo da área principal muda

#### Scenario: Árvore de rotas montável fora do browser
- **WHEN** a mesma árvore de rotas usada pela aplicação é montada em um
  router de memória, a partir de uma rota inicial qualquer
- **THEN** a página correspondente a essa rota é renderizada, com o mesmo
  layout compartilhado e as mesmas regras de proteção de rota da
  aplicação real
