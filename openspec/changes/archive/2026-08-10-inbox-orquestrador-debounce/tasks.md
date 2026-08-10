## 1. Modelo de dados e migration (apps/inbox)

- [x] 1.1 [apps/inbox] Criar `PendingDispatchStatus` (`Pending`,
      `Dispatching`, `Failed`) em `Orchestration/Entities/`
- [x] 1.2 [apps/inbox] Criar `BufferedMessage` (texto + `ReceivedAt`) como
      tipo owned em `Orchestration/Entities/`
- [x] 1.3 [apps/inbox] Criar entidade `PendingDispatch` (`SessionId`,
      `Status`, `Messages` (owned collection), `LastMessageAt`, `TaskId`
      nullable, `ExpectedToken` nullable, `AttemptCount` int, default 0)
      em `Orchestration/Entities/`
- [x] 1.4 [apps/inbox] Mapear `PendingDispatch` em `AppDbContext`
      (`ToTable("pending_dispatches")`, `Messages` via `ToJson()`, FK para
      `Session.Id`, índice único parcial `(SessionId) WHERE status =
      'Pending'`, propriedade shadow `Property<uint>("Version").
      IsRowVersion()` — mapeada para `xmin` pela convenção do provider
      Npgsql, ver design.md Decisão 5)
- [x] 1.5 [apps/inbox] Gerar migration EF Core
      (`AddPendingDispatch`) e conferir o SQL gerado (índice parcial e
      coluna `jsonb`)

## 2. Ingestão e debounce (apps/inbox)

- [x] 2.1 [apps/inbox] Criar `DebounceOptions` (`Window`, `SweepInterval`,
      `MaxDispatchAttempts`) com defaults (10s / 2s / 3) e registro via
      `IOptions`
- [x] 2.2 [apps/inbox] Criar `IInboundMessageOrchestrator` +
      `InboundMessageOrchestrator.ReceiveMessageAsync(channelId,
      externalId, text, receivedAt, ct)`: resolve `Contact`/`Session` via
      `IContactSessionResolver`, encontra ou cria `PendingDispatch`
      `Pending` da sessão (mesmo padrão de captura de violação de
      unicidade de `ContactSessionResolver.FindOrCreateContactAsync`),
      adiciona a mensagem e atualiza `LastMessageAt`
- [x] 2.3 [apps/inbox] Registrar `IInboundMessageOrchestrator` no DI
      (`Program.cs`)

## 3. Cliente A2A (apps/inbox)

- [x] 3.1 [apps/inbox] Adicionar `PackageReference Include="A2A"` ao
      `Buteco.Inbox.csproj` (versão já centralizada em
      `Directory.Packages.props`)
- [x] 3.2 [apps/inbox] Criar `IA2AClientFactory`/`A2AClientFactory`:
      constrói `A2A.A2AClient` por `AgentId` usando `HttpClient` nomeado
      (`IHttpClientFactory`, mesmo padrão de `AgentReferenceValidator`) e
      `ApiOptions.BaseUrl` + `/agents/{agentId}/a2a`
- [x] 3.3 [apps/inbox] Registrar o `HttpClient` nomeado e
      `IA2AClientFactory` no DI (`Program.cs`)

## 4. Disparo do debounce (apps/inbox)

- [x] 4.1 [apps/inbox] Criar `DebounceSweepService : BackgroundService`
      com `PeriodicTimer(SweepInterval)`, buscando `PendingDispatch`
      `Pending` com `LastMessageAt` mais antigo que `Window`
- [x] 4.2 [apps/inbox] Implementar claim idempotente: transicionar
      `Pending` → `Dispatching` via `SaveChangesAsync` com concorrência
      `xmin`; capturar `DbUpdateConcurrencyException` e pular a
      candidata sem erro
- [x] 4.3 [apps/inbox] Implementar o disparo: resolver `Channel.AgentId` a
      partir da `Session` reivindicada, gerar `ExpectedToken`, montar
      `SendMessageRequest` (mensagens concatenadas do buffer,
      `ContextId` da `Session`, `Configuration.PushNotificationConfig`
      apontando para o endpoint receptor com o token gerado) e chamar
      `A2AClient.SendMessageAsync`
- [x] 4.4 [apps/inbox] Tratar resposta síncrona: se
      `SendMessageResponse.Task.Status` já for terminal de rejeição,
      logar e remover a `PendingDispatch` imediatamente; se `Submitted`,
      persistir `TaskId` e manter `Dispatching`
- [x] 4.5 [apps/inbox] Tratar falha de transporte
      (`HttpRequestException`/`TaskCanceledException` de timeout):
      `LogWarning` estruturado com `SessionId`/`AgentId`/`AttemptCount`,
      incrementar `AttemptCount`, voltar `Status` para `Pending` e
      atualizar `LastMessageAt` para o momento da falha (reentra na
      varredura do próximo tick, design.md Decisão 9) — sem propagar
      exceção
- [x] 4.5.1 [apps/inbox] Quando `AttemptCount` atingir
      `MaxDispatchAttempts` após uma falha de transporte: `LogError`
      estruturado (perda definitiva da mensagem), marcar
      `Status = Failed` e remover a `PendingDispatch`
