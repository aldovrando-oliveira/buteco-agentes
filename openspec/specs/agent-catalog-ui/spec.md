# agent-catalog-ui Specification

## Purpose

TBD - defined by change frontend-cadastro-agentes. Update Purpose after archive.

## Requirements

### Requirement: Listagem de agentes na interface
O sistema SHALL prover, em `apps/frontend`, uma página que lista os agentes
cadastrados consumindo `GET /agents`, com link para o detalhe de cada
agente e um botão para iniciar o cadastro de um novo agente.

#### Scenario: Lista carregada com sucesso
- **WHEN** o usuário acessa a página de agentes e existem agentes
  cadastrados
- **THEN** a interface exibe o nome de cada agente com um link para sua
  página de detalhe, e um botão para cadastrar um novo agente

#### Scenario: Lista vazia
- **WHEN** o usuário acessa a página de agentes e não existe nenhum agente
  cadastrado
- **THEN** a interface indica que não há agentes cadastrados e mantém
  visível o botão para cadastrar um novo agente

#### Scenario: Erro ao carregar a lista
- **WHEN** a chamada a `GET /agents` falha (erro de rede ou resposta de
  erro do servidor)
- **THEN** a interface exibe um estado de erro na página, sem quebrar a
  navegação do restante da aplicação

### Requirement: Cadastro de agente pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para cadastrar um
agente informando nome e instruções (system prompt), enviando os dados via
`POST /agents`.

#### Scenario: Cadastro com sucesso
- **WHEN** o usuário preenche nome e instruções válidos e submete o
  formulário
- **THEN** a interface envia `POST /agents`, exibe uma notificação de
  sucesso e redireciona o usuário para a página de detalhe do agente
  recém-criado

#### Scenario: Cadastro rejeitado por validação do servidor
- **WHEN** o servidor responde com erro de validação (HTTP 400) por nome
  ou instruções ausentes
- **THEN** a interface exibe a mensagem de erro correspondente no campo do
  formulário associado, sem navegar para outra página

#### Scenario: Falha de rede ou do servidor ao cadastrar
- **WHEN** a chamada a `POST /agents` falha por um motivo diferente de
  validação (erro de rede ou erro do servidor)
- **THEN** a interface exibe uma notificação de erro genérica e mantém os
  dados já preenchidos no formulário

### Requirement: Detalhe de agente somente leitura
O sistema SHALL prover, em `apps/frontend`, uma página que exibe os dados
completos de um agente consumindo `GET /agents/{id}`, sem nenhuma ação de
edição ou exclusão.

#### Scenario: Detalhe carregado com sucesso
- **WHEN** o usuário acessa a página de detalhe de um agente existente
- **THEN** a interface exibe nome, instruções e as datas de criação e
  atualização do agente, sem nenhum controle de edição ou exclusão

#### Scenario: Agente inexistente
- **WHEN** o usuário acessa a página de detalhe de um id que não
  corresponde a nenhum agente cadastrado (`GET /agents/{id}` responde 404)
- **THEN** a interface exibe um estado de "agente não encontrado", sem
  quebrar a navegação do restante da aplicação
