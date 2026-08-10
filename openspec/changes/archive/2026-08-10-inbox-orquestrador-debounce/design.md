## Context

`apps/inbox` hoje resolve `Contact`/`Session` (`IContactSessionResolver`,
change `inbox-crm-contato-sessao`) e valida `Channel.AgentId` contra
`apps/api` via HTTP (`AgentReferenceValidator`, change
`inbox-catalogo-canais`), mas nunca chamou `SendMessage` de verdade — todo
o protocolo A2A até aqui só existe do lado servidor (`apps/api` via
`A2AServer`/`MapA2A`, `apps/workers` como executor).

Investigação feita em `/opsx:explore` antes deste design (resumida aqui,
não repetida a cada Decision):

- **Instância única**: `docker-compose.yml` só sobe Postgres e RabbitMQ —
  nenhum app roda dentro dele, nenhum `replicas` declarado em lugar
  nenhum. Nada impede múltiplas instâncias de `apps/inbox` no futuro. O
  precedente de `apps-workers-delegacao-execucao` (que enfrentou o mesmo
  argumento para `apps/workers`) não assumiu instância única de graça —
  construiu `ConversationContextLock` (`pg_advisory_lock`) para
  funcionar correto em múltiplas réplicas. Esse padrão específico não se
  transporta aqui: `pg_advisory_lock` serializa uma seção crítica entre
  instâncias, mas não resolve visibilidade de um buffer que só existe na
  memória de uma instância. Decisão 1 abaixo resolve isso de outro jeito.
- **Timer/scheduling**: único `BackgroundService` do projeto é
  `TaskJobConsumer` (`apps/workers`), puramente orientado a evento
  (RabbitMQ, `prefetchCount: 1`). Nenhum `PeriodicTimer`/`Timer` em
  lugar nenhum — campo livre, sem convenção a seguir ou quebrar.
- **Cliente A2A**: não existe nenhum hoje. O pacote `A2A`
  (`PackageVersion` já fixado em `Directory.Packages.props`, já
  referenciado por `apps/api` e `apps/workers` do lado servidor) embute
  um cliente completo: `A2A.A2AClient : IA2AClient`, construtor
  `A2AClient(Uri baseUrl, HttpClient? httpClient = null)`, método
  `SendMessageAsync(SendMessageRequest, CancellationToken)`.
  `SendMessageRequest.Configuration.PushNotificationConfig` é o mesmo
  tipo `A2A.PushNotificationConfig` que `apps/workers/Notifications/
  PushNotificationSender.cs` já usa do lado servidor.

## Goals / Non-Goals

**Goals:**
- Ingestão de mensagem normalizada `(ChannelId, ExternalId, texto,
  receivedAt)` via serviço interno, sem endpoint HTTP.
- Debounce por sessão: mensagens que chegam dentro da janela configurada
  se agrupam num único `SendMessage`; mensagens fora da janela disparam
  chamadas separadas.
- Buffer de debounce sobrevive a restart de `apps/inbox` e é visível a
  qualquer instância — sem depender de `apps/inbox` rodar como instância
  única.
- Round-trip real e completo contra `apps/api`: `SendMessage` via
  `A2A.A2AClient`, com `pushNotificationConfig` apontando para um
  endpoint novo do próprio `apps/inbox`, validado por token por chamada.
- Falha de transporte do `SendMessage` (`apps/api` inalcançável, timeout)
  não derruba o orquestrador nem descarta a mensagem do usuário na
  primeira falha — é reintentada por um número limitado de tentativas
  antes de ser considerada perdida, sempre observável via log
  estruturado (ver Decisão 9).

**Non-Goals:**
- Nenhum adapter real de canal (WhatsApp/Telegram) — changes futuras
  consomem `IInboundMessageOrchestrator`.
- Nenhuma abstração de entrega/outbox para o canal externo — só provar o
  round-trip. Alternativa descartada: construir essa abstração agora,
  especulando sobre um formato que nenhum adapter real ainda confirma.
