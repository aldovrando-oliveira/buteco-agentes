## Context

`apps/api` expõe `/agents/{id}/a2a` via um `A2AServer` composto manualmente
(não via `AddA2AServer`/DI — ver `openspec/changes/archive/2026-07-26-backend-agente-a2a-mvp/design.md`,
Decisão 2). `SendMessage` cria a task em `submitted` e publica um job mínimo
(`TaskJobMessage { TaskId, AgentId, ContextId }`) no RabbitMQ; `apps/workers`
consome o job, chama o LLM e escreve o resultado de volta no mesmo store
Postgres (`a2a_tasks`, coluna `payload` jsonb com a `AgentTask` serializada).
Push notification é Non-Goal explícito desde essa change original e desde
`backend-a2a-agent-card` (`Capabilities.PushNotifications` fixado em `false`).

**Investigação feita contra o SDK real** (decompilação de `A2A`
1.0.0-preview2 via `ilspycmd`, mesmo método das changes anteriores — não
apenas o README):

1. `SendMessageRequest.Configuration` (tipo `SendMessageConfiguration?`) tem
   um campo `PushNotificationConfig` (tipo `PushNotificationConfig?`)
   embutido diretamente no `SendMessage` — **não** existe corrida entre o
   registro do webhook e a task terminar, porque o config chega atomicamente
   junto da própria mensagem que cria a task. `A2AServer.ResolveContextAsync`
   copia `request.Configuration` para `RequestContext.Configuration`, que
   chega para `IAgentHandler.ExecuteAsync(RequestContext context, ...)` —
   ou seja, direto para `EnqueueingAgentHandler`, sem nenhuma composição
   adicional necessária.
2. Os 4 métodos JSON-RPC dedicados do protocolo
   (`CreateTaskPushNotificationConfigAsync`, `Get`/`List`/`Delete...`, já
   roteados sem uso real por `RoutingA2ARequestHandler` desde o MVP) têm
   implementação base no `A2AServer` que **sempre lança**
   `A2AException("Push notifications not supported.", A2AErrorCode.PushNotificationNotSupported)`
   — são `virtual`, mas o SDK não tem nenhum storage por trás. Suportar esse
   caminho exigiria subclassar `A2AServer` e implementar CRUD do zero — sem
   necessidade, dado que o mecanismo embutido cobre o escopo (um webhook,
   registrado na criação da task).
3. `PushNotificationConfig`: `{ Id?: string, Url: string (obrigatório),
   Authentication?: AuthenticationInfo { Scheme: string (obrigatório),
   Credentials?: string }, Token?: string }`.
4. `TaskUpdater.SubmitAsync()` **não aceita parâmetro de Metadata** — só
   cria `AgentTask { Id, ContextId, Status }`. A persistência de cada evento
   (`ApplyEventAsync`) roda dentro do próprio `A2AServer`, em background, sob
   um lock privado (`ChannelEventNotifier.AcquireTaskLockAsync`) inacessível
   a código externo. `PostgresTaskStore.SaveTaskAsync` (implementação
   própria, idêntica em `apps/api` e `apps/workers`) faz overwrite cego do
   `payload` inteiro, sem controle de concorrência otimista. Gravar Metadata
   diretamente de dentro de `EnqueueingAgentHandler` competiria, sem
   sincronização, com esse loop interno do `A2AServer` — risco real de write
   perdido, não hipotético (ver Decision 1).
5. Nenhum código de disparo de webhook existe em lugar nenhum do pacote
   `A2A`/`A2A.AspNetCore` (a classe `A2AServer` inteira foi decompilada —
   nenhum `HttpClient`, nenhuma chamada de rede). Payload e lógica de
   disparo são 100% responsabilidade nossa em `apps/workers`.

## Goals / Non-Goals

**Goals:**
- Cliente A2A registrar um `pushNotificationConfig` junto do `SendMessage`
  que cria a task.
- `apps/workers` disparar uma chamada HTTP `POST` para a URL registrada
  quando a task atingir um estado terminal processado pelo worker
  (`completed`/`failed`), com a `AgentTask` completa como payload.
- `AgentCard.Capabilities.PushNotifications = true`.

**Non-Goals:**
- Implementar os métodos JSON-RPC dedicados de push notification config
  (`Create`/`Get`/`List`/`DeleteTaskPushNotificationConfig`) — o mecanismo
  embutido no `SendMessage` cobre o escopo (ver Context, item 2).
- Mitigação de SSRF/allowlist de URL (Decision 4).
- Retry ou fila de reentrega de webhook (Decision 3).
- Tratamento especial para tasks criadas por delegação — nunca têm push
  config registrado (delegação não passa por `EnqueueingAgentHandler`/
  `SendMessage` externo), então nada as dispara; sem código específico
  necessário.
