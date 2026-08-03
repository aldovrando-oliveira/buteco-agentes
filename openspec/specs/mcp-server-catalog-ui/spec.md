# mcp-server-catalog-ui Specification

## Purpose

TBD - defined by change frontend-mcp-servidores-catalogo. Update Purpose after archive.

## Requirements

### Requirement: Listagem de servidores MCP na interface
O sistema SHALL prover, em `apps/frontend`, uma página que lista os
servidores MCP cadastrados consumindo `GET /mcp-servers`, exibindo para
cada servidor o nome (com link para o detalhe), a URL, o tipo de
autenticação (`AuthType`) e um indicador do estado (`isActive`), além de
um botão para iniciar o cadastro de um novo servidor MCP.

#### Scenario: Lista carregada com sucesso
- **WHEN** o usuário acessa a página de servidores MCP e existem
  servidores cadastrados
- **THEN** a interface exibe, para cada servidor, o nome com link para o
  detalhe, a URL, o tipo de autenticação, um indicador visual do estado
  (ativo ou inativo), e um botão para cadastrar um novo servidor MCP

#### Scenario: Lista vazia
- **WHEN** o usuário acessa a página de servidores MCP e não existe
  nenhum servidor cadastrado
- **THEN** a interface indica que não há servidores MCP cadastrados e
  mantém visível o botão para cadastrar um novo

#### Scenario: Erro ao carregar a lista
- **WHEN** a chamada a `GET /mcp-servers` falha (erro de rede ou resposta
  de erro do servidor)
- **THEN** a interface exibe um estado de erro na página, sem quebrar a
  navegação do restante da aplicação

#### Scenario: Indicador distingue servidor MCP inativo na lista
- **WHEN** a lista inclui um servidor MCP com `isActive: false`
- **THEN** a interface exibe, na linha desse servidor, um indicador
  visual diferente do usado para servidores com `isActive: true`

### Requirement: Cadastro de servidor MCP pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para cadastrar
um servidor MCP informando nome, descrição, URL e tipo de autenticação
(`None` ou `BearerToken`), com um campo de credencial exibido e obrigatório
apenas quando o tipo de autenticação selecionado exigir credencial,
enviando os dados via `POST /mcp-servers`, e uma ação para cancelar o
cadastro e voltar à listagem sem enviar nenhuma requisição.

#### Scenario: Cadastro com sucesso sem autenticação
- **WHEN** o usuário preenche nome, descrição, URL válidos, seleciona
  `AuthType: None` e submete o formulário
- **THEN** a interface envia `POST /mcp-servers` sem nenhum campo de
  credencial, exibe uma notificação de sucesso e redireciona o usuário
  para a página de detalhe do servidor recém-criado

#### Scenario: Cadastro com sucesso com autenticação por token
- **WHEN** o usuário preenche nome, descrição, URL válidos, seleciona
  `AuthType: BearerToken` e informa uma credencial não vazia, e submete o
  formulário
- **THEN** a interface envia `POST /mcp-servers` incluindo a credencial
  informada, exibe uma notificação de sucesso e redireciona o usuário
  para a página de detalhe do servidor recém-criado

#### Scenario: Campo de credencial exibido apenas quando exigido pelo tipo de autenticação
- **WHEN** o usuário seleciona `AuthType: BearerToken` no formulário de
  cadastro
- **THEN** a interface exibe o campo de credencial como obrigatório

#### Scenario: Campo de credencial ausente quando o tipo de autenticação é None
- **WHEN** o usuário seleciona `AuthType: None` no formulário de cadastro
- **THEN** a interface não exibe nenhum campo de credencial

#### Scenario: Cadastro rejeitado por validação do servidor
- **WHEN** o servidor responde com erro de validação (HTTP 400) por nome,
  URL, tipo de autenticação ou credencial ausentes ou inválidos
- **THEN** a interface exibe a mensagem de erro correspondente no campo
  do formulário associado, sem navegar para outra página

#### Scenario: Falha de rede ou do servidor ao cadastrar
- **WHEN** a chamada a `POST /mcp-servers` falha por um motivo diferente
  de validação (erro de rede ou erro do servidor)
- **THEN** a interface exibe uma notificação de erro genérica e mantém os
  dados já preenchidos no formulário

#### Scenario: Cancelar o cadastro
- **WHEN** o usuário aciona a ação "Cancelar" no formulário de cadastro,
  preenchido ou não
- **THEN** a interface navega para a listagem de servidores MCP
  (`/mcp-servers`) sem enviar nenhuma requisição a `POST /mcp-servers` e
  sem exibir nenhum diálogo de confirmação

