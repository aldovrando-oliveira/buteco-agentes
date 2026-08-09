## 1. Entidades Contact e Session (apps/inbox)

- [x] 1.1 (apps/inbox) Criar `Contacts/Entities/Contact.cs` (`Id`,
      `ChannelId`, `ExternalId`, `CreatedAt`, construtor privado + público —
      mesmo formato de `Channel.cs`).
- [x] 1.2 (apps/inbox) Criar `Contacts/Entities/Session.cs` (`Id`,
      `ContactId`, `ContextId` (`string`, não `Guid` — ver Decisão 2 do
      design.md), `StartedAt`, `LastActivityAt`, `ClosedAt` nullable,
      construtor privado + público, método `Touch()`/`RegisterActivity()`
      que atualiza `LastActivityAt`).
- [x] 1.3 (apps/inbox) Configurar `AppDbContext.OnModelCreating` para as
      tabelas `contacts` e `sessions`: `Contact.ChannelId` como FK real para
      `Channel.Id` (`HasOne`/`WithMany`, `OnDelete(Restrict)` — ver Decisão
      1 do design.md); unique constraint em `Contact(ChannelId, ExternalId)`
      (`HasIndex(...).IsUnique()` — primeiro índice único do projeto);
      `Session.ContactId` como FK para `Contact.Id`; `Session.ContextId`
      como coluna de texto.
- [x] 1.4 (apps/inbox) Adicionar `DbSet<Contact>` e `DbSet<Session>` a
      `AppDbContext.cs`.
- [x] 1.5 (apps/inbox) Gerar a migration aditiva (`dotnet ef migrations add
      AddContactSessionCrm`) com as tabelas `contacts` e `sessions`.

## 2. Resolução de sessão por inatividade (apps/inbox)

- [x] 2.1 (apps/inbox) Criar `Contacts/SessionOptions.cs` (seção `Session`,
      propriedade `InactivityTimeout` do tipo `TimeSpan`); adicionar
      `Session:InactivityTimeout` (`"01:00:00"`) a
      `appsettings.Development.json` e a `appsettings.json` (default de
      produção também `01:00:00`, ver Decisão 3 do design.md); documentar
      `Session__InactivityTimeout` em `.env.example`.
- [x] 2.2 (apps/inbox) Criar `Contacts/IContactSessionResolver.cs`
      (`FindOrCreateSessionAsync(Guid channelId, string externalId,
      CancellationToken cancellationToken)`).
- [x] 2.3 (apps/inbox) Criar `Contacts/ContactSessionResolver.cs`: busca
      `Contact` por `(channelId, externalId)`; se não existir, tenta criar e
      captura violação de unique constraint (`DbUpdateException`
      envolvendo violação de índice único do Npgsql) para re-consultar em
      vez de propagar erro (Decisão 7 do design.md). **Imediatamente após
      capturar a exceção**, destacar a entidade `Contact` malsucedida do
      change tracker (`dbContext.Entry(failedContact).State =
      EntityState.Detached`) antes de prosseguir — o EF Core não reverte
      automaticamente o estado `Added` de uma entidade cujo
      `SaveChangesAsync` falhou; sem o detach, o `SaveChangesAsync`
      seguinte (o da `Session`, passo abaixo, que roda no mesmo
      `DbContext` `Scoped`) tentaria reinserir esse mesmo `Contact` ainda
      rastreado e falharia de novo, dessa vez sem captura. Só depois do
      detach: busca a `Session` mais recente do `Contact` (agora
      re-consultado do banco, `OrderByDescending(LastActivityAt)`); se não
      existir ou `UtcNow - LastActivityAt > InactivityTimeout`, cria nova
      `Session` (`ContextId = Guid.NewGuid().ToString()`); senão reaproveita
      e atualiza `LastActivityAt` via `SaveChangesAsync`.
- [x] 2.4 (apps/inbox) Registrar `IOptions<SessionOptions>` e
      `IContactSessionResolver` (`AddScoped`, já que depende de
      `AppDbContext`, que é `Scoped`) no DI em `Program.cs`.

## 3. Queries e Endpoints de Contact/Session (apps/inbox)

- [x] 3.1 (apps/inbox) Criar `Contacts/Responses/ContactResponse.cs`
      (`Id`, `ChannelId`, `ExternalId`, `CreatedAt`) e
      `Contacts/Responses/SessionResponse.cs` (`Id`, `ContactId`,
      `ContextId`, `StartedAt`, `LastActivityAt`, `ClosedAt`).
- [x] 3.2 (apps/inbox) Criar `Contacts/Queries/ListContacts/` (`Query` +
      `QueryHandler`, mesmo formato de `ListChannelsQueryHandler`).
- [x] 3.3 (apps/inbox) Criar `Contacts/Queries/GetContactSessions/` (`Query`
      recebendo `ContactId` + `QueryHandler` retornando `null` se o
      `Contact` não existir, lista — possivelmente vazia — de
      `SessionResponse` caso contrário).
- [x] 3.4 (apps/inbox) Criar `Contacts/Endpoints/ContactEndpoints.cs`
      mapeando `GET /contacts` e `GET /contacts/{id}/sessions` (404 quando
      o `Contact` não existe, `200 []` quando existe sem sessão — mesmo
      padrão de `GetChannelByIdAsync`).
- [x] 3.5 (apps/inbox) Registrar `app.MapContactEndpoints()` em
      `Program.cs`.

## 4. Testes (apps/inbox)

- [x] 4.1 (apps/inbox) Criar `ContactSessionResolverTests.cs` usando
      `InboxFactoryFixture`: primeira chamada cria `Contact` e `Session`;
      chamada subsequente dentro do timeout reaproveita a mesma `Session` e
      atualiza `LastActivityAt`; chamada após o timeout (inserindo a
      `Session` anterior já com `LastActivityAt` no passado via SQL direto,
      sem `IClock` — mesmo mecanismo de `ConversationHistoryTests.cs`,
      `apps/workers`) cria uma nova `Session` (novo `ContextId`) para o
      mesmo `Contact`; duas chamadas com o mesmo `(ChannelId, ExternalId)`
      sempre resolvem para o mesmo `Contact`; o mesmo `ExternalId` em
      `ChannelId` diferentes resolve para `Contact`s distintos.
- [x] 4.2 (apps/inbox) Adicionar teste de corrida em
      `ContactSessionResolverTests.cs`: duas chamadas concorrentes
      (`Task.WhenAll`) com o mesmo `(ChannelId, ExternalId)` resolvem para
      o mesmo `Contact`, sem lançar exceção não tratada (cobre o
      retry-as-find da Decisão 7).
- [x] 4.3 (apps/inbox) Criar `ContactEndpointsTests.cs` cobrindo, via
      `InboxFactoryFixture`: `GET /contacts` retorna lista vazia sem
      contatos e lista completa com contatos existentes (criados via
      `IContactSessionResolver` diretamente no teste, já que não há endpoint
      de escrita); `GET /contacts/{id}/sessions` retorna a lista de sessões
      de um contato existente, lista vazia para contato existente sem
      sessão, e 404 para contato inexistente.

## 5. Documentação

- [x] 5.1 (raiz) Atualizar `.env.example` com `Session__InactivityTimeout`
      na seção de `apps/inbox`, mesmo estilo das variáveis já documentadas
      ali (`Inbox__CredentialEncryptionKey`, `Api__BaseUrl`).
