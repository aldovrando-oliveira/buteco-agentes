## 1. Persistência: banco próprio e infraestrutura EF Core (apps/inbox)

- [x] 1.1 (apps/inbox) Adicionar `Microsoft.EntityFrameworkCore`,
      `Microsoft.EntityFrameworkCore.Design` e
      `Npgsql.EntityFrameworkCore.PostgreSQL` ao `Buteco.Inbox.csproj`
      (sem `Version`, já pinados em `Directory.Packages.props`).
- [x] 1.2 (apps/inbox) Criar `Infrastructure/AppDbContext.cs` e
      `Infrastructure/InfrastructureServiceCollectionExtensions.cs`,
      mesmo mecanismo de `apps/api` (lê `ConnectionStrings:Postgres` via
      `IConfiguration`, `AddDbContext<AppDbContext>`).
- [x] 1.3 (apps/inbox) Adicionar `ConnectionStrings:Postgres` a
      `appsettings.Development.json`, apontando para `Database=buteco_inbox`
      (mesmo host/porta já usados por `apps/api`/`apps/workers`).
- [x] 1.4 (raiz) Rodar `dotnet ef database update` de fato contra a nova
      connection string e **confirmar empiricamente** que o banco
      `buteco_inbox` é criado automaticamente (Decisão 1 do design.md,
      marcada como não verificada nesta exploração). Se não for criado
      automaticamente, adicionar script em `docker-entrypoint-initdb.d/`
      via `docker-compose.yml` para criar o banco no primeiro boot do
      container, e documentar o ajuste na Decisão 1.
- [x] 1.5 (raiz) Adicionar `POSTGRES_INBOX_DB` (ou equivalente) a
      `.env.example` se o passo 1.4 exigir mudança em `docker-compose.yml`;
      caso contrário, documentar em `.env.example` que
      `ConnectionStrings__Postgres` de `apps/inbox` só muda o nome do banco
      na mesma connection string do Postgres já existente.

## 2. Mediator/CQRS (apps/inbox)

- [x] 2.1 (apps/inbox) Adicionar `Mediator.Abstractions` e
      `Mediator.SourceGenerator` ao `Buteco.Inbox.csproj` (mesmo snippet de
      `PrivateAssets`/`IncludeAssets` já usado em `apps/api`).
- [x] 2.2 (apps/inbox) Registrar `AddMediator` em `Program.cs` com
      `options.ServiceLifetime = ServiceLifetime.Scoped` explícito (ver
      Decisão 2 do design.md — sem isso, `Build()` falha por causa da
      dependência em `AppDbContext`, `Scoped`).

## 3. Entidade Channel e criptografia de credenciais (apps/inbox)

- [x] 3.1 (apps/inbox) Criar `Channels/Entities/ChannelType.cs` (enum
      fechado: `WhatsApp`, `Telegram`).
- [x] 3.2 (apps/inbox) Criar `Channels/Entities/Channel.cs` (`Id`,
      `ChannelType`, `Name`, `EncryptedCredentials`, `AgentId`, `IsActive`,
      `CreatedAt`, `UpdatedAt`, construtor + métodos `UpdateDetails`,
      `SetEncryptedCredentials`, `Activate`, `Deactivate` — mesmo formato de
      `McpServer`).
- [x] 3.3 (apps/inbox) Criar `Channels/Security/IChannelCredentialCipher.cs`
      e `Channels/Security/AesGcmChannelCredentialCipher.cs`, cópia
      funcional de `AesGcmMcpCredentialCipher` (nonce 96 bits, chave 256
      bits, formato `nonce || ciphertext || tag` em base64).
- [x] 3.4 (apps/inbox) Criar `Channels/Security/InboxCryptoOptions.cs`
      (seção `Inbox`, propriedade `CredentialEncryptionKey`); registrar
      `IOptions<InboxCryptoOptions>` e `IChannelCredentialCipher` no DI em
      `Program.cs`.
- [x] 3.5 (apps/inbox) Adicionar `Inbox:CredentialEncryptionKey` a
      `appsettings.Development.json` (chave de dev gerada via
      `openssl rand -base64 32`, distinta da chave de `Mcp` já usada por
      `apps/api`/`apps/workers`) e documentar `Inbox__CredentialEncryptionKey`
      em `.env.example`.
