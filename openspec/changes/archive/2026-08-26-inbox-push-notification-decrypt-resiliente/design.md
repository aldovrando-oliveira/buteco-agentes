## Context

`PushNotificationEndpoints.DeliverResponseAsync` (`apps/inbox/src/Buteco.Inbox/Orchestration/PushNotifications/Endpoints/PushNotificationEndpoints.cs:121-176`) roda dentro de `ReceiveAsync`, chamada só quando a task concluída carrega uma mensagem de resposta (`ExtractResponseText(task) is not null`). Hoje o método tem esta forma:

```csharp
var dispatchInfo = await (...).FirstAsync(cancellationToken);          // fora do try

var outboundMessage = new OutboundMessage(
    dispatchInfo.Id,
    credentialCipher.Decrypt(dispatchInfo.EncryptedCredentials),        // fora do try
    dispatchInfo.ExternalId,
    responseText);

var occurredAt = DateTimeOffset.UtcNow;

try
{
    var sender = adapterRegistry.GetOutboundMessageSender(dispatchInfo.ChannelType);
    await sender.SendAsync(outboundMessage, cancellationToken);
    dbContext.Messages.Add(MessageEntity.CreateOutbound(
        pendingDispatch.SessionId, responseText, occurredAt, MessageDeliveryStatus.Sent, deliveryFailureReason: null));
}
catch (Exception exception)
{
    logger.LogError(exception, "Falha ao entregar a resposta do agente ao canal {ChannelId} (tipo {ChannelType})", dispatchInfo.Id, dispatchInfo.ChannelType);
    dbContext.Messages.Add(MessageEntity.CreateOutbound(
        pendingDispatch.SessionId, responseText, occurredAt, MessageDeliveryStatus.Failed, exception.Message));
}
```

Os dois `CreateOutbound` — sucesso e falha — usam só `pendingDispatch.SessionId`, `responseText`, `occurredAt` e o resultado da própria tentativa (`Sent`/`Failed` + motivo). Confirmado contra a assinatura real (`Message.CreateOutbound(Guid sessionId, string content, DateTimeOffset occurredAt, MessageDeliveryStatus deliveryStatus, string? deliveryFailureReason)`, `apps/inbox/src/Buteco.Inbox/Messages/Entities/Message.cs:64-80`): nenhum parâmetro vem de `dispatchInfo` (`ChannelId`, `ChannelType`, `ExternalId` não são usados por `CreateOutbound`). Isso sustenta a Decisão 1 como está — mover a consulta e o `Decrypt` para dentro do `try` não precisa produzir nenhum valor adicional para o `catch` funcionar.

Depois que `DeliverResponseAsync` retorna — com ou sem exceção capturada internamente — `ReceiveAsync` sempre executa, incondicionalmente e **sem `try/catch` próprio**, para toda push notification aceita (com resposta ou sem):

```csharp
await UpdateMessageDispatchStatusesAsync(dbContext, pendingDispatch.Id, MessageDispatchStatus.Completed, CancellationToken.None);  // linha 94
dbContext.Remove(pendingDispatch);                                                                                                  // linha 96
await dbContext.SaveChangesAsync(CancellationToken.None);                                                                           // linha 97
```

Ou seja: **o destino do `PendingDispatch` e do `MessageDispatchStatus` já é uniforme e já está correto** para qualquer falha capturada dentro de `DeliverResponseAsync` — é exatamente o comportamento coberto hoje por `ReceiveAsync_SenderThrows_PersistsOutboundMessageAsFailedWithReasonAndDoesNotFailRequest` (`apps/inbox/tests/Buteco.Inbox.Tests/PushNotificationEndpointsTests.cs:117`). O problema não é decidir um novo destino — é que a consulta de `dispatchInfo` e o `Decrypt` acontecem **antes** de qualquer `try`, então uma falha ali nunca chega a esse caminho: escapa de `DeliverResponseAsync`, escapa de `ReceiveAsync`, vira 500 não tratado, e nada do bloco acima roda. É o mesmo defeito estrutural de `DebounceSweepService` antes de `inbox-sweep-service-resiliencia` — o `try/catch` certo existe, mas a chamada que mais realisticamente falha está fora dele.