- Disparo de webhook para tasks `rejected` — rejeição acontece de forma
  síncrona em `apps/api` (`EnqueueingAgentHandler`), antes de qualquer
  publicação no RabbitMQ; como o push config só é persistido por
  `apps/workers` (Decision 1), uma task rejeitada nunca chega a ter push
  config em Metadata, então não há o que disparar — não é um branch de
  código a mais, é consequência direta do desenho.
- Nenhuma UI, nenhuma mudança em `apps/frontend`.

## Decisions

### Decision 1: Push config trafega no `TaskJobMessage` (RabbitMQ); `apps/workers` persiste em `Metadata`, nunca `apps/api`

`EnqueueingAgentHandler` lê `context.Configuration?.PushNotificationConfig`
e inclui no `TaskJobMessage` publicado — um novo campo opcional, mesmo
padrão de `TaskId`/`AgentId`/`ContextId` já existentes:

```csharp
// apps/api e apps/workers — TaskJobMessage.cs (cópia independente em cada app,
// mesmo padrão já usado para Agent.cs — ver design.md do MVP, item 9)
public sealed record TaskJobMessage(
    string TaskId,
    Guid AgentId,
    string ContextId,
    PushNotificationConfig? PushNotificationConfig = null);
```

`AgentExecutionService` (workers), na transição para um estado terminal
(`completed` via `updater.CompleteAsync`, `failed` via `updater.FailAsync`),
grava o config recebido em `AgentTask.Metadata` através do mesmo parâmetro
`beforeSave: Action<AgentTask>?` de `ApplyStepAsync` já usado para
`conversationSession` — só que agora **nos dois caminhos** (sucesso e catch
de falha; hoje só o caminho de sucesso passa `beforeSave`). Nova classe
seguindo o padrão já estabelecido (`DelegationDepth.cs`,
`ConversationSessionCodec.cs`):

```csharp
// apps/workers/src/Buteco.Workers/Agents/PushNotificationConfigCodec.cs
public static class PushNotificationConfigCodec
{
    public const string MetadataKey = "pushNotificationConfig";

    public static JsonElement Encode(PushNotificationConfig config) =>
        JsonSerializer.SerializeToElement(config);
}
```

O disparo real do webhook (Decision 2) usa o `PushNotificationConfig` **em
memória**, direto de `message.PushNotificationConfig` — não relê de
`Metadata`. A escrita em `Metadata` existe só para consistência com o padrão
já estabelecido no projeto e para deixar o config visível via `GetTask` após
a task terminar (mesmo raciocínio de `conversationSession`/`delegationDepth`
— nenhum dado novo "invisível" no store), não porque o disparo dependa dela.

**Por que não gravar em `apps/api`, dentro de `EnqueueingAgentHandler`**
(ideia original cogitada antes da investigação): exigiria injetar
`ITaskStore` no handler, ler a task de volta com retry (mesmo idioma de
`GetTaskWithRetryAsync` do worker) e regravá-la — competindo, sem nenhum
lock compartilhado, com o loop de background do próprio `A2AServer`
(`ApplyEventAsync`) que está aplicando o evento `Submit`/possíveis eventos de
continuação para a mesma task ao mesmo tempo. Como `SaveTaskAsync` é um
overwrite cego do payload inteiro (Context, item 4), essa corrida pode
perder a escrita de qualquer um dos dois lados — descartada por ser um risco
real de bug de concorrência, não só uma composição "fora do caminho fácil".

**Por que não deixar de persistir em Metadata** (alternativa: só repassar
via `TaskJobMessage` e usar direto no disparo, sem gravar em lugar nenhum):
descartada porque o config ficaria invisível para qualquer inspeção via
`GetTask`, inconsistente com o padrão já estabelecido de dados
per-execução relevantes viverem em `Metadata` (mesma tabela `a2a_tasks` que
os dois apps já leem/escrevem de forma cross-compatível e testada,
`CrossAppTaskStoreCompatibility.Tests`).

**Por que não uma tabela dedicada**: mesmo raciocínio já usado para
Skills/`delegationDepth` — não há identidade própria nem necessidade de
consulta relacional por trás desse dado.

### Decision 2: `apps/workers` dispara o webhook, no mesmo ponto onde `AgentExecutionService` já escreve o estado terminal

