## Context

`PushNotificationEndpoints.ReceiveAsync` (`apps/inbox`,
`Orchestration/PushNotifications/Endpoints/`) é o receptor do webhook de
push notification disparado por `PushNotificationSender`
(`apps/workers`) quando uma `AgentTask` atinge estado terminal — o outro
lado do round-trip descrito por `a2a-push-notifications` e por
`inbox-message-orchestration`, Requirement "Endpoint receptor de push
notification valida o token da chamada"/"Conteúdo bufferizado é removido
ao final do ciclo de disparo".

Dois defeitos reais, achados via screenshot do inbox em produção e
confirmados por leitura de código (`AgentExecutionService.cs`,
`Program.cs` de `apps/workers`):

### Defeito 1 — campo errado para o texto da resposta

`AgentExecutionService.ExecuteAsync` (`apps/workers`), no caminho de
sucesso, grava a resposta do LLM assim:

```csharp
var parts = new List<Part> { Part.FromText(response.Text) };
var savedTask = await ApplyStepAsync(taskStore, message.TaskId, message.ContextId, task,
    async updater =>
    {
        await updater.AddArtifactAsync(parts, cancellationToken: cancellationToken);
        await updater.CompleteAsync(cancellationToken: cancellationToken); // sem mensagem final
    }, ...);
```

`TaskUpdater.CompleteAsync(message: null, ...)` nunca preenche
`AgentTask.Status.Message` — o texto vai para `AgentTask.Artifacts`.
`ExtractResponseText` (versão anterior) lia `task.Status.Message?.Parts`,
sempre `null` no caminho de sucesso real. `responseText` vinha `null`,
`DeliverResponseAsync` nunca era chamado, e a `Message` de entrada ainda
assim virava `Completed` (linha que roda incondicionalmente, com ou sem
resposta — comportamento correto para o caso "task completou sem texto",
mas aplicado aqui a um caso que na verdade tinha texto, só que no campo
errado).

A spec de `a2a-push-notifications` (Requirement "Webhook disparado ao
final do processamento pelo worker") já dizia que o corpo do webhook
inclui "a `AgentTask` completa (incluindo estado `completed` e
**artifacts**)" — o contrato já estava certo; a leitura em `apps/inbox`
que estava errada.

### Defeito 2 — cancelamento da requisição derruba persistência já após entrega ao canal

`Program.cs` de `apps/workers` registra o `HttpClient` nomeado usado por
`PushNotificationSender` com timeout fixo:

```csharp
builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
```

Decisão deliberada e já documentada (`a2a-push-notifications`, Decision
3: "Fire-and-forget, timeout de 5 segundos, sem retry") — o racional
original era proteger a conclusão da task no store de `apps/workers`
(já persistida antes da chamada de webhook) de qualquer lentidão do lado
receptor. O que a Decision 3 não considerou: esse timeout também limita,
de fato, quanto tempo o **receptor** (`apps/inbox`) tem para terminar de
processar antes que a conexão HTTP subjacente seja abortada pelo
cliente.

`ReceiveAsync` (versão anterior) usava o `CancellationToken` do parâmetro
da própria requisição — vinculado por model binding a
`HttpContext.RequestAborted` — em toda a função: na consulta inicial do
`PendingDispatch`, dentro de `DeliverResponseAsync` (que chama a API do
canal de origem, ex. Telegram), e no `SaveChangesAsync` final. Se o
round-trip completo ultrapassar os 5s do cliente, o cliente aborta a
conexão; Kestrel detecta o disconnect e sinaliza
`HttpContext.RequestAborted`; qualquer chamada subsequente que ainda use
esse token (inclusive `SaveChangesAsync`, que pode rodar **depois** do
`sender.SendAsync` para o Telegram já ter retornado com sucesso) lança
`OperationCanceledException` — nenhuma mudança rastreada é persistida,
mas o envio ao canal (efeito colateral externo, já irreversível) já
aconteceu. Resultado: resposta entregue no canal, `PendingDispatch` nunca
removido, `Message`s nunca marcadas `Completed`.

## Goals / Non-Goals

**Goals:**
- Extrair o texto de resposta do campo onde ele realmente está
  (`Artifacts`), não de `Status.Message`.
- Garantir que, uma vez que uma push notification com token válido seja
  aceita, o processamento (entrega ao canal + persistência local)
  completa independentemente de o chamador (`apps/workers`) já ter
  desistido de esperar a resposta dentro do seu próprio timeout.

**Non-Goals:**
- Mudar o timeout de 5s ou a política de retry de `PushNotificationSender`
  (`apps/workers`, Decision 3 de `a2a-push-notifications`) — permanece
  fire-and-forget por design; esta correção é só do lado receptor.
