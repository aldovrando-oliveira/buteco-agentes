## Context

`apps/inbox` hoje persiste `Channel`, `Contact`, `Session` e `PendingDispatch`
(`Infrastructure/AppDbContext.cs`). `PendingDispatch` é o buffer de debounce
de `inbox-orquestrador-debounce`, não histórico: agrupa texto de entrada
numa coleção owned/JSON (`Messages`, `ToJson()`), dispara `SendMessage`
contra `apps/api`, e é **removida** (`dbContext.Remove`) em praticamente todo
caminho terminal:

- `DebounceSweepService.HandleResponseAsync` — resposta síncrona já terminal
  (rejeição)
- `DebounceSweepService.TryDispatchAsync`, catch de `A2AException` — rejeição
  de protocolo
- `DebounceSweepService.HandleTransportFailureAsync` — falha de transporte
  esgotando `MaxDispatchAttempts`
- `PushNotificationEndpoints.ReceiveAsync` — push notification válida,
  sucesso ou não

A resposta de saída do agente passa por `IOutboundMessageSender.SendAsync`
(`PushNotificationEndpoints.DeliverResponseAsync`, único caller no repo
inteiro) e não é gravada em nenhum lugar além do canal externo.

O histórico "de verdade" da conversa vive em `apps/api`/`apps/workers`
(`A2ATaskRecord.Payload`, por `ContextId`), mas isolamento estrito entre apps
(sem FK, sem `ProjectReference`, bancos Postgres separados —
`01-ARQUITETURA_E_CONVENCOES.md`) significa que `apps/inbox` precisa da
própria visão para a etapa 3 (tela de operador, change separada).

### Varredura obrigatória: risco de aliasing do change tracker do EF Core (pendência de `inbox-fix-concorrencia-orquestrador`)

Pré-requisito desta change, porque justifica a restrição de desenho da
Decisão 1 abaixo. Dois eixos, rodados sobre `apps/api`, `apps/inbox` e
`apps/workers` inteiros:

- **Eixo A** (gatilhos de re-leitura): `ReloadAsync`, `.Reload(`, `.Entry(`,
  `ChangeTracker.Clear`, tratamento de `DbUpdateConcurrencyException`.
- **Eixo B** (superfícies owned/JSON de verdade): `OwnsMany`, `OwnsOne`,
  `.ToJson(`.

**Eixo A** só existe em `apps/inbox`: três `dbContext.Entry(x).State =
EntityState.Detached` (`ContactSessionResolver.cs:72`,
`InboundMessageOrchestrator.cs:59,112`), todos seguidos de rebusca via LINQ
— nunca `ReloadAsync`. Nenhum `.Reload(` ou `ChangeTracker.Clear` no repo
inteiro. `DbUpdateConcurrencyException` só é tratada nesses dois arquivos.

**Eixo B** — um único site real no repo inteiro é `OwnsMany`/`ToJson`:

| Superfície | Mapeamento EF Core | (1) Mutação | (2) Relida na mesma `DbContext` após mutar? | (3) Concorrência real? | Classificação |
|---|---|---|---|---|---|
| `PendingDispatch.Messages` (`apps/inbox/AppDbContext.cs:101`) | `OwnsMany(...).ToJson()` — navegação owned, rastreada por snapshot estrutural | In-place (`Messages.Add`) | Sim — detach + rebusca via `FindPendingAsync` (`InboundMessageOrchestrator.cs`, Decisão 1 de `inbox-fix-concorrencia-orquestrador`) | Sim — causa raiz confirmada do bug original | **Já corrigido.** Risco fechado com o mecanismo certo (detach+rebusca); `ReloadAsync` foi descartado justamente por causar aliasing de snapshot nessa mesma coleção. Não reabrir. |
| `Agent.Skills` (`apps/api/AppDbContext.cs:47`) | `HasConversion` escalar em jsonb + `ValueComparer` explícito — **não** é navegação owned | Substituída por lista nova inteira em `Agent.UpdateDetails` — nunca `.Add`/`.Remove` sobre a lista existente | Não há nenhum `Entry()`/reload nas proximidades de `Agent` | Não | **Seguro.** Categoria de mapeamento diferente da que causou o bug — um `HasConversion` serializa o valor "current" inteiro a cada `SaveChanges`, não depende de snapshot estrutural por elemento. |
| `AgentMcpServer.AllowedTools` (`apps/api/AppDbContext.cs:104`) | Mesmo padrão `HasConversion` escalar + `ValueComparer` | Substituída wholesale em `ReplaceAgentMcpServersCommandHandler` | Não | Não | **Seguro**, mesmo motivo. |
| `Contact.Metadata` (`apps/inbox/AppDbContext.cs:47`) | Mesmo padrão `HasConversion` escalar + `ValueComparer` | Nunca mutada — gravada só na criação (`ContactSessionResolver.FindOrCreateContactAsync`), sem nenhum método de atualização | N/A (nunca mutada após persistida) | Não | **Seguro** — imutável por design, categoria de mapeamento também diferente. |
| `Channel.EncryptedCredentials` (`apps/inbox/Channels/Entities/Channel.cs:14`) | `string` escalar simples — nem é coleção | Substituição total via `SetEncryptedCredentials` | N/A | N/A | **Fora de categoria.** Nunca foi uma coleção owned/JSON; citada na pendência original por engano de categoria. |