- Nenhum conteúdo de mensagem retido além do necessário para o próprio
  debounce disparar (ver Decisão 6) — nada de histórico/auditoria de
  mensagens em `apps/inbox`, mesmo Non-Goal já registrado em
  `inbox-crm-contato-sessao`.
- Nenhuma configuração de janela de debounce por canal — constante
  global via `IOptions`.
- Nenhuma política de retry com backoff/jitter/dead-letter — a
  reintentativa desta fatia (Decisão 9) reaproveita a própria varredura
  do debounce com um teto simples de tentativas; retry como
  infraestrutura própria e configurável é superfície fora desta fatia.
- Nenhuma reconciliação ativa (ex. `GetTask` de polling) para tasks cujo
  push notification nunca chega — ver Riscos.
- Nenhuma UI, nenhuma mudança em `apps/frontend`.

## Árvore de pastas proposta

```
apps/inbox/src/Buteco.Inbox/
  Orchestration/
    Entities/
      PendingDispatch.cs
      PendingDispatchStatus.cs
      BufferedMessage.cs
    IInboundMessageOrchestrator.cs
    InboundMessageOrchestrator.cs
    DebounceOptions.cs
    DebounceSweepService.cs
    A2AClientFactory.cs
    IA2AClientFactory.cs
    PushNotifications/
      Endpoints/
        PushNotificationEndpoints.cs
  Infrastructure/
    AppDbContext.cs                              (+ DbSet<PendingDispatch>)
    Migrations/
      <timestamp>_AddPendingDispatch.cs
  Program.cs                                      (registro dos novos serviços)
apps/inbox/tests/Buteco.Inbox.Tests/
  Orchestration/
    InboundMessageOrchestratorTests.cs
    DebounceSweepServiceTests.cs
    RoundTripTests.cs
    PushNotificationEndpointsTests.cs
  Support/
    FakeA2AServerHttpMessageHandler.cs             (ou uso direto de WebApplicationFactory de apps/api, ver Decisão 10)
```

## Decisions

### 1. Buffer de debounce persistido em PostgreSQL, não em memória — e instância única deixa de ser premissa

Nova tabela `pending_dispatches` (uma linha por sessão com debounce em
aberto) guarda as mensagens bufferizadas, o timestamp da última
mensagem recebida, e (a partir do disparo) o token esperado da chamada
`SendMessage` pendente. Nenhum estado de debounce vive em memória.

**Por quê**: perder uma mensagem de usuário de verdade — a consequência
de "em memória" se `apps/inbox` reiniciar no meio da janela — é mais
grave que os casos "em memória, aceito" já existentes no projeto (ex.
conexão MCP efêmera, perdida e reaberta sem consequência para o
usuário). E nada impede `apps/inbox` escalar para múltiplas instâncias
no futuro (mesmo argumento que `apps-workers-delegacao-execucao` já
enfrentou).

**Corolário**: como qualquer instância pode ler e disparar uma linha da
tabela, `apps/inbox` **não precisa de instância única como requisito
operacional** — ao contrário do que a exploração inicial cogitava como
possível fallback. Isso simplifica a operação (nenhuma trava de
deployment a documentar) ao custo de uma tabela nova e um pouco mais de
código de persistência.

**Consistência com a Decisão 9**: a gravidade que justifica esta
Decisão — perder uma mensagem de usuário de verdade é inaceitável o
bastante para pagar o custo de uma tabela nova — se aplica igualmente a
uma falha na tentativa ativa de disparo (`apps/api` inalcançável,
timeout), não só à espera passiva antes do disparo. A primeira versão
deste design tratava as duas situações de forma inconsistente (buffer
persistido, mas descartado na primeira falha de rede); a Decisão 9 foi
revista para não reabrir essa mesma classe de perda um passo adiante.

