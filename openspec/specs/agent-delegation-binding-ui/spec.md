# agent-delegation-binding-ui Specification

## Purpose

TBD - defined by change frontend-agente-delegacoes-secao. Update Purpose after archive.

## Requirements

### Requirement: Seção de gestão de delegações de saída em AgentDetailPage

O sistema SHALL prover, em `apps/frontend`, dentro da página de detalhe
do agente (`AgentDetailPage`), uma seção sempre editável que permite
visualizar e substituir o conjunto de delegações de saída do agente,
usando como opções o catálogo completo de agentes já disponível via
`useAgentsQuery()` (mesma fonte de `AgentListPage`), sem nenhuma query
nova nem página/rota própria.

#### Scenario: Seção carregada com sucesso
- **WHEN** o usuário acessa a página de detalhe de um agente existente
- **THEN** a interface exibe, na própria página, uma seção com um
  controle de seleção múltipla contendo todos os agentes do catálogo
  exceto o próprio agente, uma ação para salvar as delegações e uma
  ação para cancelar

#### Scenario: Catálogo de agentes com um único agente cadastrado
- **WHEN** o usuário acessa a página de detalhe de um agente e ele é o
  único agente cadastrado no catálogo
- **THEN** a interface exibe a seção com o controle de seleção múltipla
  sem nenhuma opção disponível, sem quebrar a página

### Requirement: Pré-seleção a partir do vínculo atual

O sistema SHALL pré-selecionar, ao carregar a seção, os agentes
presentes em `agent.delegatesTo` (retornado por `GET /agents/{id}`).

#### Scenario: Agentes já delegados aparecem pré-selecionados
- **WHEN** o usuário acessa a página de detalhe de um agente cujo
  `delegatesTo` já inclui um ou mais agentes
- **THEN** a interface exibe cada um desses agentes já selecionado no
  controle de seleção múltipla, sem exigir nenhuma ação do usuário

#### Scenario: Agente sem nenhuma delegação de saída
- **WHEN** o usuário acessa a página de detalhe de um agente cujo
  `delegatesTo` é uma lista vazia
- **THEN** a interface exibe o controle de seleção múltipla sem nenhuma
  opção pré-selecionada

### Requirement: Exclusão do próprio agente das opções de delegação

O sistema SHALL excluir o próprio agente (o agente cuja página de
detalhe está sendo exibida) da lista de opções do controle de seleção
múltipla, mesmo que ele conste no catálogo completo de agentes.

#### Scenario: Agente atual ausente das opções
- **WHEN** o usuário acessa a página de detalhe de um agente
- **THEN** o próprio agente não aparece como opção selecionável no
  controle de seleção múltipla, independentemente de quantos outros
  agentes existam no catálogo

### Requirement: Indicador de agente inativo nas opções de delegação

O sistema SHALL exibir, para cada agente inativo (`isActive: false`)
listado como opção no controle de seleção múltipla, um indicador visual
de que ele está inativo, mantendo-o selecionável.

#### Scenario: Agente inativo aparece como opção com indicador
- **WHEN** o catálogo de agentes inclui um agente com `isActive: false`
  que não é o próprio agente da página
- **THEN** a interface exibe esse agente como opção selecionável no
  controle de seleção múltipla, com um indicador visual de que está
  inativo

#### Scenario: Selecionar um agente inativo como delegação é permitido
- **WHEN** o usuário seleciona, no controle de seleção múltipla, um
  agente com `isActive: false`
- **THEN** a interface o inclui normalmente no conjunto de delegações a
  ser salvo, sem rejeitar a seleção nem exibir aviso

### Requirement: Nenhuma detecção de ciclo de delegação na interface

O sistema SHALL permitir que o usuário selecione, no controle de seleção
múltipla, qualquer combinação de agentes como delegações de saída,
incluindo combinações que formem um ciclo indireto (A→B→C→A) ou um par
bidirecional (A→B e B→A) quando consideradas em conjunto com delegações
já cadastradas em outros agentes — sem detectar, avisar ou bloquear
esse caso na interface.

