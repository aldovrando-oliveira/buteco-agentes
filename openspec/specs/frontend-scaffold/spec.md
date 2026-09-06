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

### Requirement: Esquema de cor automático com alternância manual
O sistema SHALL configurar, em `apps/frontend`, o Mantine com o esquema de
cor `auto` como padrão — seguindo a preferência de esquema declarada pelo
sistema operacional de quem nunca escolheu — oferecendo uma alternância
manual entre os esquemas `light` e `dark` que persiste a escolha do usuário
entre sessões.

A resolução do esquema inicial SHALL acontecer antes da primeira pintura da
página, de modo que quem usa o sistema operacional no escuro não veja a
interface piscar em claro enquanto a aplicação monta.

Os controles que exibem ou alternam o esquema SHALL ler o esquema efetivo, e
não a preferência crua — que sob o padrão `auto` não é nem `light` nem
`dark` — para que o rótulo do controle descreva o que está na tela e o
primeiro acionamento produza mudança visível.

#### Scenario: Aplicação segue a preferência do sistema operacional
- **WHEN** a aplicação é acessada pela primeira vez, sem nenhuma preferência
  de tema salva anteriormente
- **THEN** a interface é renderizada no esquema de cor declarado pelo sistema
  operacional do usuário

#### Scenario: Alternância de tema persiste entre sessões
- **WHEN** o usuário aciona o controle de alternância de tema e recarrega a
  aplicação
- **THEN** a interface é renderizada no esquema de cor escolhido
  anteriormente, sobrepondo-se à preferência do sistema operacional

#### Scenario: Sem piscada de esquema na carga inicial
- **WHEN** a aplicação é carregada por um usuário cujo esquema resolvido é o
  escuro, seja por preferência salva, seja pela preferência do sistema
  operacional
- **THEN** a página já é pintada no esquema escuro, sem exibir o esquema claro
  em nenhum momento antes da aplicação montar

#### Scenario: Controle de alternância descreve o esquema efetivo
- **WHEN** o usuário sem preferência salva, cujo sistema operacional está no
  escuro, visualiza o controle de alternância de tema
- **THEN** o controle oferece mudar para o esquema claro, e não para o escuro
  que já está em uso

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

### Requirement: Lint e formatação configurados
O sistema SHALL incluir, em `apps/frontend`, configuração de ESLint e Prettier
aplicável ao código TypeScript/React do projeto.

#### Scenario: Lint executa sem erros no código inicial
- **WHEN** um desenvolvedor executa o script de lint do `apps/frontend`
- **THEN** o processo termina sem erros de lint no código gerado pelo scaffold

#### Scenario: Formatação é verificável
- **WHEN** um desenvolvedor executa o script de checagem de formatação do Prettier em `apps/frontend`
- **THEN** o processo confirma que todos os arquivos estão formatados de acordo com a configuração do projeto
