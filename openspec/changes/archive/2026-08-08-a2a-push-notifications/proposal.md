## Why

O protocolo A2A suporta notificação assíncrona de conclusão de task via
webhook (`pushNotificationConfig`), mas isso é Non-Goal explícito desde a
primeira change (`backend-agente-a2a-mvp`) — hoje o único jeito de saber que
uma task terminou é fazer polling em `GetTask`. Isso bloqueia a linha de
integração com canais de entrada (inboxes/ChatWoot/Waha), que precisa saber
quando um agente termina de processar sem manter um loop de polling por
conversa. Fechar esse Non-Goal agora.

## What Changes

- `apps/api`: `EnqueueingAgentHandler` passa a ler
  `RequestContext.Configuration?.PushNotificationConfig` (campo do próprio
  protocolo A2A, embutido no `SendMessage` — confirmado via decompilação do
  SDK `A2A` 1.0.0-preview2, sem necessidade de nenhuma chamada JSON-RPC
  separada) e propagá-lo, quando presente, no `TaskJobMessage` publicado no
  RabbitMQ.
- `apps/workers`: `AgentExecutionService` passa a persistir o push config
  recebido em `AgentTask.Metadata` (mesmo mecanismo já usado para
  `conversationSession`/`delegationDepth`) e, ao transicionar uma task para
  um estado terminal (`completed`/`failed`), disparar uma chamada HTTP
  `POST` fire-and-forget para a URL registrada, com a `AgentTask` completa
  como payload — sem bloquear nem atrasar a conclusão da task em caso de
  falha do webhook.
- `AgentCardEndpoints.cs`: `Capabilities.PushNotifications` passa de `false`
  para `true`.
- `docs/a2a-integration.md`: atualizado para refletir o novo comportamento
  (hoje documenta explicitamente que a configuração de push notification é
  ignorada).

## Capabilities

### New Capabilities
- `a2a-push-notifications`: registro de push notification config via
  `SendMessage` e disparo de webhook fire-and-forget por `apps/workers` ao
  final do processamento de uma task.

### Modified Capabilities
- `a2a-agent-card`: `AgentCard.Capabilities.PushNotifications` passa a ser
  declarado como `true` (era fixado em `false`).

## Impact

- `apps/api/src/Buteco.Api/A2A/EnqueueingAgentHandler.cs`
- `apps/api/src/Buteco.Api/A2A/AgentCardEndpoints.cs`
- `apps/api/src/Buteco.Api/Messaging/TaskJobMessage.cs`
- `apps/workers/src/Buteco.Workers/Messaging/TaskJobMessage.cs`
- `apps/workers/src/Buteco.Workers/Agents/AgentExecutionService.cs`
- `apps/workers/src/Buteco.Workers/Program.cs` (novo `HttpClient` nomeado)
- `docs/a2a-integration.md`
- Sem migration de banco (Metadata já existe em `AgentTask`, campo `jsonb`
  livre).
- Sem mudança em `apps/frontend`.
