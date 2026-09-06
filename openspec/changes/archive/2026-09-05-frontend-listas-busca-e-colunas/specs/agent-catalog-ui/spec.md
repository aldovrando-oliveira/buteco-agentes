## MODIFIED Requirements

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