**O limite real dessa correção**, levando a mesma leitura até o fim: o bloco final de `ReceiveAsync` acima (linhas 94-97) não tem `try/catch` próprio, e roda de qualquer jeito depois que `DeliverResponseAsync` volta a executar sem lançar — inclusive quando o motivo do `catch` interno foi uma indisponibilidade real do Postgres, não um dado inválido. Nesse cenário específico, `SaveChangesAsync` (linha 97) tem o mesmo motivo para falhar, segundos depois, contra o mesmo banco — e essa segunda falha não é capturada em lugar nenhum: escapa de `ReceiveAsync`, vira 500 não tratado, e o `PendingDispatch` continua preso, pelo mesmo sintoma que esta change existe para corrigir, só que por um caminho que a Decisão 1 não toca (está fora de `DeliverResponseAsync`). **Isso não é um gap introduzido por esta change** — o mesmo bloco final já roda sem proteção hoje, incondicionalmente, para o caminho já existente e já testado de falha do `sender.SendAsync`, e mesmo para push notifications sem nenhuma mensagem de resposta (`responseText is null`, `DeliverResponseAsync` nunca é chamado). É um gap pré-existente e mais amplo que o defeito determinístico que motivou esta change — ver Non-Goals, Risks e Open Questions.

Causa raiz e evidência de regressão já fechadas pela exploração `roundtrip-tres-apps-nao-completa` (não reinvestigar): `9fd8a87` corrigiu `ExtractResponseText` para efetivamente encontrar a resposta do agente (antes sempre devolvia `null`), o que fez `DeliverResponseAsync` executar pela primeira vez e expôs o `Decrypt` desprotegido logo atrás. Último commit bom: `c72c64f` (passa em 1s). Primeiro commit ruim: `9fd8a87`.

## Goals / Non-Goals

**Goals:**
- Toda chamada capaz de falhar **dentro de `DeliverResponseAsync`** fica coberta pelo mesmo `try/catch` que já existe ali, sem alterar o destino do `PendingDispatch`/`MessageDispatchStatus` (já correto, ver Context). Não inclui o bloco final e incondicional de `ReceiveAsync` (linhas 94-97, fora de `DeliverResponseAsync`) — ver Non-Goals e Risks.
- Falha ao resolver `dispatchInfo` ou ao decifrar a credencial persiste `Message` de saída como `Failed` com o motivo, do mesmo jeito que falha do sender já faz hoje.
- Nenhuma referência a `dispatchInfo` sobrevive no `catch` além do que está garantido a existir em qualquer ponto do bloco protegido.
- Teste de acordo (convenção 5, par "com item"/"sem item") que força a falha de `Decrypt` diretamente, sem depender de `tests/InboxOrchestratorRoundTrip.Tests` (que continua parado nesta base até a change de fixture subsequente).

**Non-Goals:**
- Proteger o bloco final e incondicional de `ReceiveAsync` (`UpdateMessageDispatchStatusesAsync` + `dbContext.Remove(pendingDispatch)` + `dbContext.SaveChangesAsync`, `PushNotificationEndpoints.cs:94-97`) contra indisponibilidade de infraestrutura do Postgres. Gap real, mas pré-existente a esta change e mais amplo que `DeliverResponseAsync` (roda para toda push notification aceita, com resposta ou sem) — não introduzido nem alargado por esta change. Registrado como item em aberto com gatilho — ver Risks e Open Questions.
- Corrigir o fixture de `tests/InboxOrchestratorRoundTrip.Tests` (`RoundTripTests.CreateChannelAsync` usando o literal `"irrelevante-nesta-fatia"` em vez de ciphertext real via `IChannelCredentialCipher.Encrypt`). Fica para `roundtrip-fixture-credencial-real`, **depois** desta change — se o fixture fosse corrigido primeiro, o round-trip voltaria a passar mesmo sem a correção de produção, escondendo de novo o defeito real que esta change corrige.
- Mudar o desenho do `PushNotificationSender` (`apps/workers`): fire-and-forget, sem retry, timeout fixo de 5s — decisão deliberada de `a2a-push-notifications`, Decision 3, fora de escopo aqui.
- Rotação de chave AES-GCM ou qualquer mudança em `IChannelCredentialCipher`/`AesGcmChannelCredentialCipher`.
- Relaxar o timeout de `tests/InboxOrchestratorRoundTrip.Tests` — não é o problema (a exploração já provou isso).
- Estender esta correção a outro handler além de `DeliverResponseAsync` — nenhum outro ponto do fluxo A2A/push notification tem o mesmo padrão de "chamada fora do bloco protegido" identificado aqui (ver varredura na Decisão 3).