Interseção real dos dois eixos (mutado in-place **e** relido na mesma
instância **e** concorrência real) = só `PendingDispatch.Messages`, e esse
já foi corrigido. **Nenhum segundo site com os três "sim"** — a pendência de
varredura está fechada, sem necessidade de nova change de concorrência.

**Correção de débito, feita junto (convenção 9)**: a seção *Risks* de
`openspec/changes/archive/2026-08-16-inbox-fix-concorrencia-orquestrador/design.md`
ainda descrevia o mecanismo abandonado (`ReloadAsync`) num item de risco, em
vez do mecanismo final (detach+rebusca) documentado na Decisão 1 do mesmo
arquivo — corrigida como parte desta change (arquivo já arquivado, correção
textual, sem impacto funcional).

## Goals / Non-Goals

**Goals:**
- Persistir mensagens de entrada e saída por `Session`, com direção,
  conteúdo, tipo de conteúdo, instante e (entrada) identificador externo
  para deduplicação de webhook reentregue.
- Persistir status de entrega da mensagem de saída — sucesso/falha do envio
  ao provedor via `IOutboundMessageSender`, com motivo quando falhar — sem
  mudar o contrato do plugin.
- Espelhar o estado do ciclo de dispatch (`Pending`/`Dispatching`/`Failed`/
  `Completed` — quatro estados, ver Decisão 6) nas mensagens de entrada
  agrupadas por ele, sobrevivendo à remoção da `PendingDispatch`
  correspondente.
- Capturar e manter atualizado `Contact.DisplayName` a cada mensagem de
  entrada, para as duas origens (WAHA, Telegram).
- Expor as duas consultas de leitura que a etapa 3 (UI, change separada) vai
  precisar: mensagens de uma `Session` em ordem cronológica, e sessões de um
  canal por última atividade com prévia da última mensagem (ver Decisão 10).

**Non-Goals:**
- Retenção/expurgo de mensagens (TTL) — sem gatilho de volume ainda
  atingido; registrado como item em aberto em `02-HISTORICO_E_STATUS.md`
  quando esta change for aplicada.
- Qualquer UI — etapa 3, change separada.
- Recibos de entrega/leitura — o status de saída modela só sucesso/falha do
  nosso envio ao provedor.
- Unificação de identidade de contato entre canais — `Contact` continua
  `(ChannelId, ExternalId)`.
- Persistir conteúdo binário de mídia — só um marcador de tipo (ver Decisão
  8).
- Mudar o contrato de plugin (`IChannelConfigValidator`/
  `IOutboundMessageSender`/`IInboundWebhookHandler`/
  `IChannelWebhookProvisioner`) — os quatro seguem exatamente como são.

## Árvore de pastas (novo/modificado)