**Alternativa considerada e descartada**: buffer em memória +
`apps/inbox` documentado como instância única. Mais simples de
implementar agora, mas transforma uma escolha de infraestrutura futura
(escalar para 2 réplicas) em perda silenciosa de mensagens de usuário —
sem nenhum erro, sem nenhum log, só timers que nunca disparam na
instância errada. Peso da consequência não compensa a simplicidade.

### 2. Mecanismo de debounce: `BackgroundService` varrendo a tabela, não `Timer` por conversa

`DebounceSweepService : BackgroundService` roda um `PeriodicTimer` com
intervalo curto (`DebounceOptions.SweepInterval`, default 2s) e, a cada
tick, busca linhas `PendingDispatchStatus.Pending` cujo `LastMessageAt`
já passou da janela configurada (`DebounceOptions.Window`, default
10s), tentando disparar cada uma (ver Decisão 5 para o claim
idempotente).

**Por quê**: decorre diretamente da Decisão 1. Um `Timer`/
`System.Threading.Timer` por conversa é estado em memória do processo
que o criou — não sobrevive a restart, não é visível a outra instância.
Incompatível com um buffer persistido e multi-instância por
construção, não por escolha de estilo.

**Trade-off aceito**: a janela de debounce efetiva tem uma imprecisão
de até `SweepInterval` (mensagem que completa a janela pode esperar até
mais 2s antes do próximo tick perceber). Aceitável para o caso de uso
(debounce de digitação humana, não latência crítica).

### 3. Cliente A2A: `A2A.A2AClient` do pacote já referenciado, não JSON-RPC manual nem `Microsoft.Agents.AI`

`A2AClientFactory` (interno a este design, não confundir com
`A2A.A2AClientFactory` do pacote) resolve, por `AgentId`, um
`A2A.A2AClient` construído com `new Uri($"{apiBaseUrl}/agents/{agentId}/a2a")`
e um `HttpClient` nomeado via `IHttpClientFactory` — mesmo padrão de
`AgentReferenceValidator` (`ApiOptions.BaseUrl`, timeout curto e fixo).

**Por quê**: `A2A.A2AClient` já existe no pacote `A2A`, já referenciado
por `apps/api`/`apps/workers`; não é um `AIAgent` completo
(`Microsoft.Agents.AI`, usado só por `apps/workers` para orquestrar
LLM), é só um cliente HTTP+JSON-RPC tipado. `SendMessageRequest.
Configuration.PushNotificationConfig` reaproveita o mesmo tipo `A2A.
PushNotificationConfig` que `PushNotificationConfigCodec`/
`PushNotificationSender` (`apps/workers`) já usam do lado servidor —
sem duplicar modelo.

**Não usar `A2A.A2AClientFactory`** (o do pacote): resolve o binding de
protocolo a partir de um `AgentCard` (chamada `GET` extra para
descobrir se o agente fala JSON-RPC ou HTTP+JSON REST). Desnecessário
aqui — já sabemos que `apps/api` só expõe JSON-RPC
(`app.MapA2A(handler, "/agents/{id}/a2a")`, confirmado nos testes de
`apps/api` com payload `jsonrpc: "2.0"`), não há negociação de
protocolo a fazer.

### 4. Superfície de ingestão: serviço interno, sem endpoint HTTP

`IInboundMessageOrchestrator.ReceiveMessageAsync(Guid channelId, string
externalId, string text, DateTimeOffset receivedAt, CancellationToken)`
— resolve `Contact`/`Session` via `IContactSessionResolver` já
existente, depois bufferiza (cria ou atualiza a `PendingDispatch`
`Pending` da sessão). Mesmo padrão de `IContactSessionResolver`:
chamável direto por teste agora, por adapters reais (WhatsApp/Telegram)
depois — nenhum endpoint HTTP nesta fatia.

### 5. Claim idempotente do disparo: concorrência otimista via `xmin`, não lock explícito