## Decisions

### 1. Ampliar o bloco `try` para cobrir a consulta de `dispatchInfo` e o `Decrypt`, não decidir um novo destino para `PendingDispatch`

Como o Context mostra, `ReceiveAsync` já trata "`DeliverResponseAsync` retornou, com ou sem falha capturada" de forma uniforme — sempre marca `Completed` e remove o `PendingDispatch`. Isso já é o comportamento certo para uma falha de entrega (convenção 4: degradação graciosa, falha vira estado persistido em vez de bloquear o ciclo). A correção é estrutural, não uma nova política: mover o início do `try` para antes da consulta de `dispatchInfo`, cobrindo em sequência a consulta, a construção de `outboundMessage` (que inclui o `Decrypt`), a resolução do sender e o envio.

`occurredAt = DateTimeOffset.UtcNow` permanece fora do `try` — não pode falhar, e é usado tanto no caminho de sucesso quanto no de falha do `catch`, então precisa existir antes de qualquer possível exceção.

**Alternativa rejeitada**: um `try/catch` próprio só ao redor de `Decrypt`, deixando a consulta de `dispatchInfo` como está. Rejeitada porque a varredura completa do método (Decisão 3 abaixo) mostra que a consulta é igualmente uma chamada capaz de falhar sem proteção — corrigir só o `Decrypt` deixaria o mesmo defeito estrutural na consulta, motivo pelo qual a pergunta "há outras chamadas desprotegidas?" faz parte do escopo desta change.

### 2. Log do `catch` referencia `pendingDispatch.SessionId`, não mais `dispatchInfo.Id`/`ChannelType`

Com a consulta de `dispatchInfo` agora dentro do `try`, o `catch` pode ser alcançado **antes** de `dispatchInfo` existir (se a própria consulta falhar) — `dispatchInfo.Id`/`dispatchInfo.ChannelType`, usados na mensagem de log atual, deixam de estar garantidos. A mensagem de log passa a identificar a falha por `pendingDispatch.SessionId` (parâmetro do método, disponível em qualquer ponto), sem tentar diferenciar "falhou na consulta" de "falhou no decrypt" de "falhou no sender" pelo texto da mensagem — a exceção original, sempre incluída via `ILogger.LogError(exception, ...)`, já carrega o tipo e a mensagem reais para quem for diagnosticar (mesmo raciocínio da Decisão 3 de `inbox-sweep-service-resiliencia`: log de erro sempre, com a exceção original, é suficiente — não precisa de um tipo fechado de exceção nem de uma mensagem de log ramificada por etapa).

### 3. `catch (Exception)` único e amplo, sem lista fechada de tipos — mantém o padrão já existente no método

`Decrypt` lança `CryptographicException` para os dois casos verificados em `AesGcmChannelCredentialCipher.Decrypt` (base64 inválido, tamanho insuficiente) e potencialmente outras exceções da própria `AesGcm` (tag inválida, dado corrompido). A consulta de `dispatchInfo` pode lançar por falha transitória de Postgres (mesma classe de `57P01` já documentada em `inbox-sweep-service-resiliencia`) ou, em tese, `InvalidOperationException` do `FirstAsync` se a junção não encontrar nenhuma linha.

**Decisão: manter o `catch (Exception exception)` já existente no método, apenas envolvendo mais código — não introduzir uma lista fechada de tipos "esperados" (`CryptographicException`, `NpgsqlException`, etc.).** Mesmo argumento da Decisão 3 de `inbox-sweep-service-resiliencia`, aplicado de novo: uma lista fechada é frágil e incompleta por natureza, e o objetivo aqui é o mesmo — qualquer falha nesse trecho deve degradar como falha de entrega, não derrubar o handler, independentemente do tipo exato. Isso não esconde bug de programação em silêncio: todo erro capturado continua logado em nível erro com a exceção original (Decisão 2), então um bug determinístico nesse trecho (ex. `NullReferenceException` num caminho novo) se manifestaria como log de erro repetido a cada push notification que passasse por ali — sinal forte, não silêncio.

