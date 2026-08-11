## 1. Terceiro contrato de plugin (`apps/inbox`)

- [x] 1.1 Criar `Channels/Adapters/IInboundWebhookHandler.cs` (design.md, Decision 1)
- [x] 1.2 Estender `IChannelAdapterRegistry`/`ChannelAdapterRegistry` com `GetInboundWebhookHandler(string channelType)` retornando `null` quando não registrado (design.md, Decision 1)
- [x] 1.3 Estender `ChannelAdapterRegistrationExtensions.ValidateChannelAdapterRegistrations` para comparar os três conjuntos de chave (design.md, Decision 2)
- [x] 1.4 Atualizar/estender `ChannelAdapterRegistrationExtensionsTests.cs` cobrindo composição incompleta com qualquer um dos três contratos faltando, e o caminho feliz com os três presentes

## 2. Rota genérica de webhook (`apps/inbox`)

- [x] 2.1 Criar `Channels/Webhooks/Endpoints/WebhookEndpoints.cs` com `MapPost("/webhooks/{channelId:guid}", ...)` (design.md, Decision 1)
- [x] 2.2 Implementar `ReceiveAsync`: resolve `Channel` por `channelId` (404 se ausente), resolve handler via `IChannelAdapterRegistry.GetInboundWebhookHandler` (400 se ausente), despacha `HandleAsync`
- [x] 2.3 Registrar `app.MapWebhookEndpoints()` em `Program.cs`
- [x] 2.4 Testes de integração da rota genérica: 404 para `channelId` inexistente, 400 para `ChannelType` sem handler, despacho correto para o handler do `ChannelType` resolvido (usando um handler de teste, mesmo padrão de `TestChannelConfigValidator`/`TestOutboundMessageSender`)

## 3. `webhookUrl` no formato definitivo (`apps/inbox`)

- [x] 3.1 Ajustar `ChannelResponse.FromEntity`/`BuildWebhookUrl` para `/webhooks/{channelId}` (design.md, ajuste de convenção)
- [x] 3.2 Atualizar `ChannelEndpointsTests.cs`/`CreateChannelCommandHandlerTests.cs` que hoje verificam o formato antigo (`/webhooks/{channelType}/{channelId}`)

## 4. `Contact.Metadata` (`apps/inbox`)

- [x] 4.1 Adicionar `Metadata` (`IReadOnlyDictionary<string, string>`) a `Contact`, exigido no construtor, sem `Update`/mutação após criado (design.md, Decision 8)
- [x] 4.2 Mapear `Metadata` em `AppDbContext` como `jsonb` com `ValueComparer` customizado
- [x] 4.3 Gerar migration `AddContactMetadata` (coluna `NOT NULL DEFAULT '{}'::jsonb`) e conferir que o diff só afeta a tabela `Contacts`
- [x] 4.4 Estender `IContactSessionResolver.FindOrCreateSessionAsync`/`ContactSessionResolver` com parâmetro `contactMetadata`, usado só no branch de criação do `Contact`
- [x] 4.5 Estender `IInboundMessageOrchestrator.ReceiveMessageAsync`/`InboundMessageOrchestrator` com parâmetro `contactMetadata`, propagado ao resolver
- [x] 4.6 Atualizar `ContactResponse.FromEntity` para incluir `Metadata`
- [x] 4.7 Atualizar chamadores existentes do orchestrator/resolver (incluindo o handler de webhook de teste, se aplicável) para passar `contactMetadata` explícito
- [x] 4.8 Atualizar `ContactSessionResolverTests.cs`/`InboundMessageOrchestratorTests.cs`/`DebounceRestartAndConcurrencyTests.cs`/`ContactEndpointsTests.cs` para a nova assinatura, cobrindo: metadado gravado na criação; metadado informado em chamada subsequente não sobrescreve o já persistido

## 5. Adapter WAHA — validação de credencial (`apps/inbox`)

- [x] 5.1 Criar `Channels/Adapters/Waha/WahaCredential.cs` (`ServiceUrl`, `SessionName`, `AuthToken`) (design.md, Decision 4)
- [x] 5.2 Criar `Channels/Adapters/Waha/WahaChannelConfigValidator.cs`: JSON inválido, `ServiceUrl` não absoluta, `SessionName`/`AuthToken` vazios são rejeitados
- [x] 5.3 Testes unitários de `WahaChannelConfigValidator`: credencial válida aceita; cada campo inválido isoladamente rejeitado com a mensagem correspondente

## 6. Adapter WAHA — envio de resposta (`apps/inbox`)