Rejeições síncronas (`IsActive`, `Provider`/`Model` ausentes) acontecem
dentro do próprio `SendMessage`, em `apps/api` — o cliente já recebe esse
resultado na resposta HTTP, sem necessidade de notificação assíncrona. Só as
transições que só acontecem depois que `apps/workers` processa
(`completed`/`failed`) precisam do webhook.

Novo componente `PushNotificationSender` (namespace `Buteco.Workers.Notifications`,
justificando pasta nova — mesmo nível de `Mcp/`, `AgentDelegations/`):
recebe `PushNotificationConfig` + `AgentTask` (a task já projetada e salva,
retornada por `ApplyStepAsync`), monta a requisição (Decision 6) e a
dispara. Chamado de dois pontos em `AgentExecutionService.ExecuteAsync`:
logo após o `ApplyStepAsync` de `CompleteAsync` (sucesso) e logo após o de
`FailAsync` (catch) — só quando `message.PushNotificationConfig is not null`.

**Alternativa descartada**: disparar de `apps/api`, observando alguma
transição de estado. Descartada — `apps/api` não tem nenhum ponto de
observação de conclusão assíncrona da task (quem processa é o worker, em
outro processo); replicar isso em `apps/api` duplicaria a orquestração de
estado que já existe em `apps/workers`.

### Decision 3: Fire-and-forget, timeout de 5 segundos, sem retry

`HttpClient` nomeado (mesmo padrão de `McpTransportFactory`/
`McpConnectionTester`: `builder.Services.AddHttpClient(nome)`), com
`.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5))`
no registro em `Program.cs` — primeiro uso de timeout customizado em um
`HttpClient` nomeado no projeto, aditivo, sem impacto nos clientes MCP
existentes.

A chamada acontece **depois** que o estado terminal já foi persistido no
Postgres — a task já está `completed`/`failed` no store independente do
resultado do webhook. `PushNotificationSender` captura qualquer exceção
(`HttpRequestException`, timeout via `TaskCanceledException`, qualquer
outra) e loga como aviso, sem relançar; resposta não-2xx também só é
logada, nunca tratada como falha que precise de ação. Nenhum client de
polling é afetado mesmo que o webhook falhe 100% das vezes.

**Alternativa descartada**: retry com backoff. Sem necessidade concreta
ainda — mesma filosofia de degradação graciosa já usada em toda falha de
dependência externa do projeto (MCP, resumo de histórico, delegação).

### Decision 4: Sem mitigação de SSRF — Non-Goal explícito

O webhook aponta para uma URL fornecida pelo cliente que registrou o push
config, sem allowlist de domínio nem bloqueio de faixa de IP privado/
loopback nesta fatia. Mesmo espírito já usado para a ausência de
autenticação em toda a linha de MCP — registrado conscientemente, não como
lacuna esquecida. Precisa ser revisitado antes de expor esse mecanismo a
clientes não confiáveis de verdade (quando a linha de caixas de entrada
avançar e clientes externos passarem a registrar webhooks arbitrários).

### Decision 5: `AgentCard.Capabilities.PushNotifications = true`, incondicional

```csharp
Capabilities = new AgentCapabilities { Streaming = false, PushNotifications = true }
```

Declarado incondicionalmente (o agente suporta o mecanismo se solicitado,
não depende de já ter um config registrado num momento específico) — mesmo
padrão de `Streaming`, que continua `false` (não implementado).
`docs/a2a-integration.md` também é atualizado: hoje documenta explicitamente
que a `configuration` do `SendMessage` é ignorada e que
`capabilities.pushNotifications` é sempre `false` — ambos os trechos, mais o
exemplo de payload do `AgentCard`, precisam refletir o novo comportamento.

### Decision 6: Autenticação do webhook — `Authorization` + `X-A2A-Notification-Token`

Nenhum dos dois campos (`Authentication`, `Token`) é consumido pelo SDK —
é lógica nossa, não algo confirmado pela decompilação (o SDK só carrega os
dados, não define como uma chamada de webhook deveria usá-los).

- Se `PushNotificationConfig.Authentication` presente: header
  `Authorization: {Scheme} {Credentials}`.
- Se `PushNotificationConfig.Token` presente (independente, os dois podem
  coexistir): header `X-A2A-Notification-Token: {Token}` — convenção do
  protocolo A2A público para o receptor validar que a chamada veio do
  agente que ele mesmo registrou o webhook.

## Estrutura de Arquivos

