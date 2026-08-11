## Why

`apps/inbox` tem os contratos de plugin de canal (`IChannelConfigValidator`,
`IOutboundMessageSender`) abertos desde `inbox-adapter-contrato-catalogo`,
mas nenhum adapter real ainda os implementa — só um adapter de teste, sem
integração de rede. Também falta a peça que aquela change deixou como
Non-Goal explícito: nenhum endpoint HTTP está mapeado para receber webhooks,
apesar de `webhookUrl` já ser computado e exposto. Antes de conectar o
primeiro canal de verdade, é preciso fechar os dois pontos com o WAHA
(WhatsApp HTTP API): a primeira implementação real dos contratos existentes,
e o terceiro contrato que faltava — recepção de webhook.

## What Changes

- Novo contrato de plugin `IInboundWebhookHandler`, resolvido por
  `ChannelType` via DI keyed (mesmo padrão de `IChannelConfigValidator`/
  `IOutboundMessageSender`). Uma rota HTTP genérica única,
  `POST /webhooks/{channelId}`, resolve o `Channel` pelo id, lê o
  `ChannelType` já persistido e despacha para o handler registrado desse
  tipo.
- **BREAKING**: `webhookUrl` muda de formato — de
  `/webhooks/{channelType}/{channelId}` para `/webhooks/{channelId}`.
  Nenhum endpoint real consumia o formato antigo (nunca foi mapeado), então
  não há cliente existente afetado; o contrato de resposta (`ChannelResponse`)
  não muda de shape, só o valor computado.
- `ValidateChannelAdapterRegistrations` (checagem de integridade no startup,
  `inbox-adapter-contrato-catalogo`) passa a exigir os três contratos
  (`IChannelConfigValidator` + `IOutboundMessageSender` +
  `IInboundWebhookHandler`) sob a mesma chave, não mais dois.
- Primeiro adapter real, registrado sob `ChannelType` `"waha"`:
  `WahaChannelConfigValidator`, `WahaOutboundMessageSender` (chama
  `POST /api/sendText` do WAHA) e `WahaInboundWebhookHandler` (processa o
  evento `"message"` do webhook do WAHA).
- `Contact` ganha `Metadata` (dicionário genérico de string, ex.
  `{"phone": "..."}`), capturado na criação do contato a partir do dado que
  o adapter de origem tiver disponível — `WahaInboundWebhookHandler` grava o
  número de telefone sem o sufixo de domínio do WhatsApp. `ExternalId`
  continua carregando o identificador bruto usado pelo adapter para
  reconstruir a conversa (não muda).
- Novo serviço `waha` em `docker-compose.yml`, para desenvolvimento local.

## Capabilities

### New Capabilities
(nenhuma — as três capabilities de `apps/inbox` envolvidas já existem)

### Modified Capabilities
- `inbox-channel-adapter-plugin`: novo contrato de plugin
  `IInboundWebhookHandler` e o endpoint genérico que o invoca; formato da
  `webhookUrl` computada muda.
- `inbox-contact-session`: `Contact` passa a capturar um metadado adicional
  (dicionário de string) na criação, quando o adapter de origem fornecer;
  consulta de contatos passa a incluir esse metadado na resposta.
- `inbox-message-orchestration`: o serviço de ingestão de mensagem
  normalizada passa a aceitar o metadado de contato opcional, propagando-o
  até a resolução de `Contact`/`Session`.

## Impact

- `apps/inbox` apenas (nenhuma mudança em `apps/api`/`apps/workers`/`apps/frontend`).
- Código afetado: `Channels/Adapters/` (novo `IInboundWebhookHandler`,
  extensão de `ChannelAdapterRegistrationExtensions`, extensão de
  `IChannelAdapterRegistry`/`ChannelAdapterRegistry`), novo diretório
  `Channels/Adapters/Waha/`, `Channels/Responses/ChannelResponse.cs`,
  `Channels/Endpoints/ChannelEndpoints.cs` ou novo
  `Webhooks/Endpoints/WebhookEndpoints.cs`, `Contacts/Entities/Contact.cs`
  (novo campo + migration), `Contacts/IContactSessionResolver.cs`,
  `Contacts/ContactSessionResolver.cs`, `Contacts/Responses/ContactResponse.cs`,
  `Orchestration/IInboundMessageOrchestrator.cs`,
  `Orchestration/InboundMessageOrchestrator.cs`, `Program.cs` (registro DI
  do adapter WAHA).
- Nova migration EF Core em `apps/inbox` (campo `Metadata` em `Contact`).
- `docker-compose.yml`: novo serviço `waha`.
- Nenhuma UI.