Ao processar uma `PendingDispatch` candidata, o `DebounceSweepService`
tenta transicioná-la de `Pending` para `Dispatching` via
`SaveChangesAsync` usando `xmin` do Postgres como token de concorrência
do EF Core: uma propriedade shadow `uint` (`Version`) marcada
`.IsRowVersion()`, que a convenção `NpgsqlValueGenerationConvention` do
provider Npgsql detecta (`uint` + `ValueGenerated.OnAddOrUpdate` +
`IsConcurrencyToken` + tipo de armazenamento `xid`) e renomeia
automaticamente para a coluna de sistema real `xmin` — sem coluna nova
de verdade, só a propriedade shadow no modelo do EF Core. (Verificado
por decompilação do pacote `Npgsql.EntityFrameworkCore.PostgreSQL`
10.0.3 realmente referenciado neste projeto: o método
`UseXminAsConcurrencyToken()` mencionado em versões antigas de
documentação/exemplos **não existe** nesta versão do provider — a
convenção baseada em `IsRowVersion()` é o mecanismo atual.) Se
`DbUpdateConcurrencyException` for lançada, outra instância já
reivindicou a linha — pula para a próxima candidata sem erro.

**Por quê**: prova exigida explicitamente nesta fatia — múltiplas
instâncias do `BackgroundService` não podem disparar a mesma mensagem
duas vezes. `xmin` é o mecanismo idiomático do Npgsql/EF Core para essa
classe de problema (nenhuma escrita adicional, nenhum lock mantido
aberto), mais simples que `SELECT ... FOR UPDATE SKIP LOCKED` via SQL
cru e mais alinhado ao estilo já usado no projeto (confiar em
mecanismos do EF Core/Postgres, reservar SQL cru para os poucos casos
que já exigem — `pg_advisory_lock`, violação de unicidade). O mesmo
claim se aplica igualmente às reintentativas da Decisão 9 — uma
`PendingDispatch` que volta para `Pending` após falha de transporte
disputa o próximo claim exatamente como uma candidata nova.

### 6. Token de push notification: persistido na própria `PendingDispatch`, expurgada ao terminar

Ao transicionar para `Dispatching`, um token novo (`Guid`) é gerado e
gravado em `PendingDispatch.ExpectedToken` antes da chamada
`SendMessageAsync`, incluído em `Configuration.PushNotificationConfig.
Token`. O endpoint receptor (`POST /internal/push-notifications`)
localiza a `PendingDispatch` pelo `Id` do `AgentTask` recebido
(`PendingDispatch.TaskId`, gravado a partir da resposta síncrona do
`SendMessage`) e compara o header `X-A2A-Notification-Token` — nome
exato já estabelecido por `PushNotificationSender`, confirmado em
`PushNotificationEndToEndTests.cs` — contra `ExpectedToken`. Sem
correspondência (`TaskId` não encontrado, ou token divergente): HTTP
401, nenhum processamento adicional.

**Persistido, não em memória**: decorre da Decisão 1 — o processo que
recebe a resposta pode não ser o mesmo que fez a chamada.

**Expurgo**: ao alcançar qualquer estado terminal (push notification
validada, ou falha síncrona/rejeição, ver Decisão 8), a linha é
**deletada** — não mantida para auditoria. Alternativa descartada:
reter linhas terminais por um período. Rejeitada pelo mesmo raciocínio
já usado para o Non-Goal de conteúdo de mensagem: `apps/inbox` não é
onde histórico de conversa mora (isso é `apps/api`), reter aqui seria
duplicar dado sem consumidor definido nesta fatia.

### 7. Mensagens bufferizadas: coluna `jsonb` (owned collection `ToJson()`), não tabela filha

`PendingDispatch.Messages` é uma `List<BufferedMessage>` (texto +
`ReceivedAt`) mapeada como owned collection JSON (EF Core `ToJson()`,
suportado nativamente pelo Npgsql provider) em vez de uma tabela filha
`buffered_messages`. Reescrita do documento inteiro a cada mensagem
nova é aceitável no volume esperado (poucas mensagens por rajada de
debounce).

### 8. Resposta síncrona do `SendMessage`: `Rejected` é terminal na hora, não espera push notification