```
apps/api/src/Buteco.Api/
├── A2A/
│   ├── EnqueueingAgentHandler.cs      (editado: lê Configuration?.PushNotificationConfig,
│   │                                    inclui no TaskJobMessage publicado)
│   └── AgentCardEndpoints.cs          (editado: Capabilities.PushNotifications = true)
└── Messaging/
    └── TaskJobMessage.cs              (editado: novo campo PushNotificationConfig)

apps/api/tests/Buteco.Api.Tests/
└── AgentCardEndpointTests.cs          (editado: Capabilities.PushNotifications === true)

apps/workers/src/Buteco.Workers/
├── Messaging/
│   └── TaskJobMessage.cs              (editado: espelho do campo novo, mesmo padrão
│                                        de duplicação intencional já usado no projeto)
├── Agents/
│   ├── AgentExecutionService.cs       (editado: grava Metadata nos dois caminhos
│   │                                    terminais, dispara PushNotificationSender)
│   └── PushNotificationConfigCodec.cs (novo: MetadataKey + Encode)
├── Notifications/
│   └── PushNotificationSender.cs      (novo: monta headers, dispara POST fire-and-forget)
└── Program.cs                         (editado: registra HttpClient nomeado com timeout)

apps/workers/tests/Buteco.Workers.Tests/
├── Notifications/
│   └── PushNotificationSenderTests.cs (novo: servidor HTTP fake como "webhook do cliente")
└── AgentExecutionServiceTests.cs      (editado ou novo: round-trip completo via RabbitMQ real)

docs/a2a-integration.md                (editado: configuration, capabilities, exemplo de AgentCard)
```

Nenhum conteúdo novo em `libs/` — o único ponto de reuso de código entre os
apps continua sendo o pacote público `A2A` (ambos já o referenciam), mesmo
raciocínio já estabelecido desde o MVP para `AgentTask`/`TaskState`.
`Notifications/` é uma pasta nova em `apps/workers` porque o disparo de
webhook é uma responsabilidade nova e coesa, no mesmo nível de `Mcp/`
(chamada a um serviço externo com sua própria composição de `HttpClient`),
não uma extensão natural de `Agents/` (que já concentra bastante lógica de
orquestração de execução).

## Risks / Trade-offs

- **[Risco]** SSRF — o webhook chama qualquer URL fornecida pelo cliente,
  incluindo endereços internos/loopback → **Mitigação**: nenhuma nesta
  fatia, Non-Goal consciente (Decision 4); revisitar antes de expor a
  clientes não confiáveis.
- **[Risco]** Task processada por uma versão de `apps/workers` mais antiga
  que não reconhece o novo campo `PushNotificationConfig` no
  `TaskJobMessage` → **Mitigação**: campo opcional com default `null`; o
  contrato JSON tolera ausência (`System.Text.Json` usa o default do
  parâmetro do record quando a propriedade não vem no payload) — rollout
  de `apps/api` e `apps/workers` não precisa ser coordenado no mesmo
  instante.
- **[Trade-off]** Falha total do webhook (URL sempre inválida) nunca é
  visível para o cliente que o registrou — sem retry nem qualquer sinal de
  erro além dos logs do worker → aceito conscientemente (Decision 3), mesmo
  trade-off já aceito em MCP/resumo de histórico/delegação.
- **[Trade-off]** Sem suporte aos métodos JSON-RPC dedicados de push
  notification config — um cliente A2A "de espec" que tente `GetTask
  PushNotificationConfig` para confirmar o registro recebe
  `PushNotificationNotSupported`, mesmo com `Capabilities.PushNotifications
  = true` — aceito porque o mecanismo embutido no `SendMessage` é
  suficiente para o caso de uso real (linha de caixas de entrada), e
  implementar o CRUD dedicado exigiria subclassar `A2AServer` sem
  necessidade concreta ainda.

## Migration Plan

1. Nenhuma migration de banco — `AgentTask.Metadata` já existe (`jsonb`
   livre), mesmo campo já usado para `conversationSession`/`delegationDepth`.
2. Deploy aditivo em ambos os apps, sem ordem obrigatória (ver Risco acima)
   — `apps/api` pode subir a mudança antes ou depois de `apps/workers` sem
   quebrar o contrato do `TaskJobMessage`.
3. Rollback: reverter qualquer um dos dois binários independentemente —
   `apps/api` sem o campo novo simplesmente para de propagar push config
   (comportamento atual); `apps/workers` sem o código novo ignora o campo
   extra no JSON (compatibilidade automática do `System.Text.Json` com
   propriedade desconhecida em um record) e volta a nunca disparar webhook.

## Open Questions

Nenhuma pergunta de negócio/produto em aberto. O timeout de 5 segundos
(Decision 3) é um valor inicial razoável, não uma incerteza — ajustável
depois de observar comportamento real de webhooks de clientes (ex.: linha
de caixas de entrada), sem exigir mudança de design.
