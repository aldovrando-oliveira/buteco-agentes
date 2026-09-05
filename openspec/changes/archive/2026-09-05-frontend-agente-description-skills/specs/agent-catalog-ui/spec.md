## MODIFIED Requirements

### Requirement: Cadastro de agente pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para cadastrar um
agente informando nome, instruções (system prompt), provider, model,
descrição (opcional) e skills (lista opcional de pares nome + descrição
opcional), com as opções de provider e model obtidas de `GET /providers` e
as opções de model restritas ao provider selecionado, enviando os dados via
`POST /agents` — incluindo sempre os campos `description` (nulo quando em
branco) e `skills` (lista vazia quando nenhuma) — e uma ação para cancelar
o cadastro e voltar à listagem de agentes sem enviar nenhuma requisição.
Quando `GET /providers` não retorna nenhum provider configurado, a
interface SHALL exibir uma mensagem explicando a causa em vez do
formulário.

#### Scenario: Cadastro com sucesso
- **WHEN** o usuário preenche nome, instruções, provider e model válidos e
  submete o formulário
- **THEN** a interface envia `POST /agents`, exibe uma notificação de
  sucesso e redireciona o usuário para a página de detalhe do agente
  recém-criado

#### Scenario: Cadastro com descrição e skills
- **WHEN** o usuário preenche uma descrição e adiciona uma ou mais skills
  (cada uma com nome e, opcionalmente, descrição) e submete o formulário
- **THEN** a interface envia `POST /agents` com `description` contendo o
  texto informado e `skills` contendo cada skill com seu `name` e sua
  `description` (nula quando não informada)

#### Scenario: Cadastro sem descrição nem skills envia os campos vazios explicitamente
- **WHEN** o usuário submete o formulário sem preencher descrição e sem
  adicionar nenhuma skill
- **THEN** a interface envia `POST /agents` com `description: null` e
  `skills: []`, sem omitir nenhum dos dois campos

#### Scenario: Nome de skill é obrigatório
- **WHEN** o usuário adiciona uma skill, deixa o nome em branco e submete
  o formulário
- **THEN** a interface exibe uma mensagem de erro de validação na linha
  dessa skill e não envia `POST /agents`

#### Scenario: Remover uma skill antes de submeter
- **WHEN** o usuário adiciona uma skill e aciona a ação de remover dessa
  linha
- **THEN** a interface remove a linha e o submit seguinte não inclui essa
  skill em `skills`

#### Scenario: Cadastro rejeitado por validação do servidor
- **WHEN** o servidor responde com erro de validação (HTTP 400) por nome,
  instruções, provider, model ou nome de skill ausentes ou inválidos
- **THEN** a interface exibe a mensagem de erro correspondente no campo do
  formulário associado — para erros com chave `skills[i].name`, na linha
  da skill de índice `i` — sem navegar para outra página

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
ausentes, a descrição do agente (ou uma indicação explícita de que não há
descrição), a lista de skills do agente (nome e, quando houver, descrição
de cada uma; ou uma indicação explícita de que não há skills), um resumo
dos servidores MCP vinculados ao agente (`agent.mcpServers`), com um link
para editar o agente, um link para a página de gestão do vínculo com
servidores MCP (`/agents/{id}/mcp-servers`) e uma ação para ativá-lo ou
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
- **THEN** a interface exibe nome, descrição, instruções, skills, as datas
  de criação e atualização, um indicador do estado (`isActive`) do agente,
  o provider e o model configurados, um resumo dos servidores MCP
  vinculados, um link para editar o agente, um link para a página de
  gestão do vínculo com servidores MCP e uma ação para ativá-lo ou
  desativá-lo

#### Scenario: Descrição exibida quando presente
- **WHEN** o agente exibido tem `description` não nula
- **THEN** a interface exibe o texto da descrição junto ao nome do agente

#### Scenario: Ausência de descrição indicada explicitamente
- **WHEN** o agente exibido tem `description` nula
- **THEN** a interface exibe uma indicação de que o agente não tem
  descrição, em vez de um espaço vazio

