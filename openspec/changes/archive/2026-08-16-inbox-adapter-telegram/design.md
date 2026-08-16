## Context

`inbox-adapter-waha` provou os três contratos de plugin do catálogo de
canais (`IChannelConfigValidator`, `IOutboundMessageSender`,
`IInboundWebhookHandler`) com o WAHA, mas deixou dois riscos aceitos
conscientemente, ambos documentados no design.md daquela change:

1. **Configuração de sessão manual** (Decision 3 do WAHA) — motivada por
   uma ambiguidade real: a API de sessões do WAHA (`POST /api/sessions`)
   costuma exigir uma API key de admin, potencialmente distinta da usada
   para `sendText`, e automatizar acoplaria o cadastro do canal à
   disponibilidade de um serviço externo sem resposta clara para o que
   fazer se ele estiver fora do ar.
2. **Nenhuma verificação de autenticidade do webhook de entrada**
   (Decision 6 do WAHA) — `AuthToken` da credencial WAHA autentica só as
   chamadas de saída; qualquer cliente HTTP que descubra um `channelId`
   pode postar um payload falso.

O Telegram Bot API não tem a ambiguidade do risco 1: um único token por
bot (`BotToken`) autentica toda chamada, incluindo `setWebhook`, em
`https://api.telegram.org/bot<token>/METODO`. Não existe conceito de
sessão separada do bot, nem de escopo de credencial diferente para
configuração vs. uso. Isso torna a automação do webhook segura de fazer
aqui, ao contrário do WAHA. O Telegram também resolve nativamente o risco
2: `setWebhook` aceita um `secret_token` opcional (1-256 caracteres,
`A-Z a-z 0-9 _ -`), devolvido em toda chamada de webhook subsequente no
header `X-Telegram-Bot-Api-Secret-Token` — verificável sem nenhuma
infraestrutura adicional.

Investigação obrigatória feita antes deste design (via `/opsx:explore`,
contra `https://core.telegram.org/bots/api` via `web_fetch`, não busca):

- `setWebhook`: `url` (obrigatório), `secret_token` (opcional, formato
  acima). Outros parâmetros opcionais (`certificate`, `max_connections`,
  `allowed_updates`, `drop_pending_updates`) não são usados nesta fatia.
- `Update`: `update_id` + no máximo um campo entre vários mutuamente
  exclusivos (`message`, `edited_message`, `channel_post`,
  `callback_query`, etc.) — filtrar por presença de `message` é
  estruturalmente suficiente para ignorar os demais tipos, mesmo
  raciocínio da Decision 5 do WAHA (assinar só o evento certo evita filtro
  defensivo).
- `Message`: `chat.id` (Integer), `from.{id, is_bot, first_name,
  username?}`, `text?`.
- Erro genérico da API: `{ ok: false, error_code: Integer, description:
  String, parameters?: ResponseParameters }`. A documentação oficial
  avisa que `error_code` é "subject to change" e não fixa o código/
  descrição exatos para token inválido — confirmado que não existe
  contrato formal para isso. Implicação direta na Decision 9: checar
  `ok`/status HTTP genericamente, nunca casar contra um `error_code` ou
  texto de `description` específico.
- Limites de taxa reais: 1 msg/seg por chat, 30/seg total, 20/min por
  grupo — sem tratamento nesta fatia (Non-Goals, Decision 10).

Investigação obrigatória feita via leitura do código real (não assumida):