```
apps/inbox/src/Buteco.Inbox/
├── Messages/                                          [NOVO]
│   ├── Entities/
│   │   ├── Message.cs                                 [NOVO] entidade, ver Decisão 3
│   │   ├── MessageDirection.cs                         [NOVO] enum: Inbound, Outbound
│   │   ├── MessageContentType.cs                       [NOVO] enum: Text, Image, Audio, Document
│   │   ├── MessageDeliveryStatus.cs                    [NOVO] enum: Sent, Failed (só Outbound)
│   │   └── MessageDispatchStatus.cs                    [NOVO] enum: Pending, Dispatching, Failed, Completed (só Inbound, ver Decisão 6)
│   ├── Queries/
│   │   └── GetSessionMessages/
│   │       ├── GetSessionMessagesQuery.cs               [NOVO]
│   │       └── GetSessionMessagesQueryHandler.cs        [NOVO]
│   ├── Endpoints/
│   │   └── MessageEndpoints.cs                          [NOVO] GET /sessions/{sessionId}/messages
│   └── Responses/
│       └── MessageResponse.cs                           [NOVO]
├── Orchestration/
│   ├── IInboundMessageOrchestrator.cs                   [MOD] ReceiveMessageAsync ganha externalMessageId, displayName
│   ├── InboundMessageOrchestrator.cs                    [MOD] persiste Message de entrada, dedup, grava PendingDispatchId/DispatchStatus
│   ├── DebounceSweepService.cs                          [MOD] espelha Status de PendingDispatch em Message.DispatchStatus (Decisão 6)
│   └── PushNotifications/Endpoints/
│       └── PushNotificationEndpoints.cs                 [MOD] persiste Message de saída, marca Completed nas de entrada do grupo
├── Contacts/
│   ├── Entities/Contact.cs                              [MOD] DisplayName (nullable) + UpdateDisplayName(...)
│   ├── ContactSessionResolver.cs                        [MOD] aceita displayName, chama UpdateDisplayName a cada chamada (único ponto de escrita, Decisão 9)
│   ├── Queries/
│   │   └── GetChannelSessions/                          [NOVO]
│   │       ├── GetChannelSessionsQuery.cs                [NOVO]
│   │       └── GetChannelSessionsQueryHandler.cs         [NOVO] sessões de um canal por última atividade, com prévia da última Message (Decisão 10)
│   ├── Endpoints/
│   │   └── ChannelSessionEndpoints.cs                    [NOVO] GET /channels/{channelId}/sessions
│   └── Responses/
│       └── ChannelSessionResponse.cs                     [NOVO] inclui DisplayName/ExternalId do Contact e prévia da última mensagem
├── Channels/Adapters/
│   ├── Waha/
│   │   ├── WahaWebhookPayload.cs                        [MOD] campo de id de mensagem + nome de exibição (nomes exatos: ver Decisão 5, verificar contra doc real)
│   │   └── WahaInboundWebhookHandler.cs                 [MOD] repassa os campos novos
│   └── Telegram/
│       ├── TelegramWebhookModels.cs                     [MOD] message_id (verificar campo exato)
│       └── TelegramInboundWebhookHandler.cs              [MOD] repassa message_id
└── Infrastructure/
    ├── AppDbContext.cs                                   [MOD] DbSet<Message>, mapeamento, índices (Decisão 7)
    └── Migrations/                                       [NOVO] migration AddMessage

apps/inbox/tests/Buteco.Inbox.Tests/
├── MessagePersistenceTests.cs                            [NOVO] Testcontainers — entrada/saída/dedup/status/vazio/mídia
├── ChannelSessionEndpointsTests.cs                       [NOVO] sessões de canal com prévia — com item/sem item/canal inexistente
├── WahaInboundWebhookHandlerTests.cs                     [MOD] cobre id de mensagem + display name + marcador de mídia
├── TelegramInboundWebhookHandlerTests.cs                 [MOD] idem
├── ContactSessionResolverTests.cs                        [MOD] DisplayName capturado/atualizado
├── DebounceSweepServiceTests.cs                          [MOD] Message.DispatchStatus espelhado (Pending/Dispatching/Failed)
└── PushNotificationEndpointsTests.cs                     [MOD] Message de saída (sucesso/falha) persistida + DispatchStatus Completed
```

Nenhum diretório em `libs/`, nenhuma mudança em `apps/api`, `apps/workers` ou
`apps/frontend` — isolamento entre apps preservado (convenção do projeto).

## Decisions

### Decisão 1: Tabela relacional própria, sem tocar `PendingDispatch` nem sua coleção owned/JSON

`Message` é uma entidade e tabela EF Core novas e independentes, sem
navegação de/para `PendingDispatch` no sentido `PendingDispatch → Message` —
a correlação, quando existir, vai na direção oposta (`Message →
PendingDispatch.Id`, ver Decisão 6). `PendingDispatch.cs` e o mapeamento
`entity.OwnsMany(dispatch => dispatch.Messages, messages =>
messages.ToJson())` em `AppDbContext.cs:101` não são alterados nesta change.

**Por quê**: é exatamente a área do bug de concorrência corrigido em
`inbox-fix-concorrencia-orquestrador` (ver varredura acima) — qualquer
mudança que crie uma segunda forma de mutar/re-ler essa coleção reabriria a
superfície de risco que acabou de ser fechada, exigindo o mesmo nível de
teste de concorrência real (Testcontainers, não caminho feliz) que aquele
fix exigiu. Duplicar o texto da mensagem entre `PendingDispatch.Messages`
(efêmero, existe só durante a janela de debounce + dispatch em voo) e
`Message` (durável) é aceito conscientemente — o volume duplicado é
desprezível (mensagens de texto curtas, janela de poucos minutos) frente ao
custo de reabrir uma área que já causou perda silenciosa de mensagem em
produção.

**Alternativa descartada**: estender `PendingDispatch` para referenciar
`Message` (ex. `Messages` owned collection ganhar um `MessageId`) —
rejeitada explicitamente pela restrição de desenho desta change; teria
exigido re-validar toda a análise de concorrência da coleção owned/JSON.

### Decisão 2: Ponto único de escrita — entrada em `InboundMessageOrchestrator`, saída em `PushNotificationEndpoints`

