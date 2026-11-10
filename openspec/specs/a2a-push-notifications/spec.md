# a2a-push-notifications Specification

## Purpose

TBD - defined by change a2a-push-notifications. Update Purpose after archive.
## Requirements
### Requirement: Registro de push notification config embutido no SendMessage
O sistema SHALL aceitar um `pushNotificationConfig` opcional dentro do campo
`configuration` de um `SendMessage`, sem exigir nenhuma chamada JSON-RPC
separada para registrá-lo.

#### Scenario: SendMessage com pushNotificationConfig é aceito normalmente
- **WHEN** um cliente envia `SendMessage` incluindo `configuration.pushNotificationConfig`
  com uma `url` válida
- **THEN** a task é criada normalmente em `submitted`, exatamente como uma
  requisição sem `pushNotificationConfig`

#### Scenario: SendMessage sem pushNotificationConfig não registra nenhum webhook
- **WHEN** um cliente envia `SendMessage` sem `configuration.pushNotificationConfig`
- **THEN** a task é criada normalmente e nenhum webhook é disparado quando
  ela atingir um estado terminal

### Requirement: Push notification config persistido pelo worker
O sistema SHALL, em `apps/workers`, persistir o `pushNotificationConfig`
recebido em `AgentTask.Metadata` ao transicionar a task para um estado
terminal que o worker processa (`completed` ou `failed`), codificado no
mesmo formato de fio que o restante do payload A2A persistido — camelCase
e sem campos opcionais ausentes serializados como `null`.

#### Scenario: Metadata contém o config após completed
- **WHEN** uma task com `pushNotificationConfig` registrado é processada
  com sucesso pelo worker
- **THEN** a task consultável via `GetTask` tem `Metadata` contendo o
  `pushNotificationConfig` registrado

#### Scenario: Metadata contém o config após failed
- **WHEN** uma task com `pushNotificationConfig` registrado falha durante
  o processamento pelo worker
- **THEN** a task consultável via `GetTask` tem `Metadata` contendo o
  `pushNotificationConfig` registrado

#### Scenario: Metadata persistida segue o formato de fio da spec A2A
- **WHEN** uma task com `pushNotificationConfig` registrado (incluindo
  `url` e, quando presentes, `token`/`authentication`) atinge um estado
  terminal processado pelo worker
- **THEN** o `pushNotificationConfig` dentro de `Metadata`, consultável
  via `GetTask`/`ListTasks`, usa os nomes de campo em camelCase (`url`,
  `token`, `authentication`, `id`) e omite qualquer campo opcional não
  fornecido — nunca grava esse campo como propriedade com valor `null`

### Requirement: Webhook disparado ao final do processamento pelo worker
O sistema SHALL, em `apps/workers`, disparar uma requisição HTTP `POST`
para a `url` do `pushNotificationConfig` registrado, com a `AgentTask`
completa como corpo, quando a task transicionar para `completed` ou
`failed` — e somente nesse caso.

#### Scenario: Task completed com push config dispara o webhook
- **WHEN** uma task com `pushNotificationConfig` registrado é concluída
  com sucesso
- **THEN** uma requisição `POST` é feita para a `url` registrada, com a
  `AgentTask` completa (incluindo estado `completed` e artifacts) como
  corpo

#### Scenario: Task failed com push config dispara o webhook
- **WHEN** uma task com `pushNotificationConfig` registrado falha durante
  o processamento
- **THEN** uma requisição `POST` é feita para a `url` registrada, com a
  `AgentTask` completa (incluindo estado `failed`) como corpo

#### Scenario: Task sem push config nunca dispara nenhuma chamada
- **WHEN** uma task sem `pushNotificationConfig` registrado atinge
  `completed` ou `failed`
- **THEN** nenhuma requisição HTTP de notificação é feita

#### Scenario: Task rejeitada nunca dispara o webhook
- **WHEN** uma task é rejeitada de forma síncrona (agente inativo, sem
  `provider`/`model`, ou `provider` indisponível) antes de chegar a um
  worker
- **THEN** nenhuma requisição HTTP de notificação é feita, independente do
  que o cliente tenha enviado em `configuration.pushNotificationConfig`

