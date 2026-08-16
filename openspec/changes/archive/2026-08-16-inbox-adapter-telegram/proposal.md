## Why

O catálogo de canais de `apps/inbox` tem hoje um único adapter real, o WAHA
(`inbox-adapter-waha`), que provou os três contratos de plugin
(`IChannelConfigValidator`, `IOutboundMessageSender`,
`IInboundWebhookHandler`) mas deixou a configuração do lado externo
(sessão WAHA, webhook) como passo manual — decisão explícita naquela
change, motivada por uma ambiguidade real de escopo de credencial (a API de
sessões do WAHA costuma exigir uma API key de admin, potencialmente
distinta da usada para enviar mensagens). O Telegram Bot API não tem essa
ambiguidade: um único token por bot autentica toda chamada, incluindo
`setWebhook`, então o cadastro de um canal Telegram pode configurar o
webhook automaticamente, sem o passo manual que o WAHA exige. Esta fatia
prova os três contratos existentes pela segunda vez com uma plataforma
diferente e fecha, para este adapter, dois riscos que o WAHA aceitou
conscientemente: nenhuma verificação de autenticidade do webhook de entrada
(o Telegram oferece `secret_token` nativo) e nenhum sinal de erro quando a
configuração externa falha (a automação faz o cadastro falhar em vez de
criar um canal que nunca vai receber mensagens).

## What Changes

- Terceira via de prova dos três contratos de plugin existentes —
  `TelegramChannelConfigValidator`, `TelegramOutboundMessageSender`,
  `TelegramInboundWebhookHandler` — registrados sob `ChannelType`
  `"telegram"`, mesmo padrão de `AddKeyedSingleton` do WAHA.
- Novo, quarto contrato de plugin, **opcional** (ao contrário dos três
  existentes, que são obrigatórios em conjunto para todo `ChannelType`
  registrado): um ponto de extensão para provisionamento automático de
  configuração externa no cadastro do canal. Só o adapter `"telegram"`
  implementa; o WAHA continua sem implementar, de propósito.
  `ValidateChannelAdapterRegistrations` passa a tratar esse quarto
  contrato à parte dos três obrigatórios.
- `CreateChannelCommandHandler` estendido: quando o adapter do
  `channelType` informado implementa o contrato de provisionamento, invoca
  `setWebhook` do Telegram (com a `webhookUrl` computada e um
  `secret_token` gerado por canal) antes de persistir o `Channel`. Falha na
  chamada externa impede o cadastro inteiro — nenhum canal é persistido
  sem o webhook de fato registrado no Telegram.
- `UpdateChannelCommandHandler` estendido: troca de `BotToken` de um canal
  Telegram existente re-executa o provisionamento (novo `secret_token`,
  nova chamada a `setWebhook`), pelo mesmo mecanismo do cadastro.
- `TelegramInboundWebhookHandler` valida o header
  `X-Telegram-Bot-Api-Secret-Token` de toda requisição recebida contra o
  `secret_token` persistido (parte da credencial cifrada), rejeitando
  (HTTP 401) qualquer divergência ou ausência — fecha, para este adapter, o
  risco que o contrato genérico e o WAHA aceitaram sem verificação
  (`inbox-adapter-waha`, Decision 6).
- `TelegramInboundWebhookHandler` também rejeita webhooks para um canal com
  `isActive: false` — primeiro adapter do catálogo a dar efeito prático à
  desativação sobre o processamento de mensagens de entrada; escopado a
  este adapter, sem alterar o comportamento do WAHA (que continua sem essa
  verificação).

## Capabilities

### New Capabilities
(nenhuma — as capabilities envolvidas já existem)

### Modified Capabilities
- `inbox-channel-adapter-plugin`: novo contrato de plugin opcional para
  provisionamento automático de configuração externa no cadastro/
  atualização de canal; `ValidateChannelAdapterRegistrations` passa a
  distinguir contratos obrigatórios (três) de opcionais (este quarto).
- `inbox-channel-catalog`: cadastro de canal passa a invocar o
  provisionamento automático quando o adapter o implementa, falhando o
  cadastro inteiro se o provisionamento falhar; atualização de canal passa
  a re-executar o provisionamento quando a credencial de um adapter que o
  implementa é substituída.

## Impact

- `apps/inbox` apenas (nenhuma mudança em `apps/api`/`apps/workers`/`apps/frontend`).
- Código afetado: novo diretório `Channels/Adapters/Telegram/`
  (`TelegramCredential`, `TelegramChannelConfigValidator`,
  `TelegramOutboundMessageSender`, `TelegramInboundWebhookHandler`, e o
  provisionador do quarto contrato), `Channels/Adapters/IChannelAdapterRegistry.cs`/
  `ChannelAdapterRegistry.cs` (novo método para o quarto contrato, nulável),
  `Channels/Adapters/ChannelAdapterRegistrationExtensions.cs` (checagem de
  composição distinguindo obrigatório de opcional),
  `Channels/Commands/CreateChannel/CreateChannelCommandHandler.cs`,
  `Channels/Commands/UpdateChannel/UpdateChannelCommandHandler.cs`,
  `Program.cs` (registro DI do adapter Telegram).
- Nenhuma migration EF Core nova (credencial permanece `string` opaca
  cifrada — `TelegramCredential` é só o shape do JSON por dentro, mesmo
  padrão de `WahaCredential`).
- Nenhuma UI.
