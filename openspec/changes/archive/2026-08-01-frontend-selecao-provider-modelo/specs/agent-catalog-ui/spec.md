## MODIFIED Requirements

### Requirement: Listagem de agentes na interface
O sistema SHALL prover, em `apps/frontend`, uma página que lista os agentes
cadastrados consumindo `GET /agents`, com link para o detalhe de cada
agente, um indicador do estado (`isActive`) de cada agente, o provider e o
model configurados para cada agente, um indicador de que o agente precisa
de reconfiguração quando o provider ou o model estão ausentes, e um botão
para iniciar o cadastro de um novo agente.

#### Scenario: Lista carregada com sucesso
- **WHEN** o usuário acessa a página de agentes e existem agentes
  cadastrados
- **THEN** a interface exibe o nome de cada agente com um link para sua
  página de detalhe, um indicador visual do estado (ativo ou inativo) de
  cada agente, o provider e o model configurados para cada agente, e um
  botão para cadastrar um novo agente

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

#### Scenario: Indicador de reconfiguração exibido para agente sem provider ou model
- **WHEN** a lista inclui um agente com `provider` nulo ou `model` nulo
- **THEN** a interface exibe, na linha desse agente, um indicador visual
  de que o agente precisa de reconfiguração

#### Scenario: Indicador de reconfiguração ausente para agente com provider e model configurados
- **WHEN** a lista inclui um agente com `provider` e `model` preenchidos
- **THEN** a interface não exibe o indicador de reconfiguração na linha
  desse agente

### Requirement: Cadastro de agente pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para cadastrar um
agente informando nome, instruções (system prompt), provider e model, com
as opções de provider e model obtidas de `GET /providers` e as opções de
model restritas ao provider selecionado, enviando os dados via
`POST /agents`, e uma ação para cancelar o cadastro e voltar à listagem de
agentes sem enviar nenhuma requisição. Quando `GET /providers` não retorna
nenhum provider configurado, a interface SHALL exibir uma mensagem
explicando a causa em vez do formulário.

#### Scenario: Cadastro com sucesso
- **WHEN** o usuário preenche nome, instruções, provider e model válidos e
  submete o formulário
- **THEN** a interface envia `POST /agents`, exibe uma notificação de
  sucesso e redireciona o usuário para a página de detalhe do agente
  recém-criado

#### Scenario: Cadastro rejeitado por validação do servidor
- **WHEN** o servidor responde com erro de validação (HTTP 400) por nome,
  instruções, provider ou model ausentes ou inválidos
- **THEN** a interface exibe a mensagem de erro correspondente no campo do
  formulário associado, sem navegar para outra página

#### Scenario: Falha de rede ou do servidor ao cadastrar
- **WHEN** a chamada a `POST /agents` falha por um motivo diferente de
  validação (erro de rede ou erro do servidor)
- **THEN** a interface exibe uma notificação de erro genérica e mantém os
  dados já preenchidos no formulário

#### Scenario: Cancelar o cadastro
- **WHEN** o usuário aciona a ação "Cancelar" no formulário de cadastro,
  preenchido ou não
- **THEN** a interface navega para a listagem de agentes (`/agents`) sem
  enviar nenhuma requisição a `POST /agents` e sem exibir nenhum diálogo de
  confirmação

#### Scenario: Seleção de model restrita ao provider escolhido
- **WHEN** o usuário seleciona um provider no formulário de cadastro
- **THEN** a interface exibe, no campo de model, apenas os models
  disponíveis para o provider selecionado

#### Scenario: Trocar o provider reseta o model selecionado
- **WHEN** o usuário já selecionou um model e então seleciona um provider
  diferente
- **THEN** a interface limpa a seleção de model

#### Scenario: Provider e model são obrigatórios
- **WHEN** o usuário submete o formulário de cadastro sem selecionar
  provider ou sem selecionar model
- **THEN** a interface exibe uma mensagem de erro de validação no campo
  correspondente e não envia `POST /agents`

#### Scenario: Nenhum provider configurado bloqueia o cadastro
- **WHEN** o usuário acessa a página de cadastro de agente e `GET /providers`
  retorna uma lista vazia
