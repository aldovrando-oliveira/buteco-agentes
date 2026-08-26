## MODIFIED Requirements

### Requirement: Disparo do debounce envia SendMessage real contra apps/api
O sistema SHALL, ao disparar um buffer de debounce, montar e enviar um
`SendMessage` real contra `apps/api`, usando o `AgentId` do `Channel` e o
`ContextId` da `Session` resolvida, incluindo uma configuração de push
notification que aponta para um endpoint receptor do próprio `apps/inbox`
com um token gerado para essa chamada específica. O sistema SHALL também
incluir, em `Message.Metadata`:
- sob a chave `messageInstant`, o instante de recebimento da mensagem
  mais recente do buffer (`PendingDispatch.LastMessageAt`);
- sob a chave `channelType`, o valor de `Channel.ChannelType` da sessão
  de origem, sem transformação;
- sob a chave `contactExternalId`, o valor de `Contact.ExternalId` da
  sessão de origem, sem transformação;

cada um serializado como valor escalar (string) usando as mesmas opções
de serialização do restante do pipeline A2A
(`A2AJsonUtilities.DefaultOptions`) — nunca as opções padrão do
`JsonSerializer`, e nunca agrupados num único valor objeto.

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

#### Scenario: SendMessage disparado inclui o tipo do canal em Metadata
- **WHEN** um buffer de debounce é disparado
- **THEN** o `Message` enviado inclui `Metadata["channelType"]` com o
  valor de `Channel.ChannelType` da sessão de origem, como string escalar
  sem transformação

#### Scenario: SendMessage disparado inclui o identificador externo do contato em Metadata
- **WHEN** um buffer de debounce é disparado
- **THEN** o `Message` enviado inclui `Metadata["contactExternalId"]` com
  o valor de `Contact.ExternalId` da sessão de origem, como string
  escalar sem transformação

#### Scenario: channelType e contactExternalId são gravados como chaves escalares separadas, nunca agrupadas
- **WHEN** um buffer de debounce é disparado
- **THEN** `Metadata["channelType"]` e `Metadata["contactExternalId"]`
  são dois valores `JsonElement` do tipo string cada, e nenhuma chave
  agrupa os dois num objeto único
