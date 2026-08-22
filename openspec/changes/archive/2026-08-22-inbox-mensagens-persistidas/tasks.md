## 1. Débito de documentação (pendência de varredura)

- [x] 1.1 Corrigir a seção *Risks* de
      `openspec/changes/archive/2026-08-16-inbox-fix-concorrencia-orquestrador/design.md`
      para descrever o mecanismo final (detach + rebusca), não o abandonado
      (`ReloadAsync`) — convenção 9 (concluído durante o explore/proposta
      desta change, sem impacto em código).

## 2. Entidade `Message` e migration (apps/inbox)

- [x] 2.1 Criar `Messages/Entities/Message.cs`, `MessageDirection.cs`,
      `MessageContentType.cs`, `MessageDeliveryStatus.cs`,
      `MessageDispatchStatus.cs` (design.md, Decisão 3).
- [x] 2.2 Mapear `Message` em `Infrastructure/AppDbContext.cs`: `DbSet<Message>`,
      FK `SessionId → Session.Id` (`OnDelete(Cascade)`), índice composto
      `(SessionId, OccurredAt)`, índice único parcial `(SessionId, ExternalId)
      WHERE Direction = 'Inbound' AND ExternalId IS NOT NULL` (design.md,
      Decisões 3, 5, 7).
- [x] 2.3 Adicionar `Contact.DisplayName` (nullable) e
      `Contact.UpdateDisplayName(string?)` em
      `Contacts/Entities/Contact.cs` (design.md, Decisão 9) — sem alterar o
      comportamento existente de `Metadata`.
- [x] 2.4 Gerar migration EF Core (`AddMessage`) cobrindo tabela `messages`,
      índices, e a coluna `DisplayName` em `contacts` (design.md, Migration
      Plan).

## 3. Identificador externo de mensagem — WAHA (apps/inbox)

- [x] 3.1 Confirmar contra a documentação real do WAHA (ou instância de
      referência) o nome exato do campo de identificador de mensagem e do
      campo de nome de exibição (`pushName` ou equivalente, possivelmente
      aninhado) — convenção 6, não assumir de memória (design.md, Decisões
      5 e 9). Confirmado via web: `payload.id` (doc oficial) e
      `payload._data.Info.PushName` (engine GOWS, só via discussão da
      comunidade — confiança menor, registrado em comentário no código).
- [x] 3.2 Adicionar os campos confirmados em `WahaWebhookMessagePayload`
      (`Channels/Adapters/Waha/WahaWebhookPayload.cs`).
- [x] 3.3 Atualizar `WahaInboundWebhookHandler.HandleAsync` para repassar o
      identificador externo de mensagem e o nome de exibição extraído para
      `IInboundMessageOrchestrator.ReceiveMessageAsync`.

## 4. Identificador externo de mensagem — Telegram (apps/inbox)

- [x] 4.1 Confirmar contra a documentação real da Bot API do Telegram o
      campo `message_id` (e reconfirmar `username`/`first_name` já
      capturados) — convenção 6 (design.md, Decisão 5). Confirmado via web:
      `message.message_id`, campo obrigatório do objeto Message.
- [x] 4.2 Adicionar `MessageId` em `TelegramMessage`
      (`Channels/Adapters/Telegram/TelegramWebhookModels.cs`).
- [x] 4.3 Atualizar `TelegramInboundWebhookHandler.HandleAsync` para
      repassar o identificador externo de mensagem e o nome de exibição
      (`Username`/`FirstName`) para
      `IInboundMessageOrchestrator.ReceiveMessageAsync`.

## 5. Mídia — marcador de tipo (apps/inbox)

- [x] 5.1 Confirmar contra documentação real o shape de mídia de cada
      adapter (WAHA `hasMedia`/`media`; Telegram `photo`/`voice`/`document`
      no `Update`) — convenção 6 (design.md, Decisão 8). Confirmado via web:
      WAHA `payload.hasMedia`/`payload.media.mimetype`; Telegram
      `message.photo`/`voice`/`document`/`audio` (presença, texto
      acompanhante vai em `caption`, não `text`).
- [x] 5.2 Mapear o `ContentType` (`Text`/`Image`/`Audio`/`Document`) a partir
      do payload de cada adapter, com marcador textual quando não houver
      texto acompanhando a mídia.

## 6. Orquestrador de entrada (apps/inbox)

- [x] 6.1 Estender `IInboundMessageOrchestrator.ReceiveMessageAsync` com
      `externalMessageId` e `displayName` (design.md, Decisões 2, 5, 9).