`SendMessageResponse.Task.Status` pode já vir `Rejected` na resposta
síncrona (agente inativo, sem provider/model configurado — ver
`EnqueueingAgentHandler`, `apps/api`) — esse caminho nunca passa por
`apps/workers`, e a spec `a2a-push-notifications` já garante que uma
task rejeitada **nunca dispara o webhook**. Se o orquestrador
simplesmente persistisse o `ExpectedToken` e esperasse, a
`PendingDispatch` ficaria `Dispatching` para sempre, sem qualquer sinal
de erro.

**Decisão**: o orquestrador inspeciona `SendMessageResponse.Task.Status`
imediatamente após a chamada. Se já for terminal (`Rejected`), loga e
apaga a `PendingDispatch` na hora — nunca registra
`pushNotificationConfig` para uma chamada que a própria resposta já
mostrou que não vai completar. Só quando o status retornado é
`Submitted` a `PendingDispatch` permanece `Dispatching`, aguardando o
endpoint receptor.

### 9. Falha de transporte do `SendMessage` (timeout, `apps/api` inalcançável): reintentada pela própria varredura, com teto de tentativas — não descartada na primeira falha

**A tensão, direta**: a Decisão 1 justifica uma tabela nova, migration,
e concorrência via `xmin` especificamente porque perder uma mensagem de
usuário de verdade é mais grave que os casos "em memória, aceito" já
existentes no projeto. Uma falha de transporte na tentativa ativa de
disparo (`apps/api` inalcançável, timeout) tem exatamente essa mesma
forma — mensagem real do usuário, sem resposta, causa de infraestrutura
plausivelmente transitória — só que um passo depois da espera passiva
que a Decisão 1 já protege. Descartar a `PendingDispatch` na primeira
falha de transporte (versão anterior deste design) reabriria, um passo
adiante, exatamente a perda que a Decisão 1 existe para evitar — sem
nenhuma instabilidade de rede ter durado mais que o timeout de uma
única chamada HTTP.

**Decisão**: `HttpRequestException`/`TaskCanceledException` (timeout)
são capturadas e logadas com `LogWarning` estruturado (`SessionId`,
`AgentId`, `AttemptCount`), mas a `PendingDispatch` **não é removida**.
Em vez disso: `AttemptCount` é incrementado, `Status` volta para
`Pending`, e `LastMessageAt` é atualizado para o momento da falha — o
que faz a própria condição que o `DebounceSweepService` já avalia a
cada tick (`Pending` com `LastMessageAt` mais antigo que `Window`,
Decisão 2) reapresentar essa `PendingDispatch` como candidata ao
disparo no ciclo seguinte, sem nenhum mecanismo de retry/backoff novo.
Só quando `AttemptCount` atinge `DebounceOptions.MaxDispatchAttempts`
(novo, default 3) a `PendingDispatch` é marcada `Status = Failed`,
removida, e a perda definitiva é logada com `LogError` (não
`LogWarning`) — nível de severidade diferente de propósito, porque
nesse ponto deixou de ser "uma tentativa falhou" e passou a ser "uma
mensagem de usuário foi perdida de verdade".

**Reaproveitar a janela de debounce como intervalo de retry** é uma
simplificação deliberada: evita introduzir um campo/config de backoff
separado só para este caso. Se, na prática, o valor certo de `Window`
para agrupar mensagens divergir do intervalo certo para reintentar uma
falha de rede, isso vira uma Decision própria numa change futura — não
antecipado aqui.

**Alternativa considerada e descartada**: manter o comportamento
anterior (descarta na primeira falha), só documentando a tensão com a
Decisão 1 como risco aceito conscientemente. Rejeitada — o argumento de
gravidade da Decisão 1 se aplica aqui sem nenhuma diferença real
(mesma mensagem real, mesma ausência de resposta, mesma causa de
infraestrutura); pagar o custo de uma tabela persistida com concorrência
otimista num lado do fluxo e aceitar a mesma perda sem tentar de novo
no outro lado, um passo adiante, seria inconsistente — não uma escolha
de trade-off genuína.

