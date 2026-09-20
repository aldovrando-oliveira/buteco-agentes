## MODIFIED Requirements

### Requirement: Substituição do conjunto de delegações de saída de um agente
O sistema SHALL permitir, via `apps/api`, definir o conjunto completo de
agentes para os quais um agente delega (delegações de saída), substituindo
qualquer vínculo anterior pelo conjunto informado. A operação SHALL ser
idempotente quando o mesmo conjunto for enviado repetidamente.

O sistema SHALL rejeitar a operação por completo (nenhum vínculo do
payload aplicado) quando o agente Source (identificado na URL) não
existir, quando qualquer `TargetAgentId` do conjunto não corresponder a
nenhum agente cadastrado, quando o conjunto incluir o próprio agente
Source como `TargetAgentId` (auto-delegação), ou quando o conjunto fechar
um ciclo de delegação.

#### Scenario: Vincular delegações a um agente sem vínculo prévio
- **WHEN** um cliente envia `PUT /agents/{id}/delegations` com
  `targetAgentIds` cujos ids são todos de agentes existentes e diferentes
  do próprio `id`, para um agente que ainda não delega para nenhum outro
- **THEN** a API responde com HTTP 200 e o agente passa a ter todos os
  agentes informados como delegações de saída

#### Scenario: Substituir o conjunto de delegações por um conjunto diferente
- **WHEN** um cliente envia `PUT /agents/{id}/delegations` com
  `targetAgentIds` diferente do conjunto atualmente vinculado a um agente
- **THEN** a API remove as delegações que não estão na nova lista, adiciona
  as que estão presentes e ainda não existiam, e responde com HTTP 200 e o
  conjunto atualizado

#### Scenario: Remover todas as delegações de um agente
- **WHEN** um cliente envia `PUT /agents/{id}/delegations` com
  `targetAgentIds` vazio para um agente que tem delegações de saída
  cadastradas
- **THEN** a API remove todas as delegações existentes e responde com HTTP
  200 e o agente sem nenhuma delegação de saída

#### Scenario: Reenviar o mesmo conjunto é idempotente
- **WHEN** um cliente envia `PUT /agents/{id}/delegations` duas vezes
  seguidas com o mesmo `targetAgentIds`
- **THEN** ambas as chamadas respondem com HTTP 200 e o mesmo conjunto de
  delegações, sem erro na segunda chamada

#### Scenario: Delegar para um agente inexistente é rejeitado
- **WHEN** um cliente envia `PUT /agents/{id}/delegations` incluindo em
  `targetAgentIds` um id que não corresponde a nenhum agente cadastrado
- **THEN** a API responde com erro de validação (HTTP 400) e não altera
  nenhuma delegação existente do agente

#### Scenario: Vincular delegações a um agente Source inexistente retorna 404
- **WHEN** um cliente envia `PUT /agents/{id}/delegations` para um `id` de
  agente que não existe
- **THEN** a API responde com HTTP 404

#### Scenario: Auto-delegação é rejeitada
- **WHEN** um cliente envia `PUT /agents/{id}/delegations` incluindo o
  próprio `id` em `targetAgentIds`
- **THEN** a API responde com erro de validação (HTTP 400) informando que um
  agente não pode delegar para si mesmo, e não altera nenhuma delegação
  existente do agente

#### Scenario: Delegar para um agente Target inativo é permitido
- **WHEN** um cliente envia `PUT /agents/{id}/delegations` incluindo em
  `targetAgentIds` o id de um agente com `isActive: false`
- **THEN** a API cria a delegação normalmente, sem rejeitar por causa do
  estado inativo do agente Target

## ADDED Requirements

### Requirement: Recusa de ciclo de delegação no cadastro
O sistema SHALL rejeitar, em `apps/api`, qualquer operação de substituição do
conjunto de delegações de saída que resulte em um ciclo de delegação — isto é,
quando existir, a partir de algum `TargetAgentId` informado, um caminho de
delegações de qualquer comprimento que volte ao próprio agente Source.

A avaliação SHALL considerar o grafo de delegações **como ele ficaria depois da
substituição**: as delegações de saída atuais do agente Source são desprezadas e
as informadas no payload são consideradas no lugar delas. Uma operação que
desfaça um ciclo já existente SHALL ser aceita.