- `WebhookEndpoints.ReceiveAsync`
  ([WebhookEndpoints.cs:19-41](../../../apps/inbox/src/Buteco.Inbox/Channels/Webhooks/Endpoints/WebhookEndpoints.cs#L19-L41))
  não checa `Channel.IsActive` hoje — resolve `Channel` só por id, resolve
  handler por `ChannelType`, processa incondicionalmente. Nenhum
  precedente em nenhum adapter anterior (Decision 8).
- `PushNotificationEndpoints.DeliverResponseAsync`
  ([PushNotificationEndpoints.cs:93-140](../../../apps/inbox/src/Buteco.Inbox/Orchestration/PushNotifications/Endpoints/PushNotificationEndpoints.cs#L93-L140)),
  a direção de saída, também não checa `IsActive`. Hoje `IsActive` não tem
  efeito prático em nenhuma direção — ver Risks.

## Goals / Non-Goals

**Goals:**
- Segunda implementação real dos três contratos de plugin existentes,
  provando-os contra uma plataforma com modelo de credencial mais simples
  que o WAHA (token único, sem sessão).
- Quarto contrato de plugin, **opcional**, para provisionamento
  automático de configuração externa no cadastro/atualização de canal —
  primeiro ponto de extensão do catálogo que nem todo `ChannelType`
  precisa implementar.
- `CreateChannelCommandHandler`/`UpdateChannelCommandHandler` invocam esse
  contrato quando o adapter o implementa; falha na chamada externa impede
  a operação inteira (nenhum canal Telegram é persistido com webhook não
  registrado, nenhuma credencial é substituída sem o webhook
  correspondente).
- Verificação de autenticidade do webhook de entrada via `secret_token`
  nativo — primeiro adapter do catálogo a fechar esse risco.
- Canal Telegram desativado rejeita webhooks recebidos — primeiro
  precedente de enforcement de `IsActive` sobre processamento de
  mensagens, escopado a este adapter.

**Non-Goals:**
- Nenhuma reconfiguração automática de webhook em `PUT /channels/{id}`
  além de troca de `BotToken` (Decision 6) — nome/`AgentId` mudando
  sozinhos não reprovisionam nada.
- Nenhum `deleteWebhook` automático ao desativar um canal — desativação
  continua só um campo de estado; o efeito prático fica inteiramente na
  recepção do webhook (Decision 8), não na ação de desativar.
- Nenhum tratamento de rate limit do Telegram (Decision 10).
- Nenhuma correção da assimetria de `IsActive` em
  `PushNotificationEndpoints` (saída) — nomeada como Risk, não corrigida
  aqui.
- Nenhuma mudança de comportamento do WAHA — `WebhookEndpoints` genérico
  não passa a checar `IsActive` para todo `ChannelType`; a checagem fica
  dentro de `TelegramInboundWebhookHandler`, escopada a este adapter
  (Decision 8, "Alternativa descartada").
- Nenhum SDK de terceiros para o Telegram Bot API — `TelegramOutboundMessageSender`/
  `TelegramWebhookProvisioner` usam `HttpClient` bruto, mesmo padrão do
  WAHA (`WahaOutboundMessageSender`). Nenhuma dependência NuGet nova.
- Nenhuma UI.

## Árvore de pastas (novo/modificado, `apps/inbox`)

```
apps/inbox/src/Buteco.Inbox/
├── Channels/
│   ├── Adapters/
│   │   ├── IChannelWebhookProvisioner.cs                    [NOVO] Decision 1
│   │   ├── ChannelWebhookProvisioningResult.cs               [NOVO] Decision 2
│   │   ├── IChannelAdapterRegistry.cs                        [MOD]  Decision 1
│   │   ├── ChannelAdapterRegistry.cs                         [MOD]  Decision 1
│   │   ├── ChannelAdapterRegistrationExtensions.cs           [MOD]  Decision 3
│   │   └── Telegram/
│   │       ├── TelegramCredential.cs                         [NOVO] Decision 7
│   │       ├── TelegramChannelConfigValidator.cs             [NOVO] Decision 8
│   │       ├── TelegramOutboundMessageSender.cs               [NOVO] Decision 9 (parte 1)
│   │       ├── TelegramWebhookProvisioner.cs                  [NOVO] Decision 9 (parte 2)
│   │       ├── TelegramInboundWebhookHandler.cs                [NOVO] Decision 11
│   │       └── TelegramWebhookModels.cs                       [NOVO] shapes de Update/Message (internal)
│   ├── Commands/
│   │   ├── CreateChannel/
│   │   │   ├── CreateChannelCommandHandler.cs                 [MOD]  Decision 4
│   │   │   └── CreateChannelResult.cs                         [MOD]  Decision 5
│   │   └── UpdateChannel/
│   │       ├── UpdateChannelCommandHandler.cs                 [MOD]  Decision 6
│   │       └── UpdateChannelResult.cs                         [MOD]  Decision 6
│   ├── Endpoints/
│   │   └── ChannelEndpoints.cs                                [MOD]  Decision 5 (mapeamento HTTP)
│   ├── Responses/
│   │   └── ChannelResponse.cs                                 [MOD]  `BuildWebhookUrl` promovido a helper reutilizável (Decision 4)
│   └── Webhooks/Endpoints/
│       └── WebhookEndpoints.cs                                [MOD]  Decision 8 (detalhe de implementação — respeita o `StatusCode` definido pelo handler)
└── Program.cs                                                  [MOD]  registro DI do adapter Telegram

apps/inbox/tests/Buteco.Inbox.Tests/
├── TelegramChannelConfigValidatorTests.cs                     [NOVO]
├── TelegramOutboundMessageSenderTests.cs                      [NOVO]
├── TelegramWebhookProvisionerTests.cs                         [NOVO]
├── TelegramInboundWebhookHandlerTests.cs                      [NOVO]
├── ChannelAdapterRegistrationExtensionsTests.cs                [MOD]
├── CreateChannelCommandHandlerTests.cs                         [MOD]
└── UpdateChannelCommandHandlerTests.cs                         [MOD]
```

Nenhum diretório novo em `libs/` — todo o trabalho é local a `apps/inbox`,
mesmo padrão do WAHA.

## Decisions

### Decision 1: Quarto contrato de plugin, opcional — `IChannelWebhookProvisioner`

```csharp
// Channels/Adapters/IChannelWebhookProvisioner.cs
namespace Buteco.Inbox.Channels.Adapters;

public interface IChannelWebhookProvisioner
{
    Task<ChannelWebhookProvisioningResult> ProvisionAsync(
        Guid channelId,
        string credential,
        string webhookUrl,
        CancellationToken cancellationToken);
}
```

Resolvido via `IChannelAdapterRegistry`, quarto método, **nulável** (ao
contrário dos três existentes):

```csharp
public interface IChannelAdapterRegistry
{
    bool IsRegistered(string channelType);
    IChannelConfigValidator GetConfigValidator(string channelType);
    IOutboundMessageSender GetOutboundMessageSender(string channelType);
    IInboundWebhookHandler? GetInboundWebhookHandler(string channelType);
    IChannelWebhookProvisioner? GetWebhookProvisioner(string channelType);
}
```

Nome escolhido — **`IChannelWebhookProvisioner`**, não
`IChannelActivationHandler`/`IChannelActivationProvisioner` (cogitado na
exploração): o catálogo já tem `ActivateChannelCommand`/
`DeactivateChannelCommand` para o campo de estado `IsActive` — nomear este
contrato novo com "Activation" colidiria conceitualmente com um recurso já
existente e não relacionado. "Webhook" é preciso: hoje o único
provisionamento automático é `setWebhook`; se um adapter futuro precisar
provisionar outra coisa (ex. registrar um canal em um serviço de
mensageria diferente de webhook), esse é o momento de revisitar o nome —
não generalizar preventivamente agora.

**Alternativa descartada**: `if (channelType == "telegram")` inline em
`CreateChannelCommandHandler`. Rejeitada — a arquitetura do catálogo,
desde `inbox-adapter-contrato-catalogo`, nunca ramificou comportamento por
`ChannelType` fora dos próprios adapters, sempre resolvendo por DI keyed
via `IChannelAdapterRegistry`. Um `if` direto no handler genérico seria o
primeiro type-switch da arquitetura, quebrando esse costume pela primeira
vez para economizar um contrato. O custo de manter o costume (um
contrato novo, porém opcional) é baixo e mantém `CreateChannelCommandHandler`
sem nenhum conhecimento de `"telegram"` como string mágica.

### Decision 2: `ChannelWebhookProvisioningResult` — sucesso carrega a credencial aumentada

```csharp
// Channels/Adapters/ChannelWebhookProvisioningResult.cs
namespace Buteco.Inbox.Channels.Adapters;

public sealed record ChannelWebhookProvisioningResult(
    bool Success,
    string? UpdatedCredential,
    string? ErrorMessage)
{
    public static ChannelWebhookProvisioningResult Succeeded(string updatedCredential) =>
        new(true, updatedCredential, null);

    public static ChannelWebhookProvisioningResult Failed(string errorMessage) =>
        new(false, null, errorMessage);
}
```

Mesmo padrão de resultado tipado já usado por
`AgentReferenceValidationResult`/`ChannelConfigValidationResult` — sem
lançar exceção para um caminho de falha esperado (Telegram inalcançável,
token inválido). `UpdatedCredential` existe porque o provisionamento
**gera** o `secret_token` (Decision 7) e precisa devolvê-lo embutido na
credencial em texto plano, para o handler chamador cifrar e persistir —
o provisionador é o único lugar que sabe como embutir esse dado no shape
de credencial específico do Telegram; o handler genérico
(`CreateChannelCommandHandler`) permanece agnóstico ao shape.

**Alternativa descartada**: `ProvisionAsync` lançar exceção em vez de
devolver um resultado tipado, propagando até o handler genérico e sendo
capturada lá. Rejeitada pelo mesmo raciocínio já estabelecido em
`AgentReferenceValidator` (o mais próximo já existente no código para
"chamada de rede externa que pode falhar de forma esperada") — resultado
tipado deixa explícito, na assinatura, que falha é um caminho normal, não
uma condição excepcional.

### Decision 3: `ValidateChannelAdapterRegistrations` — obrigatório vs. opcional

```csharp
public static void ValidateChannelAdapterRegistrations(this IServiceCollection services)
{
    var validatorKeys = KeysFor<IChannelConfigValidator>(services);
    var senderKeys = KeysFor<IOutboundMessageSender>(services);
    var webhookHandlerKeys = KeysFor<IInboundWebhookHandler>(services);
    var provisionerKeys = KeysFor<IChannelWebhookProvisioner>(services);

    // Os três primeiros continuam obrigatórios em conjunto — mesma
    // checagem já existente (inbox-adapter-waha, Decision 2).
    var requiredSets = new (string Name, HashSet<object> Keys)[]
    {
        (nameof(IChannelConfigValidator), validatorKeys),
        (nameof(IOutboundMessageSender), senderKeys),
        (nameof(IInboundWebhookHandler), webhookHandlerKeys),
    };

    var requiredKeys = validatorKeys.Union(senderKeys).Union(webhookHandlerKeys);

    var problems = requiredKeys
        .SelectMany(key => requiredSets
            .Where(set => !set.Keys.Contains(key))
            .Select(set => $"'{key}' não tem {set.Name} registrado"))
        .ToList();

    // provisionerKeys NÃO entra em requiredSets: é o quarto contrato,
    // opcional. Única checagem sobre ele é que nenhuma chave apareça só
    // nele sem os três obrigatórios (um adapter não pode implementar só
    // o provisionador, sem os contratos base).
    problems.AddRange(provisionerKeys
        .Where(key => !requiredKeys.Contains(key))
        .Select(key => $"'{key}' tem {nameof(IChannelWebhookProvisioner)} registrado sem os três contratos obrigatórios"));

    if (problems.Count > 0)
    {
        throw new InvalidOperationException(
            $"Registro de adapters de canal incompleto: {string.Join("; ", problems)}.");
    }
}
```

`"waha"` continua sem `IChannelWebhookProvisioner` registrado — a checagem
não falha por isso, ao contrário dos três primeiros contratos, onde faltar
qualquer um já é erro de composição. `"telegram"` registra os quatro.

**Alternativa descartada**: tratar o quarto contrato como obrigatório
também, exigindo que todo adapter futuro implemente provisionamento
automático (mesmo que seja um provisionador "no-op"). Rejeitada — forçaria
todo adapter futuro sem automação possível (ex. um canal de e-mail
genérico, sem `setWebhook` equivalente) a escrever um provisionador vazio
só para satisfazer a checagem, contrariando o próprio motivo de o
contrato existir (só automatiza quem pode).

### Decision 4: `CreateChannelCommandHandler` — ordem de operações evita necessidade de rollback

```csharp
public async ValueTask<CreateChannelResult> Handle(CreateChannelCommand command, CancellationToken cancellationToken)
{
    var validation = await agentReferenceValidator.ValidateAsync(command.AgentId, cancellationToken);
    switch (validation) { /* inalterado */ }

    var configValidation = adapterRegistry.GetConfigValidator(command.ChannelType).Validate(command.Credential);
    if (!configValidation.IsValid)
    {
        return CreateChannelResult.InvalidCredential(configValidation.Errors);
    }

    // Channel construído em memória — Guid.NewGuid() já roda no
    // construtor (Channels/Entities/Channel.cs:33), sem nenhum
    // SaveChangesAsync ainda. channel.Id já está disponível aqui.
    var encryptedCredentials = credentialCipher.Encrypt(command.Credential);
    var channel = new Channel(command.ChannelType, command.Name, encryptedCredentials, command.AgentId);

    var provisioner = adapterRegistry.GetWebhookProvisioner(command.ChannelType);
    if (provisioner is not null)
    {
        var webhookUrl = ChannelResponse.BuildWebhookUrl(publicUrlOptions.Value.BaseUrl, channel.Id);
        var provisioning = await provisioner.ProvisionAsync(channel.Id, command.Credential, webhookUrl, cancellationToken);
        if (!provisioning.Success)
        {
            return CreateChannelResult.ProvisioningFailed(provisioning.ErrorMessage!);
        }

        channel.SetEncryptedCredentials(credentialCipher.Encrypt(provisioning.UpdatedCredential!));
    }

    dbContext.Channels.Add(channel);
    await dbContext.SaveChangesAsync(cancellationToken);

    return CreateChannelResult.Success(ChannelResponse.FromEntity(channel, publicUrlOptions.Value.BaseUrl));
}
```

`Channel` é construído **antes** de `dbContext.Channels.Add` e antes de
qualquer `SaveChangesAsync` — nada foi persistido ainda quando o
provisionamento roda, então uma falha do Telegram simplesmente retorna sem
nunca chamar `Add`/`SaveChangesAsync`. Isso responde a pergunta deixada
em aberto na exploração ("transacional ou ordem de operações evita
rollback?") a favor da segunda opção: nenhuma transação nova é necessária
porque o `Channel` só entra no `DbContext` depois do provisionamento ter
sucesso.

`ChannelResponse.BuildWebhookUrl` é promovido de `private static` para
`internal static` (mesmo arquivo,
[ChannelResponse.cs:36-37](../../../apps/inbox/src/Buteco.Inbox/Channels/Responses/ChannelResponse.cs#L36-L37))
— reaproveitado aqui para computar a mesma URL que `FromEntity` computa na
resposta, sem duplicar a string `/webhooks/{channelId}` em dois lugares.

**Alternativa descartada**: gerar `channel.Id` antecipadamente
(`Guid.NewGuid()` no handler) e passá-lo a um novo construtor de `Channel`
que aceita o `Id` como parâmetro. Rejeitada — desnecessária: `Channel` já
gera seu próprio `Id` no construtor atual, e nada impede ler `channel.Id`
de uma instância só construída em memória, antes de `Add`/`SaveChanges`.
Mudar o construtor da entidade adicionaria superfície sem ganho.

### Decision 5: `CreateChannelResult`/`ChannelEndpoints` — novo Outcome `ProvisioningFailed`

```csharp
public enum CreateChannelOutcome
{
    Success,
    AgentNotFound,
    AgentValidationFailed,
    InvalidCredential,
    ProvisioningFailed,
}

public sealed record CreateChannelResult(
    ChannelResponse? Channel,
    CreateChannelOutcome Outcome,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    string? ProvisioningError = null)
{
    // ... factories existentes inalteradas ...
    public static CreateChannelResult ProvisioningFailed(string errorMessage) =>
        new(null, CreateChannelOutcome.ProvisioningFailed, ProvisioningError: errorMessage);
}
```

`ChannelEndpoints.CreateChannelAsync` ganha um `case` explícito antes do
`_ =>` genérico:

```csharp
CreateChannelOutcome.ProvisioningFailed => TypedResults.Problem(
    $"Não foi possível configurar o webhook do canal junto à plataforma externa: {result.ProvisioningError}",
    statusCode: StatusCodes.Status502BadGateway),
```

HTTP 502, mesma família de `AgentValidationFailed` (falha de dependência
externa), mas com mensagem própria — não reaproveita
`AgentValidationFailedDetail`, que é especificamente sobre `apps/api`.
`UpdateChannelResult`/`UpdateChannelAsync` ganham o mesmo `Outcome`/`case`,
espelhado (Decision 6).

### Decision 6: `UpdateChannelCommandHandler` — troca de `BotToken` reprovisiona

Quando a atualização inclui novas credenciais (`!string.IsNullOrWhiteSpace(command.Credential)`)
e o adapter do `channel.ChannelType` já persistido implementa
`IChannelWebhookProvisioner`, o handler reprovisiona **antes** de chamar
`channel.SetEncryptedCredentials`:

```csharp
if (!string.IsNullOrWhiteSpace(command.Credential))
{
    var configValidation = adapterRegistry.GetConfigValidator(channel.ChannelType).Validate(command.Credential);
    if (!configValidation.IsValid)
    {
        return UpdateChannelResult.InvalidCredential(configValidation.Errors);
    }

    var credentialToPersist = command.Credential;

    var provisioner = adapterRegistry.GetWebhookProvisioner(channel.ChannelType);
    if (provisioner is not null)
    {
        var webhookUrl = ChannelResponse.BuildWebhookUrl(publicUrlOptions.Value.BaseUrl, channel.Id);
        var provisioning = await provisioner.ProvisionAsync(channel.Id, command.Credential, webhookUrl, cancellationToken);
        if (!provisioning.Success)
        {
            return UpdateChannelResult.ProvisioningFailed(provisioning.ErrorMessage!);
        }

        credentialToPersist = provisioning.UpdatedCredential!;
    }

    channel.SetEncryptedCredentials(credentialCipher.Encrypt(credentialToPersist));
}
```

Reprovisionar sempre gera um **novo** `secret_token` (mesmo gerador da
Decision 7, sem reaproveitar o anterior) e chama `setWebhook` de novo —
mais simples do que decriptar a credencial anterior só para extrair o
`WebhookSecret` velho, e estruturalmente correto: se o `BotToken` mudou, o
bot pode ser outro; um `secret_token` novo por reprovisionamento evita
qualquer suposição sobre o segredo anterior continuar válido.
`channel.SetEncryptedCredentials` só é chamado depois do provisionamento
ter sucesso (ou não ter sido necessário) — mesma ordem de operações da
Decision 4, sem necessidade de desfazer nada em caso de falha, porque
nenhuma escrita acontece antes.

**Alternativa descartada**: falhar explicitamente pedindo para recriar o
canal em vez de reprovisionar. Rejeitada — reprovisionar é o mesmo
mecanismo já construído para o cadastro (Decision 1/4), sem custo
adicional de implementação; falhar force o operador a descobrir o
`AgentId`/nome do canal antigo e recriar tudo manualmente, pior
experiência sem benefício de segurança correspondente (o novo
`secret_token` já invalida qualquer suposição sobre o estado anterior).

### Decision 7: Shape da credencial Telegram

```csharp
// Channels/Adapters/Telegram/TelegramCredential.cs
namespace Buteco.Inbox.Channels.Adapters.Telegram;

public sealed record TelegramCredential(string BotToken, string? WebhookSecret = null);
```

Sem `ServiceUrl` (sempre `https://api.telegram.org`, nunca configurável
por canal) nem `SessionName` (o Telegram não distingue sessão de bot — um
`BotToken` já identifica univocamente o destino de toda chamada). Mais
simples que `WahaCredential` de propósito, não por descuido — contraste
direto:

| Campo | `WahaCredential` | `TelegramCredential` |
|---|---|---|
| Endpoint da plataforma | `ServiceUrl` (variável — self-hosted) | Fixo, não persistido |
| Identificador de instância | `SessionName` | N/A — `BotToken` já é único |
| Segredo de autenticação de saída | `AuthToken` | `BotToken` |
| Segredo de verificação de entrada | nenhum (Decision 6 do WAHA) | `WebhookSecret` |

`WebhookSecret` é opcional no record (`null` até o primeiro
provisionamento bem-sucedido) porque o shape que o cliente envia em
`POST /channels`/`PUT /channels/{id}` nunca inclui esse campo — ele só
existe depois que `TelegramWebhookProvisioner` (Decision 9) o gera e
embute na credencial que `CreateChannelCommandHandler`/
`UpdateChannelCommandHandler` cifram e persistem (Decisions 4/6). Um
`WebhookSecret` eventualmente enviado pelo cliente é ignorado — a
credencial persistida é sempre a devolvida pelo provisionador, não a
enviada na requisição.

### Decision 8: Extração do webhook Telegram e enforcement de `IsActive`

```csharp
// Channels/Adapters/Telegram/TelegramInboundWebhookHandler.cs
public sealed class TelegramInboundWebhookHandler(
    IServiceScopeFactory scopeFactory,
    IChannelCredentialCipher credentialCipher) : IInboundWebhookHandler
{
    public async Task HandleAsync(Guid channelId, HttpRequest request, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var channel = await dbContext.Channels.FindAsync([channelId], cancellationToken);
        if (channel is null || !channel.IsActive)
        {
            request.HttpContext.Response.StatusCode = channel is null
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status401Unauthorized;
            return;
        }

        var credential = JsonSerializer.Deserialize<TelegramCredential>(credentialCipher.Decrypt(channel.EncryptedCredentials))!;
        var receivedSecret = request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
        if (receivedSecret is null || receivedSecret != credential.WebhookSecret)
        {
            request.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var update = await JsonSerializer.DeserializeAsync<TelegramUpdate>(request.Body, JsonOptions, cancellationToken);
        if (update?.Message?.Chat?.Id is null || update.Message.Text is null)
        {
            return;
        }

        var externalId = update.Message.Chat.Id.Value.ToString();
        var metadata = new Dictionary<string, string>();
        if (update.Message.From?.Username is { } username) metadata["username"] = username;
        if (update.Message.From?.FirstName is { } firstName) metadata["firstName"] = firstName;

        var orchestrator = scope.ServiceProvider.GetRequiredService<IInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(channelId, externalId, update.Message.Text, DateTimeOffset.UtcNow, metadata, cancellationToken);
    }
}
```

Três pontos de decisão embutidos aqui:

1. **`IsActive` checado dentro do handler, não em `WebhookEndpoints`
   genérico**: escopado a este adapter — `WebhookEndpoints` (compartilhado
   por todo `ChannelType`, incluindo `"waha"`) permanece sem checar
   `IsActive`, preservando o comportamento do WAHA sem mudança não pedida
   (Non-Goals). Uma checagem genérica em `WebhookEndpoints` afetaria o
   WAHA de graça — mudança de comportamento maior que o escopo desta
   fatia, e nenhum requisito de produto pediu isso para o WAHA.
2. **Ordem das checagens**: `IsActive` antes de `secret_token` — um canal
   desativado responde 401 mesmo com o secret correto, sem vazar a
   informação "seu secret está certo, mas o canal está desligado" a um
   chamador não autenticado (não muda o código HTTP observável, mas evita
   ramificar em texto de erro diferente por esse caminho).
3. **`IServiceScopeFactory` + `AppDbContext`/`IInboundMessageOrchestrator`
   resolvidos por escopo**: mesmo raciocínio do `WahaInboundWebhookHandler`
   (design.md do WAHA, Decision 5, "Detalhe de implementação") — o handler
   é `AddKeyedSingleton`, mas `AppDbContext`/`IInboundMessageOrchestrator`
   são `Scoped`; um escopo próprio por chamada resolve ambos corretamente.
   `IChannelCredentialCipher` é `Singleton` (`Program.cs:33`), então entra
   direto no construtor, sem passar pelo escopo.

`update.Message` sem `Text` (ex. mensagem só de mídia) é ignorado, sem
erro — mesmo tratamento de "não processável, não inválido" da Decision 5
do WAHA para eventos fora de `"message"`.

**Alternativa descartada**: `WebhookEndpoints` genérico carregar `Channel`
inteiro e passá-lo ao handler (em vez de só `channelId`), evitando a
segunda consulta que `TelegramInboundWebhookHandler` faz por conta
própria. Rejeitada — mudaria a assinatura de `IInboundWebhookHandler`
para todo adapter existente e futuro só para economizar uma consulta by-id
(rápida, indexada por chave primária) dentro de um handler que já abre
seu próprio escopo por outros motivos.

**Detalhe de implementação (revelado durante a implementação, não uma
Decision nova)**: `WebhookEndpoints.ReceiveAsync`
([WebhookEndpoints.cs](../../../apps/inbox/src/Buteco.Inbox/Channels/Webhooks/Endpoints/WebhookEndpoints.cs))
sempre devolvia `TypedResults.Ok()` incondicionalmente depois de
`await handler.HandleAsync(...)` — `TypedResults.Ok()` força
`Response.StatusCode = 200` na execução, **sobrescrevendo** qualquer
`StatusCode` que um handler já tivesse definido diretamente em
`request.HttpContext.Response`. Isso quebraria silenciosamente o 401 desta
Decision (o cliente sempre receberia 200, mesmo com `secret_token`
divergente ou canal inativo). Correção: `ReceiveAsync` passa a devolver
`TypedResults.StatusCode(request.HttpContext.Response.StatusCode)` em vez
de `TypedResults.Ok()` fixo — como `HttpResponse.StatusCode` já é `200`
por padrão quando nenhum handler o altera, o comportamento observável do
WAHA/`test-channel` não muda; só passa a refletir corretamente o que um
handler como o do Telegram define explicitamente.

### Decision 9: `TelegramOutboundMessageSender` e `TelegramWebhookProvisioner`

```csharp
// Channels/Adapters/Telegram/TelegramOutboundMessageSender.cs
public sealed class TelegramOutboundMessageSender(IHttpClientFactory httpClientFactory) : IOutboundMessageSender
{
    public async Task SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        var credential = JsonSerializer.Deserialize<TelegramCredential>(message.DecryptedCredential)!;

        using var client = httpClientFactory.CreateClient();
        var body = new { chat_id = message.ContactExternalId, text = message.ResponseText };
        var response = await client.PostAsJsonAsync(
            $"https://api.telegram.org/bot{credential.BotToken}/sendMessage", body, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
```

```csharp
// Channels/Adapters/Telegram/TelegramWebhookProvisioner.cs
public sealed class TelegramWebhookProvisioner(IHttpClientFactory httpClientFactory) : IChannelWebhookProvisioner
{
    public async Task<ChannelWebhookProvisioningResult> ProvisionAsync(
        Guid channelId, string credential, string webhookUrl, CancellationToken cancellationToken)
    {
        var parsed = JsonSerializer.Deserialize<TelegramCredential>(credential)!;
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); // 64 chars, A-F0-9 — dentro do charset aceito pelo Telegram

        using var client = httpClientFactory.CreateClient();
        var body = new { url = webhookUrl, secret_token = secret };
        var response = await client.PostAsJsonAsync(
            $"https://api.telegram.org/bot{parsed.BotToken}/setWebhook", body, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<TelegramApiResponse>(cancellationToken);
        if (!response.IsSuccessStatusCode || payload is not { Ok: true })
        {
            return ChannelWebhookProvisioningResult.Failed(payload?.Description ?? "Falha desconhecida ao chamar setWebhook do Telegram.");
        }

        return ChannelWebhookProvisioningResult.Succeeded(
            JsonSerializer.Serialize(parsed with { WebhookSecret = secret }));
    }
}

internal sealed record TelegramApiResponse(bool Ok, string? Description);
```

`chat_id = message.ContactExternalId` diretamente, sem transformação —
mesmo raciocínio da Decision 7 do WAHA: `ExternalId` já é
`update.Message.Chat.Id` convertido para string (Decision 8), e o
Telegram aceita `chat_id` como `Integer` ou `String` na API, então nenhuma
reconversão é necessária.

`Convert.ToHexString` gera só `0-9`/`A-F` — subconjunto do charset
`A-Z a-z 0-9 _ -` aceito por `secret_token`, sem necessidade de validar
caracteres depois de gerado. 32 bytes (256 bits) de entropia, gerado por
`RandomNumberGenerator` (criptograficamente seguro), bem acima do mínimo
de 1 caractere exigido e dentro do máximo de 256.

Checagem de sucesso via `IsSuccessStatusCode`/`payload.Ok`, nunca contra
`error_code`/`description` específicos (Context — a documentação oficial
não fixa esses valores para token inválido). `payload.Description` é
propagado como `ErrorMessage` só para diagnóstico humano (exibido na
resposta HTTP 502 do endpoint, Decision 5) — nunca usado para ramificar
lógica.

**Alternativa descartada**: um único tipo `TelegramApiClient` compartilhado
entre `TelegramOutboundMessageSender` e `TelegramWebhookProvisioner`, com
métodos `SendMessageAsync`/`SetWebhookAsync`. Rejeitada por ora — os dois
adapters já são pequenos o suficiente (uma chamada HTTP cada) que a
duplicação da URL-base/`HttpClient` anônimo é menor custo que introduzir
uma abstração nova; mesmo padrão de "sem abstração prematura" já seguido
pelo WAHA (`WahaOutboundMessageSender` não compartilha nada com o
handler de webhook).

### Decision 10: Nenhum tratamento de rate limit

Limites reais do Telegram (1 msg/seg por chat, 30/seg total, 20/min por
grupo) não são tratados nesta fatia — risco aceito e nomeado
explicitamente (Risks), não uma omissão silenciosa. O debounce já
existente (`inbox-orquestrador-debounce`) agrupa rajadas de mensagens do
mesmo contato antes de disparar uma única resposta, reduzindo — mas não
eliminando — a chance de bater no limite por conversa. Nenhuma lógica de
backoff/retry/fila é adicionada.

## Risks / Trade-offs

- **[Risco]** Nenhum tratamento de rate limit do Telegram (Decision 10) —
  em volume alto, `TelegramOutboundMessageSender.SendAsync` pode receber
  HTTP 429 do Telegram e lançar (`EnsureSuccessStatusCode`), propagando
  até `PushNotificationEndpoints.DeliverResponseAsync`, que só loga (Non-Goal
  de retry herdado, mesmo comportamento do WAHA) → **Mitigação**: aceito
  conscientemente nesta fatia; se volume real justificar, uma fatia futura
  adiciona backoff/fila especificamente para este adapter.
- **[Risco]** Assimetria de enforcement de `IsActive`: a partir desta
  change, um canal Telegram desativado rejeita webhooks de entrada
  (Decision 8), mas `PushNotificationEndpoints` (saída, qualquer
  `ChannelType`) continua entregando respostas de agente
  independentemente de `IsActive` → **Mitigação**: nomeado aqui como
  Non-Goal explícito; não é regressão (nenhuma direção verificava
  `IsActive` antes desta change), mas fica mais visível agora que uma
  direção verifica e a outra não. Fatia futura pode fechar a saída pelo
  mesmo padrão, se necessário.
- **[Risco]** `TelegramInboundWebhookHandler` decifra a credencial a cada
  webhook recebido (uma chamada `AES-GCM` por requisição, mais uma
  consulta `FindAsync` por `channelId`) — custo maior por requisição do
  que `WahaInboundWebhookHandler`, que não decifra nada → **Mitigação**:
  aceito — é o preço da verificação de `secret_token` que o WAHA não
  paga por não a ter; `AES-GCM`/`FindAsync` por chave primária são ambos
  baratos o suficiente para o volume esperado de webhooks por canal.
- **[Trade-off]** Quarto contrato de plugin **opcional** é o primeiro
  ponto de extensão do catálogo que nem todo `ChannelType` precisa
  implementar — `ValidateChannelAdapterRegistrations` (Decision 3) fica
  mais complexa (dois grupos de contrato em vez de um) → aceito porque a
  alternativa (type-switch inline, Decision 1) quebraria um costume
  arquitetural mais valioso de preservar do que a simplicidade da
  checagem de composição.
- **[Trade-off]** `secret_token` é gerado sempre novo a cada
  provisionamento/reprovisionamento (Decision 6), nunca reaproveitado —
  significa que trocar só o nome de um canal Telegram (sem trocar
  credencial) não reprovisiona, mas trocar `BotToken` sempre invalida o
  `secret_token` anterior (mesmo que o novo token pertença ao mesmo bot)
  → aceito como comportamento mais simples e seguro (Decision 6).

## Migration Plan

- Nenhuma migration EF Core — `TelegramCredential` é só o shape do JSON
  por dentro de `Channel.EncryptedCredentials` (já `string` opaco),
  mesmo padrão do `WahaCredential`.
- Deploy aditivo: `IChannelWebhookProvisioner` é contrato novo, nenhum
  adapter existente (`"waha"`, `"test-channel"`) precisa implementá-lo;
  `ValidateChannelAdapterRegistrations` continua passando para eles sem
  mudança (Decision 3); `CreateChannelResult`/`UpdateChannelResult` ganham
  um `Outcome` novo (`ProvisioningFailed`) que só é alcançado para
  `channelType == "telegram"` — nenhum cliente existente aciona esse
  caminho.
- Rollback: reverter o binário de `apps/inbox`; nenhuma migration para
  reverter.