- Adicionar deduplicação de push notification reentregue (ex. se um
  proxy/infra intermediário reenviar a mesma chamada) — fora do escopo
  observado neste incidente; `ReceiveAsync` já é idempotente na leitura
  (uma push notification para um `PendingDispatch` já removido recebe
  401, não erro), o que já cobre reentrega enquanto o processamento
  original tiver completado.
- Qualquer mudança em `apps/api` ou `apps/frontend`.

## Decisions

### Ler a resposta de `task.Artifacts`, não de `task.Status.Message`

`ExtractResponseText` passa a usar
`task.Artifacts?.LastOrDefault()?.Parts` — mesmo padrão já usado em
`AgentDelegationToolSetResolver.cs:193` (apps/workers) e nos helpers de
teste `ExtractArtifactText` de `AgentDelegationConcurrencyTests`/
`AgentDelegationExecutionTests`. `LastOrDefault()` porque
`AgentExecutionService` só adiciona um artifact por execução bem-sucedida
(o texto da resposta do turno atual) — não há caso hoje de múltiplos
artifacts por task nesta fatia, mas pegar o último em vez do primeiro
mantém o mesmo raciocínio defensivo já usado no código de delegação.

**Alternativa considerada**: também preencher `Status.Message` em
`AgentExecutionService.CompleteAsync` (lado do worker), mantendo
`ExtractResponseText` como estava. Rejeitada — exigiria mudar
`apps/workers`, quando o defeito real é só a leitura em `apps/inbox`; o
padrão de artifact para o texto de resposta já é o estabelecido no
projeto (delegação já lê assim), então alinhar o receptor a esse padrão é
mais consistente que introduzir um segundo canal (`Status.Message`)
carregando a mesma informação.

### `CancellationToken.None` a partir da localização do `PendingDispatch`

Em `ReceiveAsync`, a busca inicial do `PendingDispatch` continua usando o
token da requisição (barato, sem efeito colateral, cancelar aí é
inofensivo). A partir do ponto em que a push notification é aceita como
válida (token confere), todo o restante — `DeliverResponseAsync`,
`UpdateMessageDispatchStatusesAsync`, `SaveChangesAsync` final — passa a
usar `CancellationToken.None`.

**Motivo**: o chamador (`PushNotificationSender`) nunca espera nem reage
à resposta HTTP deste endpoint — é fire-and-forget por decisão já
tomada (Decision 3). Não há razão para o processamento do lado receptor
ficar refém do timeout do chamador uma vez que a notificação já foi
validada; o trabalho deve completar de forma independente, exatamente
como qualquer outro processamento em background que não deve ser
interrompido por um cliente HTTP que desiste de esperar.

**Alternativa considerada**: aumentar o timeout de 5s em
`PushNotificationSender` para dar folga ao round-trip. Rejeitada — não
elimina a classe do problema (qualquer timeout finito no cliente,
combinado com um canal de origem lento o suficiente, reabre a mesma
janela), e mudaria uma decisão já tomada e documentada (Decision 3) sem
necessidade — o problema real é a propagação indevida do cancelamento do
lado do servidor, não a duração do timeout em si.

**Alternativa considerada**: mover a entrega ao canal e a persistência
para um `BackgroundService`/fila interna, respondendo `200 OK` ao
chamador imediatamente após validar o token. Rejeitada por
desproporcional ao problema — a mudança de `CancellationToken.None`
resolve a causa raiz (propagação de cancelamento) com uma alteração
mínima, sem introduzir mais um componente assíncrono/infra nova só para
este caso.

## Risks / Trade-offs

- **[Aceito]** Se o processamento pós-validação (entrega ao canal +
  persistência) travar de verdade (não só ficar lento) por algum motivo
  não relacionado ao cliente, `CancellationToken.None` significa que não
  há mais nenhum cancelamento automático vindo do lado do chamador para
  interromper — mitigado pelo fato de que as chamadas envolvidas (HTTP
  para o canal, `SaveChangesAsync`) já têm seus próprios timeouts
  independentes (`HttpClient` default para os senders de canal,
  timeout de conexão do Postgres) — nenhuma delas depende de
  `HttpContext.RequestAborted` para eventualmente desistir.
- **[Aceito]** Não resolve reentrega de push notification por retry de
  infraestrutura (proxy, load balancer) — fora do escopo observado; ver
  Non-Goals.

## Migration Plan

Nenhuma migração de banco, nenhuma mudança de contrato HTTP. Deploy
aditivo, só `apps/inbox`, sem ordem de dependência com `apps/workers` ou
`apps/api`.