- **THEN** a interface exibe uma mensagem explicando que nenhum provider de
  LLM está configurado, em vez do formulário de cadastro

### Requirement: Detalhe de agente
O sistema SHALL prover, em `apps/frontend`, uma página que exibe os dados
completos de um agente consumindo `GET /agents/{id}`, incluindo seu estado
(`isActive`), o provider e o model configurados, um indicador de que o
agente precisa de reconfiguração quando o provider ou o model estão
ausentes, com um link para editar o agente e uma ação para ativá-lo ou
desativá-lo. O bloco com o link de edição e a ação de ativar/desativar
SHALL ser exibido antes, na ordem do documento, dos dados do agente (nome,
instruções, datas de criação e atualização). O campo de instruções SHALL
ser renderizado interpretando sua sintaxe markdown (títulos, listas,
negrito, tabelas, texto riscado, `---` como separador) como formatação
real, dentro de um container que usa o espaço vertical disponível da
viewport (sem exceder esse espaço) e que nunca fica menor que uma altura
mínima utilizável, com rolagem interna própria quando o conteúdo excede o
espaço disponível — de forma que um conteúdo de instruções longo não faça
a página inteira crescer indefinidamente, nem fique menor do que o
necessário para ser lido confortavelmente.

#### Scenario: Detalhe carregado com sucesso
- **WHEN** o usuário acessa a página de detalhe de um agente existente
- **THEN** a interface exibe nome, instruções, as datas de criação e
  atualização, um indicador do estado (`isActive`) do agente, o provider e
  o model configurados, um link para editar o agente e uma ação para
  ativá-lo ou desativá-lo

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

#### Scenario: Bloco de ações exibido antes dos dados do agente
- **WHEN** a página de detalhe de um agente é renderizada
- **THEN** o link para editar e a ação de ativar/desativar aparecem, na
  ordem do documento, antes do nome, das instruções e das datas do agente

#### Scenario: Instruções renderizadas como markdown formatado
- **WHEN** o campo `instructions` do agente contém sintaxe markdown (por
  exemplo, um título iniciado com `#`, texto em `**negrito**`, ou um item
  de lista iniciado com `-`)
- **THEN** a interface renderiza esses elementos como formatação real
  (por exemplo, o título vira um elemento de heading real), em vez de
  exibir a sintaxe markdown como texto literal

#### Scenario: Bloco de instruções usa o espaço vertical disponível, com rolagem quando excede
- **WHEN** o conteúdo renderizado das instruções excede o espaço vertical
  disponível na viewport para o container de instruções
- **THEN** a interface exibe uma rolagem interna própria desse container,
  sem que o restante da página cresça além da altura necessária para o
  conteúdo

#### Scenario: Bloco de instruções mantém altura mínima utilizável
- **WHEN** o espaço vertical disponível na viewport para o container de
  instruções é pequeno (ex.: janela do navegador baixa ou nível de zoom
  alto)
- **THEN** a interface mantém uma altura mínima para o container de
  instruções, mesmo que isso exija rolagem da página para ver o restante
  do conteúdo abaixo dele

#### Scenario: Indicador de reconfiguração exibido quando provider ou model ausentes
- **WHEN** o agente exibido tem `provider` nulo ou `model` nulo
- **THEN** a interface exibe um indicador visual de que o agente precisa
  de reconfiguração

#### Scenario: Indicador de reconfiguração ausente quando provider e model configurados
- **WHEN** o agente exibido tem `provider` e `model` preenchidos
- **THEN** a interface não exibe o indicador de reconfiguração

### Requirement: Edição de agente pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para editar o
nome, as instruções (system prompt), o provider e o model de um agente já
cadastrado, pré-preenchido com os dados atuais do agente, com as opções de
provider e model obtidas de `GET /providers` e as opções de model
restritas ao provider selecionado, enviando os dados via
`PUT /agents/{id}`, e uma ação para cancelar a edição e voltar à página de
detalhe do agente sem enviar nenhuma requisição. Quando o provider ou o
model atuais do agente não estão entre as opções disponíveis em
`GET /providers`, a interface SHALL exibir o valor atual como uma opção
informativa e não re-selecionável, em vez de omiti-lo silenciosamente ou
apresentar o campo vazio. Quando `GET /providers` não retorna nenhum
provider configurado, a interface SHALL exibir uma mensagem explicando a
causa em vez do formulário.