- [x] 4.6 [apps/inbox] Registrar `DebounceSweepService` como
      `IHostedService` (`Program.cs`)

## 5. Endpoint receptor de push notification (apps/inbox)

- [x] 5.1 [apps/inbox] Criar `PushNotificationEndpoints` com
      `POST /internal/push-notifications`, desserializando o corpo como
      `AgentTask` (`A2A.AgentTask`)
- [x] 5.2 [apps/inbox] Validar o header `X-A2A-Notification-Token` contra
      `PendingDispatch.ExpectedToken` da linha localizada por
      `AgentTask.Id` (== `PendingDispatch.TaskId`); responder 401 sem
      processar o payload quando o header estiver ausente ou divergente
- [x] 5.3 [apps/inbox] Ao validar com sucesso, logar a conclusão do
      round-trip (nível informativo, incluindo `TaskId`/`SessionId`) e
      remover a `PendingDispatch`
- [x] 5.4 [apps/inbox] Mapear o endpoint em `Program.cs`
      (`app.MapPushNotificationEndpoints()` ou equivalente)

## 6. Testes unitários (apps/inbox)

- [x] 6.1 [apps/inbox] `InboundMessageOrchestratorTests`: mensagem cria
      `PendingDispatch` nova; segunda mensagem antes do disparo é
      agrupada na mesma `PendingDispatch`
- [x] 6.2 [apps/inbox] `DebounceSweepServiceTests`: mensagens dentro da
      janela geram um único `SendMessage`; mensagens fora da janela
      (duas rodadas de sweep) geram dois `SendMessage` distintos
      (`A2AClient`/`IA2AClientFactory` mockado). **Capturar o
      `SendMessageRequest` recebido pelo mock e afirmar isoladamente**
      que `AgentId` usado para resolver o `A2AClient` corresponde ao
      `Channel` da sessão de origem e que `Message.ContextId` corresponde
      ao `ContextId` da `Session` resolvida — cobre o Scenario "SendMessage
      disparado usa o AgentId do Channel e o ContextId da Session" (spec)
      de forma isolada, sem depender só do teste de round-trip (7.2) para
      apontar a causa se essa correspondência quebrar
- [x] 6.3 [apps/inbox] Teste de resposta síncrona `Rejected`: disparo
      encerra sem persistir espera por push notification, buffer
      removido
- [x] 6.4 [apps/inbox] Teste de falha de transporte com retry:
      `SendMessage` lança `HttpRequestException`/timeout na primeira
      tentativa — orquestrador não propaga exceção, loga
      (`LogWarning`), `AttemptCount` incrementa e `PendingDispatch`
      volta para `Pending`; no próximo ciclo de varredura (janela
      reaplicada), uma segunda tentativa bem-sucedida remove a
      `PendingDispatch` normalmente
- [x] 6.4.1 [apps/inbox] Teste de esgotamento de tentativas:
      `SendMessage` falha repetidamente até `AttemptCount` atingir
      `MaxDispatchAttempts` — `PendingDispatch` é marcada `Failed`,
      removida, e a falha final é logada em `LogError` (não
      `LogWarning`)
- [x] 6.5 [apps/inbox] `PushNotificationEndpointsTests`: token correto é
      aceito e remove a `PendingDispatch`; token ausente ou divergente é
      rejeitado (401) e não altera a `PendingDispatch`
- [x] 6.6 [apps/inbox] Teste de sobrevivência a restart: `PendingDispatch`
      criada antes de recriar o `AppDbContext`/reiniciar o host continua
      presente e dispara normalmente depois
- [x] 6.7 [apps/inbox] Teste de idempotência entre instâncias: duas
      instâncias de `DebounceSweepService` competindo pelo mesmo
      `PendingDispatch` expirado — apenas uma dispara o `SendMessage`
      (mesmo espírito de `AgentDelegationConcurrencyTests`, apps/workers)

## 7. Teste de round-trip ponta a ponta (apps/inbox, apps/api, apps/workers)

- [x] 7.1 [apps/inbox, apps/api, apps/workers] Montar fixture de teste
      com `apps/api` real (`WebApplicationFactory<Program>`, mesmo padrão
      de `A2ATaskLifecycleFixture`) e `apps/workers` real via
      Testcontainers Postgres/RabbitMQ (mesmo padrão de
      `WorkerInfrastructureFixture`), mais o host de teste de
      `apps/inbox` apontando `ApiOptions.BaseUrl` para a `apps/api` de
      teste e expondo seu próprio endpoint receptor
- [x] 7.2 [apps/inbox, apps/api, apps/workers] `RoundTripTests`: mensagem
      chega via `IInboundMessageOrchestrator.ReceiveMessageAsync` →
      debounce dispara → `SendMessage` real processado por `apps/api` →
      job processado por `apps/workers` → push notification recebida no
      endpoint de `apps/inbox` com token validado e payload correto

## 8. Documentação e configuração

- [x] 8.1 [apps/inbox] Adicionar seções `Debounce`/`PushNotifications` de
      exemplo em `appsettings.json`/`appsettings.Development.json`
      (janela, intervalo de varredura)
- [x] 8.2 [apps/inbox] Conferir que `docker-compose.yml`/instruções de
      execução local não precisam de mudança (nenhum serviço novo além
      do já existente Postgres)
