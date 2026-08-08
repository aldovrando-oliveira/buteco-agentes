## 1. Contrato do job (apps/api + apps/workers)

- [x] 1.1 (apps/api) Adicionar campo opcional `PushNotificationConfig` (tipo
      `A2A.PushNotificationConfig?`, default `null`) ao record
      `TaskJobMessage` em `apps/api/src/Buteco.Api/Messaging/TaskJobMessage.cs`.
- [x] 1.2 (apps/workers) Espelhar o mesmo campo no
      `apps/workers/src/Buteco.Workers/Messaging/TaskJobMessage.cs`.

## 2. Leitura do config na criação da task (apps/api)

- [x] 2.1 (apps/api) Em `EnqueueingAgentHandler.ExecuteAsync`, ler
      `context.Configuration?.PushNotificationConfig` e incluir no
      `TaskJobMessage` publicado no RabbitMQ.
- [x] 2.2 (apps/api) Teste: `SendMessage` com `configuration.pushNotificationConfig`
      é aceito normalmente e a task é criada em `submitted` (mesmo teste de
      integração já usado para o fluxo básico, verificando que a mensagem
      publicada no RabbitMQ carrega o config).
- [x] 2.3 (apps/api) Teste: `SendMessage` sem `configuration.pushNotificationConfig`
      continua funcionando exatamente como hoje (regressão).

## 3. Persistência do config em Metadata (apps/workers)

- [x] 3.1 (apps/workers) Criar `Agents/PushNotificationConfigCodec.cs` com
      `MetadataKey = "pushNotificationConfig"` e `Encode(PushNotificationConfig)`.
- [x] 3.2 (apps/workers) Em `AgentExecutionService.ExecuteAsync`, no caminho
      de sucesso (`ApplyStepAsync` de `CompleteAsync`), incluir a chave
      `pushNotificationConfig` no dicionário de `Metadata` passado via
      `beforeSave` quando `message.PushNotificationConfig is not null` (ao
      lado da chave `conversationSession` já existente).
- [x] 3.3 (apps/workers) No caminho de falha (bloco `catch`, `ApplyStepAsync`
      de `FailAsync`), passar um `beforeSave` novo que grava a mesma chave
      `pushNotificationConfig` em `Metadata` quando presente (hoje esse
      caminho não passa `beforeSave` nenhum).
- [x] 3.4 (apps/workers) Teste: task processada com sucesso e
      `PushNotificationConfig` presente na mensagem tem
      `Metadata["pushNotificationConfig"]` gravado corretamente após
      `completed`.
- [x] 3.5 (apps/workers) Teste: task que falha durante o processamento e
      `PushNotificationConfig` presente na mensagem tem
      `Metadata["pushNotificationConfig"]` gravado corretamente após
      `failed`.

## 4. Disparo do webhook (apps/workers)

- [x] 4.1 (apps/workers) Criar `Notifications/PushNotificationSender.cs`:
      recebe `PushNotificationConfig` + `AgentTask`, monta o `POST` com a
      `AgentTask` serializada (`A2AJsonUtilities.DefaultOptions`) como
      corpo, define headers `Authorization: {Scheme} {Credentials}` quando
      `Authentication` presente e `X-A2A-Notification-Token: {Token}`
      quando `Token` presente, dispara via `HttpClient` nomeado, captura
      qualquer exceção (`HttpRequestException`, timeout, etc.) e loga como
      aviso sem relançar — resposta não-2xx também só é logada.
- [x] 4.2 (apps/workers) Registrar `HttpClient` nomeado em `Program.cs` com
      `.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5))`.
- [x] 4.3 (apps/workers) Em `AgentExecutionService.ExecuteAsync`, chamar
      `PushNotificationSender` logo após o `ApplyStepAsync` de sucesso e
      logo após o de falha, só quando `message.PushNotificationConfig is not null`,
      passando a `AgentTask` retornada por `ApplyStepAsync`.
- [x] 4.4 (apps/workers) Teste: servidor HTTP fake fazendo o papel do
      "webhook do cliente" — task `completed` com push config dispara uma
      chamada `POST` com o payload esperado (`AgentTask` completa,
      `status.state = completed`, artifacts presentes).
- [x] 4.5 (apps/workers) Teste: mesmo fake, task `failed` com push config
      dispara `POST` com `status.state = failed`.
- [x] 4.6 (apps/workers) Teste: task sem push config registrado não dispara
      nenhuma chamada ao fake (comportamento atual preservado).
- [x] 4.7 (apps/api) Teste de integração: `SendMessage` com
      `configuration.pushNotificationConfig` registrado contra um agente
      inativo (rejeição síncrona em `EnqueueingAgentHandler`, task nunca
      chega a `apps/workers`) — confirma que o fake webhook nunca recebe
      nenhuma chamada. Cobre o Scenario "Task rejeitada nunca dispara o
      webhook" do spec.md; exercita o caminho de código específico (não só
      a garantia genérica documentada no Non-Goal/Risk do design.md).
- [x] 4.8 (apps/workers) Teste: fake responde `404` — task permanece
      `completed`/`failed` no store, sem erro propagado nem retry.
- [x] 4.9 (apps/workers) Teste: fake não responde dentro do timeout
      configurado — processamento da task não é bloqueado além do timeout,
      task já persistida com estado terminal antes da tentativa de
      notificação. (Simula o `TaskCanceledException` que o `HttpClient`
      lançaria de verdade ao estourar o timeout, em vez de esperar 5s reais
      — mesmo caminho de exceção, teste rápido.)
- [x] 4.10 (apps/workers) Teste: `Authentication` presente no config
      resulta em header `Authorization` correto na chamada ao fake.
- [x] 4.11 (apps/workers) Teste: `Token` presente no config resulta em
      header `X-A2A-Notification-Token` correto na chamada ao fake.
- [x] 4.12 (apps/workers) Teste: `pushNotificationConfig` sem
      `Authentication` nem `Token` preenchidos — a chamada ao fake não
      inclui nem o header `Authorization` nem `X-A2A-Notification-Token`.
      Contraponto das tasks 4.10/4.11, que testam cada header isoladamente
      quando presente.
- [x] 4.13 (apps/workers) Teste de round-trip completo: task processada via
      RabbitMQ real (`TaskJobConsumer`/`AgentExecutionService`) com
      `PushNotificationConfig` real até um estado terminal, confirmando que
      o fake recebeu a chamada com o payload esperado. A perna
      `SendMessage` real de `apps/api` → `TaskJobMessage` publicado já é
      coberta isoladamente pela task 2.2 (isolamento entre apps não permite
      um único teste acionando os dois processos — mesma limitação já
      aceita pelos testes de round-trip existentes em `apps/workers`, que
      sempre semeiam/publicam diretamente em vez de invocar `apps/api` de
      verdade).

## 5. AgentCard (apps/api)

- [x] 5.1 (apps/api) Em `AgentCardEndpoints.cs`, alterar
      `Capabilities.PushNotifications` de `false` para `true`.
- [x] 5.2 (apps/api) Atualizar `AgentCardEndpointTests.cs` para asserir
      `Capabilities.PushNotifications === true`.

## 6. Documentação

- [x] 6.1 Atualizar `docs/a2a-integration.md`: seção de `configuration` do
      `SendMessage` (deixa de dizer que `pushNotificationConfig` é
      ignorado), tabela de `capabilities` (`pushNotifications: true`), e o
      exemplo de payload do `AgentCard`. Nova seção dedicada "Push
      Notification (Webhook)" com shape, exemplo de requisição, payload
      recebido e limitações (sem retry, sem SSRF, sem métodos JSON-RPC
      dedicados).