#### Scenario: Formulário de edição pré-preenchido com os dados atuais
- **WHEN** o usuário acessa a página de edição de um agente existente cujo
  provider e model constam entre as opções disponíveis
- **THEN** a interface exibe o formulário com o nome, as instruções, o
  provider e o model atuais do agente já selecionados

#### Scenario: Edição com sucesso
- **WHEN** o usuário altera nome, instruções, provider e/ou model para
  valores válidos e submete o formulário de edição
- **THEN** a interface envia `PUT /agents/{id}`, exibe uma notificação de
  sucesso e navega de volta para a página de detalhe do agente editado

#### Scenario: Edição rejeitada por validação do servidor
- **WHEN** o servidor responde com erro de validação (HTTP 400) por nome,
  instruções, provider ou model ausentes ou inválidos
- **THEN** a interface exibe a mensagem de erro correspondente no campo do
  formulário associado, sem navegar para outra página

#### Scenario: Falha de rede ou do servidor ao editar
- **WHEN** a chamada a `PUT /agents/{id}` falha por um motivo diferente de
  validação (erro de rede ou erro do servidor)
- **THEN** a interface exibe uma notificação de erro genérica e mantém os
  dados já preenchidos no formulário, sem navegar para outra página

#### Scenario: Cancelar a edição
- **WHEN** o usuário aciona a ação "Cancelar" no formulário de edição,
  com ou sem alterações não salvas
- **THEN** a interface navega para a página de detalhe do agente
  (`/agents/{id}`) sem enviar nenhuma requisição a `PUT /agents/{id}` e
  sem exibir nenhum diálogo de confirmação

#### Scenario: Seleção de model restrita ao provider escolhido
- **WHEN** o usuário seleciona um provider diferente no formulário de
  edição
- **THEN** a interface exibe, no campo de model, apenas os models
  disponíveis para o provider selecionado

#### Scenario: Trocar o provider reseta o model selecionado, exceto no carregamento inicial
- **WHEN** o usuário, depois que o formulário já está carregado, seleciona
  um provider diferente do que estava selecionado
- **THEN** a interface limpa a seleção de model, sem limpar nenhum outro
  campo do formulário

#### Scenario: Provider e model são obrigatórios
- **WHEN** o usuário submete o formulário de edição sem provider
  selecionado ou sem model selecionado
- **THEN** a interface exibe uma mensagem de erro de validação no campo
  correspondente e não envia `PUT /agents/{id}`

#### Scenario: Provider atual do agente fora das opções disponíveis é exibido como opção informativa e não re-selecionável
- **WHEN** o usuário acessa a página de edição de um agente cujo `provider`
  não consta na lista retornada por `GET /providers`
- **THEN** a interface exibe o provider atual do agente como o valor
  selecionado no campo de provider, sinaliza visualmente que ele não está
  disponível, e não permite selecioná-lo novamente caso o usuário troque
  para outro provider e queira voltar

#### Scenario: Model atual do agente fora das opções disponíveis é exibido como opção informativa e não re-selecionável
- **WHEN** o usuário acessa a página de edição de um agente cujo `model`
  não consta entre os models disponíveis para o provider atual do agente
- **THEN** a interface exibe o model atual do agente como o valor
  selecionado no campo de model, sinaliza visualmente que ele não está
  disponível, e não permite selecioná-lo novamente caso o usuário troque
  para outro model e queira voltar

#### Scenario: Nenhum provider configurado bloqueia a edição
- **WHEN** o usuário acessa a página de edição de um agente e
  `GET /providers` retorna uma lista vazia
- **THEN** a interface exibe uma mensagem explicando que nenhum provider de
  LLM está configurado, em vez do formulário de edição