### Requirement: Detalhe de servidor MCP
O sistema SHALL prover, em `apps/frontend`, uma página que exibe os dados
completos de um servidor MCP consumindo `GET /mcp-servers/{id}` — nome,
descrição, URL, tipo de autenticação e estado (`isActive`) — nunca
incluindo nenhum campo de credencial, com um link para editar o servidor
e uma ação para ativá-lo ou desativá-lo.

#### Scenario: Detalhe carregado com sucesso
- **WHEN** o usuário acessa a página de detalhe de um servidor MCP
  existente
- **THEN** a interface exibe nome, descrição, URL, tipo de autenticação e
  um indicador do estado (`isActive`) do servidor, um link para editá-lo
  e uma ação para ativá-lo ou desativá-lo, sem exibir nenhum campo de
  credencial

#### Scenario: Servidor MCP inexistente
- **WHEN** o usuário acessa a página de detalhe de um id que não
  corresponde a nenhum servidor MCP cadastrado (`GET /mcp-servers/{id}`
  responde 404)
- **THEN** a interface exibe um estado de "servidor não encontrado", sem
  quebrar a navegação do restante da aplicação

### Requirement: Edição de servidor MCP pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para editar o
nome, a descrição, a URL, o tipo de autenticação e a credencial de um
servidor MCP já cadastrado, pré-preenchido com os dados atuais do
servidor (exceto a credencial, que a API nunca retorna), enviando os
dados via `PUT /mcp-servers/{id}`, e uma ação para cancelar a edição e
voltar à página de detalhe sem enviar nenhuma requisição. Quando o campo
de credencial for exibido (tipo de autenticação diferente de `None`) e o
usuário submeter o formulário sem preenchê-lo, a interface SHALL enviá-lo
em branco, comunicando visualmente que isso mantém a credencial
atualmente persistida em vez de removê-la.

#### Scenario: Formulário de edição pré-preenchido, sem o campo de credencial preenchido
- **WHEN** o usuário acessa a página de edição de um servidor MCP
  existente com `AuthType: BearerToken`
- **THEN** a interface exibe o formulário com nome, descrição, URL e tipo
  de autenticação atuais já preenchidos, e o campo de credencial vazio
  com uma indicação de que deixá-lo em branco mantém a credencial atual

#### Scenario: Edição mantendo a credencial atual
- **WHEN** o usuário altera nome, descrição ou URL, mantém
  `AuthType: BearerToken` e deixa o campo de credencial em branco, e
  submete o formulário de edição
- **THEN** a interface envia `PUT /mcp-servers/{id}` sem o campo de
  credencial preenchido, exibe uma notificação de sucesso e navega de
  volta para a página de detalhe do servidor editado

#### Scenario: Edição trocando a credencial
- **WHEN** o usuário preenche uma nova credencial no formulário de edição
  de um servidor com `AuthType: BearerToken` e submete
- **THEN** a interface envia `PUT /mcp-servers/{id}` incluindo a nova
  credencial, exibe uma notificação de sucesso e navega de volta para a
  página de detalhe do servidor editado

#### Scenario: Trocar o tipo de autenticação para None oculta o campo de credencial
- **WHEN** o usuário, no formulário de edição de um servidor com
  `AuthType: BearerToken`, seleciona `AuthType: None`
- **THEN** a interface oculta o campo de credencial e não o envia em
  `PUT /mcp-servers/{id}`

#### Scenario: Edição rejeitada por validação do servidor
- **WHEN** o servidor responde com erro de validação (HTTP 400) por nome,
  URL, tipo de autenticação ou credencial ausentes ou inválidos —
  incluindo o caso de `AuthType` diferente de `None` sem nenhuma
  credencial jamais persistida para esse servidor
- **THEN** a interface exibe a mensagem de erro correspondente no campo
  do formulário associado, sem navegar para outra página

#### Scenario: Falha de rede ou do servidor ao editar
- **WHEN** a chamada a `PUT /mcp-servers/{id}` falha por um motivo
  diferente de validação (erro de rede ou erro do servidor)
- **THEN** a interface exibe uma notificação de erro genérica e mantém os
  dados já preenchidos no formulário, sem navegar para outra página

#### Scenario: Cancelar a edição
- **WHEN** o usuário aciona a ação "Cancelar" no formulário de edição,
  com ou sem alterações não salvas
- **THEN** a interface navega para a página de detalhe do servidor MCP
  (`/mcp-servers/{id}`) sem enviar nenhuma requisição a
  `PUT /mcp-servers/{id}` e sem exibir nenhum diálogo de confirmação

### Requirement: Ativação e desativação de servidor MCP pela interface
O sistema SHALL prover, em `apps/frontend`, ações para ativar e desativar
um servidor MCP cadastrado, consumindo `POST /mcp-servers/{id}/activate`
e `POST /mcp-servers/{id}/deactivate` a partir da página de detalhe.
Desativar um servidor MCP SHALL exigir confirmação explícita do usuário
antes de enviar a requisição; ativar SHALL NOT exigir confirmação.

