## RENAMED Requirements

- FROM: `### Requirement: Seção de gestão de delegações de saída em AgentDetailPage`
- TO: `### Requirement: Aba Delegações no detalhe do agente`

- FROM: `### Requirement: Cancelar restaura a seleção original sem enviar requisição`
- TO: `### Requirement: Descartar restaura a seleção original sem enviar requisição`

## MODIFIED Requirements

### Requirement: Aba Delegações no detalhe do agente

O sistema SHALL prover, em `apps/frontend`, uma aba **Delegações** dentro
da página de detalhe do agente que permite visualizar e substituir o
conjunto de delegações de saída do agente, listando como opções o catálogo
completo de agentes já disponível via `useAgentsQuery()` (mesma fonte da
listagem de agentes), sem nenhuma query nova nem página própria. A aba
SHALL exibir a lista de agentes-alvo como linhas selecionáveis
individualmente, com um resumo da quantidade de agentes-alvo escolhidos.

#### Scenario: Aba carregada com sucesso
- **WHEN** o usuário abre a aba Delegações no detalhe de um agente
  existente
- **THEN** a interface exibe uma lista com todos os agentes do catálogo
  exceto o próprio agente, cada um com um controle de seleção próprio, e
  um resumo da quantidade de agentes-alvo selecionados

#### Scenario: Catálogo de agentes com um único agente cadastrado
- **WHEN** o usuário abre a aba Delegações de um agente e ele é o único
  agente cadastrado no catálogo
- **THEN** a interface exibe a aba indicando que não há nenhum outro
  agente disponível para delegação, sem quebrar a página

### Requirement: Pré-seleção a partir do vínculo atual

O sistema SHALL pré-selecionar, ao abrir a aba Delegações, os agentes
presentes em `agent.delegatesTo` (retornado por `GET /agents/{id}`).

#### Scenario: Agentes já delegados aparecem pré-selecionados
- **WHEN** o usuário abre a aba Delegações de um agente cujo
  `delegatesTo` já inclui um ou mais agentes
- **THEN** a interface exibe cada um desses agentes já selecionado na
  lista, sem exigir nenhuma ação do usuário

#### Scenario: Agente sem nenhuma delegação de saída
- **WHEN** o usuário abre a aba Delegações de um agente cujo
  `delegatesTo` é uma lista vazia
- **THEN** a interface exibe a lista sem nenhum agente selecionado

### Requirement: Indicador de agente inativo nas opções de delegação

O sistema SHALL exibir, para cada agente listado como opção de delegação,
o modelo configurado desse agente, e, quando o agente está inativo
(`isActive: false`), um indicador visual de inatividade no lugar do
modelo, mantendo-o selecionável.

#### Scenario: Agente inativo aparece como opção com indicador
- **WHEN** o catálogo de agentes inclui um agente com `isActive: false`
  que não é o próprio agente da página
- **THEN** a interface exibe esse agente como opção selecionável na lista,
  com um indicador visual de que está inativo

#### Scenario: Agente ativo exibe o modelo configurado
- **WHEN** o catálogo de agentes inclui um agente ativo com `model`
  preenchido
- **THEN** a interface exibe o modelo desse agente na linha
  correspondente

#### Scenario: Selecionar um agente inativo como delegação é permitido
- **WHEN** o usuário seleciona, na lista, um agente com `isActive: false`
- **THEN** a interface o inclui normalmente no conjunto de delegações a
  ser salvo, sem rejeitar a seleção nem exibir aviso

### Requirement: Descartar restaura a seleção original sem enviar requisição

A interface SHALL restaurar, ao acionar a ação de descartar na barra de
alterações não salvas, a seleção para o estado de `agent.delegatesTo` mais
recente carregado, descartando qualquer seleção ou desseleção feita pelo
usuário, sem enviar nenhuma requisição a `PUT /agents/{id}/delegations`.

#### Scenario: Descartar abandona alterações não salvas
- **WHEN** o usuário altera a seleção da lista (marca ou desmarca
  agentes) e aciona a ação de descartar antes de salvar
- **THEN** a interface restaura a seleção para o conjunto original de
  `agent.delegatesTo`, sem enviar nenhuma requisição a
  `PUT /agents/{id}/delegations`, e a barra de alterações não salvas
  deixa de ser exibida

## ADDED Requirements

### Requirement: Busca por nome na lista de agentes-alvo

O sistema SHALL prover, na aba Delegações, um campo de busca que filtra a
lista de agentes-alvo pelo nome, sem afetar a seleção já feita nem enviar
nenhuma requisição.

#### Scenario: Buscar filtra a lista pelo nome
- **WHEN** o usuário digita um trecho de nome no campo de busca
- **THEN** a interface exibe apenas os agentes cujo nome contém esse
  trecho, ignorando diferença entre maiúsculas e minúsculas

#### Scenario: Busca sem correspondência
- **WHEN** o texto buscado não corresponde ao nome de nenhum agente do
  catálogo
- **THEN** a interface indica que nenhum agente corresponde à busca, em
  vez de exibir uma lista vazia sem explicação

#### Scenario: Filtrar não altera a seleção
- **WHEN** o usuário seleciona um agente, digita uma busca que o exclui da
  lista exibida e depois limpa a busca
- **THEN** o agente selecionado continua selecionado, e o conjunto a ser
  salvo permanece inalterado durante a filtragem

### Requirement: Barra de alterações não salvas nas delegações

O sistema SHALL exibir, na aba Delegações, uma barra fixa de alterações
não salvas somente enquanto a seleção diferir do conjunto salvo em
`agent.delegatesTo`, com uma ação para descartar as alterações e uma ação
para salvá-las. A comparação SHALL ignorar a ordem em que os agentes foram
selecionados.

#### Scenario: Sem alterações, a barra não aparece
- **WHEN** o usuário abre a aba Delegações e não altera a seleção
- **THEN** a interface não exibe a barra de alterações não salvas

#### Scenario: Alterar a seleção faz a barra aparecer
- **WHEN** o usuário marca ou desmarca um agente-alvo, deixando a seleção
  diferente de `agent.delegatesTo`
- **THEN** a interface exibe a barra de alterações não salvas, com uma
  ação para descartar e uma ação para salvar

#### Scenario: Desfazer manualmente as alterações faz a barra sumir
- **WHEN** o usuário altera a seleção e depois a devolve, por ações
  próprias, exatamente ao conjunto salvo do agente
- **THEN** a interface deixa de exibir a barra de alterações não salvas

### Requirement: Aviso ao sair com alterações não salvas nas delegações

O sistema SHALL avisar o usuário antes de descartar uma seleção de
delegações não salva, quando ele tenta trocar de aba, sair da página de
detalhe do agente ou fechar a janela, oferecendo a opção de permanecer
editando.

#### Scenario: Trocar de aba com alterações não salvas pede confirmação
- **WHEN** o usuário tem alterações não salvas na aba Delegações e aciona
  outra aba do detalhe do agente
- **THEN** a interface exibe um pedido de confirmação antes de trocar de
  aba, informando que as alterações serão descartadas

#### Scenario: Cancelar a confirmação mantém a seleção e a aba
- **WHEN** o usuário, no pedido de confirmação, escolhe continuar editando
- **THEN** a interface permanece na aba Delegações com a seleção intacta

#### Scenario: Sem alterações, a navegação não é interrompida
- **WHEN** o usuário não tem alterações não salvas e troca de aba ou sai
  da página
- **THEN** a interface navega diretamente, sem exibir nenhum pedido de
  confirmação
