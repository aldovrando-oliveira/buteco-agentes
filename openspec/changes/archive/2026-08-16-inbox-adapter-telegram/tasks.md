## 1. Quarto contrato de plugin, opcional (`apps/inbox`)

- [x] 1.1 Criar `Channels/Adapters/IChannelWebhookProvisioner.cs` (design.md, Decision 1)
- [x] 1.2 Criar `Channels/Adapters/ChannelWebhookProvisioningResult.cs` com `Succeeded`/`Failed` (design.md, Decision 2)
- [x] 1.3 Estender `IChannelAdapterRegistry`/`ChannelAdapterRegistry` com `GetWebhookProvisioner(string channelType)` retornando `null` quando não registrado (design.md, Decision 1)

## 2. `ChannelAdapterRegistrationExtensions` — obrigatório vs. opcional (`apps/inbox`)

- [x] 2.1 Estender `ValidateChannelAdapterRegistrations` distinguindo os três contratos obrigatórios (inalterados) do quarto contrato opcional; falhar apenas se `IChannelWebhookProvisioner` estiver registrado para um `ChannelType` sem os três obrigatórios completos (design.md, Decision 3)
- [x] 2.2 Atualizar `ChannelAdapterRegistrationExtensionsTests.cs`: caso feliz sem provisionador (só os três obrigatórios); caso feliz com os quatro; caso de falha com provisionador registrado sem os três obrigatórios

## 3. `ChannelResponse.BuildWebhookUrl` reutilizável (`apps/inbox`)

- [x] 3.1 Promover `ChannelResponse.BuildWebhookUrl` de `private static` para `internal static`, sem mudar sua assinatura nem o formato computado (design.md, Decision 4)

## 4. `CreateChannelCommandHandler` — provisionamento no cadastro (`apps/inbox`)

- [x] 4.1 Construir `Channel` em memória (sem `Add`/`SaveChangesAsync`) antes de resolver o provisionador, para que `channel.Id` esteja disponível para computar `webhookUrl` (design.md, Decision 4)
- [x] 4.2 Resolver `IChannelWebhookProvisioner` via `IChannelAdapterRegistry.GetWebhookProvisioner`; quando presente, chamar `ProvisionAsync(channel.Id, command.Credential, webhookUrl, cancellationToken)` antes de `dbContext.Channels.Add` (design.md, Decision 4)
- [x] 4.3 Em caso de falha do provisionamento, retornar sem persistir nada (design.md, Decision 4); em caso de sucesso, `channel.SetEncryptedCredentials` com a credencial devolvida pelo provisionador antes de `Add`/`SaveChangesAsync`
- [x] 4.4 Adicionar `CreateChannelOutcome.ProvisioningFailed` e `CreateChannelResult.ProvisioningFailed(string errorMessage)` (design.md, Decision 5)
- [x] 4.5 Mapear `CreateChannelOutcome.ProvisioningFailed` em `ChannelEndpoints.CreateChannelAsync` para HTTP 502 com mensagem própria (design.md, Decision 5)
- [x] 4.6 Testes em `CreateChannelCommandHandlerTests.cs`: `channelType` sem provisionador não invoca `GetWebhookProvisioner`/segue fluxo atual inalterado; `channelType` com provisionador invoca `ProvisionAsync` com `channel.Id`/credencial/`webhookUrl` corretos antes do `SaveChangesAsync`; falha do provisionamento não persiste nenhum `Channel` (usando um fake de `IChannelWebhookProvisioner`, mesmo padrão de `AgentReferenceValidator` fake já usado nos testes existentes); sucesso do provisionamento persiste a credencial cifrada devolvida pelo provisionador, não a originalmente enviada

## 5. `UpdateChannelCommandHandler` — reprovisionamento na troca de credencial (`apps/inbox`)

