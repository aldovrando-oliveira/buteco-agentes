## RENAMED Requirements

- FROM: `### Requirement: Detalhe de agente somente leitura`
- TO: `### Requirement: Detalhe de agente`

## MODIFIED Requirements

### Requirement: Listagem de agentes na interface
O sistema SHALL prover, em `apps/frontend`, uma página que lista os agentes
cadastrados consumindo `GET /agents`, com link para o detalhe de cada
agente, um indicador do estado (`isActive`) de cada agente, e um botão
para iniciar o cadastro de um novo agente.

#### Scenario: Lista carregada com sucesso
- **WHEN** o usuário acessa a página de agentes e existem agentes
  cadastrados
- **THEN** a interface exibe o nome de cada agente com um link para sua
  página de detalhe, um indicador visual do estado (ativo ou inativo) de
  cada agente, e um botão para cadastrar um novo agente

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

#### Scenario: Indicador distingue agente inativo na lista
- **WHEN** a lista inclui um agente com `isActive: false`
- **THEN** a interface exibe, na linha desse agente, um indicador visual
  diferente do usado para agentes com `isActive: true`

### Requirement: Detalhe de agente
O sistema SHALL prover, em `apps/frontend`, uma página que exibe os dados
completos de um agente consumindo `GET /agents/{id}`, incluindo seu estado
(`isActive`), com um link para editar o agente e uma ação para
ativá-lo ou desativá-lo.

#### Scenario: Detalhe carregado com sucesso
- **WHEN** o usuário acessa a página de detalhe de um agente existente
- **THEN** a interface exibe nome, instruções, as datas de criação e
  atualização, um indicador do estado (`isActive`) do agente, um link
  para editar o agente e uma ação para ativá-lo ou desativá-lo

#### Scenario: Agente inexistente
- **WHEN** o usuário acessa a página de detalhe de um id que não
  corresponde a nenhum agente cadastrado (`GET /agents/{id}` responde 404)
- **THEN** a interface exibe um estado de "agente não encontrado", sem
  quebrar a navegação do restante da aplicação

#### Scenario: Ação de ativar exibida para agente inativo
- **WHEN** o agente exibido tem `isActive: false`
- **THEN** a interface exibe uma ação para ativá-lo, e não exibe uma ação
  para desativá-lo

#### Scenario: Ação de desativar exibida para agente ativo
- **WHEN** o agente exibido tem `isActive: true`
- **THEN** a interface exibe uma ação para desativá-lo, e não exibe uma
  ação para ativá-lo

## ADDED Requirements

### Requirement: Edição de agente pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para editar o
nome e as instruções (system prompt) de um agente já cadastrado, pré-
preenchido com os dados atuais do agente, enviando os dados via
`PUT /agents/{id}`.

#### Scenario: Formulário de edição pré-preenchido com os dados atuais
- **WHEN** o usuário acessa a página de edição de um agente existente
- **THEN** a interface exibe o formulário com o nome e as instruções
  atuais do agente já preenchidos

#### Scenario: Edição com sucesso
- **WHEN** o usuário altera nome e/ou instruções para valores válidos e
  submete o formulário de edição
- **THEN** a interface envia `PUT /agents/{id}`, exibe uma notificação de
  sucesso e navega de volta para a página de detalhe do agente editado

#### Scenario: Edição rejeitada por validação do servidor
- **WHEN** o servidor responde com erro de validação (HTTP 400) por nome
  ou instruções ausentes
- **THEN** a interface exibe a mensagem de erro correspondente no campo do
  formulário associado, sem navegar para outra página

#### Scenario: Falha de rede ou do servidor ao editar
- **WHEN** a chamada a `PUT /agents/{id}` falha por um motivo diferente de
  validação (erro de rede ou erro do servidor)
- **THEN** a interface exibe uma notificação de erro genérica e mantém os
  dados já preenchidos no formulário, sem navegar para outra página

### Requirement: Ativação e desativação de agente pela interface
O sistema SHALL prover, em `apps/frontend`, ações para ativar e desativar
um agente cadastrado, consumindo `POST /agents/{id}/activate` e
`POST /agents/{id}/deactivate` a partir da página de detalhe do agente.
Desativar um agente SHALL exigir confirmação explícita do usuário antes de
enviar a requisição; ativar um agente SHALL NOT exigir confirmação.

#### Scenario: Ativar agente com sucesso, sem confirmação
- **WHEN** o usuário aciona a ação de ativar um agente inativo
- **THEN** a interface envia `POST /agents/{id}/activate` imediatamente,
  sem exibir nenhum diálogo de confirmação, exibe uma notificação de
  sucesso e atualiza o indicador de estado exibido para "ativo"

#### Scenario: Desativar agente exige confirmação antes de enviar a requisição
- **WHEN** o usuário aciona a ação de desativar um agente ativo
- **THEN** a interface exibe um diálogo de confirmação antes de enviar
  qualquer requisição, e `POST /agents/{id}/deactivate` só é enviado se o
  usuário confirmar explicitamente a ação nesse diálogo

#### Scenario: Cancelar a confirmação de desativação não envia a requisição
- **WHEN** o usuário aciona a ação de desativar um agente ativo e, no
  diálogo de confirmação exibido, escolhe cancelar
- **THEN** a interface fecha o diálogo sem enviar `POST
  /agents/{id}/deactivate` e o agente permanece exibido como ativo

#### Scenario: Confirmar a desativação envia a requisição e atualiza o estado
- **WHEN** o usuário aciona a ação de desativar um agente ativo e, no
  diálogo de confirmação exibido, confirma a ação
- **THEN** a interface envia `POST /agents/{id}/deactivate`, exibe uma
  notificação de sucesso e atualiza o indicador de estado exibido para
  "inativo"

#### Scenario: Falha de rede ou do servidor ao ativar ou desativar
- **WHEN** a chamada a `POST /agents/{id}/activate` ou `POST
  /agents/{id}/deactivate` falha (erro de rede ou erro do servidor)
- **THEN** a interface exibe uma notificação de erro genérica e mantém o
  indicador de estado exibido igual ao estado anterior à tentativa