**Entrada**: `IInboundMessageOrchestrator.ReceiveMessageAsync` já é o ponto
único de fato — `WahaInboundWebhookHandler` e `TelegramInboundWebhookHandler`
convergem nele via `IServiceScopeFactory`. `Message` de entrada é persistida
dentro de `InboundMessageOrchestrator.ReceiveForSessionAsync`, no mesmo
`DbContext`/`SaveChangesAsync` que já resolve `Session` e grava/atualiza
`PendingDispatch` — evita duplicar a lógica de persistência em cada adapter,
e evita uma segunda ida ao banco.

**Saída**: `IOutboundMessageSender.SendAsync` só é chamado em
`PushNotificationEndpoints.DeliverResponseAsync` (blast radius confirmado:
único caller no repo inteiro) — já é ponto único de fato, sem alternativa a
avaliar. `Message` de saída é persistida ali, no mesmo `try/catch` que hoje
só loga a falha do sender.

**Alternativa descartada**: persistir dentro de cada
`IInboundWebhookHandler` — rejeitada porque duplicaria a lógica de
persistência (identificador externo, deduplicação, `DisplayName`) por
adapter, crescendo a cada novo canal, quando o orquestrador já centraliza
exatamente esse ponto de convergência.

### Decisão 3: Modelo de `Message`

```csharp
public class Message
{
    public Guid Id { get; }
    public Guid SessionId { get; }
    public MessageDirection Direction { get; }       // Inbound, Outbound
    public string Content { get; }
    public MessageContentType ContentType { get; }    // Text, Image, Audio, Document
    public DateTimeOffset OccurredAt { get; }

    // Só Inbound — identificador de mensagem do provedor, usado para
    // deduplicar webhook reentregue (Decisão 5). Null em Outbound.
    public string? ExternalId { get; }

    // Só Outbound — resultado do IOutboundMessageSender.SendAsync
    // (Decisão 4). Null em Inbound.
    public MessageDeliveryStatus? DeliveryStatus { get; }
    public string? DeliveryFailureReason { get; }

    // Só Inbound — correlação opcional com o ciclo de dispatch que
    // consumiu esta mensagem (Decisão 6). Sem FK real — a linha de
    // PendingDispatch referenciada é removida ao atingir estado terminal.
    public Guid? PendingDispatchId { get; }
    public MessageDispatchStatus? DispatchStatus { get; }
}
```

`SessionId` com FK real para `Session` (mesmo `DbContext`/banco,
`OnDelete(Cascade)`, mesmo padrão de `PendingDispatch.SessionId`).

`ContentType` é obrigatório (não nullable) em ambas as direções, inclusive
saída. Na saída, o valor é sempre `Text` nesta fatia: a extração de resposta
do agente já em produção (`PushNotificationEndpoints.ExtractResponseText`)
já ignora partes não-textuais da `AgentTask` (`Raw`/`Url`/`Data` — comentário
existente: "Partes não textuais... são ignoradas nesta fatia") e o envio ao
agente também só usa `Part.FromText`
(`DebounceSweepService.BuildSendMessageRequest`) — não existe hoje nenhum
caminho de saída que produza outro tipo de conteúdo, então gravar sempre
`Text` reflete o sistema real, não uma suposição. Revisitar se um caminho de
saída não-textual for adicionado no futuro.

### Decisão 4: Status de entrega modela só sucesso/falha do envio ao provedor

`IOutboundMessageSender.SendAsync(OutboundMessage, CancellationToken)` já
distingue os dois casos sem qualquer alteração de assinatura: retorno normal
= enviado, exceção = falhou. `PushNotificationEndpoints.DeliverResponseAsync`
já envolve a chamada num `try/catch` — hoje só loga; passa a também gravar
`Message` com `DeliveryStatus.Sent` no caminho feliz e
`DeliveryStatus.Failed` + `DeliveryFailureReason` (mensagem da exceção) no
catch.

**Tensão explícita com a convenção 4** (degradação graciosa: falha de
dependência externa é logada, não propagada): essa convenção continua
valendo integralmente no nível de *comportamento* — a falha do sender
continua não derrubando a task principal, `PendingDispatch` continua sendo
removida normalmente, nenhuma exceção nova é propagada. O que muda é que a
falha, antes só visível em log, passa a ser **estado consultável**
(`Message.DeliveryStatus = Failed` numa `Session` específica) — não é uma
exceção à convenção 4, é uma extensão dela: log continua existindo, e ganha
uma contraparte durável e específica da sessão, que é exatamente o que a
etapa 3 precisa mostrar ao operador (ícone de erro com motivo, conforme
`02-HISTORICO_E_STATUS.md`, "Próximo passo").

Não são recibos de entrega/leitura do destinatário final — o WhatsApp/
Telegram nunca confirma isso de volta para `apps/inbox` nesta fatia, e não é
modelado.