**Alternativa considerada e descartada**: política de retry completa
com backoff exponencial/jitter/dead-letter. Rejeitada — superfície de
design própria (curva de backoff, teto de tempo total, fila de
mensagens mortas), desproporcional para esta fatia. Reaproveitar a
cadência de varredura que já existe, com um teto simples de tentativas,
já converte "uma instabilidade de rede pontual" em não-evento, sem
construir infraestrutura de retry de propósito geral.

### 10. Teste de round-trip: `apps/api`/`apps/workers` de teste real, não mock do protocolo A2A

O teste ponta a ponta usa `WebApplicationFactory<Program>` de
`apps/api` real (mesmo padrão de `A2ATaskLifecycleFixture`) mais uma
instância real de `apps/workers` contra RabbitMQ/Postgres via
Testcontainers (mesmo padrão de `WorkerInfrastructureFixture`) — não um
fake do protocolo A2A. Justificativa: o valor desta fatia é provar o
round-trip de verdade; mockar o lado servidor esconderia exatamente a
integração que a change existe para validar.

## Riscos / Trade-offs

- **[Risco] Push notification nunca chega** (worker cai no meio do
  processamento, ou o próprio `PushNotificationSender` falha sem retry
  — comportamento já existente e aceito em `a2a-push-notifications`) →
  a `PendingDispatch` correspondente fica presa em `Dispatching` para
  sempre, sem sinal de erro automático. **Mitigação nesta fatia**:
  nenhuma — fica registrado como Non-Goal explícito (nenhuma
  reconciliação/polling de `GetTask`). Vira dívida visível para uma
  change futura (job de expiração/alerta de `PendingDispatch` presa há
  muito tempo).
- **[Risco] `apps/api` indisponível por período prolongado** (mais que
  `MaxDispatchAttempts × Window`, ver Decisão 9) → mesmo com retry
  reaproveitando a varredura, a mensagem do usuário ainda é perdida
  depois de esgotadas as tentativas. Diferença deliberada em relação à
  versão anterior deste design: a perda deixa de ser silenciosa na
  primeira falha e passa a ser explícita, logada em nível `Error`, só
  depois de tentativas reais — reduz a janela de perda de "um timeout
  isolado" para "instabilidade que persiste por múltiplos ciclos", não
  a elimina.
- **[Trade-off] Reescrita de documento `jsonb` inteiro por mensagem
  nova** (Decisão 7) → aceitável no volume esperado; se debounce
  agrupar dezenas de mensagens por rajada, reconsiderar.
- **[Trade-off] Imprecisão de até `SweepInterval` na janela efetiva**
  (Decisão 2) → aceitável para debounce de digitação humana.
- **[Risco] Corrida entre duas mensagens quase simultâneas do mesmo
  `SessionId` criando duas `PendingDispatch` `Pending`** → mitigado com
  o mesmo padrão de `ContactSessionResolver.FindOrCreateContactAsync`:
  índice único parcial `(SessionId) WHERE status = 'Pending'` no banco,
  captura de `DbUpdateException`/violação de unicidade e novo `SELECT`
  para reaproveitar a linha já criada pela requisição concorrente.

## Open Questions

- Valores default de `DebounceOptions.Window` (10s), `SweepInterval`
  (2s) e `MaxDispatchAttempts` (3) são um ponto de partida razoável para
  debounce de digitação humana e tolerância a instabilidade transitória
  de rede, mas não foram validados com nenhum canal real ainda (os
  adapters ainda não existem) — podem precisar de ajuste quando o
  primeiro adapter (WhatsApp/Telegram) for implementado.
- Se/quando `apps/inbox` precisar de um mecanismo geral de reconciliação
  para tasks travadas (não só as desta fatia), decidir se isso é um
  `BackgroundService` adicional reaproveitando `DebounceSweepService` ou
  um componente separado — não decidido aqui de propósito (ver Risco
  acima).