### 4. Varredura completa de `DeliverResponseAsync` — só duas chamadas desprotegidas, ambas cobertas pela Decisão 1

Classificação de cada chamada do método:

| Chamada | Hoje | Depois desta change |
|---|---|---|
| `dbContext.Sessions/Contacts/Channels` query (`FirstAsync`) | fora do `try` — **desprotegida** | dentro do `try` |
| `credentialCipher.Decrypt(...)` | fora do `try` — **desprotegida** | dentro do `try` |
| `new OutboundMessage(...)` (construtor) | fora do `try` | dentro do `try` (não lança na prática — record/DTO simples — mas passa a estar coberto por estar entre as duas chamadas acima) |
| `DateTimeOffset.UtcNow` | fora do `try` | permanece fora — não pode falhar (Decisão 1) |
| `adapterRegistry.GetOutboundMessageSender(...)` | dentro do `try` | dentro do `try` (sem mudança) |
| `sender.SendAsync(...)` | dentro do `try` | dentro do `try` (sem mudança) |
| `MessageEntity.CreateOutbound(...)` (sucesso e falha) | dentro do `try`/`catch` | dentro do `try`/`catch` (sem mudança) |

Nenhuma outra chamada capaz de falhar fica fora do bloco protegido depois desta change.

## Risks / Trade-offs

- **[Risco] `Decrypt` ou a consulta de `dispatchInfo` falham por um motivo lógico/determinístico** (credencial corrompida, chave rotacionada, base64 inválido, nenhuma linha correspondente) **e a exceção continua escapando** → mitigação: Decisão 1, ambas movidas para dentro do `try` já existente. `DeliverResponseAsync` não propaga mais essa exceção, e o bloco final de `ReceiveAsync` (linhas 94-97) completa normalmente, porque o `SaveChangesAsync` seguinte não tem motivo próprio para falhar — a causa era o dado, não o Postgres. Contraparte de teste: novo `Fact` em `PushNotificationEndpointsTests.cs`, par direto de `ReceiveAsync_SenderThrows_PersistsOutboundMessageAsFailedWithReasonAndDoesNotFailRequest`, semeando o canal com um valor de `EncryptedCredentials` inválido (não passado por `cipher.Encrypt`, mesmo padrão usado por `RoundTripTests.CreateChannelAsync`) e asserindo que (a) a resposta HTTP continua `200 OK`, (b) o `PendingDispatch` é removido, (c) a `Message` de saída é persistida como `Failed` com o motivo, (d) nenhum `IOutboundMessageSender` é invocado.
- **[Risco] `Decrypt` ou a consulta de `dispatchInfo` falham por indisponibilidade real de infraestrutura do Postgres** (mesma classe de `57P01` documentada em `inbox-sweep-service-resiliencia`) → **mitigado só parcialmente pela Decisão 1**. A exceção não escapa mais de dentro de `DeliverResponseAsync`, mas o `SaveChangesAsync` do bloco final de `ReceiveAsync` (linhas 94-97, fora de `DeliverResponseAsync`, sem `try/catch` próprio) roda logo em seguida contra o mesmo Postgres indisponível, com o mesmo motivo para falhar — nesse cenário específico, o `PendingDispatch` continua preso e a exceção volta a escapar (de `ReceiveAsync` desta vez), pelo mesmo sintoma que esta change existe para corrigir, só que por um caminho que a Decisão 1 não toca. **Não é regressão nem gap introduzido por esta change** — o mesmo bloco final já roda sem proteção hoje, para o caminho já existente de falha do `sender.SendAsync` e mesmo para push notifications sem resposta. Diferente de um `PendingDispatch` `Pending`, um `PendingDispatch` `Dispatching` preso por essa via **não** é revisitado pela próxima varredura de `DebounceSweepService` (o candidato do ciclo de varredura é filtrado por `Status == PendingDispatchStatus.Pending`, `DebounceSweepService.ProcessDueDispatchesAsync`) — ficaria preso até intervenção manual. Escopo explicitamente fora desta change (Non-Goal): proteger esse bloco exigiria estender a mudança para `ReceiveAsync` inteiro e decidir o que fazer quando nem a persistência final completa — maior que o defeito determinístico que motivou esta change. Sem contraparte de teste nesta change; registrado como item em aberto com gatilho (Open Questions).
- **[Risco] `catch` amplo demais escondendo bug de programação em silêncio** → mitigação: Decisão 3 — todo erro capturado é logado em nível erro com a exceção original, sempre; um bug determinístico nesse trecho gera log de erro repetido a cada push notification, não silêncio (mesmo argumento de `inbox-sweep-service-resiliencia`, Decisão 3).
- **[Risco] Mensagem de log perde contexto útil (`ChannelId`/`ChannelType`) ao trocar para `SessionId`** → aceito conscientemente (Decisão 2): `SessionId` é suficiente para localizar o registro em qualquer um dos três pontos de falha possíveis, e a exceção original já carrega o motivo técnico. `ChannelId`/`ChannelType` só estariam disponíveis quando a consulta de `dispatchInfo` tivesse sucesso — condicionar o formato da mensagem a isso adicionaria ramificação sem ganho proporcional (Decisão 2 argumenta isso).
- **[Risco] Outra chamada desprotegida no mesmo método passar despercebida** → mitigação: Decisão 4, varredura completa e explícita de todas as chamadas de `DeliverResponseAsync`, não só o `Decrypt` que motivou a change.
- **[Risco] Correção de produção fica sem efeito prático observável nesta base** (porque `tests/InboxOrchestratorRoundTrip.Tests` continua parado até a change de fixture) → não é mitigado por esta change; é o motivo pelo qual a ordem D1/D2 do `proposal.md` importa (produção primeiro) e pelo qual o novo teste em `PushNotificationEndpointsTests.cs` (que não depende do round-trip de três apps) é a contraparte real, não o round-trip.

