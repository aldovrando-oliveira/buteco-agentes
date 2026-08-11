## Why

`apps/inbox` hoje trata `ChannelType` como um enum C# fechado
(`WhatsApp`/`Telegram`) e não tem nenhum ponto de extensão para validar
configuração específica de cada plataforma nem para entregar a resposta do
agente de volta ao canal de origem — `inbox-orquestrador-debounce` deixou
esse último ponto como Non-Goal explícito ("nenhuma abstração de
entrega/outbox... especulando sobre um formato que nenhum adapter real
ainda confirma"). Antes de integrar o primeiro canal real (WAHA, próxima
change), é preciso abrir esses dois pontos de plugin — tipo de canal
aberto e validado contra adapters de fato registrados, validação de
configuração por tipo, e entrega de resposta — provados por um adapter de
teste, sem nenhuma integração de rede real ainda.

## What Changes

- `ChannelType`: de enum C# fechado para identificador de string aberto,
  validado em runtime contra o registro DI (keyed) de adapters
  efetivamente registrados — `POST/PUT /channels` com um `channelType` sem
  adapter correspondente é rejeitado com HTTP 400. **BREAKING**: o shape
  de erro para `channelType` inválido muda de "lista fixa de valores
  aceitos" para "nenhum adapter registrado para este tipo" — o contrato
  HTTP (400) não muda, a mensagem sim.
- Novo contrato `IChannelConfigValidator`, resolvido por `ChannelType` via
  DI keyed: cada módulo de adapter registra o validador do próprio schema
  de credencial. `CreateChannelCommandHandler`/`UpdateChannelCommandHandler`
  chamam esse validador sobre a credencial em texto plano, antes de
  criptografar — substituindo a ausência atual de qualquer validação de
  formato. Nesta fatia, um validador de teste (schema fictício) prova o
  mecanismo.
- Novo contrato `IOutboundMessageSender`, resolvido por `ChannelType` via
  DI keyed: o endpoint receptor de push notification (hoje só loga e
  remove o `PendingDispatch`) passa a resolver o `Channel` de origem
  (join `Session → Contact → Channel`), decifrar a credencial e invocar o
  sender registrado para entregar a resposta do agente ao canal. Nesta
  fatia, um sender de teste apenas captura a chamada — sem integração de
  rede real.
- `ChannelResponse` ganha `webhookUrl`, computado a partir de `ChannelId`
  e da configuração `PublicUrl:BaseUrl` já existente em `apps/inbox`
  (reaproveitada de `inbox-orquestrador-debounce`, não uma configuração
  nova) — nunca persistido.

## Capabilities

### New Capabilities
- `inbox-channel-adapter-plugin`: contrato de extensão por tipo de canal —
  registro de adapters via DI keyed, validação de `ChannelType` contra
  adapters registrados, `IChannelConfigValidator` e
  `IOutboundMessageSender` como pontos de plugin, provados nesta fatia por
  implementações de teste (sem WAHA/Telegram reais).

### Modified Capabilities
- `inbox-channel-catalog`: `channelType` deixa de ser validado contra uma
  lista fechada (`WhatsApp`/`Telegram`) e passa a ser validado contra os
  adapters registrados; cadastro/atualização passam a chamar o validador
  de configuração do adapter correspondente antes de criptografar a
  credencial; consulta/listagem de canal passam a incluir `webhookUrl`.
- `inbox-message-orchestration`: o encerramento do disparo por push
  notification válida deixa de apenas remover o buffer — também entrega a
  resposta do agente ao canal de origem via o sender registrado para o
  `ChannelType` do `Channel` envolvido.

## Impact

- `apps/inbox` apenas (nenhuma mudança em `apps/api`/`apps/workers`/`apps/frontend`).
- Código afetado: `Channels/Entities/ChannelType.cs`, `Channels/Entities/Channel.cs`,
  `Channels/Endpoints/ChannelEndpoints.cs`,
  `Channels/Commands/CreateChannel/CreateChannelCommandHandler.cs`,
  `Channels/Commands/UpdateChannel/UpdateChannelCommandHandler.cs`,
  `Channels/Responses/ChannelResponse.cs`,
  `Orchestration/PushNotifications/Endpoints/PushNotificationEndpoints.cs`,
  `Program.cs` (novos registros DI keyed).
- Nenhuma migration de banco — `ChannelType` já é persistido como `text`
  (`HasConversion<string>()` desde `inbox-catalogo-canais`).
- Nenhuma mudança em `docker-compose.yml`, nenhuma UI.
