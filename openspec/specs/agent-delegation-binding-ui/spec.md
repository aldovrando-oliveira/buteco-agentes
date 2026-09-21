# agent-delegation-binding-ui Specification

## Purpose

TBD - defined by change frontend-agente-delegacoes-secao. Update Purpose after archive.

## Requirements

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

### Requirement: Nenhuma detecção de ciclo de delegação na interface

O sistema SHALL permitir que o usuário selecione, na lista de agentes-alvo,
qualquer combinação de agentes como delegações de saída, incluindo combinações
que formem um ciclo indireto (A→B→C→A) ou um par bidirecional (A→B e B→A)
quando consideradas em conjunto com delegações já cadastradas em outros agentes
— **sem detectar, avisar ou bloquear esse caso antes do submit**.

A detecção de ciclo é do servidor, e a interface SHALL **não** reimplementá-la:
ela não conhece o grafo completo de delegações de todos os agentes, e duplicar a
regra criaria uma segunda fonte de verdade que divergiria na primeira mudança
dessa regra.

Exibir a recusa que o servidor devolveu **não** é detecção na interface, e é
obrigação do requisito de erro de submit acima.

#### Scenario: Selecionar uma delegação que fecha um ciclo indireto é permitido
- **WHEN** o agente B já delega para o agente C, o agente C já delega
  para o agente A, e o usuário, na página de detalhe do agente A,
  seleciona o agente B como delegação de saída
- **THEN** a interface permite a seleção e o submit normalmente, sem
  exibir nenhum aviso sobre o ciclo resultante antes da resposta do servidor

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

### Requirement: Erro de submit exibido via notificação genérica

O sistema SHALL distinguir, quando `PUT /agents/{id}/delegations` falhar,
**recusa permanente de falha transitória**, sem interromper a renderização do
restante da página de detalhe do agente nem exigir recarregamento.

Quando a resposta for **400 com uma mensagem de validação sob o campo
`targetAgentIds`**, a interface SHALL exibir **a mensagem que a API enviou**, em
um aviso que **permanece na tela** até a próxima tentativa ou o descarte, e
SHALL **não** exibir a notificação genérica nem instruir a tentar de novo —
essa recusa é permanente, e mandar repetir a operação afirmaria mais do que o
sistema sabe.

Para **qualquer outra falha** — indisponibilidade de rede, erro de servidor, ou
resposta sem mensagem sob aquele campo — a interface SHALL exibir a notificação
de erro genérica, com a instrução de tentar de novo, que ali está correta.

A interface SHALL exibir a mensagem da API **como ela veio**, sem interpretá-la,
sem extrair partes dela e sem sugerir qual vínculo remover — o sistema conhece o
caminho que a recusa nomeia, não a preferência do operador sobre qual aresta
desfazer.

O aviso de recusa SHALL desaparecer quando a operação seguinte tiver sucesso ou
quando o operador descartar as alterações, para que a tela não continue afirmando
uma recusa que já não vale.

O aviso SHALL **permanecer enquanto o operador edita a seleção**, inclusive
depois de ele já ter desmarcado o agente que fechava o ciclo. Não é omissão: o
caminho nomeado na recusa é a única informação que diz **qual aresta remover**, e
um aviso que sumisse ao primeiro clique desapareceria exatamente no instante em
que passa a ser útil. A limpeza acontece no submit seguinte, não na edição.

#### Scenario: Falha no submit não quebra a página
- **WHEN** o usuário aciona "Salvar delegações" e `PUT
  /agents/{id}/delegations` responde com erro
- **THEN** a interface mantém a seleção atual do controle e mantém o restante
  da página de detalhe do agente renderizado normalmente

#### Scenario: Recusa por ciclo exibe o caminho que a API enviou
- **WHEN** o usuário aciona "Salvar delegações" e `PUT
  /agents/{id}/delegations` responde `400` com uma mensagem sob
  `targetAgentIds` nomeando o caminho do ciclo entre agentes
- **THEN** a interface exibe essa mensagem, com o caminho, em um aviso que
  permanece na tela

#### Scenario: Recusa permanente não instrui a tentar de novo
- **WHEN** a interface exibe uma recusa recebida como `400` sob
  `targetAgentIds`
- **THEN** a interface **não** exibe a notificação de erro genérica nem
  qualquer instrução de repetir a operação

#### Scenario: Outra recusa de validação da mesma rota também é exibida
- **WHEN** o usuário aciona "Salvar delegações" e a rota responde `400` sob
  `targetAgentIds` por um motivo que não é ciclo — auto-delegação, ou id que
  não corresponde a agente cadastrado
- **THEN** a interface exibe a mensagem enviada pela API, pelo mesmo caminho da
  recusa por ciclo, sem tratamento específico por motivo

#### Scenario: Falha transitória mantém a notificação genérica
- **WHEN** o usuário aciona "Salvar delegações" e a requisição falha por
  indisponibilidade de rede ou erro de servidor
- **THEN** a interface exibe a notificação de erro genérica, com a instrução de
  tentar de novo, e não exibe nenhum aviso de recusa permanente

#### Scenario: Sucesso seguinte remove o aviso de recusa
- **WHEN** a interface está exibindo um aviso de recusa e o usuário corrige a
  seleção e salva com sucesso
- **THEN** o aviso de recusa desaparece e a interface exibe a notificação de
  sucesso

#### Scenario: Descartar remove o aviso de recusa
- **WHEN** a interface está exibindo um aviso de recusa e o usuário descarta as
  alterações
- **THEN** o aviso de recusa desaparece e a seleção volta à original

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
