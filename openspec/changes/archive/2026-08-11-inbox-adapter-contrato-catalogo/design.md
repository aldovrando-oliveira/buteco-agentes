## Context

`apps/inbox` tem hoje dois pontos fechados que impedem o primeiro adapter de
canal real de ser plugado sem reabrir o catálogo:

1. `ChannelType` é um `enum` C# fechado (`WhatsApp`, `Telegram`), persistido
   como `text` no Postgres via `HasConversion<string>()`
   ([AppDbContext.cs:24](../../../apps/inbox/src/Buteco.Inbox/Infrastructure/AppDbContext.cs#L24)).
   A coluna já é texto — a migration original
   ([20260809021250_InitialCreate.cs:19](../../../apps/inbox/src/Buteco.Inbox/Infrastructure/Migrations/20260809021250_InitialCreate.cs#L19))
   criou `ChannelType = table.Column<string>(type: "text", ...)`. Abrir o
   tipo não exige nenhuma migration — só a remoção do enum e do
   `HasConversion` (que deixa de fazer sentido quando o tipo em C# já é
   `string`).
2. `Channel.EncryptedCredentials` é opaco por design
   (`inbox-catalogo-canais`, Decision 3) e continua opaco — mas hoje nada
   valida o formato da credencial antes de cifrar. Não há também nenhum
   ponto de entrega de resposta: `PushNotificationEndpoints.ReceiveAsync`
   ([PushNotificationEndpoints.cs:51-58](../../../apps/inbox/src/Buteco.Inbox/Orchestration/PushNotifications/Endpoints/PushNotificationEndpoints.cs#L51-L58))
   só loga e remove o `PendingDispatch` — Non-Goal explícito de
   `inbox-orquestrador-debounce`: "Nenhuma abstração de entrega/outbox
   para o canal externo — só provar o round-trip. Alternativa descartada:
   construir essa abstração agora, especulando sobre um formato que
   nenhum adapter real ainda confirma."

Esta fatia fecha os dois pontos com contratos de plugin, provados por
implementações de teste registradas via DI — nenhum adapter real (WAHA,
Telegram) é escrito aqui.

Para chegar do `PendingDispatch` até o `Channel` de origem, é necessário o
mesmo join que `DebounceSweepService.TryDispatchAsync` já faz
([DebounceSweepService.cs:84-90](../../../apps/inbox/src/Buteco.Inbox/Orchestration/DebounceSweepService.cs#L84-L90)):
`Session.ContactId → Contact.ChannelId → Channel`, mas selecionando
`channel.Id`/`ChannelType`/`EncryptedCredentials` em vez de `AgentId`. A
resposta do agente propriamente dita vem de
`AgentTask.Status.Message.Parts` (pacote `A2A`, decompilado para
confirmar o shape: `TaskStatus.Message` é um `Message?` com `Parts: List<Part>`,
cada `Part.Text` opcional) — mesmo padrão inverso de
`BuildSendMessageRequest`, que monta `Parts = [Part.FromText(...)]` na ida.

## Goals / Non-Goals

**Goals:**
- `ChannelType` validado em runtime contra adapters efetivamente
  registrados no DI, não mais uma lista fechada no código.
- `IChannelConfigValidator` chamado por
  `CreateChannelCommandHandler`/`UpdateChannelCommandHandler` sobre a
  credencial em texto plano, antes de cifrar.
- `IOutboundMessageSender` chamado por `PushNotificationEndpoints.ReceiveAsync`
  no lugar do atual loga-e-apaga, entregando a resposta do agente ao
  canal de origem.
- `ChannelResponse.webhookUrl` computado a partir de `ChannelId` e da
  `PublicUrlOptions` já existente.
- Um adapter de teste prova os três mecanismos de ponta a ponta, com
  cobertura de teste real (não simulada).
- Registro incompleto de um adapter (validador sem sender, ou vice-versa)
  falha o startup do processo com uma mensagem identificando o
  `ChannelType` problemático, em vez de ser descoberto só na primeira
  mensagem de resposta de um canal mal configurado.

**Non-Goals:**
- Nenhum adapter WAHA ou Telegram real — nenhuma chamada de rede para
  fora de `apps/inbox` nesta fatia.
- Nenhum endpoint HTTP mapeado no path retornado por `webhookUrl` — só o
  valor computado e exposto (ver Riscos).
- Nenhuma entrada em `docker-compose.yml`.
- Nenhuma UI.
- Nenhuma migration de banco.
- Nenhuma política de retry/backoff para `IOutboundMessageSender` —
  mesmo Non-Goal de retry como infraestrutura própria já registrado em
  `inbox-orquestrador-debounce`; falha do sender nesta fatia só é logada
  (ver Decision 3).

## Decisions

### Decision 1: `ChannelType` de enum fechado para string aberta, validada contra adapters registrados

`Channel.ChannelType` e todo o fluxo de request/command passam de `enum
ChannelType` para `string`, normalizada para lower-invariant no limite do
sistema (endpoint HTTP) antes de qualquer uso — tanto para persistir
quanto para consultar o registro de adapters. Isso preserva o
comportamento case-insensitive que `Enum.TryParse(ignoreCase: true)` já
dava hoje ([ChannelEndpoints.cs:132](../../../apps/inbox/src/Buteco.Inbox/Channels/Endpoints/ChannelEndpoints.cs#L132)),
sem depender de um parser de enum.

`ChannelEndpoints.ValidateShape` troca `Enum.TryParse` pela checagem
`IChannelAdapterRegistry.IsRegistered(channelType)` (Decision 5) — um
`channelType` sem adapter correspondente continua rejeitado com HTTP 400
(`ValidationProblem`, mesmo formato de erro já usado), só a mensagem muda
de "lista fixa de valores aceitos" para "nenhum adapter registrado para
este tipo".

`Channels/Entities/ChannelType.cs` é removido — não há mais um tipo C#
fechado para representar isso.

**Consequência sobre os testes existentes**: `CreateChannelCommandHandlerTests.cs`
e `ChannelEndpointsTests.cs` hoje usam `"WhatsApp"`/`"Telegram"` (ou
`ChannelType.WhatsApp`/`.Telegram`) como valores *válidos*. Como nenhum
adapter real é registrado nesta fatia, esses valores deixam de ter
adapter e passariam a ser rejeitados. Esses testes são atualizados para
usar o identificador do adapter de teste (`"test-channel"` — ver Decision
6), não `"WhatsApp"`/`"Telegram"`. O teste que já usa um tipo inválido de
propósito (`"SMS"`, em `CreateChannel_WithInvalidChannelType_ReturnsValidationProblem`)
não muda — continua sem adapter registrado, continua sendo rejeitado.

**Alternativa descartada**: manter o enum fechado, adicionar caso a caso
a cada novo canal. Contraria diretamente o objetivo desta change (abrir
o catálogo para plugins).

**Alternativa descartada**: registrar o adapter de teste desta fatia sob
o identificador `"WhatsApp"`, para não precisar tocar nos testes
existentes. Rejeitada — semanticamente enganoso (um `channelType`
"WhatsApp" resolvendo para um sender que só captura a chamada, sem
nenhuma integração real, contraria o próprio Non-Goal desta fatia) e
colide com o identificador que a change do WAHA real vai querer registrar
em seguida.

### Decision 2: `IChannelConfigValidator`, chamado antes de cifrar

```csharp
// Channels/Adapters/IChannelConfigValidator.cs
public interface IChannelConfigValidator
{
    ChannelConfigValidationResult Validate(string credential);
}

// Channels/Adapters/ChannelConfigValidationResult.cs
public sealed record ChannelConfigValidationResult(bool IsValid, IReadOnlyDictionary<string, string[]> Errors)
{
    public static ChannelConfigValidationResult Success() =>
        new(true, new Dictionary<string, string[]>());

    public static ChannelConfigValidationResult Failure(IReadOnlyDictionary<string, string[]> errors) =>
        new(false, errors);
}
```

`CreateChannelCommandHandler`/`UpdateChannelCommandHandler` resolvem o
validador via `IChannelAdapterRegistry.GetConfigValidator(channelType)`
(Decision 5) e chamam `Validate(command.Credential)` sobre o texto plano,
**antes** de `credentialCipher.Encrypt`. Resultado inválido retorna o
mesmo formato de erro de validação já usado (`ValidationProblem`/HTTP
400) — os detalhes de `Errors` são repassados como estão, sem
reinterpretação pelo handler.

Em `UpdateChannelCommandHandler`, a validação só roda quando `Credential`
é enviado (`!string.IsNullOrWhiteSpace(command.Credential)`) — mesma
condição que já existe hoje para decidir se recriptografa
([UpdateChannelCommandHandler.cs:44](../../../apps/inbox/src/Buteco.Inbox/Channels/Commands/UpdateChannel/UpdateChannelCommandHandler.cs#L44)).
Como `ChannelType` não é atualizável, o validador resolvido é sempre o do
`channel.ChannelType` já persistido.

Nesta fatia, `TestChannelConfigValidator` prova o mecanismo com um schema
fictício de 1-2 campos (ex. credencial precisa ter um separador `|` com
duas partes não vazias) — suficiente para os cenários de teste
aceitar/rejeitar, sem modelar nenhum formato real de WAHA.

### Decision 3: `IOutboundMessageSender`, chamado pelo endpoint receptor de push notification

```csharp
// Channels/Adapters/IOutboundMessageSender.cs
public interface IOutboundMessageSender
{
    Task SendAsync(OutboundMessage message, CancellationToken cancellationToken);
}

// Channels/Adapters/OutboundMessage.cs
public sealed record OutboundMessage(
    Guid ChannelId,
    string DecryptedCredential,
    string ContactExternalId,
    string ResponseText);
```

`PushNotificationEndpoints.ReceiveAsync` passa a, depois de validar o
token (comportamento inalterado):

1. Resolver `(Channel.Id, ChannelType, EncryptedCredentials, Contact.ExternalId)`
   via o join `Session → Contact → Channel` (mesmo padrão de
   `DebounceSweepService.TryDispatchAsync`).
2. Decifrar a credencial com `IChannelCredentialCipher.Decrypt` — **o
   endpoint decifra, não o sender**. Simétrico à Decision 2: assim como o
   validador recebe texto plano antes de cifrar na entrada, o sender
   recebe texto plano depois de decifrar na saída. `IOutboundMessageSender`
   não depende de `IChannelCredentialCipher`.
3. Extrair o texto da resposta de `task.Status.Message?.Parts` —
   concatenando os `Part.Text` não nulos com `'\n'` (mesmo padrão de
   `PendingDispatch.ConcatenatedText()`); partes não textuais (`Raw`/`Url`/`Data`)
   são ignoradas nesta fatia.
4. Se `task.Status.Message` for `null` (task terminal sem mensagem
   associada), **não** chamar o sender — só logar e remover o
   `PendingDispatch`, igual ao comportamento atual. Não há o que entregar.
5. Resolver `IOutboundMessageSender` via
   `IChannelAdapterRegistry.GetOutboundMessageSender(channelType)`
   (Decision 5) e chamar `SendAsync`.
6. Remover o `PendingDispatch` (comportamento já existente, inalterado).

Falha do sender (exceção) nesta fatia é apenas logada — sem retry, sem
nova reapresentação do `PendingDispatch` (que já foi removido). Retry de
entrega ao canal externo é responsabilidade dos adapters reais, fora do
escopo desta fatia (ver Non-Goals).

Nesta fatia, `TestOutboundMessageSender` só captura a chamada (guarda o
último `OutboundMessage` recebido, exposto para asserção em teste) — sem
nenhuma integração de rede.

### Decision 4: `webhookUrl` reaproveita `PublicUrlOptions` existente

`PublicUrlOptions`/`PublicUrl:BaseUrl`
([PublicUrlOptions.cs](../../../apps/inbox/src/Buteco.Inbox/Options/PublicUrlOptions.cs))
já existe — criada em `inbox-orquestrador-debounce`, já registrada em
`Program.cs:22` e nos dois `appsettings*.json`, hoje usada só para montar
a `pushNotificationConfig.url` enviada a `apps/api`. Não é uma
configuração nova.

`ChannelResponse.FromEntity` ganha um segundo parâmetro (`publicUrlBaseUrl`)
usado só para computar:

```csharp
WebhookUrl = $"{publicUrlBaseUrl.TrimEnd('/')}/webhooks/{channel.ChannelType}/{channel.Id}"
```

Nunca persistido — computado a cada resposta, mesmo padrão de
`SupportedInterfaces[].Url` em `backend-a2a-agent-card`
([design.md:249-257](../../../openspec/changes/archive/2026-08-04-backend-a2a-agent-card/design.md#L249-L257)).
Convenção de path `/webhooks/{channelType}/{channelId}` — cada tipo de
adapter pode querer uma rota própria sob o próprio prefixo no futuro
(ex. verificação de assinatura específica do WAHA).

**Nenhum endpoint é mapeado nesse path nesta fatia** — só o valor é
exposto (ver Riscos).

### Decision 5: Resolução por `ChannelType` via DI keyed, exposta através de `IChannelAdapterRegistry`

Registro dos adapters usa **DI keyed** (`AddKeyedSingleton`/`GetKeyedService`,
`Microsoft.Extensions.DependencyInjection`, disponível desde .NET 8) —
primeira vez usado em qualquer um dos três apps do monorepo. Cada módulo
de adapter registra, em `Program.cs`, seu `IChannelConfigValidator` e
`IOutboundMessageSender` sob a mesma chave (`ChannelType` lower-invariant):

```csharp
builder.Services.AddKeyedSingleton<IChannelConfigValidator, TestChannelConfigValidator>("test-channel");
builder.Services.AddKeyedSingleton<IOutboundMessageSender, TestOutboundMessageSender>("test-channel");
```

Os handlers/endpoints não chamam `GetKeyedService` diretamente — dependem
de uma interface própria, para preservar o estilo de teste unitário já
usado no projeto (`CreateChannelCommandHandlerTests` instancia o handler
com `new CreateChannelCommandHandler(dbContext, cipher, validatorMock.Object)`,
sem container de DI):

```csharp
// Channels/Adapters/IChannelAdapterRegistry.cs
public interface IChannelAdapterRegistry
{
    bool IsRegistered(string channelType);
    IChannelConfigValidator GetConfigValidator(string channelType);
    IOutboundMessageSender GetOutboundMessageSender(string channelType);
}
```

A implementação (`ChannelAdapterRegistry`, `Singleton`) recebe
`IServiceProvider` e delega para `GetKeyedService<T>(channelType)`
internamente. `IsRegistered` verifica a presença do
`IChannelConfigValidator` para o tipo (todo adapter é obrigado a
registrar um validador — mesmo que trivial — então essa é a checagem de
existência única, sem estrutura de registro duplicada). Handlers/endpoints
recebem `IChannelAdapterRegistry` no construtor e são testados com
`Mock<IChannelAdapterRegistry>`, mesmo padrão de `Mock<IAgentReferenceValidator>`
já usado.

"Todo adapter é obrigado a registrar um validador" acima é uma convenção
de prosa — nada no `IChannelAdapterRegistry` força um adapter a registrar
os dois serviços juntos. `IsRegistered` continua checando só o validador
(é o suficiente para o caminho de `POST/PUT /channels`, que só precisa do
validador). A integridade do par completo (validador **e** sender para
todo `ChannelType`) é responsabilidade de uma checagem separada, que roda
uma vez no startup — ver Decision 7.

**Alternativa descartada**: `IEnumerable<IChannelConfigValidator>` +
propriedade `SupportedChannelType` escaneada a cada resolução. Rejeitada
— DI keyed é o mecanismo idiomático do framework para exatamente este
caso (resolver serviço por chave conhecida em runtime), evita escanear
uma coleção a cada chamada, e o projeto já centraliza todos os registros
de DI em `Program.cs` no mesmo estilo direto (`AddSingleton<TInterface, TImpl>()`)
que `AddKeyedSingleton` estende naturalmente.

**Alternativa descartada**: expor `IServiceProvider`/`IKeyedServiceProvider`
diretamente nos handlers/endpoints, sem a interface `IChannelAdapterRegistry`.
Rejeitada — quebraria o padrão de teste unitário sem container já
estabelecido em todo o projeto (`CreateChannelCommandHandlerTests`,
`DebounceSweepServiceTests`, etc.), forçando esses testes a montar um
`ServiceCollection` real só para mockar um validador.

### Decision 6: Identificador do adapter de teste desta fatia

O adapter de teste usa o `ChannelType` `"test-channel"` — deliberadamente
não parecido com nenhum canal real (`waha`, `telegram`), para não colidir
com o que a próxima change vai registrar. Usado tanto no validador quanto
no sender de teste, e nos testes de integração/unitários atualizados
(Decision 1).

### Decision 7: Checagem de integridade do registro de adapters no startup

Nada impede hoje um módulo de adapter futuro (a começar pela change do
WAHA) de registrar `IChannelConfigValidator` sob um `ChannelType` e
esquecer de registrar o `IOutboundMessageSender` correspondente sob a
mesma chave, ou vice-versa. Como `IsRegistered` (Decision 5) só confere o
validador, `POST /channels` passaria normalmente para esse `ChannelType`
— o buraco só apareceria quando a primeira resposta de agente precisasse
ser entregue por esse canal, dentro de `PushNotificationEndpoints.ReceiveAsync`,
com `GetOutboundMessageSender` resolvendo para nada. Longe demais do
ponto de registro para ser um erro trivial de rastrear.

Correção: uma checagem de integridade roda uma única vez, no startup do
processo, logo após todos os adapters chamarem `AddKeyedSingleton` em
`Program.cs` e antes de `builder.Build()`. Ela inspeciona diretamente o
`IServiceCollection` (que já expõe, por `ServiceDescriptor`, quais
serviços são *keyed* e sob qual chave — `IsKeyedService`/`ServiceKey`,
disponível desde .NET 8, sem precisar resolver nada via
`IServiceProvider`) e compara o conjunto de chaves registradas para
`IChannelConfigValidator` contra o conjunto de chaves registradas para
`IOutboundMessageSender`. Qualquer chave presente em um conjunto e
ausente no outro é uma falha de composição — o processo não deve subir:

```csharp
// Channels/Adapters/ChannelAdapterRegistrationExtensions.cs
public static class ChannelAdapterRegistrationExtensions
{
    public static void ValidateChannelAdapterRegistrations(this IServiceCollection services)
    {
        var validatorKeys = KeysFor<IChannelConfigValidator>(services);
        var senderKeys = KeysFor<IOutboundMessageSender>(services);

        var missingSender = validatorKeys.Except(senderKeys);
        var missingValidator = senderKeys.Except(validatorKeys);

        var problems = missingSender
            .Select(key => $"'{key}' tem IChannelConfigValidator registrado, mas nenhum IOutboundMessageSender")
            .Concat(missingValidator.Select(key => $"'{key}' tem IOutboundMessageSender registrado, mas nenhum IChannelConfigValidator"))
            .ToList();

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Registro de adapters de canal incompleto: {string.Join("; ", problems)}.");
        }
    }

    private static HashSet<object> KeysFor<TService>(IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ServiceType == typeof(TService) && descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ServiceKey!)
            .ToHashSet();
}
```

Chamada em `Program.cs` imediatamente após o bloco de registro dos
adapters (Decision 5/6):

```csharp
builder.Services.AddKeyedSingleton<IChannelConfigValidator, TestChannelConfigValidator>("test-channel");
builder.Services.AddKeyedSingleton<IOutboundMessageSender, TestOutboundMessageSender>("test-channel");
builder.Services.ValidateChannelAdapterRegistrations();
```

Uma composição incompleta lança `InvalidOperationException` com o
`ChannelType` problemático na mensagem, direto na inicialização —
`builder.Build()` nem chega a rodar. Mesma filosofia fail-fast já usada
em outros pontos do projeto (ex. `apiBaseUrl` obrigatório em `Program.cs:34-35`,
que lança `InvalidOperationException` se `Api:BaseUrl` não estiver
configurado).

Testado diretamente contra o método de extensão, com um `ServiceCollection`
isolado (sem precisar subir o host completo via `WebApplicationFactory` —
mais rápido e aponta exatamente para o `ChannelType` problemático):
registrar só um validador de teste sob uma chave nova e confirmar que
`ValidateChannelAdapterRegistrations()` lança com essa chave na mensagem;
o mesmo com só um sender; e o caminho feliz (par completo) não lança.
Como `Program.cs` chama esse método incondicionalmente antes de
`Build()`, esse teste cobre exatamente a condição sob a qual o host real
falharia ao iniciar, sem o custo de um teste de integração completo.

**Alternativa descartada**: verificar a integridade em runtime, dentro de
`ChannelAdapterRegistry.GetOutboundMessageSender`, lançando quando a
resolução falhar. Rejeitada — descobre o problema só na primeira
mensagem de resposta de um canal mal configurado (exatamente o cenário
que motivou esta Decision), não no startup.

**Alternativa descartada**: um `IHostedService`/health check que roda a
mesma verificação depois de `builder.Build()`. Rejeitada — checar contra
o `IServiceCollection` antes de `Build()` já é suficiente e mais simples
(não depende de resolver nada do container), e falha a composição em vez
de deixar o processo subir e falhar um health check separadamente.

## Risks / Trade-offs

- **[Risco]** `webhookUrl` aponta para uma rota (`/webhooks/{channelType}/{channelId}`)
  que não existe nesta fatia — um cliente que tentasse de fato chamar essa
  URL receberia 404 do próprio Kestrel → **Mitigação**: aceitável porque
  nenhum canal real é cadastrado nesta fatia (Non-Goal); o contrato de
  resposta já fica estável para quando a rota for mapeada na change do
  WAHA, sem precisar de outra mudança de shape em `ChannelResponse`.
- **[Risco]** Falha do `IOutboundMessageSender` (exceção) não tem retry
  nesta fatia — uma resposta do agente pode ser perdida se o adapter real
  falhar ao entregar → **Mitigação**: aceito conscientemente, mesmo
  raciocínio de "retry como infraestrutura própria é Non-Goal" já
  registrado em `inbox-orquestrador-debounce`; adapters reais (change do
  WAHA) decidem sua própria política de retry/fila, se necessário.
- **[Trade-off]** DI keyed é mecanismo novo no monorepo (nenhum
  precedente em `apps/api`/`apps/workers`) → aceito porque é a ferramenta
  correta do framework para "resolver por chave de runtime", e fica
  isolado atrás de `IChannelAdapterRegistry` — nenhum outro código do
  projeto precisa conhecer `GetKeyedService` diretamente.
- **[Trade-off]** Atualizar `CreateChannelCommandHandlerTests.cs` e
  `ChannelEndpointsTests.cs` para trocar `"WhatsApp"`/`"Telegram"` por
  `"test-channel"` é churn real em testes existentes, não simulado →
  aceito porque é consequência direta e honesta de "nenhum adapter
  WhatsApp/Telegram real existe ainda" (Non-Goal desta fatia e da change
  anterior) — manter os testes usando um `channelType` sem adapter
  registrado quebraria os próprios testes, não o comportamento do
  sistema.

## Migration Plan

- Nenhuma migration de banco — `ChannelType` já é `text`
  ([Decision 1](#decision-1-channeltype-de-enum-fechado-para-string-aberta-validada-contra-adapters-registrados)).
  Verificação: rodar `dotnet ef migrations add <nome>` após a mudança de
  tipo em C# e confirmar que o diff gerado é vazio (nenhuma alteração de
  schema); se o EF detectar qualquer diferença, investigar antes de
  prosseguir.
- Deploy aditivo — nenhum endpoint existente muda de contrato HTTP (o
  400 de `channelType` inválido continua 400; `webhookUrl` é um campo
  novo em uma resposta já existente). Rollback é reverter o binário sem
  nenhuma coordenação especial com `apps/api`/`apps/workers` (isolamento
  estrito entre apps, nenhum dos dois é afetado por esta change).