- [x] 3.6 (apps/inbox) Configurar `AppDbContext.OnModelCreating` para a
      tabela `channels`: `ChannelType` com `HasConversion<string>()`,
      `EncryptedCredentials` como coluna de texto (não jsonb tipado — o
      valor já é a string cifrada em base64), `AgentId` como `Guid` sem
      `HasOne`/FK (não há tabela `agents` neste banco).
- [x] 3.7 (apps/inbox) Gerar a migration inicial (`dotnet ef migrations add
      InitialCreate`) com a tabela `channels`.

## 4. Validação de AgentId contra apps/api (apps/inbox)

- [x] 4.1 (apps/inbox) Criar `Options/ApiOptions.cs` (seção `Api`,
      propriedade `BaseUrl`); adicionar `Api:BaseUrl` a
      `appsettings.Development.json` (`http://localhost:5017`) e
      `Api__BaseUrl` a `.env.example`.
- [x] 4.2 (apps/inbox) Criar `Agents/IAgentReferenceValidator.cs` e
      `Agents/AgentReferenceValidator.cs`: chama `GET /agents/{id}` em
      `apps/api` via `HttpClient` nomeado (`IHttpClientFactory`), retorna um
      resultado com três estados (encontrado / não encontrado / falha de
      comunicação) — nunca lança exceção para o caller. Mapeamento
      explícito: HTTP 200 → encontrado (independente de `isActive` no
      payload, ver Decisão 7 do design.md — o validador não inspeciona
      esse campo); HTTP 404 → não encontrado; qualquer outro status (ex.
      5xx) ou exceção de transporte (timeout, conexão recusada, host
      inalcançável) → falha de comunicação.
- [x] 4.2.1 (apps/inbox) Adicionar comentário inline em
      `AgentReferenceValidator` (ou no ponto onde
      `CreateChannelCommandHandler`/`UpdateChannelCommandHandler` chamam o
      validador) explicando que `isActive` do agente não é verificado,
      referenciando a Decisão 7 do design.md — mesmo padrão de comentário
      WHY já usado no projeto (ex. `AesGcmMcpCredentialCipher`,
      `ReplaceAgentMcpServersCommandHandler`).
- [x] 4.3 (apps/inbox) Registrar em `Program.cs`:
      `builder.Services.AddHttpClient(AgentReferenceValidator.HttpClientName,
      client => client.BaseAddress = new Uri(apiBaseUrl)).ConfigureHttpClient(client
      => client.Timeout = TimeSpan.FromSeconds(5))` (mesmo padrão de
      `PushNotificationSender` em `apps/workers`).

## 5. Commands, Queries e Endpoints de Channel (apps/inbox)

- [x] 5.1 (apps/inbox) Criar `Channels/Requests/CreateChannelRequest.cs` e
      `Channels/Requests/UpdateChannelRequest.cs` (DTOs HTTP, campos
      nuláveis, mesmo padrão de `CreateMcpServerRequest`).
- [x] 5.2 (apps/inbox) Criar `Channels/Responses/ChannelResponse.cs`
      (`Id`, `ChannelType`, `Name`, `AgentId`, `IsActive`, `CreatedAt`,
      `UpdatedAt` — sem nenhum campo de credencial).
- [x] 5.3 (apps/inbox) Criar `Channels/Commands/CreateChannel/` (Command,
      Handler, Result com estados sucesso/AgentId inválido/apps/api
      inalcançável) — Handler chama `IAgentReferenceValidator` antes de
      persistir.
- [x] 5.4 (apps/inbox) Criar `Channels/Commands/UpdateChannel/` (mesmo
      formato, revalida `AgentId` só quando ele muda em relação ao
      persistido).
- [x] 5.5 (apps/inbox) Criar `Channels/Commands/ActivateChannel/` e
      `Channels/Commands/DeactivateChannel/` (mesmo formato de
      `ActivateMcpServer`/`DeactivateMcpServer`).
- [x] 5.6 (apps/inbox) Criar `Channels/Queries/GetChannelById/` e
      `Channels/Queries/ListChannels/`.