#### Scenario: Skills listadas com nome e descrição
- **WHEN** o agente exibido tem uma ou mais entradas em `skills`
- **THEN** a interface exibe o nome de cada skill e, para as que têm
  `description` não nula, também a descrição

#### Scenario: Ausência de skills indicada explicitamente
- **WHEN** o agente exibido tem `skills` vazio
- **THEN** a interface exibe uma indicação de que nenhuma skill foi
  declarada, em vez de uma lista vazia sem explicação

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

#### Scenario: Resumo lista os nomes dos servidores MCP vinculados
- **WHEN** o agente exibido tem um ou mais servidores em `mcpServers`
- **THEN** a interface exibe o nome de cada servidor MCP vinculado no
  resumo da página de detalhe

#### Scenario: Resumo indica ausência de vínculo quando nenhum servidor MCP está vinculado
- **WHEN** o agente exibido tem `mcpServers` vazio
- **THEN** a interface exibe uma indicação de que nenhum servidor MCP está
  vinculado, em vez de uma lista vazia sem explicação

#### Scenario: Link para a página de gestão do vínculo sempre presente
- **WHEN** a página de detalhe de um agente é renderizada, com ou sem
  servidores MCP vinculados
- **THEN** a interface exibe um link para `/agents/{id}/mcp-servers`

### Requirement: Edição de agente pela interface
O sistema SHALL prover, em `apps/frontend`, um formulário para editar o
nome, as instruções (system prompt), o provider, o model, a descrição e as
skills de um agente já cadastrado, pré-preenchido com os dados atuais do
agente (incluindo descrição e skills), com as opções de provider e model
obtidas de `GET /providers` e as opções de model restritas ao provider
selecionado, enviando os dados via `PUT /agents/{id}` — incluindo sempre
os campos `description` e `skills`, de forma que salvar sem alterá-los
preserve os valores atuais do agente — e uma ação para cancelar a edição e
voltar à página de detalhe do agente sem enviar nenhuma requisição. Quando
o provider ou o model atuais do agente não estão entre as opções
disponíveis em `GET /providers`, a interface SHALL exibir o valor atual
como uma opção informativa e não re-selecionável, em vez de omiti-lo
silenciosamente ou apresentar o campo vazio. Quando `GET /providers` não
retorna nenhum provider configurado, a interface SHALL exibir uma mensagem
explicando a causa em vez do formulário.

#### Scenario: Formulário de edição pré-preenchido com os dados atuais
- **WHEN** o usuário acessa a página de edição de um agente existente cujo
  provider e model constam entre as opções disponíveis
- **THEN** a interface exibe o formulário com o nome, as instruções, o
  provider, o model, a descrição e as skills atuais do agente já
  preenchidos

#### Scenario: Salvar sem alterar descrição e skills preserva os valores atuais
- **WHEN** o usuário acessa a página de edição de um agente que tem
  descrição e skills cadastradas, altera apenas outro campo (por exemplo,
  o nome) e submete o formulário
- **THEN** a interface envia `PUT /agents/{id}` com `description` e
  `skills` iguais aos valores carregados do agente, sem omiti-los nem
  enviá-los vazios

#### Scenario: Limpar a descrição ou remover todas as skills na edição
- **WHEN** o usuário apaga o texto da descrição e/ou remove todas as
  skills de um agente que as tinha, e submete o formulário
- **THEN** a interface envia `PUT /agents/{id}` com `description: null`
  e/ou `skills: []`, explicitamente

#### Scenario: Edição com sucesso
- **WHEN** o usuário altera nome, instruções, provider, model, descrição
  e/ou skills para valores válidos e submete o formulário de edição
- **THEN** a interface envia `PUT /agents/{id}`, exibe uma notificação de
  sucesso e navega de volta para a página de detalhe do agente editado

#### Scenario: Edição rejeitada por validação do servidor
- **WHEN** o servidor responde com erro de validação (HTTP 400) por nome,
  instruções, provider, model ou nome de skill ausentes ou inválidos
- **THEN** a interface exibe a mensagem de erro correspondente no campo do
  formulário associado — para erros com chave `skills[i].name`, na linha
  da skill de índice `i` — sem navegar para outra página

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