- [x] 6.2 Em `InboundMessageOrchestrator.ReceiveForSessionAsync`, persistir
      `Message` de entrada (com `PendingDispatchId`/`DispatchStatus =
      Pending`) no mesmo `SaveChangesAsync` que resolve `Session` e
      grava/atualiza `PendingDispatch` — sem tocar `PendingDispatch.cs` nem
      sua coleção owned/JSON `Messages` (design.md, Decisões 1, 2, 6).
- [x] 6.3 Tratar violação do índice único de deduplicação
      (`DbUpdateException`) com o mesmo padrão de detach + no-op já usado
      para `Contact`/`PendingDispatch` — sem adicionar ao buffer de
      debounce nesse caso (design.md, Decisão 5).
- [x] 6.4 Em `ContactSessionResolver.FindOrCreateSessionAsync` (único ponto
      de escrita, design.md Decisão 9), chamar `Contact.UpdateDisplayName(...)`
      a cada chamada, tanto na criação quanto no reaproveitamento de
      `Contact` existente.

## 7. Estado de dispatch renderável (apps/inbox)

- [x] 7.1 Em `DebounceSweepService.TryDispatchAsync`, ao marcar
      `PendingDispatch` como `Dispatching`, atualizar `DispatchStatus` das
      `Message` do mesmo `PendingDispatchId` no mesmo `SaveChangesAsync`
      (design.md, Decisão 6).
- [x] 7.2 Em `DebounceSweepService`, atualizar `DispatchStatus` das `Message`
      correspondentes para `Failed`, no mesmo `SaveChangesAsync`, nos três
      pontos que terminam um ciclo sem resposta possível: catch de
      `A2AException` em `TryDispatchAsync` (rejeição de protocolo), ramo de
      resposta síncrona terminal em `HandleResponseAsync` (rejeição), e
      `HandleTransportFailureAsync` ao esgotar `MaxDispatchAttempts`; no
      ramo de retentativa (abaixo do limite) de `HandleTransportFailureAsync`,
      voltar `DispatchStatus` para `Pending` (mesmo espelhamento do
      `RegisterTransportFailure` que devolve `PendingDispatch` a `Pending`)
      (design.md, Decisão 6).
- [x] 7.3 Em `PushNotificationEndpoints.ReceiveAsync`, ao processar a push
      notification válida (único caminho que chega até aqui), atualizar
      `DispatchStatus` para `Completed` nas `Message` do grupo antes de
      remover a `PendingDispatch` (design.md, Decisão 6).

## 8. Persistência de mensagem de saída (apps/inbox)

- [x] 8.1 Em `PushNotificationEndpoints.DeliverResponseAsync`, persistir
      `Message` de saída no `try`, com `DeliveryStatus = Sent` (design.md,
      Decisões 2, 4).
- [x] 8.2 No `catch` existente (hoje só loga), persistir `Message` de saída
      com `DeliveryStatus = Failed` e `DeliveryFailureReason` a partir da
      exceção, sem propagar a exceção (design.md, Decisão 4).
- [x] 8.3 Gravar `ContentType = Text` em toda `Message` de saída persistida
      nesta fatia — único valor possível hoje (design.md, Decisão 3).

## 9. Consultas de leitura — mensagens por sessão e sessões por canal (apps/inbox)

- [x] 9.1 Criar `GetSessionMessagesQuery`/`GetSessionMessagesQueryHandler`
      (`Messages/Queries/GetSessionMessages/`) — mensagens de uma `Session`
      em ordem cronológica, 404 para sessão inexistente, lista vazia para
      sessão sem mensagem (design.md, Decisão 10).
- [x] 9.2 Expor `GET /sessions/{sessionId}/messages` em
      `Messages/Endpoints/MessageEndpoints.cs` — sem `.AllowAnonymous()`,
      herdando o `FallbackPolicy` (design.md, Decisão 10).
- [x] 9.3 Criar `GetChannelSessionsQuery`/`GetChannelSessionsQueryHandler`
      (`Contacts/Queries/GetChannelSessions/`) — sessões de um `Channel`
      ordenadas por `LastActivityAt` (mais recente primeiro), com
      `Contact.DisplayName`/`ExternalId` e prévia (direção, conteúdo,
      instante) da última `Message` de cada sessão, via o índice
      `(SessionId, OccurredAt)` já criado na task 2.2 — 404 para canal
      inexistente, lista vazia para canal sem sessão (design.md, Decisão
      10).