### Requirement: Falha do webhook não afeta a conclusão da task
O sistema SHALL garantir que uma falha na chamada de webhook (URL
inalcançável, timeout, resposta não-2xx) não impeça nem atrase a conclusão
da task além do timeout configurado para a chamada.

#### Scenario: Webhook retorna 404 — task permanece completed
- **WHEN** a `url` registrada responde com `404 Not Found` à chamada de
  notificação
- **THEN** a task processada continua com seu estado terminal real
  (`completed` ou `failed`) inalterado no store durável

#### Scenario: Webhook não responde — task não fica presa esperando
- **WHEN** a `url` registrada não responde dentro do timeout configurado
  para a chamada de notificação
- **THEN** o processamento da task não é bloqueado além desse timeout, e a
  task já está com seu estado terminal persistido no store durável antes
  mesmo da tentativa de notificação

### Requirement: Autenticação do webhook
O sistema SHALL incluir um header `Authorization` na chamada de webhook
quando `pushNotificationConfig.authentication` estiver presente, e um
header `X-A2A-Notification-Token` quando `pushNotificationConfig.token`
estiver presente — os dois podem coexistir na mesma chamada.

#### Scenario: Authentication presente resulta em header Authorization
- **WHEN** o `pushNotificationConfig` registrado tem `authentication` com
  `scheme` e `credentials`
- **THEN** a requisição de notificação inclui um header `Authorization`
  com o valor `{scheme} {credentials}`

#### Scenario: Token presente resulta em header X-A2A-Notification-Token
- **WHEN** o `pushNotificationConfig` registrado tem `token` preenchido
- **THEN** a requisição de notificação inclui um header
  `X-A2A-Notification-Token` com o valor do `token`

#### Scenario: Nenhum dado de autenticação presente
- **WHEN** o `pushNotificationConfig` registrado não tem `authentication`
  nem `token`
- **THEN** a requisição de notificação é feita sem os headers
  `Authorization`/`X-A2A-Notification-Token`

### Requirement: O push da aplicação externa depende da operação notificação push
Para o subject `application`, um `SendMessage` com `configuration.pushNotificationConfig` SHALL exigir as operações `enviar-mensagem` e `notificacao-push` no vínculo com o agente (`external-application-agents`); sem a segunda, a chamada inteira SHALL receber `403` com o corpo vazio, sem task e sem job, em vez de ignorar o `pushNotificationConfig`. A operação SHALL cobrir só o registro: a entrega do webhook, feita pelo `apps/workers` no estado terminal, SHALL NOT reconferir o vínculo, e uma task criada com o push liberado SHALL ter o webhook entregue mesmo que o Admin retire a operação ou o agente depois. Os métodos dedicados de push config continuam fora do contrato e, para a aplicação, recebem `403` (`a2a-task-lifecycle`).

#### Scenario: Push sem a operação
- **WHEN** uma aplicação com só `enviar-mensagem` no agente A manda `SendMessage` com `configuration.pushNotificationConfig`
- **THEN** a resposta é `403`, nenhuma task é criada e nenhum job é publicado

#### Scenario: Push com a operação
- **WHEN** uma aplicação com `enviar-mensagem` e `notificacao-push` no agente A manda `SendMessage` com `configuration.pushNotificationConfig`
- **THEN** a task é criada em `submitted`, e o job publicado leva o `pushNotificationConfig`

#### Scenario: Retirar a operação não desfaz a entrega já registrada
- **WHEN** a task de uma aplicação foi criada com push liberado e, antes de ela terminar, o Admin retira `notificacao-push` do vínculo
- **THEN** o webhook é chamado quando a task atinge o estado terminal

#### Scenario: Push fora de configuration não passa
- **WHEN** uma aplicação com só `enviar-mensagem` manda `SendMessage` com `pushNotificationConfig` dentro de `params.message`, e não em `params.configuration`
- **THEN** a task é criada e o job publicado não leva `pushNotificationConfig`: o que a checagem deixou passar sem a operação é o que o SDK executou, sem webhook

