# agent-catalog-ui Specification

## Purpose

TBD - defined by change frontend-cadastro-agentes. Update Purpose after archive.

## Requirements

### Requirement: Listagem de agentes na interface
O sistema SHALL prover, em `apps/frontend`, uma página que lista os agentes
cadastrados consumindo `GET /agents`, exibindo para cada agente o nome com
link para o detalhe, a descrição, o provider e o model configurados, um
resumo das ferramentas vinculadas (quantidade de servidores MCP e
quantidade total de tools permitidas), os agentes para os quais ele
delega, e um indicador do estado (`isActive`), além de um indicador de que
o agente precisa de reconfiguração quando o provider ou o model estão
ausentes e de um botão para iniciar o cadastro de um novo agente. A página
SHALL exibir a quantidade de agentes cadastrados, um campo de busca por
nome ou descrição e um controle de filtro por estado (todos, ativos,
inativos e os que precisam de reconfiguração), ambos aplicados no cliente
sobre a coleção já carregada, e SHALL sinalizar, na linha de cada agente,
quantos dos seus servidores MCP estão vinculados sem nenhuma tool
permitida.

#### Scenario: Lista carregada com sucesso
- **WHEN** o usuário acessa a página de agentes e existem agentes
  cadastrados
- **THEN** a interface exibe, para cada agente, o nome com link para sua
  página de detalhe, a descrição, o provider e o model configurados, o
  resumo das ferramentas vinculadas, os agentes-alvo de delegação e um
  indicador visual do estado (ativo ou inativo), além da quantidade de
  agentes cadastrados e de um botão para cadastrar um novo agente

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

#### Scenario: Descrição exibida na linha do agente
- **WHEN** a lista inclui um agente com `description` não nula
- **THEN** a interface exibe a descrição desse agente na sua linha, junto
  do nome

#### Scenario: Resumo de ferramentas exibido por agente
- **WHEN** a lista inclui um agente com um ou mais servidores MCP em
  `mcpServers`
- **THEN** a interface exibe, na linha desse agente, quantos servidores
  estão vinculados e quantas tools estão permitidas no total

#### Scenario: Agente sem nenhum servidor MCP vinculado
- **WHEN** a lista inclui um agente com `mcpServers` vazio
- **THEN** a interface indica, na linha desse agente, que nenhum servidor
  está vinculado, em vez de exibir contagens zeradas sem explicação

#### Scenario: Aviso de servidor vinculado sem nenhuma tool na listagem
- **WHEN** a lista inclui um agente que tem um ou mais servidores MCP
  vinculados com a lista de tools permitidas vazia
- **THEN** a interface sinaliza, na linha desse agente, quantos servidores
  estão vinculados a ele sem nenhuma tool

#### Scenario: Agentes-alvo de delegação exibidos por agente
- **WHEN** a lista inclui um agente com um ou mais agentes em
  `delegatesTo`
- **THEN** a interface exibe, na linha desse agente, os nomes dos agentes
  para os quais ele delega

#### Scenario: Agente sem nenhuma delegação de saída
- **WHEN** a lista inclui um agente com `delegatesTo` vazio
- **THEN** a interface indica, na linha desse agente, que não há
  delegação, em vez de deixar a célula vazia sem explicação

#### Scenario: Busca por nome ou descrição
- **WHEN** o usuário digita um termo no campo de busca da listagem de
  agentes
- **THEN** a interface exibe apenas os agentes cujo nome ou descrição
  contém aquele termo, sem enviar nenhuma requisição nova

#### Scenario: Busca ignora maiúsculas e acentuação
- **WHEN** o usuário busca por um termo sem acentuação ou com caixa
  diferente da cadastrada (por exemplo, "cobranca" para um agente chamado
  "Cobrança")
- **THEN** a interface encontra esse agente normalmente

#### Scenario: Filtro por estado do agente
- **WHEN** o usuário seleciona o filtro de ativos, de inativos, ou o dos
  que precisam de reconfiguração
- **THEN** a interface exibe apenas os agentes correspondentes àquele
  estado, e o filtro de todos volta a exibir a lista inteira

#### Scenario: Busca e filtro aplicados em conjunto
- **WHEN** o usuário digita um termo de busca e também seleciona um filtro
  por estado
- **THEN** a interface exibe apenas os agentes que satisfazem as duas
  condições ao mesmo tempo