- [x] 5.7 (apps/inbox) Criar `Channels/Endpoints/ChannelEndpoints.cs`
      mapeando `POST/GET/PUT /channels`, `GET /channels/{id}`,
      `POST /channels/{id}/activate`, `POST /channels/{id}/deactivate` —
      validação de shape do request + `mediator.Send` + mapeamento
      `TypedResults`, mesmo padrão de `McpServerEndpoints`.
- [x] 5.8 (apps/inbox) Registrar `app.MapChannelEndpoints()` em
      `Program.cs`.

## 6. Testes (apps/inbox)

- [x] 6.1 (apps/inbox) Criar `tests/Buteco.Inbox.Tests/Support/InboxFactoryFixture.cs`,
      mirror de `ApiFactoryFixture`: `WebApplicationFactory<Program>` +
      `Testcontainers.PostgreSql`, chamando
      `dbContext.Database.MigrateAsync()` em `InitializeAsync`. Adicionar
      `Testcontainers`/`Testcontainers.PostgreSql` ao
      `Buteco.Inbox.Tests.csproj`.
- [x] 6.2 (apps/inbox) Criar `tests/Buteco.Inbox.Tests/Support/FakeAgentApiHttpMessageHandler.cs`,
      mirror de `FakeMcpServerHttpMessageHandler`: responde `GET
      /agents/{id}` com três formas de resultado configuráveis pelo teste —
      (a) HTTP 200 com um payload de agente, incluindo `isActive`
      configurável (`true`/`false`, para cobrir o caso de agente inativo da
      Decisão 7); (b) HTTP 404; (c) HTTP 500 (erro de servidor — resposta
      HTTP completa, sem exceção de transporte); e separadamente (d) lança
      `HttpRequestException` (host inalcançável, sem resposta HTTP
      nenhuma) — (c) e (d) são cenários distintos e ambos precisam existir
      no fake, não só (d). Substitui o `HttpMessageHandler` primário do
      cliente nomeado em `InboxFactoryFixture.ConfigureWebHost`.
- [x] 6.3 (apps/inbox) Criar `AesGcmChannelCredentialCipherTests.cs`
      (roundtrip encrypt/decrypt, chave inválida, valor corrompido) —
      mirror de `AesGcmMcpCredentialCipherTests` (apps/api).
- [x] 6.4 (apps/inbox) Criar `CreateChannelCommandHandlerTests.cs`
      (handler isolado, `IChannelCredentialCipher` e
      `IAgentReferenceValidator` reais ou fake em memória — cobre os três
      estados do validador de AgentId: encontrado, não encontrado, falha
      de comunicação).
- [x] 6.5 (apps/inbox) Criar `ChannelEndpointsTests.cs` cobrindo, via
      `InboxFactoryFixture`: CRUD completo; credencial nunca aparece em
      nenhuma resposta de leitura (create/get/list/update); `AgentId`
      válido e ativo (sucesso); `AgentId` válido porém de agente **inativo**
      é permitido tanto em `POST /channels` quanto em `PUT /channels/{id}`
      (Decisão 7/spec: "Criar canal vinculado a agente inativo é
      permitido", "Atualizar canal vinculado a agente inativo é
      permitido"); `AgentId` inexistente (404 de `apps/api` → 400 local);
      `apps/api` respondendo **HTTP 500** é rejeitado (400/502) da mesma
      forma que host inalcançável — caso distinto de
      `HttpRequestException`, cobre a lacuna entre a prosa da Decisão 4
      ("erro 5xx" tratado como falha) e o teste; `apps/api` inalcançável
      via `HttpRequestException` (400/502); ativar/desativar idempotente;
      `ChannelType` inválido rejeitado (400); `GET`/`PUT`/activate/
      deactivate de id inexistente retornam 404.

## 7. Documentação

- [x] 7.1 (raiz) Atualizar `README.md`: seção "Como subir cada app" para
      `apps/inbox` (aplicar migration antes do primeiro `dotnet run`, mesmo
      texto de `apps/api`) e seção "Como testar cada app" (remover a nota
      atual de que `apps/inbox` "não tem persistência ainda" / "não
      depende de Docker/Podman").
- [x] 7.2 (raiz) Atualizar `.env.example` com as novas variáveis de
      `apps/inbox` (`ConnectionStrings__Postgres` próprio,
      `Inbox__CredentialEncryptionKey`, `Api__BaseUrl`), agrupadas em uma
      seção nova, mesmo estilo das seções já existentes.