## Migration Plan

Sem migração de dados, sem mudança de schema, sem mudança de contrato HTTP externo. Rollout é uma alteração de código em um único arquivo de produção (`PushNotificationEndpoints.cs`), sem flag de feature — o comportamento novo (não devolver 500 não tratado) é estritamente uma melhoria sobre o atual, sem caminho que piore algo hoje funcional.

Verificação pós-implementação, a registrar em `02-HISTORICO_E_STATUS.md` (ver `tasks.md`): o novo teste em `PushNotificationEndpointsTests.cs` passa; a suíte completa de `apps/inbox` continua verde; `tests/InboxOrchestratorRoundTrip.Tests` **não** é usado para verificar esta change (continua parado pela ausência do fixture correto — Non-Goal) e não deve ser tratado como sinal de sucesso ou fracasso desta change especificamente.

## Open Questions

As incertezas de desenho desta change (escopo do `try` e do `catch`, formato da mensagem de log) foram fechadas nas Decisões 1-4, com evidência já coletada na exploração anterior e leitura direta do código atual. Uma incerteza real fica, deliberadamente fora do escopo desta change (ver Non-Goals, Risks):

**Falha de infraestrutura do Postgres durante o bloco final e incondicional de `ReceiveAsync`** (`UpdateMessageDispatchStatusesAsync` + `Remove(pendingDispatch)` + `SaveChangesAsync`, `PushNotificationEndpoints.cs:94-97` — fora de `DeliverResponseAsync`, sem `try/catch` próprio, roda para toda push notification aceita) pode deixar o `PendingDispatch` preso do mesmo jeito que o defeito corrigido por esta change, por um caminho que esta change não toca. Não é um gap novo: já existe hoje para o caminho de falha do `sender.SendAsync` e para push notifications sem resposta. Diferente de um `PendingDispatch` `Pending`, um `PendingDispatch` `Dispatching` preso por essa via **não** é revisitado pela próxima varredura de `DebounceSweepService` (o candidato do ciclo de varredura é filtrado por `Status == PendingDispatchStatus.Pending` — `DebounceSweepService.ProcessDueDispatchesAsync`), então ficaria preso até intervenção manual ou até uma change própria endereçar isso.

**Gatilho para revisitar**: a próxima vez que uma falha real de Postgres (`57P01` ou similar) for observada em produção ou em teste sob carga afetando `apps/inbox`, ou a próxima change que precisar tocar `PushNotificationEndpoints.ReceiveAsync` por outro motivo — o que vier primeiro.