#### Scenario: Nenhum agente corresponde à busca ou ao filtro
- **WHEN** existem agentes cadastrados, mas nenhum corresponde ao termo
  buscado ou ao filtro selecionado
- **THEN** a interface indica que nenhum agente corresponde à busca, com
  uma mensagem distinta da usada quando não há nenhum agente cadastrado

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
de cada uma; ou uma indicação explícita de que não há skills), as
instruções e as datas de criação e atualização, com um link para editar o
agente e uma ação para ativá-lo ou desativá-lo. O nome, o estado, a
descrição, o link de edição e a ação de ativar/desativar SHALL ser
exibidos em um cabeçalho comum a todas as abas da página (ver requisito
"Abas do detalhe do agente"), e o cabeçalho SHALL vir antes, na ordem do
documento, dos dados exibidos nas abas. A página SHALL NOT exibir link
para nenhuma página separada de gestão do vínculo com servidores MCP, nem
resumo textual dos servidores vinculados, porque essa informação passa a
ser responsabilidade da aba de ferramentas. O campo de instruções SHALL
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
- **THEN** a interface exibe nome, descrição, um indicador do estado
  (`isActive`) do agente, um link para editar o agente e uma ação para
  ativá-lo ou desativá-lo no cabeçalho, e exibe instruções, skills, o
  provider e o model configurados e as datas de criação e atualização na
  aba de visão geral

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

#### Scenario: Cabeçalho exibido antes do conteúdo das abas
- **WHEN** a página de detalhe de um agente é renderizada
- **THEN** o nome, o link para editar e a ação de ativar/desativar
  aparecem, na ordem do documento, antes do conteúdo da aba ativa

#### Scenario: Nenhum link para página separada de vínculo com servidores MCP
- **WHEN** a página de detalhe de um agente é renderizada, com ou sem
  servidores MCP vinculados
- **THEN** a interface não exibe nenhum link para uma página separada de
  gestão do vínculo com servidores MCP

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

### Requirement: Abas do detalhe do agente

O sistema SHALL organizar o conteúdo da página de detalhe do agente em três
abas — visão geral, ferramentas e delegações — exibindo, nas duas últimas,
um contador com a quantidade de servidores MCP vinculados e de agentes-alvo
de delegação, oculto quando a quantidade é zero. A aba ativa SHALL ser
refletida na URL, de forma que o endereço seja compartilhável e sobreviva a
um recarregamento da página. Apenas o conteúdo da aba ativa SHALL estar
presente na página.

#### Scenario: Três abas exibidas no detalhe do agente
- **WHEN** o usuário acessa a página de detalhe de um agente existente
- **THEN** a interface exibe as abas de visão geral, ferramentas e
  delegações, com a visão geral ativa

#### Scenario: Contadores refletem os vínculos do agente
- **WHEN** o agente exibido tem um ou mais servidores MCP em `mcpServers`
  ou um ou mais agentes em `delegatesTo`
- **THEN** a interface exibe, junto do rótulo da aba correspondente, a
  quantidade de itens de cada vínculo

#### Scenario: Contador oculto quando o vínculo está vazio
- **WHEN** o agente exibido tem `mcpServers` vazio ou `delegatesTo` vazio
- **THEN** a interface não exibe contador junto do rótulo da aba
  correspondente

#### Scenario: Aba ativa refletida na URL
- **WHEN** o usuário aciona a aba de ferramentas ou a de delegações
- **THEN** a URL passa a identificar a aba ativa, e recarregar a página
  nesse endereço reabre a mesma aba

#### Scenario: Endereço sem identificação de aba abre a visão geral
- **WHEN** o usuário acessa a página de detalhe do agente sem nenhuma aba
  identificada na URL
- **THEN** a interface exibe a aba de visão geral, sem alterar o endereço

#### Scenario: Identificação de aba desconhecida abre a visão geral
- **WHEN** o usuário acessa a página de detalhe do agente com uma
  identificação de aba que não corresponde a nenhuma das três
- **THEN** a interface exibe a aba de visão geral, sem quebrar a página e
  sem exibir erro

#### Scenario: Conteúdo da aba inativa não está presente na página
- **WHEN** o usuário está em uma das abas do detalhe do agente
- **THEN** o conteúdo das outras abas não está presente na página, e
  passa a existir apenas quando a aba correspondente é acionada
