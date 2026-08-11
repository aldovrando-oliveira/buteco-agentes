## 1. Contrato de adapter (apps/inbox)

- [x] 1.1 Criar `Channels/Adapters/IChannelConfigValidator.cs` e
  `Channels/Adapters/ChannelConfigValidationResult.cs` (design.md,
  Decision 2)
- [x] 1.2 Criar `Channels/Adapters/IOutboundMessageSender.cs` e
  `Channels/Adapters/OutboundMessage.cs` (design.md, Decision 3)
- [x] 1.3 Criar `Channels/Adapters/IChannelAdapterRegistry.cs` e a
  implementação `ChannelAdapterRegistry` (`Singleton`, delega para
  `GetKeyedService<T>(channelType)`) (design.md, Decision 5)
- [x] 1.4 Criar `Channels/Adapters/Testing/TestChannelConfigValidator.cs`
  (schema fictício de 1-2 campos) e
  `Channels/Adapters/Testing/TestOutboundMessageSender.cs` (só captura a
  chamada), registrados sob `channelType = "test-channel"` (design.md,
  Decision 6)
- [x] 1.5 Criar `Channels/Adapters/ChannelAdapterRegistrationExtensions.cs`
  com `ValidateChannelAdapterRegistrations(this IServiceCollection)`:
  compara as chaves keyed registradas para `IChannelConfigValidator`
  contra as de `IOutboundMessageSender` (via `ServiceDescriptor.IsKeyedService`/
  `ServiceKey`) e lança `InvalidOperationException` identificando o(s)
  `ChannelType` incompleto(s) quando os conjuntos não coincidem
  (design.md, Decision 7)

## 2. ChannelType aberto (apps/inbox)

- [x] 2.1 Remover `Channels/Entities/ChannelType.cs` (enum)
- [x] 2.2 Alterar `Channel.ChannelType` de `ChannelType` para `string`
  (`Channels/Entities/Channel.cs`)
- [x] 2.3 Remover `HasConversion<string>()` da configuração de
  `ChannelType` em `AppDbContext.cs` (deixa de fazer sentido com o tipo
  já sendo `string`)
- [x] 2.4 Rodar `dotnet ef migrations add` e confirmar que o diff gerado
  é vazio (nenhuma alteração de schema); se detectar qualquer diferença,
  investigar antes de prosseguir — não deve gerar migration nova
  (design.md, Migration Plan)
- [x] 2.5 Atualizar `CreateChannelCommand`/`UpdateChannelCommand` e
  `CreateChannelRequest`/`UpdateChannelRequest` para `ChannelType: string`
- [x] 2.6 Em `ChannelEndpoints.ValidateShape`, trocar `Enum.TryParse` por
  `IChannelAdapterRegistry.IsRegistered(channelType)` (normalizado para
  lower-invariant), com nova mensagem de erro (design.md, Decision 1)

## 3. Validação de configuração por adapter (apps/inbox)

- [x] 3.1 `CreateChannelCommandHandler`: resolver o validador via
  `IChannelAdapterRegistry.GetConfigValidator(command.ChannelType)` e
  chamar `Validate(command.Credential)` antes de `credentialCipher.Encrypt`,
  retornando erro de validação quando inválido (design.md, Decision 2)
- [x] 3.2 `UpdateChannelCommandHandler`: mesma chamada de validação,
  só quando `command.Credential` é informado (mesma condição já usada
  para recriptografar) (design.md, Decision 2)
- [x] 3.3 Propagar o novo outcome de "credencial inválida" nos
  `CreateChannelResult`/`UpdateChannelResult` e no mapeamento HTTP em
  `ChannelEndpoints` (`ValidationProblem`/HTTP 400)

## 4. Entrega de resposta via sender (apps/inbox)

- [x] 4.1 Em `PushNotificationEndpoints.ReceiveAsync`, após validar o
  token: resolver `(Channel.Id, ChannelType, EncryptedCredentials,
  Contact.ExternalId)` via join `Session → Contact → Channel` (mesmo
  padrão de `DebounceSweepService.TryDispatchAsync`) (design.md,
  Decision 3)
- [x] 4.2 Extrair o texto de resposta de `task.Status.Message?.Parts`
  (concatenar `Part.Text` não nulos com `'\n'`, mesmo padrão de
  `PendingDispatch.ConcatenatedText()`); se `Message` for `null`, pular
  para 4.4 sem chamar nenhum sender (design.md, Decision 3)
- [x] 4.3 Decifrar a credencial via `IChannelCredentialCipher.Decrypt` e
  invocar `IChannelAdapterRegistry.GetOutboundMessageSender(channelType).SendAsync(...)`
  com o `OutboundMessage` montado; capturar e logar exceção do sender sem
  interromper a remoção do `PendingDispatch` (design.md, Decision 3,
  Risks)
- [x] 4.4 Manter a remoção do `PendingDispatch` ao final (comportamento
  já existente)

## 5. webhookUrl (apps/inbox)

