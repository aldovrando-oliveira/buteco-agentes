## Why

`apps/inbox` hoje só sabe cadastrar canais e resolver `Contact`/`Session` —
nunca conversou de verdade com `apps/api`. Antes de existir qualquer adapter
real de canal (WhatsApp/Telegram), falta provar o pedaço que todos os
adapters vão precisar: agrupar mensagens que chegam em rajada (debounce) e
completar um round-trip real do protocolo A2A — `SendMessage` contra
`apps/api` e a resposta de volta via push notification. Sem essa peça,
nenhum adapter tem o que chamar.

## What Changes

- Novo serviço interno `IInboundMessageOrchestrator` que recebe uma
  mensagem normalizada `(ChannelId, ExternalId, texto, receivedAt)`,
  resolve `Contact`/`Session` via `IContactSessionResolver` já existente, e
  bufferiza a mensagem.
- Novo buffer de debounce persistido em PostgreSQL (nova tabela + migration
  EF Core em `apps/inbox`) — mensagens pendentes por sessão, timestamp da
  última mensagem recebida, e o token esperado da chamada `SendMessage`
  pendente. Não vive em memória.
- Novo `BackgroundService` em `apps/inbox` que varre periodicamente o
  buffer por sessões ociosas há mais que a janela de debounce configurada
  e dispara o envio.
- Novo cliente A2A mínimo em `apps/inbox`, usando `A2A.A2AClient` (pacote
  `A2A`, já referenciado por `apps/api`/`apps/workers` do lado servidor) —
  monta e envia um `SendMessage` real contra `POST /agents/{id}/a2a` de
  `apps/api`, incluindo `pushNotificationConfig` com um token gerado por
  chamada.
- Novo endpoint receptor de push notification em `apps/inbox`
  (`POST /internal/push-notifications`), que valida o header
  `X-A2A-Notification-Token` contra o token persistido da chamada pendente
  correspondente antes de aceitar o payload.
- Configuração de janela de debounce via `IOptions`, mesmo molde de
  `SessionOptions.InactivityTimeout` — constante global, não configurável
  por canal.

## Capabilities

### New Capabilities
- `inbox-message-orchestration`: ingestão de mensagem normalizada em
  `apps/inbox`, debounce persistido por sessão, disparo de `SendMessage`
  real contra `apps/api` via cliente A2A, e recepção validada da resposta
  via push notification.

### Modified Capabilities
(nenhuma — o lado emissor do webhook, já coberto por `a2a-push-notifications`,
não muda; esta change só adiciona um novo consumidor dele.)

## Impact

- **apps/inbox**: novo serviço `IInboundMessageOrchestrator`, nova tabela
  de buffer de debounce (migration EF Core), novo `BackgroundService` de
  varredura, novo cliente A2A (`A2A.A2AClient`), novo endpoint HTTP
  `POST /internal/push-notifications`, nova opção de configuração
  (janela de debounce).
- **apps/api**: nenhuma mudança — consumido apenas via `POST
  /agents/{id}/a2a` já existente.
- **apps/workers**: nenhuma mudança — o round-trip de teste depende dele
  processar a task e disparar o webhook, comportamento já existente
  (`a2a-push-notifications`).
- **apps/frontend**: nenhuma mudança.
- Dependência nova: nenhuma — `A2A` já é `PackageReference` em outros apps,
  mas passa a ser referenciado também por `apps/inbox`.