- [x] 9.4 Expor `GET /channels/{channelId}/sessions` em
      `Contacts/Endpoints/ChannelSessionEndpoints.cs` — sem
      `.AllowAnonymous()`, herdando o `FallbackPolicy` (design.md, Decisão
      10).
- [x] 9.5 Confirmar que `RouteAuthenticationExtensions` não exige nenhuma
      classificação nova para as duas rotas acima (nenhuma é anônima) e que
      a allowlist de rotas anônimas de `apps/inbox` permanece idêntica à de
      `auth-login-e-servico` (`GET /health`, `POST /webhooks/{channelId}`,
      `POST /internal/push-notifications`) — as duas rotas novas ficam fora
      dela. Confirmado: `Program.cs` não adiciona as duas rotas novas aos
      parâmetros de `ValidateRouteAuthenticationClassification`, nem chama
      `.AllowAnonymous()` nelas.

## 10. Testes (apps/inbox, Testcontainers Postgres real — convenção 5)

- [x] 10.1 Mensagem de entrada persistida a partir de webhook real do WAHA
      (`WahaInboundWebhookHandlerTests`).
- [x] 10.2 Mensagem de entrada persistida a partir de webhook real do
      Telegram (`TelegramInboundWebhookHandlerTests`).
- [x] 10.3 Webhook reentregue com o mesmo identificador externo não duplica
      mensagem — sequencial (`InboundMessageOrchestratorTests`,
      `MessagePersistenceTests`) e concorrente
      (`InboundMessageOrchestratorTests`).
- [x] 10.4 Mensagem de saída persistida como enviada, com `ContentType =
      Text`, quando `IOutboundMessageSender` não lança
      (`PushNotificationEndpointsTests`).
- [x] 10.5 Mensagem de saída persistida como falha, com motivo, quando o
      sender lança — e a task principal não é derrubada
      (`PushNotificationEndpointsTests`; `TestOutboundMessageSender` ganhou
      `ExceptionToThrow` para exercitar o caminho).
- [x] 10.6 `DispatchStatus` das mensagens de entrada transita
      Pending→Dispatching→Completed (push notification válida) e
      Pending→Failed a partir de cada uma das três causas (esgotamento de
      tentativas, rejeição síncrona, rejeição de protocolo A2A), e volta a
      Pending numa retentativa não esgotada — sobrevivendo à remoção da
      `PendingDispatch` (`DebounceSweepServiceTests`,
      `PushNotificationEndpointsTests`).
- [x] 10.7 Sessão sem nenhuma mensagem retorna lista vazia via
      `GET /sessions/{id}/messages`, sem erro; sessão inexistente retorna
      404 (`MessagePersistenceTests`).
- [x] 10.8 `DisplayName` capturado quando o payload traz (WAHA e Telegram) e
      permanece nulo quando não traz; atualizado em mensagem subsequente com
      valor novo; não apagado por mensagem subsequente sem o campo
      (`ContactSessionResolverTests` para o contrato de persistência;
      `WahaInboundWebhookHandlerTests`/`TelegramInboundWebhookHandlerTests`
      para a extração do payload de cada adapter).
- [x] 10.9 Sem infraestrutura de fault injection no projeto para simular uma
      falha literal a meio da transação — cobertura equivalente via o
      cenário de concorrência real já usado pelo fix de
      `inbox-fix-concorrencia-orquestrador` (8 chamadas concorrentes,
      `InboundMessageOrchestratorTests`): assevera que toda `Message`
      resultante aponta para o único `PendingDispatch` criado (nenhuma
      órfã/duplicada), provando que a escrita de `Message` e a mutação de
      `PendingDispatch` avançam juntas dentro do mesmo `SaveChangesAsync`
      mesmo sob a mesma corrida que expôs o bug original.
- [x] 10.10 Mensagem de mídia sem texto (WAHA e Telegram, um teste cada)
      grava o `ContentType` correspondente e um marcador textual, sem
      conteúdo binário; mensagem só de texto grava `ContentType = Text`
      (`WahaInboundWebhookHandlerTests`, `TelegramInboundWebhookHandlerTests`).
- [x] 10.11 `GET /channels/{channelId}/sessions` retorna as sessões do canal
      por última atividade, com `DisplayName`/`ExternalId` e prévia da
      última mensagem, para um canal com sessões; retorna lista vazia, sem
      erro, para um canal sem nenhuma sessão; retorna 404 para canal
      inexistente (`ChannelSessionEndpointsTests`).