- [x] 6.1 Criar `Channels/Adapters/Waha/WahaOutboundMessageSender.cs`: `POST {ServiceUrl}/api/sendText` com `X-Api-Key`, corpo `{session, chatId, text}` (design.md, Decision 7)
- [x] 6.2 Testes unitários com `HttpMessageHandler` fake (mesmo padrão de `FakeAgentApiHttpMessageHandler`): confirma URL, header `X-Api-Key`, corpo (`chatId = ContactExternalId` sem transformação); falha HTTP não-2xx propaga exceção

## 7. Adapter WAHA — recepção de webhook (`apps/inbox`)

- [x] 7.1 Criar `Channels/Adapters/Waha/WahaInboundWebhookHandler.cs`: desserializa `{event, session, payload: {from, to, body, ...}}`, filtra `event == "message"`, extrai `payload.body` e `payload.from` bruto (design.md, Decision 5)
- [x] 7.2 `WahaInboundWebhookHandler` chama `IInboundMessageOrchestrator.ReceiveMessageAsync` com `ExternalId = payload.from` (com sufixo `@c.us`) e metadado `{"phone": "<payload.from sem @c.us>"}`
- [x] 7.3 Testes unitários com fixture de payload real (baseada na documentação confirmada na investigação) para o evento `"message"`, com asserções explícitas (não só "processado corretamente"): o texto extraído é exatamente igual a `payload.body` da fixture, não vazio, e não provém de `payload.text` ou de qualquer outro campo (design.md, Context); o `ExternalId` passado a `IInboundMessageOrchestrator.ReceiveMessageAsync` é exatamente `payload.from`, incluindo o sufixo `@c.us`, sem nenhuma transformação (design.md, Decision 5)
- [x] 7.4 Teste unitário: outros valores de `event` (ex. `session.status`) são ignorados sem erro, sem chamar `IInboundMessageOrchestrator`

## 8. Registro do adapter WAHA (`apps/inbox`)

- [x] 8.1 Registrar `WahaChannelConfigValidator`, `WahaOutboundMessageSender`, `WahaInboundWebhookHandler` via `AddKeyedSingleton` sob `"waha"` em `Program.cs`, antes de `ValidateChannelAdapterRegistrations()`
- [x] 8.2 Registrar `IHttpClientFactory` (se ainda não presente para uso anônimo) usado por `WahaOutboundMessageSender` — já disponível como efeito colateral dos `AddHttpClient(...)` nomeados existentes; nenhum registro adicional necessário

## 9. `docker-compose.yml`

- [x] 9.1 Adicionar serviço `waha` (`devlikeapro/waha:latest`, `WHATSAPP_DEFAULT_ENGINE=GOWS`, porta `${WAHA_PORT:-3000}:3000`) (design.md, Decision 9)

## 10. Verificação final

- [x] 10.1 Rodar toda a suíte de testes de `apps/inbox` (`dotnet test`) — rodada completa via Podman (`DOCKER_HOST` apontando pro socket de `podman machine`, `TESTCONTAINERS_RYUK_DISABLED=true`): 87/88 passam, incluindo todos os testes novos desta fatia. A única falha restante (`InboundMessageOrchestratorTests.ReceiveMessageAsync_ConcurrentCallsSameSession_ResolveToSinglePendingDispatch`) é um bug pré-existente em `InboundMessageOrchestrator.ReceiveMessageAsync` (código de `inbox-orquestrador-debounce`, não tocado por esta change além do parâmetro `contactMetadata`) — o catch atual só trata `DbUpdateException`/violação de unicidade na criação de `PendingDispatch`, não `DbUpdateConcurrencyException` na recuperação de anexação concorrente quando duas ou mais chamadas perdem a corrida de criação ao mesmo tempo. Decisão explícita: não corrigido nesta fatia (fora do escopo declarado), registrado aqui para acompanhamento em change separada. **Correção incidental de teste, fora do escopo original mas necessária pra rodar a suíte**: `Support/OrchestrationFactoryFixture.cs` tinha uma corrida pré-existente (migration rodando só depois de `Services` já ter iniciado o host, incluindo `DebounceSweepService` com `SweepInterval: 50ms`) — corrigida migrando via um `AppDbContext` isolado antes de tocar `Services`, mesmo padrão já usado por `PostgresOnlyFixture`
- [ ] 10.2 Checklist de round-trip manual, documentado no README (seção `apps/inbox`, "Checklist de round-trip manual com WAHA") — documentado, mas não executado (depende de um WAHA real conectado a um WhatsApp de teste, fora do alcance deste ambiente)
