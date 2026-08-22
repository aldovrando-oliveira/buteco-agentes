## 1. Extração do texto de resposta (apps/inbox)

- [x] 1.1 Corrigir `PushNotificationEndpoints.ExtractResponseText` para ler
      `task.Artifacts?.LastOrDefault()?.Parts` em vez de
      `task.Status.Message?.Parts`.
- [x] 1.2 Corrigir o fixture `PushNotificationEndpointsTests.BuildAgentTask`
      para construir o `AgentTask` no formato real produzido por
      `AgentExecutionService` (`Artifacts`, não `Status.Message`) — o
      fixture antigo mascarava o defeito 1.1 porque nunca refletia o
      shape real do payload.

## 2. Robustez a cancelamento da requisição (apps/inbox)

- [x] 2.1 Em `PushNotificationEndpoints.ReceiveAsync`, trocar o
      `CancellationToken` da requisição por `CancellationToken.None` em
      todas as chamadas a partir da localização do `PendingDispatch`
      (inclusive): `DeliverResponseAsync`,
      `UpdateMessageDispatchStatusesAsync`, e o `SaveChangesAsync` final —
      mantendo o token da requisição só na consulta inicial do
      `PendingDispatch`.

## 3. Validação

- [x] 3.1 `dotnet build` de `apps/inbox` (projeto principal) — compilação
      limpa.
- [x] 3.2 `dotnet build` de `apps/inbox` (projeto de testes) — compilação
      limpa, incluindo o fixture corrigido de 1.2.
- [x] 3.3 `dotnet test apps/inbox/tests/Buteco.Inbox.Tests` com
      Testcontainers apontando para o socket da API do Podman
      (`DOCKER_HOST=unix:///.../podman-machine-default-api.sock`,
      `TESTCONTAINERS_RYUK_DISABLED=true` — Podman não roda o container
      Ryuk privilegiado que o Testcontainers usa por padrão para limpeza)
      — 155/155 testes passando, incluindo os 9 de
      `PushNotificationEndpointsTests`. Uma falha isolada em
      `InboundMessageOrchestratorTests.ReceiveMessageAsync_ConcurrentCallsSameSession_ResolveToSinglePendingDispatch`
      numa primeira rodada não se repetiu em execuções subsequentes
      (isolada 3/3 e suíte completa 1/1) — flake de timing pré-existente,
      não relacionado a esta change (arquivo não tocado por ela).
- [x] 3.4 `openspec validate inbox-push-notification-recepcao-resiliente --strict`
      e revisar a saída — `Change 'inbox-push-notification-recepcao-resiliente' is valid`.
