## ADDED Requirements

### Requirement: SendMessage para agente inativo rejeita a task sem publicar job
O sistema SHALL, ao receber `SendMessage` na rota A2A de um agente
cadastrado (`/agents/{id}/a2a`) cujo estado seja inativo (`isActive =
false`), transicionar a task para o estado `rejected` do protocolo A2A,
sem publicar nenhum job de execução no RabbitMQ e sem inventar um código
de erro HTTP próprio para esse caso.

#### Scenario: SendMessage para agente inativo é rejeitado
- **WHEN** um cliente envia `SendMessage` válido para a rota A2A de um
  agente cadastrado que está com `isActive = false`
- **THEN** a API responde com a task no estado `TASK_STATE_REJECTED` e
  nenhuma mensagem referenciando essa task é publicada no RabbitMQ

#### Scenario: Task rejeitada continua consultável via GetTask
- **WHEN** um `SendMessage` para um agente inativo resulta em task
  rejeitada
- **THEN** uma consulta subsequente via `GetTask` para o mesmo `taskId`
  responde com HTTP 200 e estado `TASK_STATE_REJECTED`, em vez de "task
  não encontrada"

#### Scenario: SendMessage para agente reativado volta ao fluxo normal
- **WHEN** um agente foi desativado e depois reativado (`isActive =
  true`), e um cliente envia `SendMessage` para a rota A2A desse agente
- **THEN** a API cria a task em `submitted` e publica o job de execução no
  RabbitMQ normalmente, mesmo que o `A2AServer` desse agente já tivesse
  sido resolvido antes da desativação
