## Context

`inbox-adapter-contrato-catalogo` abriu `ChannelType` para um identificador
de string validado em runtime contra adapters registrados via DI keyed, e
definiu dois dos três contratos de plugin (`IChannelConfigValidator`,
`IOutboundMessageSender`), provados só por um adapter de teste
(`"test-channel"`, sem integração de rede). Deixou dois pontos em aberto,
ambos fechados por esta fatia:

1. Nenhum adapter real implementa os contratos — `ValidateChannelAdapterRegistrations`
   ([ChannelAdapterRegistrationExtensions.cs](../../../apps/inbox/src/Buteco.Inbox/Channels/Adapters/ChannelAdapterRegistrationExtensions.cs))
   já garante que todo `ChannelType` registrado tem os dois contratos
   completos, mas nada além do adapter de teste existe.
2. `webhookUrl` é computada e exposta em `ChannelResponse`
   ([ChannelResponse.cs:32-33](../../../apps/inbox/src/Buteco.Inbox/Channels/Responses/ChannelResponse.cs#L32-L33))
   mas nenhuma rota é mapeada nesse path — Non-Goal explícito da change
   anterior ("Nenhum endpoint HTTP mapeado no path retornado por
   `webhookUrl`").

Investigação obrigatória feita antes deste design (via `/opsx:explore`,
contra a documentação real do WAHA em https://waha.devlike.pro/docs, não
snippet de busca):

- `POST /api/sendText`: corpo `{ "session": string, "chatId": string, "text": string }`.
  Autenticação via header `X-Api-Key: <secret>`. `chatId` no formato
  `{numero}@c.us` (indivíduo) ou `{numero}@g.us` (grupo).
- Webhook do evento `"message"`: corpo
  `{ "event": "message", "session": string, "payload": { "id", "timestamp", "from", "fromMe", "to", "body", "hasMedia", "ack", ... } }`.
  O campo de texto é `payload.body` — **não** `payload.text` (assimetria de
  nome com o `sendText`, fácil de errar copiando o nome errado entre
  entrada e saída). `payload.from`/`payload.to` no formato `{numero}@c.us`.
- WAHA distingue dois eventos de mensagem: `"message"` fecha só mensagens
  recebidas de terceiros; `"message.any"` é o único que também inclui
  mensagens da própria sessão (`fromMe: true`), sejam enviadas via
  `sendText` ou pelo operador respondendo direto no WhatsApp. Assinar só
  `"message"` (Decision 5) garante estruturalmente que este handler nunca
  processa uma mensagem própria como se fosse uma mensagem de entrada — sem
  precisar de filtro defensivo por `fromMe` no código.
- Configuração de sessão do WAHA é via `POST /api/sessions`, aceitando
  `config.webhooks[]` com `url`, `events`, `hmac.key` (assinatura nativa
  opcional dos webhooks enviados, verificável pelos headers
  `X-Webhook-Hmac`/`X-Webhook-Hmac-Algorithm: sha512` recebidos),
  `customHeaders`, `retries`. Confirma que a configuração é, de fato,
  automatizável — mas fica manual nesta fatia (Decision 3).
- Ciclo de vida de sessão: `STOPPED` → `STARTING` → `SCAN_QR_CODE` →
  `WORKING` (ou `FAILED`). Imagem oficial `devlikeapro/waha` (renomeada de
  `devlikeapro/whatsapp-http-api`), engine `GOWS` via
  `WHATSAPP_DEFAULT_ENGINE=GOWS` — engine mais recente, sem dependência de
  navegador, porta padrão 3000.

## Goals / Non-Goals

**Goals:**
- Terceiro contrato de plugin, `IInboundWebhookHandler`, resolvido por
  `ChannelType` via DI keyed, mesmo padrão dos outros dois.
- Rota HTTP genérica única `POST /webhooks/{channelId}` mapeada, resolvendo
  `Channel`/`ChannelType` pelo id da rota e despachando para o handler
  registrado.
- `ValidateChannelAdapterRegistrations` estendida para três contratos.
- Primeiro adapter real (`"waha"`): validador de credencial, sender de
  saída (`POST /api/sendText`) e handler de webhook de entrada (evento
  `"message"`), sem nenhuma simulação — integração de rede real contra um
  WAHA (local, via `docker-compose.yml`, ou remoto).
- `Contact.Metadata`: ponto de extensão genérico para dado adicional de
  exibição/CRM que um adapter capture na criação do contato — usado nesta
  fatia pelo WAHA para o telefone sem sufixo.
- `webhookUrl` no formato definitivo `/webhooks/{channelId}`.

**Non-Goals:**
- Nenhuma verificação de autenticidade do webhook de entrada nesta fatia
  (ver Decision 6 e Risks) — `AuthToken` da credencial autentica só as
  chamadas de saída desta aplicação para a API do WAHA, não o webhook de
  entrada.
- Nenhuma configuração automática da sessão WAHA via API (`POST /api/sessions`)
  — confirmado nesta exploração como decisão explícita, não lacuna (ver
  Decision 3). Fica um passo manual do operador, incluindo a configuração
  de HMAC caso ele opte por usá-la.
- Nenhum filtro defensivo por `fromMe` no `WahaInboundWebhookHandler` — a
  garantia estrutural de assinar só `"message"` (não `"message.any"`) já é
  suficiente (ver Context).
- `Contact.Metadata` não é atualizado depois da criação do `Contact` — sem
  caso de uso real que justifique refresh a cada mensagem nesta fatia.
- Nenhum adapter Telegram.
- Nenhuma UI.
- Nenhuma política de retry/backoff para `WahaOutboundMessageSender` — Non-Goal
  de retry como infraestrutura própria já herdado de
  `inbox-adapter-contrato-catalogo`; falha de transporte só é logada
  (comportamento já existente em `PushNotificationEndpoints.ReceiveAsync`,
  inalterado por esta fatia).

## Decisions

### Decision 1: `IInboundWebhookHandler`, terceiro contrato de plugin, e rota genérica única

```csharp
// Channels/Adapters/IInboundWebhookHandler.cs
namespace Buteco.Inbox.Channels.Adapters;

public interface IInboundWebhookHandler
{
    Task HandleAsync(Guid channelId, HttpRequest request, CancellationToken cancellationToken);
}
```

Resolvido por `ChannelType` via `IChannelAdapterRegistry`, que ganha um
terceiro método simétrico aos dois já existentes:

```csharp
// Channels/Adapters/IChannelAdapterRegistry.cs
public interface IChannelAdapterRegistry
{
    bool IsRegistered(string channelType);
    IChannelConfigValidator GetConfigValidator(string channelType);
    IOutboundMessageSender GetOutboundMessageSender(string channelType);
    IInboundWebhookHandler? GetInboundWebhookHandler(string channelType);
}
```

`GetInboundWebhookHandler` retorna `null` (em vez de lançar, como os outros
dois `GetRequiredKeyedService`) porque o chamador (o endpoint genérico,
abaixo) precisa distinguir "tipo sem handler" de uma falha inesperada, para
responder HTTP 400 em vez de deixar uma exceção não tratada virar 500 — a
composição completa já é garantida no startup por
`ValidateChannelAdapterRegistrations` (Decision 2), então esse `null` só
seria alcançado por um bug na própria checagem, não por operação normal.

Uma única rota genérica, mapeada uma vez em `Program.cs` (novo
`Channels/Webhooks/Endpoints/WebhookEndpoints.cs`, paralelo a
`ChannelEndpoints`/`ContactEndpoints`):

```csharp
public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/{channelId:guid}", ReceiveAsync);
        return app;
    }

    private static async Task<IResult> ReceiveAsync(
        Guid channelId,
        HttpRequest request,
        AppDbContext dbContext,
        IChannelAdapterRegistry adapterRegistry,
        CancellationToken cancellationToken)
    {
        var channel = await dbContext.Channels.FindAsync([channelId], cancellationToken);
        if (channel is null)
        {
            return TypedResults.NotFound();
        }

        var handler = adapterRegistry.GetInboundWebhookHandler(channel.ChannelType);
        if (handler is null)
        {
            return TypedResults.BadRequest();
        }

        await handler.HandleAsync(channelId, request, cancellationToken);
        return TypedResults.Ok();
    }
}
```

O handler recebe `HttpRequest` bruto (não um DTO tipado) porque o shape do
payload é decidido inteiramente pelo adapter — a rota genérica não sabe (e
não deveria saber) nada sobre o formato de nenhuma plataforma específica.
Cada adapter é responsável por ler `request.Body`, desserializar no formato
que só ele entende, e chamar `IInboundMessageOrchestrator.ReceiveMessageAsync`
diretamente.

**Alternativa descartada**: rota específica por `ChannelType`
(`/webhooks/waha/{channelId}`, `/webhooks/telegram/{channelId}`). Rejeitada
pelo mesmo raciocínio do ajuste de convenção desta change: o `ChannelType`
já está persistido no `Channel` resolvido por `channelId` e é imutável após
a criação — carregá-lo também na rota é redundante e abriria uma pergunta
de validação sem resposta clara (o que fazer se a rota e o tipo persistido
divergirem). Comportamento específico por tipo já ramifica dentro do
handler resolvido a partir do `ChannelType` do banco, não por rota
separada.

**Alternativa descartada**: `GetInboundWebhookHandler` lançando como os
outros dois métodos (`GetRequiredKeyedService`), deixando uma falta de
handler virar HTTP 500 não tratado. Rejeitada — HTTP 400 comunica melhor
"requisição para uma configuração inválida do servidor" do que expor uma
exceção não tratada; o cenário só é alcançável se
`ValidateChannelAdapterRegistrations` (Decision 2) tiver um bug, mas tratar
defensivamente aqui é barato.

### Decision 2: `ValidateChannelAdapterRegistrations` estendida para três contratos

```csharp
public static void ValidateChannelAdapterRegistrations(this IServiceCollection services)
{
    var validatorKeys = KeysFor<IChannelConfigValidator>(services);
    var senderKeys = KeysFor<IOutboundMessageSender>(services);
    var webhookHandlerKeys = KeysFor<IInboundWebhookHandler>(services);

    var allKeys = validatorKeys.Union(senderKeys).Union(webhookHandlerKeys);

    var problems = allKeys
        .Where(key => !validatorKeys.Contains(key) || !senderKeys.Contains(key) || !webhookHandlerKeys.Contains(key))
        .Select(key => DescribeMissing(key, validatorKeys, senderKeys, webhookHandlerKeys))
        .ToList();

    if (problems.Count > 0)
    {
        throw new InvalidOperationException(
            $"Registro de adapters de canal incompleto: {string.Join("; ", problems)}.");
    }
}
```

Mesmo raciocínio de fail-fast da change anterior (Decision 7 de
`inbox-adapter-contrato-catalogo`): comparar os três conjuntos de chave
diretamente no `IServiceCollection`, antes de `builder.Build()`, identifica
o `ChannelType` problemático (agora podendo faltar qualquer um dos três
contratos, não só um dos dois) no startup, não na primeira mensagem
recebida ou resposta entregue por esse canal.

**Alternativa descartada**: manter a checagem de dois contratos e deixar
`IInboundWebhookHandler` sem validação de composição, confiando no HTTP 400
defensivo da Decision 1 para cobrir o caso. Rejeitada — descobriria a
lacuna só quando o WAHA de fato tentasse entregar um webhook, exatamente o
cenário que a checagem de startup existe para evitar; o custo de estender a
checagem existente é baixo (mesmo padrão, um terceiro `KeysFor<T>`).

### Decision 3: Configuração da sessão WAHA fica manual nesta fatia

Confirmado na investigação: `POST /api/sessions` aceita `config.webhooks[]`
com a `url` diretamente, então **seria** possível `CreateChannelCommandHandler`
chamar essa API automaticamente ao cadastrar um canal `"waha"`, configurando
webhook (e HMAC) sem passo manual.

Decisão explícita: não fazer isso nesta fatia. `CreateChannelCommandHandler`
continua só persistindo a credencial (validada por
`WahaChannelConfigValidator`, Decision 4) — nenhuma chamada de rede
acontece no cadastro do canal. O operador configura a sessão no WAHA por
fora (via `POST /api/sessions` diretamente, ou pela UI do próprio WAHA),
usando a `webhookUrl` já exposta em `ChannelResponse`.

**Motivo**: automatizar acopla o cadastro do canal (`POST /channels`, hoje
uma operação puramente local a `apps/inbox`) à disponibilidade de um
serviço externo — o que fazer se o WAHA estiver fora do ar no momento do
cadastro? Bloquear a criação do canal por isso é uma mudança de
comportamento maior do que o necessário para esta fatia, e abre superfície
nova (timeout, retry, mensagem de erro específica) sem um requisito de
produto que a justifique agora. Também exigiria decidir se o `AuthToken`
usado para autenticar essa chamada de configuração é o mesmo usado para
`sendText`, ou um escopo de credencial diferente (a API de sessões costuma
exigir uma API key de admin, potencialmente distinta da API key de uso). Sem
essas respostas, a versão manual é a fatia mínima que já prova o
round-trip completo.

**Alternativa descartada**: automatizar. Rejeitada por ora pelos motivos
acima — não descartada permanentemente; se o passo manual se mostrar
fricção real de operação, uma fatia futura pode revisitar isso como
melhoria incremental sobre o que já funciona manualmente.

### Decision 4: Shape da credencial WAHA

```csharp
public sealed record WahaCredential(string ServiceUrl, string SessionName, string AuthToken);
```

Serializada como JSON antes de passar pelo fluxo de criptografia já
existente (`IChannelCredentialCipher`, opaco a todo o resto do sistema —
`Channel.EncryptedCredentials` continua sem saber que é JSON por dentro).

`WahaChannelConfigValidator.Validate(string credential)`:
1. Desserializa o JSON — falha de parse é um erro de validação (`credential`
   inválido), não uma exceção não tratada.
2. `ServiceUrl` deve ser uma URL absoluta (`Uri.TryCreate(..., UriKind.Absolute, ...)`).
3. `SessionName` e `AuthToken` não podem ser vazios/whitespace.

Mesmo padrão de erro (`ChannelConfigValidationResult.Failure` com
`Dictionary<string, string[]>`) já estabelecido pelo contrato — sem
inventar um formato de erro novo.

### Decision 5: Extração do webhook do WAHA — `payload.body`, `ExternalId` mantém o sufixo `@c.us`

`WahaInboundWebhookHandler.HandleAsync`:
1. Desserializa `request.Body` no shape confirmado na investigação
   (`{ event, session, payload: { from, to, body, fromMe, ... } }`).
2. Filtra por `event == "message"` — qualquer outro valor (ex.
   `session.status`, `message.ack`) é ignorado, sem erro (retorna
   normalmente; não é uma requisição inválida, só não é processável por
   este handler).
3. Extrai o texto de `payload.body` (não `payload.text` — nome diferente do
   campo usado no `sendText`, ver Context).
4. Usa `payload.from` **bruto**, incluindo o sufixo `@c.us`, como
   `Contact.ExternalId` — decisão de convenção: manter o sufixo evita
   qualquer reconstrução/adivinhação na saída (Decision 7 do
   `WahaOutboundMessageSender` usa o valor diretamente como `chatId`, sem
   distinguir se era `@c.us` ou `@g.us`).
5. Chama `IInboundMessageOrchestrator.ReceiveMessageAsync(channelId, externalId, texto, receivedAt, metadata, cancellationToken)`
   (assinatura estendida pela Decision 8), passando
   `{"phone": "<payload.from sem o sufixo @c.us>"}` como metadado.

**Alternativa descartada**: remover o sufixo de `ExternalId` (guardar só o
número). Rejeitada — motivo detalhado na Decision 7: forçaria o sender de
saída a reconstruir o `chatId`, sem informação suficiente pra saber se era
originalmente `@c.us` (indivíduo) ou `@g.us` (grupo).

**Detalhe de implementação (revelado durante a implementação, não uma
Decision nova)**: `WahaInboundWebhookHandler` é registrado
`AddKeyedSingleton` (tasks.md 8.1, mesmo padrão dos outros dois contratos
WAHA), mas `IInboundMessageOrchestrator` é `Scoped` (depende de
`AppDbContext`). Injetar o `Scoped` direto no construtor de um `Singleton`
é captive dependency, rejeitado por `ValidateOnBuild`. O handler recebe
`IServiceScopeFactory` (seguro em qualquer vida útil) e abre um escopo
próprio por chamada de `HandleAsync` para resolver
`IInboundMessageOrchestrator` — sem mudar o registro `AddKeyedSingleton`
do próprio handler.

### Decision 6: Nenhuma verificação de autenticidade do webhook — risco aceito, mitigação nomeada

`AuthToken`, na credencial WAHA, autentica só as chamadas de **saída** desta
aplicação para a API do WAHA (`X-Api-Key` no `sendText`) — não há nenhuma
verificação de que uma requisição recebida em `POST /webhooks/{channelId}`
realmente veio do WAHA configurado para esse canal, e não de qualquer
cliente HTTP que descubra a URL (que usa um `Guid` como único segredo
implícito).

**Risco aceito nesta fatia** — decisão confirmada explicitamente antes
deste design, não uma omissão. **Mitigação futura, agora concreta** (achado
da investigação, Context): o WAHA já assina nativamente os webhooks que
envia, quando configurado com `config.webhooks[].hmac.key` na sessão —
enviando `X-Webhook-Hmac` (HMAC-SHA512 do corpo bruto) e
`X-Webhook-Hmac-Algorithm: sha512`. Quando essa verificação virar
prioridade, o trabalho é: adicionar um campo `HmacKey` a `WahaCredential`
(Decision 4), configurá-lo via `config.webhooks[].hmac.key` no momento em
que a sessão WAHA for configurada (hoje manual, Decision 3), e validar o
header `X-Webhook-Hmac` recebido dentro de `WahaInboundWebhookHandler`
antes de processar o corpo. Nenhum desses três passos exige mudança de
contrato (`IInboundWebhookHandler` já recebe o `HttpRequest` bruto,
incluindo headers) — é extensão aditiva do adapter WAHA especificamente,
não do contrato genérico.

### Decision 7: `WahaOutboundMessageSender`

```csharp
public sealed class WahaOutboundMessageSender(IHttpClientFactory httpClientFactory) : IOutboundMessageSender
{
    public async Task SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        var credential = JsonSerializer.Deserialize<WahaCredential>(message.DecryptedCredential)!;

        using var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(credential.ServiceUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", credential.AuthToken);

        var body = new { session = credential.SessionName, chatId = message.ContactExternalId, text = message.ResponseText };
        var response = await client.PostAsJsonAsync("/api/sendText", body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
```

`chatId = message.ContactExternalId` diretamente, sem transformação
(consequência direta da Decision 5). `EnsureSuccessStatusCode` lança em
resposta não-2xx — a exceção propaga até
`PushNotificationEndpoints.DeliverResponseAsync`, que já só loga (Non-Goal
de retry herdado, comportamento inalterado por esta fatia).

`IHttpClientFactory.CreateClient()` sem nome registrado (client anônimo),
não `AddHttpClient<WahaOutboundMessageSender>` nomeado — a `BaseAddress` é
por credencial (por canal), não fixa por tipo de adapter como
`AgentReferenceValidator`/`A2AClientFactory` (que usam `apps/api` como
destino único). Mesmo padrão de `A2AClientFactory`, que também monta a
`Uri` completa por chamada em vez de usar `BaseAddress` fixo no registro de
DI.

### Decision 8: `Contact.Metadata` — dicionário genérico, capturado só na criação

```csharp
// Contacts/Entities/Contact.cs
public IReadOnlyDictionary<string, string> Metadata { get; private set; } = new Dictionary<string, string>();

public Contact(Guid channelId, string externalId, IReadOnlyDictionary<string, string> metadata)
{
    // ...
    Metadata = metadata;
}
```

Persistido como `jsonb` (`HasConversion` de/para `string` via
`JsonSerializer`, ou `.HasColumnType("jsonb")` com um `ValueComparer`
explícito para dicionários — EF Core não compara `IReadOnlyDictionary` por
valor por padrão, precisa de um comparer customizado para o change tracker
detectar corretamente que não houve mutação). Dicionário genérico
(`IReadOnlyDictionary<string, string>`), não um campo `PhoneNumber`
específico — decisão confirmada nesta exploração: mantém `Contact`
agnóstico ao tipo de adapter (o próximo adapter, ex. Telegram, guarda
`{"username": "..."}` sem precisar de outra migration no mesmo campo).

Threading do metadado desde o handler de webhook até a persistência:

```csharp
// Orchestration/IInboundMessageOrchestrator.cs
Task ReceiveMessageAsync(
    Guid channelId,
    string externalId,
    string text,
    DateTimeOffset receivedAt,
    IReadOnlyDictionary<string, string> contactMetadata,
    CancellationToken cancellationToken);

// Contacts/IContactSessionResolver.cs
Task<Session> FindOrCreateSessionAsync(
    Guid channelId,
    string externalId,
    IReadOnlyDictionary<string, string> contactMetadata,
    CancellationToken cancellationToken);
```

Parâmetro obrigatório (não opcional/nullable) — consistente com o estilo já
usado no projeto de assinaturas explícitas sem valores default escondendo
comportamento (ex. nenhum outro parâmetro de `ReceiveMessageAsync` é
opcional). Chamadores que não têm metadado (o `test-channel` da change
anterior, testes existentes) passam
`new Dictionary<string, string>()` explicitamente — mesmo custo de churn já
aceito e registrado na change anterior para `"WhatsApp"`/`"Telegram"` →
`"test-channel"`.

`ContactSessionResolver.FindOrCreateContactAsync` só usa `contactMetadata`
no branch de criação (`new Contact(channelId, externalId, contactMetadata)`);
no branch de reaproveitamento (`Contact` já existe), o parâmetro recebido é
ignorado — mesmo tratamento imutável que `ExternalId` já recebe hoje (não
há `UpdateMetadata` na entidade).

`ContactResponse.FromEntity` passa a incluir `Metadata` na resposta —
consulta de contatos (`GET /contacts`) reflete o dado capturado.

**Alternativa descartada**: campo `PhoneNumber` específico em vez de
`Metadata` genérico. Rejeitada — motivo já registrado na exploração:
amarraria o schema de `Contact` a um conceito (telefone) que não faz
sentido para todo adapter, exigindo nova migration a cada novo tipo de
identificador de exibição.

**Alternativa descartada**: parâmetro opcional/nullable (`= null`) em vez
de obrigatório. Rejeitada — nullable esconderia, na assinatura, que
"nenhum metadado" é uma escolha ativa de quem chama, não um default
inofensivo; forçar o valor vazio explícito no `test-channel` e nos testes
existentes é mais honesto sobre o novo parâmetro.

### Decision 9: `docker-compose.yml` — serviço `waha` para desenvolvimento local

```yaml
waha:
  image: devlikeapro/waha:latest
  restart: unless-stopped
  environment:
    WHATSAPP_DEFAULT_ENGINE: GOWS
  ports:
    - "${WAHA_PORT:-3000}:3000"
```

Imagem oficial confirmada nesta investigação (`devlikeapro/waha`, renomeada
de `devlikeapro/whatsapp-http-api` — usar o nome atual). Engine `GOWS`
(mais recente, sem dependência de navegador embutido, ao contrário de
`WEBJS`). Sem sessão pré-configurada, sem volume nomeado para persistência
de sessão — escopo desta fatia é rodar localmente o suficiente para o
checklist de round-trip manual (Testes, tasks.md); persistência de sessão
do WAHA entre restarts do container fica fora do escopo (o operador refaz o
QR code se o container for recriado, aceitável para desenvolvimento local).

## Risks / Trade-offs

- **[Risco]** Nenhuma verificação de autenticidade do webhook de entrada
  (Decision 6) — qualquer cliente HTTP que descubra um `channelId` (`Guid`,
  não adivinhável por força bruta, mas exposto em `ChannelResponse` para
  qualquer chamador de `GET /channels`) pode postar um payload de mensagem
  falso, criando `Contact`/`Session`/disparando `SendMessage` contra
  `apps/api` em nome de um canal que não controla → **Mitigação**: aceito
  conscientemente nesta fatia (decisão já confirmada antes deste design);
  mitigação futura concreta e de baixo custo nomeada (HMAC nativo do WAHA,
  Decision 6) para quando isso virar prioridade.
- **[Risco]** Configuração manual da sessão WAHA (Decision 3) é um passo
  fora do sistema, sem verificação automática de que foi feita
  corretamente — um canal `"waha"` cadastrado sem a sessão configurada no
  WAHA correspondente simplesmente nunca recebe mensagens, sem nenhum sinal
  de erro em `apps/inbox` → **Mitigação**: aceito como fatia mínima
  (Decision 3); o checklist de round-trip manual (Testes) existe
  exatamente para validar essa configuração de ponta a ponta antes de
  considerar o canal operacional.
- **[Trade-off]** `Contact.Metadata` como `jsonb` com `IReadOnlyDictionary`
  exige um `ValueComparer` customizado no mapeamento EF Core (Decision 8) —
  mecanismo novo neste projeto (nenhum outro campo usa dicionário) → aceito
  porque é a abordagem idiomática do EF Core para esse tipo de coluna, e
  fica isolado no mapeamento de `Contact` em `AppDbContext`, sem vazar para
  o resto do código.
- **[Trade-off]** Estender `IInboundMessageOrchestrator.ReceiveMessageAsync`
  e `IContactSessionResolver.FindOrCreateSessionAsync` com um parâmetro
  obrigatório novo é churn real nos testes existentes (`InboundMessageOrchestratorTests`,
  `ContactSessionResolverTests`, `DebounceRestartAndConcurrencyTests`) →
  aceito pelo mesmo raciocínio já usado na change anterior para churn
  equivalente: parâmetro obrigatório e explícito é mais honesto do que
  opcional escondendo a mudança de contrato (Decision 8).

## Migration Plan

- Nova migration EF Core: `Contact.Metadata` (`jsonb`, `NOT NULL DEFAULT '{}'::jsonb`
  para não quebrar linhas existentes). Verificação: rodar
  `dotnet ef migrations add AddContactMetadata` e conferir que o diff
  gerado só adiciona essa coluna, sem alterar nenhuma tabela além de
  `Contacts`.
- Deploy aditivo no restante: `IInboundWebhookHandler` é contrato novo
  (nenhum adapter existente precisa implementá-lo além do `"waha"` sendo
  adicionado nesta própria fatia); a rota `POST /webhooks/{channelId}`
  nunca existiu; `webhookUrl` muda de valor, mas nenhum cliente real
  consumia o formato antigo (nunca foi mapeado — Non-Goal explícito da
  change anterior).
- Rollback: reverter o binário de `apps/inbox`; a migration de `Contact.Metadata`
  é aditiva (coluna nova com default), reversível sem perda de dados dos
  campos existentes se for necessário reverter a migration também.