### Decisão 5: Deduplicação por identificador externo de mensagem — mecanismo novo, não reaproveitável dos existentes

**Não existe hoje nenhuma deduplicação por identificador de mensagem em
lugar nenhum do orquestrador.** Os dois índices únicos existentes resolvem
problemas diferentes:
- `Contact (ChannelId, ExternalId)` — identidade de contato, não de
  mensagem.
- `PendingDispatch.SessionId` (filtro `Status = 'Pending'`) — no máximo uma
  `PendingDispatch` em aberto por sessão, não identidade de mensagem
  individual.

`Message` ganha um índice único parcial `(SessionId, ExternalId) WHERE
"Direction" = 'Inbound' AND "ExternalId" IS NOT NULL` — mesmo padrão de
índice único + `HasFilter` já usado em `PendingDispatch` (`AppDbContext.cs`)
— e `InboundMessageOrchestrator` trata `DbUpdateException` por violação de
unicidade nesse índice com o mesmo padrão de detach + no-op já usado para
`Contact`/`PendingDispatch` (mensagem já persistida, reentrega ignorada,
sem re-adicionar ao buffer de debounce).

**Gap real, a resolver na implementação**: nem `WahaWebhookMessagePayload`
(`From/To/Body/FromMe`) nem `TelegramMessage`/`TelegramUpdate`
(`Chat/From/Text`) carregam hoje um identificador de mensagem individual —
só identificam o contato/chat de origem. `IInboundMessageOrchestrator
.ReceiveMessageAsync` e os dois handlers precisam de um parâmetro novo
(`externalMessageId`), e os dois payloads precisam ganhar o campo
correspondente:
- **WAHA**: precisa confirmar o nome exato do campo de id de mensagem contra
  a documentação real do WAHA antes de codificar (convenção 6 — "nunca
  confiar em SDK de terceiro de memória"). O design.md arquivado de
  `inbox-adapter-waha` investigou `payload.body`/`payload.from`, mas não
  documentou nenhum campo de id nem de nome de exibição — não assumir que é
  `payload.id` ou `payload._data.notifyName` sem verificar contra uma
  instância/doc real.
- **Telegram**: `update.message.message_id` é o campo documentado pela
  Bot API para identificador de mensagem — ainda assim, confirmar contra a
  documentação real (mesmo padrão de verificação que
  `TelegramWebhookModels.cs` já aplicou para `first_name`/`chat.id`) antes
  de codificar, não assumir de memória de treinamento.

Mesma verificação vale para o campo de nome de exibição (WAHA `pushName` —
não confirmado; ver Decisão 8) usado por `Contact.DisplayName`.

### Decisão 6: Estado de dispatch renderável — correlação `Message → PendingDispatch`, sem alterar `PendingDispatch`

`PendingDispatch` é efêmera por construção (ver Context) — no momento em que
uma consulta de histórico for feita, a linha que carregava o estado do
dispatch já não existe na maioria dos casos. Ler `PendingDispatch` depois do
fato não resolve o requisito de "estado do dispatch renderável".

`Message` (só `Inbound`) carrega `PendingDispatchId` (`Guid?`, sem restrição
de FK — a linha referenciada é removida) e `DispatchStatus`
(`Pending`/`Dispatching`/`Failed`/`Completed`), espelhado em todo ponto que
já muta ou remove `PendingDispatch` hoje, no mesmo `SaveChangesAsync`:

1. `InboundMessageOrchestrator` — ao criar/anexar a `PendingDispatch`, grava
   `PendingDispatchId` + `DispatchStatus = Pending` nas `Message` recém-
   persistidas.
2. `DebounceSweepService.TryDispatchAsync`, ao reivindicar a linha —
   `DispatchStatus = Dispatching`.
3. `DebounceSweepService.TryDispatchAsync`, catch de `A2AException`
   (rejeição de protocolo) — `DispatchStatus = Failed`.
4. `DebounceSweepService.HandleResponseAsync`, ramo de resposta síncrona já
   terminal sem push notification esperada (rejeição) — `DispatchStatus =
   Failed`.
5. `DebounceSweepService.HandleTransportFailureAsync` — ao esgotar
   `MaxDispatchAttempts` (`MarkFailed` + `Remove`), `DispatchStatus =
   Failed`; abaixo do limite (retry, `PendingDispatch` volta a `Pending` via
   `RegisterTransportFailure`), `DispatchStatus` volta a `Pending` — mesmo
   espelhamento do passo 1.
6. `PushNotificationEndpoints.ReceiveAsync` — ao processar uma push
   notification válida (a única forma de a `PendingDispatch` chegar até
   aqui), `DispatchStatus = Completed`, com ou sem mensagem de resposta
   associada, antes de remover a `PendingDispatch`.

Os passos 3, 4 e 5 (limite esgotado) convergem no mesmo `Failed`: as três
causas — falha de transporte esgotada, rejeição síncrona de `SendMessage`,
rejeição de protocolo A2A — têm a mesma consequência prática para quem lê a
timeline depois (nenhuma resposta vai chegar para esse grupo de mensagens);
distinguir a causa exigiria um quinto estado que nada nesta fatia consome
(ver Risks). `Completed` (renomeado de um nome inicial que colidia com
`MessageDeliveryStatus.Sent`/`Failed` da mensagem de saída — dois enums na
mesma entidade com vocabulário de "entrega" significando coisas diferentes;
ver histórico desta decisão) é atingido só via push notification, cobrindo
tanto o caso com resposta textual quanto sem.

Isso preserva a restrição da Decisão 1 por completo — nenhuma linha nova em
`PendingDispatch.cs`, nenhuma mudança no `OwnsMany`/`ToJson`. O custo é uma
escrita adicional em `Message` nos pontos acima, todos no mesmo
`SaveChangesAsync` que já existe para cada um — sem round-trip extra ao
banco.

**Alternativa descartada**: `PendingDispatch` referenciar `Message` (FK ou
coleção) — violaria a restrição de desenho explícita desta change (Decisão
1) e reabriria a superfície de risco da coleção owned/JSON.

### Decisão 7: Índices dimensionados para as duas consultas do domínio

Duas consultas guiam o desenho de índice:

1. **"Mensagens de uma Session em ordem cronológica"** — índice composto
   `(SessionId, OccurredAt)` em `Message`, mesmo padrão que já serve
   `PendingDispatches.SessionId` e `PendingDispatches.TaskId`
   (`AppDbContext.cs`). Cobre tanto o filtro (`WHERE SessionId = @id`)
   quanto a ordenação (`ORDER BY OccurredAt`) sem sort adicional.
2. **"Sessões de um canal por última atividade"** — usa `Session.LastActivityAt`
   (já uma coluna simples, sem índice hoje) combinada com o join existente
   `Session.ContactId → Contact.ChannelId` (`Contact` já tem índice único em
   `(ChannelId, ExternalId)`, que cobre o predicado `ChannelId = @id` mas
   não a ordenação por `LastActivityAt`). O índice relevante seria novo em
   `Session`: `(ContactId, LastActivityAt)` não ajuda diretamente porque o
   filtro é por `ChannelId` (via `Contact`), não por `ContactId` — a
   consulta real é `Session JOIN Contact WHERE Contact.ChannelId = @id
   ORDER BY Session.LastActivityAt DESC`. Dimensionamento proposto: manter a
   consulta como join (volume de `Session` por canal é ordens de grandeza
   menor que `Message`), sem índice composto novo nesta fatia — revisitar
   se a consulta de "sessões por canal" virar gargalo medido, não
   antecipado.

   **Revisitado por causa da Decisão 10 (prévia da última mensagem)**: essa
   consulta passou a incluir, para cada `Session` retornada, a última
   `Message` de entrada ou saída dessa sessão. Esse acesso adicional
   ("última mensagem de uma `Session`") não é uma consulta nova a
   dimensionar — é exatamente o índice composto `(SessionId, OccurredAt)`
   do item 1 acima, usado por sessão individual (`DISTINCT ON (SessionId)
   ... ORDER BY SessionId, OccurredAt DESC`, ou `ROW_NUMBER() OVER
   (PARTITION BY SessionId ORDER BY OccurredAt DESC)` — decisão de
   implementação, não de índice). Nenhum índice novo é necessário para a
   prévia; o índice de `Message` já dimensionado no item 1 serve os dois
   usos.

### Decisão 8: Mídia — marcador de tipo, sem conteúdo binário

`Message.ContentType` (`Text`/`Image`/`Audio`/`Document`) é gravado desde já
— prepara terreno para a etapa 3 diferenciar mídia na timeline — mas o
conteúdo binário não é persistido nem baixado nesta fatia. Quando não houver
texto acompanhando a mídia, `Content` grava um marcador textual (ex. `"[mídia:
image]"`). Mapeamento do shape real de mídia de cada adapter (WAHA
`hasMedia`/`media`, Telegram `photo`/`voice`/`document` no `Update`) faz
parte da implementação desta change — **verificar contra documentação real
antes de codificar** (mesma ressalva da Decisão 5), não assumir o shape de
memória de treinamento.

### Decisão 9: `Contact.DisplayName` — mutator novo, distinto do congelamento de `Metadata`

`Contact.Metadata` é gravado só na criação, por design explícito (comentário
em `Contact.cs`, Requirement "Captura de metadado do contato na criação" de
`inbox-contact-session`) — chamadas subsequentes ignoram o metadado
informado. `DisplayName` é um campo novo, com semântica **oposta**
deliberadamente: atualizado a cada mensagem de entrada, porque o nome de
exibição de um contato pode mudar ao longo do tempo (troca de nome no
WhatsApp, `username`/`first_name` no Telegram), enquanto o metadado bruto de
criação não tem esse requisito.

`Contact` ganha `UpdateDisplayName(string? displayName)`, chamado por
`ContactSessionResolver.FindOrCreateSessionAsync` — único ponto de escrita
(mesmo espírito da Decisão 2) — a cada chamada, tanto na criação quanto no
reaproveitamento de `Contact` existente. É o ponto certo porque já é o único
lugar do sistema que hoje muta `Contact` (criação em
`FindOrCreateContactAsync`) — `InboundMessageOrchestrator`, que roda depois
e não recebe nem manipula `Contact` diretamente hoje, não precisaria dessa
responsabilidade nova. Quando o
adapter não extrair nenhum nome (ex. WAHA sem `pushName` no payload,
Telegram sem `username`/`first_name`), `DisplayName` permanece `null` (na
criação) ou **não é sobrescrito** (numa atualização — evita que uma mensagem
sem esse dado apague um `DisplayName` já capturado antes).

**Por que não estender o comportamento de `Metadata` em vez de um campo
novo**: mudar a semântica de `Metadata` para "atualiza a cada mensagem"
quebraria o Requirement já especificado e testado de
`inbox-contact-session` ("metadado informado em chamada subsequente não
altera o Contact existente") — um campo novo, com contrato próprio, evita
esse conflito e deixa explícito que são dois conceitos diferentes (metadado
genérico de extensão vs. nome de exibição com necessidade real de estar
atualizado).

### Decisão 10: Duas consultas de leitura expostas agora — mensagens de uma sessão e sessões de um canal com prévia

A etapa 3 precisa de duas consultas, não uma: a timeline de uma `Session`
(mensagens em ordem cronológica) e a lista de sessões de um canal por
última atividade (a tela de entrada da etapa 3, antes de abrir qualquer
sessão). A segunda não existe em lugar nenhum hoje —
`GetContactSessions`/`GET /contacts/{id}/sessions` é por `Contact`, não por
`Channel`, e não serve esse caso. A Decisão 7 analisa o acesso subjacente,
mas nenhuma decisão anterior definia quem expõe essa segunda consulta —
ficaria no vão entre esta change e a etapa 3, que nasceria sem backend para
sua própria tela inicial. As duas consultas entram nesta change, pelo mesmo
argumento:

- `GET /sessions/{sessionId}/messages` retorna as mensagens da sessão em
  ordem cronológica (vazio, não erro, para sessão sem mensagem — 404 para
  sessão inexistente, mesmo padrão de `GET /contacts/{id}/sessions`).
- `GET /channels/{channelId}/sessions` retorna as sessões desse canal
  ordenadas por `LastActivityAt` (vazio, não erro, para canal sem nenhuma
  sessão — 404 para canal inexistente, mesmo padrão).

As duas são autenticadas por padrão (sem `.AllowAnonymous()`), herdam o
`FallbackPolicy` — nenhuma entra na allowlist de rotas anônimas de
`apps/inbox`, que permanece exatamente a de `auth-login-e-servico`
(`GET /health`, `POST /webhooks/{channelId}`,
`POST /internal/push-notifications`).

Expostas agora, mesmo com a UI (etapa 3) fora de escopo, porque: (a) seguem
a convenção de sequenciamento catálogo→vínculo→**execução**→UI — a consulta
é parte da execução/persistência, a UI é só a apresentação; (b) sem isso, os
testes desta change ("sessão sem nenhuma mensagem retorna vazio, não erro",
e o mesmo par para canal sem sessão) não têm superfície pública para
exercer; (c) mesmo padrão já estabelecido para `Contact`/`Session` em
`inbox-contact-session` (`GetContactSessions`).

**Sub-decisão: a lista de sessões de um canal inclui prévia da última
mensagem.** A etapa 3 já descreve essa lista com prévia (conteúdo e direção
da última mensagem de cada sessão) — sem ela, a etapa 3 faria uma segunda
chamada por sessão (N+1) ou perderia um elemento já especificado da tela.
`GET /channels/{channelId}/sessions` devolve, para cada sessão, os campos de
`Contact` que motivaram esta change inteira (`DisplayName`, `ExternalId` —
sem eles a lista mostra identificador cru, o problema original desta
change) e a prévia da última `Message` (conteúdo, direção, instante). Isso
faz o requisito passar a ler `Message`, não só `Session`/`Contact` — a
Decisão 7 foi revisitada explicitamente acima para cobrir esse acesso; o
índice já dimensionado ali para a timeline (`(SessionId, OccurredAt)`) serve
os dois usos, sem índice novo.

**Alternativa descartada**: devolver a lista de sessões sem prévia, deixando
a etapa 3 buscar a última mensagem de cada sessão separadamente — rejeitada
porque geraria N chamadas a `GET /sessions/{id}/messages` (ou uma variante
"última mensagem" dedicada) para renderizar uma única tela de lista, quando
uma consulta já teria toda sessão em mãos para resolver a prévia numa única
ida ao banco.

## Risks / Trade-offs

- **[Risco] Duplicação de conteúdo entre `PendingDispatch.Messages` (owned/
  JSON, efêmero) e `Message` (relacional, durável)** → Mitigação: aceito
  conscientemente (Decisão 1) — volume desprezível (texto curto, janela de
  poucos minutos) frente ao custo de reabrir a área do bug de concorrência
  já corrigido.
- **[Risco] Escrita em `Message` nos mesmos `SaveChangesAsync` que já mutam
  `PendingDispatch` (`DebounceSweepService`, `PushNotificationEndpoints`)
  introduz uma superfície nova de possível inconsistência entre as duas
  tabelas** (ex. `PendingDispatch` removida mas `Message.DispatchStatus`
  não atualizado, por exceção entre as duas operações) → Mitigação: as
  escritas em `Message` entram no mesmo `SaveChangesAsync` (mesma
  transação implícita do EF Core) que já muta `PendingDispatch` nesses três
  pontos, não uma chamada separada — ou as duas mudanças persistem juntas,
  ou nenhuma persiste (rollback). Precisa de teste dedicado cobrindo esse
  caminho (transação atômica entre `PendingDispatch` e `Message` nos três
  pontos da Decisão 6).
- **[Risco] Identificador externo de mensagem (WAHA/Telegram) e campo de
  nome de exibição do WAHA não confirmados contra documentação real** →
  Mitigação: registrado explicitamente nas Decisões 5 e 9 como verificação
  obrigatória antes de codificar (convenção 6) — não bloqueia esta proposta,
  bloqueia o início da implementação desses dois pontos específicos.
- **[Risco] `Message.DeliveryStatus`/`DispatchStatus` como estado persistido
  tensiona a convenção 4 (degradação graciosa só logada)** → Mitigação:
  tratado explicitamente na Decisão 4 — não é exceção à convenção, é
  extensão dela (log continua existindo); comportamento de não propagar
  exceção não muda em nenhum caminho existente.
- **[Trade-off] Consulta "sessões de um canal por última atividade" fica sem
  índice composto dedicado nesta fatia** (Decisão 7) → aceito
  conscientemente por volume esperado de `Session` por canal ser pequeno;
  revisitar com medição real, não antecipação.
- **[Trade-off] `DispatchStatus = Failed` agrupa três causas distintas**
  (esgotamento de tentativas de transporte, rejeição síncrona de
  `SendMessage`, rejeição de protocolo A2A — Decisão 6) → aceito
  conscientemente: as três compartilham a mesma consequência prática para
  quem lê a timeline depois (nenhuma resposta virá para esse grupo de
  mensagens), e nada na etapa 3 descrita até agora pede diferenciar a causa
  na UI (diferente do lado de saída, onde `DeliveryFailureReason` já existe
  porque o ícone de erro da etapa 3 precisa de um motivo). Revisitar se a
  UI precisar distinguir as três causas.

## Migration Plan

Uma migration EF Core nova (`AddMessage`) cria a tabela `messages` com FK
`SessionId → Session.Id` (`OnDelete(Cascade)`, mesmo padrão de
`PendingDispatch.SessionId`) e o índice único parcial da Decisão 5. Segunda
migration (ou a mesma) adiciona a coluna nullable `DisplayName` em
`Contact` — coluna nova sem default obrigatório, contatos existentes ficam
com `DisplayName = null` até a próxima mensagem recebida, sem necessidade de
backfill (nenhum dado histórico de mensagem existe para extrair o nome
retroativamente — é exatamente o problema que esta change resolve daqui pra
frente). Sem dado a migrar de `PendingDispatch` para `Message` — não há
histórico de `PendingDispatch` a recuperar (a tabela é efêmera e as linhas
já terminadas já foram removidas). Rollback: reverter a migration (`dotnet
ef database update <anterior>`); sem risco a dados de outras tabelas, já
que `Message`/`Contact.DisplayName` são aditivos.

## Open Questions

Nenhuma incerteza de negócio/produto em aberto — as decisões de escopo
(mídia como marcador, semântica de status de entrega, deduplicação nova,
índices) estão resolvidas acima. As duas verificações técnicas pendentes
(nome exato do campo de id de mensagem do WAHA e do campo de nome de
exibição do WAHA) são verificação de contrato externo antes de codificar
(convenção 6), não decisão de produto — registradas nas Decisões 5 e 9,
não aqui.