A rejeição SHALL ser atômica — nenhum vínculo do payload aplicado — e a resposta
SHALL identificar o caminho de delegações que fecha o ciclo, pelos nomes dos
agentes envolvidos.

O sistema SHALL continuar aceitando conjuntos de delegações que formem caminhos
múltiplos entre dois agentes sem fechar ciclo.

#### Scenario: Par bidirecional de delegações é rejeitado
- **WHEN** o agente A já delega para o agente B, e um cliente envia
  `PUT /agents/{B}/delegations` incluindo o agente A em `targetAgentIds`
- **THEN** a API responde com erro de validação (HTTP 400) e não cria a
  delegação de B para A

#### Scenario: Ciclo indireto de três agentes é rejeitado
- **WHEN** o agente A já delega para o agente B, o agente B já delega para o
  agente C, e um cliente envia `PUT /agents/{C}/delegations` incluindo o agente
  A em `targetAgentIds`
- **THEN** a API responde com erro de validação (HTTP 400) e não cria a
  delegação de C para A

#### Scenario: Ciclo de quatro saltos é rejeitado
- **WHEN** o agente A delega para B, B delega para C, C delega para D, e um
  cliente envia `PUT /agents/{D}/delegations` incluindo o agente A em
  `targetAgentIds`
- **THEN** a API responde com erro de validação (HTTP 400) e não cria a
  delegação de D para A

#### Scenario: A mensagem de rejeição nomeia o caminho do ciclo
- **WHEN** um cliente envia um conjunto de delegações que fecha um ciclo
  passando por mais de um agente intermediário
- **THEN** a resposta HTTP 400 apresenta, sob `targetAgentIds`, o caminho de
  delegações que fecha o ciclo com os **nomes** dos agentes que o compõem, do
  agente Source de volta a ele mesmo

#### Scenario: A rejeição por ciclo não altera as delegações existentes
- **WHEN** um agente com delegações de saída já cadastradas recebe um
  `PUT /agents/{id}/delegations` cujo conjunto fecha um ciclo
- **THEN** a API responde com HTTP 400 e uma consulta subsequente ao agente
  devolve exatamente o conjunto de delegações que ele tinha antes da chamada

#### Scenario: Caminhos múltiplos sem ciclo continuam permitidos
- **WHEN** o agente A delega para B e para C, o agente B delega para D, e um
  cliente envia `PUT /agents/{C}/delegations` incluindo o agente D em
  `targetAgentIds`
- **THEN** a API responde com HTTP 200 e cria a delegação de C para D, porque
  nenhum caminho volta ao agente Source

#### Scenario: Desfazer um ciclo já existente é aceito
- **WHEN** o grafo de delegações já contém um ciclo envolvendo o agente A, e um
  cliente envia `PUT /agents/{A}/delegations` com um conjunto que não fecha mais
  o ciclo
- **THEN** a API responde com HTTP 200 e aplica o novo conjunto, em vez de
  rejeitar a operação por causa do ciclo que ela desfaz

## REMOVED Requirements

### Requirement: Nenhuma detecção de ciclo ou de vínculo bidirecional no cadastro
**Reason**: O mecanismo de dano foi medido e é inerente ao desenho da execução de
delegação, não à contagem de processos. Um ciclo `A→B→…→A` autotrava no advisory
lock de contexto: a task do agente Source em profundidade 0 segura
`pg_advisory_lock(hashtext(agente), hashtext(contexto))` enquanto espera o
Target, e a task do mesmo agente em profundidade 2 bloqueia no mesmo lock.
Medido na exploração `replicas-de-worker` com quatro instâncias de worker — o
travamento não é resolvido por réplica. O `DelegationDepthLimit` (5) não cobre o
caso, porque a checagem de profundidade roda antes da aquisição do lock e um
ciclo de dois saltos trava em profundidade 2.

**Migration**: O cadastro de ciclo passa a ser recusado com HTTP 400 pelo
requisito "Recusa de ciclo de delegação no cadastro". Vínculos de ciclo já
persistidos não são apagados por esta mudança e continuam sendo lidos
normalmente pela execução; eles são desfeitos editando qualquer um dos agentes
do ciclo e removendo a delegação que o fecha, o que continua sendo aceito. Não
há migration de banco: a detecção é validação de cadastro sobre a tabela
`agent_delegations` existente.
