## ADDED Requirements

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