#### Scenario: Selecionar uma delegação que fecha um ciclo indireto é permitido
- **WHEN** o agente B já delega para o agente C, o agente C já delega
  para o agente A, e o usuário, na página de detalhe do agente A,
  seleciona o agente B como delegação de saída
- **THEN** a interface permite a seleção e o submit normalmente, sem
  exibir nenhum aviso sobre o ciclo resultante (A→B→C→A)

### Requirement: Seleção e desseleção de agentes-alvo

O usuário SHALL poder selecionar e desselecionar cada agente-alvo
independentemente no controle de seleção múltipla, com o estado local
refletindo a seleção antes de qualquer submit.

#### Scenario: Selecionar um agente-alvo
- **WHEN** o usuário marca um agente ainda não selecionado no controle
  de seleção múltipla
- **THEN** a interface passa a considerá-lo parte do conjunto de
  delegações a ser salvo

#### Scenario: Desselecionar um agente-alvo
- **WHEN** o usuário desmarca um agente previamente selecionado no
  controle de seleção múltipla
- **THEN** a interface deixa de considerá-lo parte do conjunto de
  delegações a ser salvo

### Requirement: Submit do conjunto de delegações via PUT /agents/{id}/delegations

Ao acionar "Salvar delegações", a interface SHALL enviar `PUT
/agents/{id}/delegations` com `{ targetAgentIds: [...] }`, contendo o id
de cada agente selecionado no controle de seleção múltipla — nunca
omitindo o campo `targetAgentIds` nem enviando-o como `null`. Em caso de
sucesso, a interface SHALL exibir uma notificação de sucesso e manter o
usuário na página de detalhe do agente, refletindo o conjunto salvo.

#### Scenario: Submit com um ou mais agentes selecionados
- **WHEN** o usuário aciona "Salvar delegações" com um ou mais agentes
  selecionados
- **THEN** a interface envia `PUT /agents/{id}/delegations` com
  `targetAgentIds` contendo o id de cada agente selecionado e exibe uma
  notificação de sucesso

#### Scenario: Submit sem nenhum agente selecionado remove todas as delegações
- **WHEN** o usuário aciona "Salvar delegações" sem nenhum agente
  selecionado (incluindo o caso de um agente que já tinha delegações e
  todas foram desmarcadas)
- **THEN** a interface envia `PUT /agents/{id}/delegations` com
  `targetAgentIds: []` e exibe uma notificação de sucesso

### Requirement: Cancelar restaura a seleção original sem enviar requisição

Ao acionar "Cancelar", a interface SHALL restaurar o controle de seleção
múltipla para o estado de `agent.delegatesTo` mais recente carregado,
descartando qualquer seleção ou desseleção feita pelo usuário, sem
enviar nenhuma requisição a `PUT /agents/{id}/delegations`.

#### Scenario: Cancelar descarta alterações não salvas
- **WHEN** o usuário altera a seleção do controle (marca ou desmarca
  agentes) e aciona "Cancelar" antes de salvar
- **THEN** a interface restaura a seleção para o conjunto original de
  `agent.delegatesTo`, sem enviar nenhuma requisição a `PUT
  /agents/{id}/delegations`

### Requirement: Erro de submit exibido via notificação genérica

O sistema SHALL exibir, quando `PUT /agents/{id}/delegations` falhar,
uma notificação de erro genérica, sem interromper a renderização do
restante da página de detalhe do agente nem exigir recarregamento.

#### Scenario: Falha no submit não quebra a página
- **WHEN** o usuário aciona "Salvar delegações" e `PUT
  /agents/{id}/delegations` responde com erro
- **THEN** a interface exibe uma notificação de erro genérica, mantém a
  seleção atual do controle e mantém o restante da página de detalhe do
  agente renderizado normalmente
