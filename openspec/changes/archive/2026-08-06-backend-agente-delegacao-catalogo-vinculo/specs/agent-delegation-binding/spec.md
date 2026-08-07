## ADDED Requirements

### Requirement: Substituição do conjunto de delegações de saída de um agente
O sistema SHALL permitir, via `apps/api`, definir o conjunto completo de
agentes para os quais um agente delega (delegações de saída), substituindo
qualquer vínculo anterior pelo conjunto informado. A operação SHALL ser
idempotente quando o mesmo conjunto for enviado repetidamente.

O sistema SHALL rejeitar a operação por completo (nenhum vínculo do
payload aplicado) quando o agente Source (identificado na URL) não
existir, quando qualquer `TargetAgentId` do conjunto não corresponder a
nenhum agente cadastrado, ou quando o conjunto incluir o próprio agente
Source como `TargetAgentId` (auto-delegação).

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
- **THEN** a API responde com erro de validação (HTTP 400) e não altera
  nenhuma delegação existente do agente

#### Scenario: Delegar para um agente Target inativo é permitido
- **WHEN** um cliente envia `PUT /agents/{id}/delegations` incluindo em
  `targetAgentIds` o id de um agente com `isActive: false`
- **THEN** a API cria a delegação normalmente, sem rejeitar por causa do
  estado inativo do agente Target

### Requirement: Nenhuma detecção de ciclo ou de vínculo bidirecional no cadastro
O sistema SHALL permitir cadastrar delegações que, em conjunto, formem um
ciclo indireto (A→B→C→A) ou um par bidirecional (A→B e B→A cadastrados
separadamente), sem rejeitar a operação por causa disso. Cada vínculo
individual continua sujeito apenas às validações desta capability (Source
existente, Target existente, sem auto-delegação).

#### Scenario: Cadastrar um ciclo indireto de delegações é permitido
- **WHEN** um cliente cadastra, em chamadas separadas de
  `PUT /agents/{id}/delegations`, o agente A delegando para B, B delegando
  para C, e C delegando para A
- **THEN** todas as três chamadas respondem com HTTP 200, e o conjunto de
  delegações resultante reflete os três vínculos, sem nenhuma rejeitada
  por formar um ciclo

#### Scenario: Cadastrar um par bidirecional de delegações é permitido
- **WHEN** um cliente cadastra o agente A delegando para B via
  `PUT /agents/{id}/delegations`, e em seguida cadastra o agente B
  delegando para A da mesma forma
- **THEN** ambas as chamadas respondem com HTTP 200, e cada agente reflete
  a delegação de saída para o outro

### Requirement: Persistência durável do vínculo de delegação entre agentes
O sistema SHALL persistir o vínculo de delegação entre agentes em
PostgreSQL, sem uso de armazenamento em memória, garantindo que os dados
sobrevivam a reinícios da aplicação.

#### Scenario: Vínculo sobrevive a restart da API
- **WHEN** um agente é vinculado como delegação de saída de outro e o
  processo de `apps/api` é reiniciado
- **THEN** uma consulta subsequente que retorne as delegações de saída
  desse agente continua refletindo o mesmo vínculo
