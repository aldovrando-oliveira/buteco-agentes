# frontend-scaffold Specification

## Purpose

TBD - defined by change estrutura-base-monorepo. Update Purpose after archive.

## Requirements

### Requirement: Projeto Vite + React + TypeScript isolado
O sistema SHALL prover, em `apps/frontend`, um projeto Vite + React +
TypeScript isolado de `apps/api` e `apps/workers`, sem nenhuma dependência ou
import de código interno desses outros apps.

#### Scenario: Build isolado do frontend
- **WHEN** um desenvolvedor executa o comando de build do Vite a partir de `apps/frontend`
- **THEN** o build conclui com sucesso sem depender de nenhum código-fonte localizado em `apps/api` ou `apps/workers`

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

### Requirement: Lint e formatação configurados
O sistema SHALL incluir, em `apps/frontend`, configuração de ESLint e Prettier
aplicável ao código TypeScript/React do projeto.

#### Scenario: Lint executa sem erros no código inicial
- **WHEN** um desenvolvedor executa o script de lint do `apps/frontend`
- **THEN** o processo termina sem erros de lint no código gerado pelo scaffold

#### Scenario: Formatação é verificável
- **WHEN** um desenvolvedor executa o script de checagem de formatação do Prettier em `apps/frontend`
- **THEN** o processo confirma que todos os arquivos estão formatados de acordo com a configuração do projeto
