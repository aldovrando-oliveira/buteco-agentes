# operator-login-ui Specification

## Purpose

TBD - defined by change auth-login-e-servico. Update Purpose after archive.

## Requirements

### Requirement: Tela de login do operador
`apps/frontend` SHALL prover uma tela de login com campos de usuário e
senha, submetendo as credenciais a `POST /auth/login` em `apps/api`. Em
caso de sucesso, o token retornado SHALL ser armazenado e o usuário SHALL
ser redirecionado para a área autenticada da aplicação. Em caso de
credencial inválida, a interface SHALL exibir uma mensagem de erro sem
armazenar token nem redirecionar.

#### Scenario: Login bem-sucedido redireciona para a área autenticada
- **WHEN** o operador preenche usuário e senha corretos na tela de login
  e submete o formulário
- **THEN** a interface armazena o token retornado e redireciona para a
  página inicial autenticada

#### Scenario: Login com credencial inválida exibe erro
- **WHEN** o operador submete o formulário de login com usuário ou senha
  incorretos
- **THEN** a interface exibe uma mensagem de erro, permanece na tela de
  login e não armazena nenhum token

### Requirement: Rotas da aplicação exigem sessão ativa
`apps/frontend` SHALL redirecionar para a tela de login qualquer
navegação para uma rota da aplicação quando não houver token armazenado.

#### Scenario: Acesso sem token redireciona para login
- **WHEN** um usuário sem token armazenado navega para qualquer rota da
  aplicação diferente da tela de login
- **THEN** a interface redireciona para a tela de login, sem renderizar o
  conteúdo da rota original

### Requirement: Resposta 401 encerra a sessão do operador
`apps/frontend` SHALL, ao receber uma resposta `401 Unauthorized` de
`apps/api` ou `apps/inbox` para qualquer chamada autenticada, limpar o
token armazenado e redirecionar para a tela de login.

#### Scenario: 401 em uma chamada autenticada encerra a sessão
- **WHEN** uma chamada de uma feature autenticada (ex. listagem de
  agentes) recebe `401 Unauthorized` de `apps/api`
- **THEN** a interface remove o token armazenado e redireciona para a
  tela de login
