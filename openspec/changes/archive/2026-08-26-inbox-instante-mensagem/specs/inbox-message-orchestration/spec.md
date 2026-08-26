## MODIFIED Requirements

### Requirement: Disparo do debounce envia SendMessage real contra apps/api
O sistema SHALL, ao disparar um buffer de debounce, montar e enviar um
`SendMessage` real contra `apps/api`, usando o `AgentId` do `Channel` e o
`ContextId` da `Session` resolvida, incluindo uma configuração de push
notification que aponta para um endpoint receptor do próprio `apps/inbox`
com um token gerado para essa chamada específica. O sistema SHALL também
incluir, em `Message.Metadata` sob a chave `messageInstant`, o instante de
recebimento da mensagem mais recente do buffer (`PendingDispatch
.LastMessageAt`), serializado como string ISO 8601 com offset usando as
mesmas opções de serialização do restante do pipeline A2A
(`A2AJsonUtilities.DefaultOptions`) — nunca as opções padrão do
`JsonSerializer`.

#### Scenario: SendMessage disparado usa o AgentId do Channel e o ContextId da Session
- **WHEN** um buffer de debounce é disparado
- **THEN** o `SendMessage` enviado usa o `AgentId` do `Channel` de origem da
  sessão e o `ContextId` da `Session` resolvida, e inclui
  `pushNotificationConfig` com uma `url` do próprio `apps/inbox` e um token
  específico dessa chamada

#### Scenario: SendMessage disparado inclui o instante da última mensagem do buffer em Metadata
- **WHEN** um buffer de debounce contendo uma ou mais mensagens é
  disparado
- **THEN** o `Message` enviado inclui `Metadata["messageInstant"]` com o
  instante de recebimento da mensagem mais recente do buffer
  (`PendingDispatch.LastMessageAt`), formatado como string ISO 8601 com
  offset

O que acontece com esse valor depois de `apps/api` recebê-lo (persistência
no store durável, formato de fio) é responsabilidade de `apps/api`
receber a requisição — ver capability `a2a-task-lifecycle`, Requirement
"Task A2A nasce em submitted e a execução é delegada de forma
assíncrona".