#### Scenario: Ativar servidor MCP com sucesso, sem confirmação
- **WHEN** o usuário aciona a ação de ativar um servidor MCP inativo
- **THEN** a interface envia `POST /mcp-servers/{id}/activate`
  imediatamente, sem exibir nenhum diálogo de confirmação, exibe uma
  notificação de sucesso e atualiza o indicador de estado exibido para
  "ativo"

#### Scenario: Desativar servidor MCP exige confirmação antes de enviar a requisição
- **WHEN** o usuário aciona a ação de desativar um servidor MCP ativo
- **THEN** a interface exibe um diálogo de confirmação antes de enviar
  qualquer requisição, e `POST /mcp-servers/{id}/deactivate` só é enviado
  se o usuário confirmar explicitamente a ação nesse diálogo

#### Scenario: Cancelar a confirmação de desativação não envia a requisição
- **WHEN** o usuário aciona a ação de desativar um servidor MCP ativo e,
  no diálogo de confirmação exibido, escolhe cancelar
- **THEN** a interface fecha o diálogo sem enviar
  `POST /mcp-servers/{id}/deactivate` e o servidor permanece exibido como
  ativo

#### Scenario: Confirmar a desativação envia a requisição e atualiza o estado
- **WHEN** o usuário aciona a ação de desativar um servidor MCP ativo e,
  no diálogo de confirmação exibido, confirma a ação
- **THEN** a interface envia `POST /mcp-servers/{id}/deactivate`, exibe
  uma notificação de sucesso e atualiza o indicador de estado exibido
  para "inativo"

#### Scenario: Falha de rede ou do servidor ao ativar ou desativar
- **WHEN** a chamada a `POST /mcp-servers/{id}/activate` ou
  `POST /mcp-servers/{id}/deactivate` falha (erro de rede ou erro do
  servidor)
- **THEN** a interface exibe uma notificação de erro genérica e mantém o
  indicador de estado exibido igual ao estado anterior à tentativa

### Requirement: Teste de conexão exibido inline na interface
O sistema SHALL prover, em `apps/frontend`, uma ação "Testar conexão" em
dois contextos: no formulário de cadastro/edição, usando os valores
atuais do formulário via `POST /mcp-servers/test` (sem exigir que o
servidor já esteja salvo); e na página de detalhe de um servidor MCP já
cadastrado, via `POST /mcp-servers/{id}/test`. Em ambos os contextos, o
resultado do teste (sucesso ou falha, com o motivo) SHALL ser exibido
inline na própria tela, sem usar notificação (toast) nem diálogo modal.

#### Scenario: Teste de conexão bem-sucedido no formulário
- **WHEN** o usuário preenche URL e tipo de autenticação (e credencial,
  se aplicável) válidos no formulário e aciona "Testar conexão"
- **THEN** a interface envia `POST /mcp-servers/test` com os valores
  atuais do formulário e exibe, na própria tela, uma indicação inline de
  sucesso

#### Scenario: Teste de conexão com falha no formulário exibe o motivo
- **WHEN** o usuário aciona "Testar conexão" no formulário e a API
  responde indicando falha (host inalcançável, credencial rejeitada, ou
  erro de validação por campo ausente ou inválido)
- **THEN** a interface exibe, na própria tela, uma indicação inline de
  falha com o motivo retornado, sem impedir o usuário de ajustar os
  campos e testar novamente

#### Scenario: Teste de conexão bem-sucedido na página de detalhe
- **WHEN** o usuário aciona "Testar conexão" na página de detalhe de um
  servidor MCP já cadastrado
- **THEN** a interface envia `POST /mcp-servers/{id}/test` e exibe, na
  própria tela, uma indicação inline de sucesso

#### Scenario: Teste de conexão com falha na página de detalhe exibe o motivo
- **WHEN** o usuário aciona "Testar conexão" na página de detalhe e a API
  responde indicando falha
- **THEN** a interface exibe, na própria tela, uma indicação inline de
  falha com o motivo retornado

#### Scenario: Alterar um campo relevante após um teste limpa o resultado exibido
- **WHEN** o usuário altera a URL, o tipo de autenticação ou a credencial
  no formulário depois de já ter exibido um resultado de teste
- **THEN** a interface deixa de exibir o resultado do teste anterior, até
  que um novo teste seja executado para a configuração atual

#### Scenario: Nenhuma notificação (toast) nem diálogo modal é usado para o resultado do teste
- **WHEN** o usuário aciona "Testar conexão" em qualquer um dos dois
  contextos (formulário ou página de detalhe)
- **THEN** a interface não exibe o resultado como notificação (toast) nem
  como diálogo modal, apenas como conteúdo inline na própria tela