- [x] 5.1 `ChannelResponse.FromEntity`: novo parâmetro `publicUrlBaseUrl`,
  computar `WebhookUrl = "{baseUrl}/webhooks/{channelType}/{channelId}"`
  (design.md, Decision 4)
- [x] 5.2 Atualizar os call sites de `ChannelResponse.FromEntity` (queries
  `GetChannelById`/`ListChannels`, commands `CreateChannel`/`UpdateChannel`/
  `ActivateChannel`/`DeactivateChannel`) para injetar `IOptions<PublicUrlOptions>`
  (já registrado em `Program.cs`) e passar `BaseUrl`

## 6. Registro DI (apps/inbox)

- [x] 6.1 `Program.cs`: registrar `IChannelAdapterRegistry` (`Singleton`)
  e os adapters de teste via `AddKeyedSingleton` sob `"test-channel"`
  (design.md, Decision 5, Decision 6)
- [x] 6.2 `Program.cs`: chamar `builder.Services.ValidateChannelAdapterRegistrations()`
  logo após o bloco de registro dos adapters (6.1), antes de
  `builder.Build()` (design.md, Decision 7)

## 7. Testes (apps/inbox)

- [x] 7.1 Atualizar `CreateChannelCommandHandlerTests.cs` e
  `ChannelEndpointsTests.cs`: trocar `ChannelType.WhatsApp`/`.Telegram`/
  `"WhatsApp"`/`"Telegram"` por `"test-channel"` nos cenários que hoje
  esperam sucesso (design.md, Decision 1, Risks). Mesmo ajuste aplicado
  a `DebounceRestartAndConcurrencyTests.cs`, `ContactEndpointsTests.cs`,
  `InboundMessageOrchestratorTests.cs`, `ContactSessionResolverTests.cs`
  e `DebounceSweepServiceTests.cs`, que também semeavam
  `new Channel(ChannelType.WhatsApp, ...)` como dado de apoio — quebrados
  pela remoção do enum, fora do escopo original desta tarefa mas
  necessários para o build.
- [x] 7.2 Teste: `channelType` sem adapter registrado é rejeitado com 400
  (cenário `"SMS"` já existente continua cobrindo isso, sem alteração)
- [x] 7.3 Teste: `channelType` de adapter registrado (`"test-channel"`) é
  aceito (coberto pelos cenários de sucesso renomeados em 7.1 — nenhum
  teste dedicado adicional)
- [x] 7.4 Teste: `TestChannelConfigValidator` rejeita credencial inválida
  e aceita credencial válida, provando que
  `CreateChannelCommandHandler`/`UpdateChannelCommandHandler` chamam o
  validador certo para o tipo certo. Implementado com um schema
  sentinela (`TestChannelConfigValidator.RejectedCredential`) em vez do
  formato `identificador|segredo` cogitado no design.md, para não exigir
  reformatar toda credencial de teste já existente — só o valor
  sentinela é rejeitado, qualquer outro não vazio é aceito.
- [x] 7.5 Teste: `UpdateChannelCommandHandler` não chama o validador
  quando `Credential` não é informado na atualização
- [x] 7.6 Teste: `webhookUrl` computado corretamente a partir de
  `ChannelId`/`PublicUrl:BaseUrl` em `ChannelResponse`
- [x] 7.7 Teste: push notification recebida com mensagem de resposta
  invoca `TestOutboundMessageSender` registrado para o `ChannelType`
  correto, com o texto de resposta e a credencial decifrada corretos
- [x] 7.8 Teste: push notification recebida sem mensagem de resposta
  associada não invoca nenhum sender, e o `PendingDispatch` é removido
  normalmente
- [x] 7.9 Teste: `ValidateChannelAdapterRegistrations()` lança
  `InvalidOperationException` identificando a chave, quando um
  `ServiceCollection` tem `IChannelConfigValidator` registrado sob uma
  chave sem `IOutboundMessageSender` correspondente (design.md, Decision 7)
- [x] 7.10 Teste: mesmo caso invertido — `IOutboundMessageSender`
  registrado sem `IChannelConfigValidator` correspondente também lança,
  identificando a chave (design.md, Decision 7)
- [x] 7.11 Teste: `ValidateChannelAdapterRegistrations()` não lança quando
  todo `ChannelType` registrado tem os dois serviços presentes sob a
  mesma chave (caminho feliz, cobre o registro real de `Program.cs`)

**Verificação**: `dotnet build` (src + tests) limpo, 0 avisos/erros.
`dotnet test` filtrado para os testes que não dependem de Docker/Testcontainers
(`CreateChannelCommandHandlerTests`, `UpdateChannelCommandHandlerTests`,
`ChannelAdapterRegistrationExtensionsTests`) — 12/12 aprovados. Os testes
de integração via `InboxFactoryFixture`/`OrchestrationFactoryFixture`
(`ChannelEndpointsTests`, `PushNotificationEndpointsTests`, e os demais
arquivos tocados em 7.1) não foram executados nesta sessão — Docker
indisponível no ambiente. Recomenda-se rodar a suíte completa
(`dotnet test`) com Docker disponível antes de mesclar.
