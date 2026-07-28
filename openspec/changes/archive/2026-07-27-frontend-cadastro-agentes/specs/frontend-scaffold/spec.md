## MODIFIED Requirements

### Requirement: Mantine configurado com AppShell inicial
O sistema SHALL incluir, em `apps/frontend`, as bibliotecas Mantine `core`,
`hooks`, `form` e `notifications`, configuradas com os providers necessários
(`MantineProvider`, `Notifications`), e um `AppShell` no estilo de
composição visual de https://ui.mantine.dev (navbar/header estruturais) que
renderiza o conteúdo roteado da aplicação (via `<Outlet/>`), com a
navegação do navbar limitada aos itens que possuem uma página real.

#### Scenario: Aplicação renderiza o AppShell inicial
- **WHEN** a aplicação frontend é iniciada em modo de desenvolvimento e
  acessada no navegador
- **THEN** a página renderiza um `AppShell` do Mantine com header e navbar
  visíveis, exibindo a página correspondente à rota atual dentro da área
  principal, sem erros no console

#### Scenario: Navbar não lista itens sem página correspondente
- **WHEN** o usuário visualiza a navbar do `AppShell`
- **THEN** só aparecem itens de navegação que apontam para uma rota
  existente na aplicação

## ADDED Requirements

### Requirement: Tema claro com alternância manual
O sistema SHALL configurar, em `apps/frontend`, o Mantine com tema claro
(`light`) como esquema de cor padrão, oferecendo uma alternância manual
entre os esquemas `light` e `dark` que persiste a escolha do usuário entre
sessões.

#### Scenario: Aplicação abre no tema claro por padrão
- **WHEN** a aplicação é acessada pela primeira vez, sem nenhuma preferência
  de tema salva anteriormente
- **THEN** a interface é renderizada no esquema de cor `light`

#### Scenario: Alternância de tema persiste entre sessões
- **WHEN** o usuário aciona o controle de alternância de tema e recarrega a
  aplicação
- **THEN** a interface é renderizada no esquema de cor escolhido
  anteriormente, sem retornar ao padrão `light`

### Requirement: Roteamento client-side configurado
O sistema SHALL prover, em `apps/frontend`, roteamento client-side via
`react-router` em modo declarativo, com o `AppShell` atuando como layout
compartilhado das rotas e a rota raiz (`/`) redirecionando para a primeira
feature disponível.

#### Scenario: Rota raiz redireciona para uma feature existente
- **WHEN** o usuário acessa a aplicação pela URL raiz (`/`)
- **THEN** a aplicação redireciona automaticamente para a rota de uma
  feature existente, sem exibir uma página vazia

#### Scenario: Navegação entre rotas preserva o layout
- **WHEN** o usuário navega entre páginas diferentes da aplicação
- **THEN** o header e a navbar do `AppShell` permanecem visíveis e não são
  recarregados, apenas o conteúdo da área principal muda