- [x] 5.1 Quando `command.Credential` é informado e o adapter do `channel.ChannelType` já persistido implementa `IChannelWebhookProvisioner`, invocar `ProvisionAsync` com `channel.Id`/`command.Credential`/`webhookUrl` antes de `channel.SetEncryptedCredentials` (design.md, Decision 6)
- [x] 5.2 Em caso de falha do reprovisionamento, retornar sem alterar `channel` nem chamar `SaveChangesAsync`; em caso de sucesso, persistir a credencial devolvida pelo provisionador
- [x] 5.3 Adicionar `UpdateChannelOutcome.ProvisioningFailed` e `UpdateChannelResult.ProvisioningFailed(string errorMessage)`, mapeado em `ChannelEndpoints.UpdateChannelAsync` para HTTP 502 (design.md, Decision 5/6)
- [x] 5.4 Testes em `UpdateChannelCommandHandlerTests.cs`: troca de credencial em canal sem provisionador segue fluxo atual inalterado; troca de credencial em canal com provisionador invoca `ProvisionAsync` com um `secret_token` novo (não reaproveitando o anterior); falha do reprovisionamento mantém as credenciais anteriormente persistidas inalteradas; atualização sem trocar credencial não invoca `GetWebhookProvisioner`

## 6. Adapter Telegram — credencial e validação (`apps/inbox`)

- [x] 6.1 Criar `Channels/Adapters/Telegram/TelegramCredential.cs` (`BotToken`, `WebhookSecret` opcional) (design.md, Decision 7)
- [x] 6.2 Criar `Channels/Adapters/Telegram/TelegramChannelConfigValidator.cs`: JSON inválido ou `BotToken` vazio/whitespace são rejeitados, mesmo padrão de erro (`ChannelConfigValidationResult.Failure`) de `WahaChannelConfigValidator`
- [x] 6.3 Testes unitários em `TelegramChannelConfigValidatorTests.cs`: credencial válida (`{"botToken": "..."}`) aceita; JSON inválido rejeitado; `BotToken` ausente/vazio/whitespace rejeitado com a mensagem correspondente

## 7. Adapter Telegram — envio de resposta (`apps/inbox`)

- [x] 7.1 Criar `Channels/Adapters/Telegram/TelegramOutboundMessageSender.cs`: `POST https://api.telegram.org/bot{BotToken}/sendMessage` com corpo `{chat_id, text}` (design.md, Decision 9)
- [x] 7.2 Testes unitários em `TelegramOutboundMessageSenderTests.cs` com `HttpMessageHandler` fake: confirma a URL exata (incluindo `BotToken` no path), corpo (`chat_id = ContactExternalId` sem transformação, `text = ResponseText`); falha HTTP não-2xx propaga exceção

## 8. Adapter Telegram — provisionamento de webhook (`apps/inbox`)

- [x] 8.1 Criar `Channels/Adapters/Telegram/TelegramWebhookProvisioner.cs`: gera `secret_token` via `RandomNumberGenerator`/`Convert.ToHexString` (32 bytes), chama `POST https://api.telegram.org/bot{BotToken}/setWebhook` com `{url, secret_token}`, devolve `ChannelWebhookProvisioningResult` (design.md, Decision 9)
- [x] 8.2 Checar sucesso via `IsSuccessStatusCode`/`payload.Ok`, nunca contra `error_code`/`description` específicos (design.md, Context/Decision 9)
- [x] 8.3 Em sucesso, devolver a credencial original com `WebhookSecret` preenchido pelo `secret_token` gerado, serializada como JSON
- [x] 8.4 Testes unitários em `TelegramWebhookProvisionerTests.cs` com `HttpMessageHandler` fake: confirma URL/corpo exatos de `setWebhook` (incluindo `secret_token` gerado, não vazio); resposta `{ok: true}` devolve credencial com `WebhookSecret` preenchido; resposta `{ok: false}`/HTTP não-2xx devolve `Failed` com a mensagem de `description`, sem lançar exceção; dois provisionamentos sucessivos geram `secret_token` diferentes

## 9. Adapter Telegram — recepção de webhook (`apps/inbox`)

- [x] 9.1 Criar `Channels/Adapters/Telegram/TelegramWebhookModels.cs` com os shapes internos de `Update`/`Message`/`Chat`/`User`, confirmados na investigação contra `core.telegram.org/bots/api` (design.md, Context)
- [x] 9.2 Criar `Channels/Adapters/Telegram/TelegramInboundWebhookHandler.cs`: abre escopo próprio (`IServiceScopeFactory`), carrega `Channel` por `channelId`; `Channel` inexistente responde 404; `Channel.IsActive == false` responde 401, sem processar (design.md, Decision 8)
- [x] 9.2a Ajustar `Channels/Webhooks/Endpoints/WebhookEndpoints.cs`: `ReceiveAsync` devolve `TypedResults.StatusCode(request.HttpContext.Response.StatusCode)` em vez de `TypedResults.Ok()` fixo, para não sobrescrever um `StatusCode` que o handler já tenha definido (design.md, Decision 8, "Detalhe de implementação")
- [x] 9.3 Decifra a credencial via `IChannelCredentialCipher` (injetado direto no construtor, `Singleton`) e valida o header `X-Telegram-Bot-Api-Secret-Token` contra `TelegramCredential.WebhookSecret`; ausente ou divergente responde 401, sem processar (design.md, Decision 8)
- [x] 9.4 Desserializa `request.Body` como `Update`; ausência de `message` ou de `message.text` é ignorada sem erro, sem chamar o orquestrador (design.md, Decision 8)
- [x] 9.5 Extrai `ExternalId = message.chat.id` (convertido para string) e metadado `{"username": ..., "firstName": ...}` quando presentes em `message.from`; chama `IInboundMessageOrchestrator.ReceiveMessageAsync` (resolvido no mesmo escopo) com esses valores (design.md, Decision 8)
- [x] 9.6 Testes unitários em `TelegramInboundWebhookHandlerTests.cs`, com fixtures de `Update` baseadas na documentação confirmada na investigação, com asserções explícitas (não "processado corretamente" genérico): `channelId` inexistente responde 404 sem chamar o orquestrador; canal com `IsActive == false` responde 401 sem chamar o orquestrador, mesmo com `secret_token` correto; header `X-Telegram-Bot-Api-Secret-Token` ausente responde 401 sem chamar o orquestrador; header divergente do `WebhookSecret` persistido responde 401 sem chamar o orquestrador; header correto processa normalmente; `Update` sem campo `message` (ex. só `callback_query`) é ignorado sem erro, sem chamar o orquestrador; `Update` com `message` sem `text` é ignorado sem erro; `ExternalId` passado ao orquestrador é exatamente `message.chat.id` convertido para string; metadado passado é exatamente `{"username": ..., "firstName": ...}` da fixture, omitindo campos ausentes

## 10. Registro do adapter Telegram (`apps/inbox`)

- [x] 10.1 Registrar `TelegramChannelConfigValidator`, `TelegramOutboundMessageSender`, `TelegramInboundWebhookHandler`, `TelegramWebhookProvisioner` via `AddKeyedSingleton` sob `"telegram"` em `Program.cs`, antes de `ValidateChannelAdapterRegistrations()` (mesmo padrão do WAHA)

## 11. Verificação final (`apps/inbox`)

- [x] 11.1 Rodar toda a suíte de testes de `apps/inbox` (`dotnet test`) — rodada completa via Podman (`DOCKER_HOST` apontando pro socket de `podman machine`, `TESTCONTAINERS_RYUK_DISABLED=true`): 120/121 passam, incluindo todos os testes novos desta fatia e todos os testes existentes (WAHA, catálogo de canais, contrato de plugin) sem alteração de comportamento. A única falha restante (`InboundMessageOrchestratorTests.ReceiveMessageAsync_ConcurrentCallsSameSession_ResolveToSinglePendingDispatch`) é o mesmo bug pré-existente já documentado em `inbox-adapter-waha/tasks.md` (10.1) — não tocado por esta change além do parâmetro `contactMetadata` já existente.
- [ ] 11.2 Checklist de round-trip manual, documentado no README (seção `apps/inbox`, "Checklist de round-trip manual com Telegram") — documentado, mas não executado (depende de um bot Telegram real e de exposição pública do processo, ex. túnel/ngrok, fora do alcance deste ambiente; mesmo estado registrado em `inbox-adapter-waha/tasks.md`, 10.2)
