## Context

`DebounceSweepService` (`apps/inbox/src/Buteco.Inbox/Orchestration/DebounceSweepService.cs`)
é um `BackgroundService` registrado via `AddHostedService<DebounceSweepService>()`
em `Program.cs:115`. Ele roda um `PeriodicTimer` no intervalo configurado
(`Debounce:SweepInterval`, 2s em produção) e, a cada tick, chama
`ProcessDueDispatchesAsync`: consulta `pending_dispatches` elegíveis
(fora da janela de debounce) direto no `AppDbContext`, e para cada
candidato chama `TryDispatchAsync`, que dispara `SendMessage` contra
`apps/api` via A2A.

O tratamento de exceção existente é estreito e cobre só o disparo:
`TryDispatchAsync` captura `A2AException` (rejeição de protocolo) e uma
lista fechada de "falha de transporte" (`IsTransportFailure`:
`HttpRequestException`, `TaskCanceledException` não originado de
cancelamento). A consulta de candidatos em `ProcessDueDispatchesAsync`
(antes do loop de disparo) não tem proteção nenhuma, e nenhuma exceção
fora daquela lista fechada — incluindo qualquer falha de acesso ao
Postgres — é capturada em ponto nenhum do ciclo.

`HostOptions.BackgroundServiceExceptionBehavior` não é configurado em
lugar nenhum do repo (confirmado por varredura em `apps/inbox`, `apps/api`,
`apps/workers`, `libs/`). O default do .NET 8+ é `StopHost` (confirmado
por decompilação de `Microsoft.Extensions.Hosting.Internal.Host` e
`Microsoft.Extensions.Hosting.BackgroundService`, ambos 10.0.10): uma
exceção não tratada em `ExecuteAsync` de qualquer `BackgroundService`
registrado no host provoca `Host.TryExecuteBackgroundServiceAsync`
logar a falha e o host inteiro (`IHost`, e portanto `IServiceProvider`)
ser parado — não só aquele serviço.

**Achado real, reproduzido, não hipotético** (rodada `/opsx:explore`
`testes-isolamento-estado-processo`): em 4 de 4 execuções verificadas da
suíte de `apps/inbox` com log detalhado, o evento abaixo apareceu — em
todas, inclusive nas que terminaram 100% verdes:

```
fail: Microsoft.Extensions.Hosting.Internal.Host[9]
      BackgroundService failed
      System.InvalidOperationException: An exception has been raised that is likely due to a transient failure.
       ---> Npgsql.PostgresException (0x80004005): 57P01: terminating connection due to administrator command
         at Buteco.Inbox.Orchestration.DebounceSweepService.ProcessDueDispatchesAsync(...)
crit: Microsoft.Extensions.Hosting.Internal.Host[10]
      The HostOptions.BackgroundServiceExceptionBehavior is configured to StopHost. A BackgroundService
      has thrown an unhandled exception, and the IHost instance is stopping.
```

Isso não é um problema de teste (a corrida de disposal do
`WebApplicationFactory` entre classes, hipótese original da exploração,
foi refutada — ver proposal.md e a correção de registro na seção
"Registro" abaixo). É um defeito de produção real: qualquer soluço
transitório de Postgres (queda de conexão, failover, pool esgotado)
durante um ciclo de varredura derruba o processo `apps/inbox` inteiro,
incluindo os endpoints de webhook de entrada do WhatsApp e Telegram.

## Goals / Non-Goals

**Goals:**
- Uma falha de infraestrutura (não relacionada a A2A/transporte) durante
  um ciclo de varredura do debounce não deve derrubar o processo
  `apps/inbox`.
- Uma falha determinística ao processar um candidato específico não deve
  bloquear os demais candidatos elegíveis no mesmo ciclo.
- A falha deve ser observável (log de nível erro), nunca engolida em
  silêncio.
- O ciclo de varredura seguinte deve continuar executando normalmente,
  sem intervenção manual e sem reinício do processo.
- Corrigir o registro em `02-HISTORICO_E_STATUS.md`: o mecanismo da
  flake de `apps/inbox` não é a corrida de disposal entre classes de
  teste — é este defeito.

**Non-Goals:**
- Corrigir `InboxFactoryFixture` (o padrão de acessar `Services` antes de
  migrar, que já foi corrigido em `OrchestrationFactoryFixture`). É
  defeito de teste, não de produção — change própria subsequente.
- Mudar o modelo de paralelismo dos testes xUnit (serializar coleções,
  desabilitar paralelismo por assembly). Reduziria a contenção de
  recursos que dispara o `57P01` com mais frequência, mas não impede a
  próxima falha transitória real de derrubar o host — trataria sintoma,
  não causa. Ver Decisão 1.
- Mudar o intervalo de varredura (2s), o mecanismo de idempotência
  (`xmin`/`pg_advisory_lock`), ou qualquer outro comportamento do
  debounce em si.
- Estender esta correção a `apps/workers`/`TaskJobConsumer`. Verificado
  (ver Decisão 4) que `TaskJobConsumer` já envolve o processamento de
  cada mensagem em `try/catch (Exception)` próprio — não compartilha o
  buraco real que existe em `DebounceSweepService`. Fora de escopo.
- Estender a `apps/api`, que não registra nenhum `BackgroundService`.

## Decisions

### 1. Corrigir a resiliência do `DebounceSweepService`, não o paralelismo dos testes

Serializar coleções do xUnit ou desabilitar paralelismo por assembly
reduziria a contenção de recursos (9 `WebApplicationFactory`+Postgres
Testcontainers concorrentes numa VM de 6 CPU/6GB) que hoje dispara o
`57P01` com mais frequência sob carga de teste. Mas não elimina o
defeito: a próxima falha transitória real de banco — em teste ou em
produção — ainda derrubaria o host, porque a causa é a ausência de
tratamento no serviço, não a concorrência de testes que apenas aumenta a
chance de expor o defeito. Rejeitada como correção principal; o defeito
de produção precisa ser corrigido na origem.

### 2. Escopo do tratamento: `try/catch` local por ciclo, não `BackgroundServiceExceptionBehavior.Ignore` no host

Duas formas possíveis de parar o `StopHost`:
(a) `try/catch` dentro do próprio `DebounceSweepService`, dentro de
`ProcessDueDispatchesAsync` (granularidade exata decidida na Decisão 3
abaixo), logando e deixando o `while (await
timer.WaitForNextTickAsync(...))` continuar;
(b) `HostOptions.BackgroundServiceExceptionBehavior = Ignore`, configurado
no host — impede o `StopHost` para qualquer `BackgroundService`.

**Decisão: (a), não (b).** Duas razões:

- **(b) sozinha não resolve o problema — piora de outro jeito.**
  `BackgroundService.StopAsync`/o runtime do host, quando configurado com
  `Ignore`, não reinicia o `ExecuteAsync` que já terminou por exceção —
  o log crítico do próprio .NET confirma isso ("the BackgroundService
  will not be restarted"). Ou seja: com `Ignore`, a primeira exceção não
  tratada mata o loop de varredura **para sempre**, silenciosamente do
  ponto de vista funcional (o processo continua respondendo webhook e
  passando health check, mas nunca mais dispara nenhum debounce). Um
  serviço "vivo mas morto" é pior que o crash atual — pelo menos hoje o
  processo cai de forma visível (falha de health check/orquestrador). A
  correção real precisa manter o `while` girando, não só impedir o
  `StopHost`.
- **(b) é política de host, afeta todo `BackgroundService` futuro sem
  distinção.** A convenção 4 desta base fala de degradação por
  dependência específica ("é logada, não propagada"), não de uma
  política cega aplicada a qualquer serviço de fundo presente ou futuro.
  Um `catch` local no serviço certo é mais previsível e documenta a
  decisão no lugar onde ela se aplica.

`(a)` sozinha resolve as duas pontas: o loop continua (não fica morto
como em `(b)`), e o `HostOptions.BackgroundServiceExceptionBehavior`
permanece no default (`StopHost`) como rede de segurança genuína para
qualquer *outro* `BackgroundService` futuro que não tenha recebido este
mesmo cuidado — não é enfraquecido globalmente por esta change.

### 3. Escopo do `catch`: dois níveis (por ciclo e por candidato), não um `catch` único por lote nem uma lista fechada de tipos "transitórios"

A tentação óbvia é copiar o padrão de `IsTransportFailure` — uma lista
fechada de tipos de exceção considerados "transitórios" (aqui seria
`NpgsqlException`, `TimeoutException`, etc.) e deixar o resto escapar.
**Rejeitada**: uma lista fechada de exceções de infraestrutura é frágil
e incompleta por natureza (Npgsql/EF Core têm dezenas de subtipos de
exceção transitória, e a lista teria que ser mantida manualmente).

**Primeira versão deste design (revisada) propunha um único `catch`
amplo ao redor de toda a chamada de `ProcessDueDispatchesAsync`.**
Corrigida após revisão: isso não replica o precedente de
`TaskJobConsumer` que a versão anterior desta mesma decisão invocava —
`TaskJobConsumer` captura **por mensagem** (por unidade de trabalho
individual, dentro do handler `consumer.ReceivedAsync`), não por lote
inteiro. Um `catch` único por ciclo, colocado por fora do `foreach` de
candidatos, tem uma consequência real que não estava nos Risks: se um
candidato específico falhar de forma **determinística** (ex.: dado
malformado causando `NullReferenceException` dentro de
`TryDispatchAsync`, ao montar `SendMessageRequest`, ANTES de qualquer
exceção capturada pelos catches estreitos já existentes), a exceção
aborta o `foreach` antes de alcançar os candidatos seguintes na mesma
lista. Se esse candidato permanecer elegível na próxima consulta (o que
depende do ponto exato de falha vs. o `SaveChangesAsync` de
`MarkDispatching` — não é garantido que sempre se autocorrija) e
continuar na mesma posição relativa da lista, ele bloqueia os candidatos
que vêm depois dele **a cada ciclo**, indefinidamente — o mesmo padrão
"vivo mas morto" que a Decisão 2 usou para rejeitar
`BackgroundServiceExceptionBehavior.Ignore`, só que agora aplicado a uma
fatia de candidatos em vez de ao serviço inteiro.

**Decisão corrigida: dois níveis de `catch` dentro de
`ProcessDueDispatchesAsync`, cada um cobrindo uma falha diferente:**

- **Nível de ciclo** — ao redor da consulta que produz `candidateIds`
  (o bloco `using (var scope = ...) { ... ToListAsync(...) }`): é
  exatamente onde o `57P01` reproduzido de fato ocorreu (Context acima).
  Se a consulta falhar, não há lista de candidatos para iterar — loga o
  erro e a chamada retorna sem processar nenhum candidato neste ciclo; o
  próximo tick do `PeriodicTimer` consulta de novo, do zero.
- **Nível de candidato** — ao redor de cada `await
  TryDispatchAsync(candidateId, cancellationToken)` dentro do `foreach`:
  replica fielmente o precedente de `TaskJobConsumer` (por unidade de
  trabalho). Se um candidato falhar por qualquer exceção fora das já
  tratadas (`A2AException`/`IsTransportFailure`), loga o erro
  identificando o `candidateId` e o `foreach` **continua para o próximo
  candidato** — nenhum candidato problemático bloqueia os demais no
  mesmo ciclo.

**Ambos os `catch` excluem cancelamento genuíno do próprio processo**
(`OperationCanceledException` quando `cancellationToken
.IsCancellationRequested` é verdadeiro) — mesmo idioma já usado em
`IsTransportFailure` (`TaskCanceledException => !cancellationToken
.IsCancellationRequested`). Sem essa exclusão, um shutdown normal do
processo geraria uma entrada de log de **erro** espúria a cada
cancelamento, mascarando um evento normal como falha.

**Isso não esconde erro de programação em silêncio** — resolve a
tensão do proposal.md sem precisar distinguir tipos de exceção: cada
ocorrência, transitória ou não, é logada em nível erro
(`ILogger.LogError`) com a exceção original, em qualquer um dos dois
níveis. Um bug de programação real e determinístico (ex.:
`NullReferenceException` num caminho de código novo, atado a um
candidato específico) falharia em **todo ciclo de 2s para aquele
candidato**, gerando uma entrada de log de erro a cada ocorrência — um
sinal de monitoramento muito mais forte e persistente do que um único
crash de processo (que pode nem gerar alerta, dependendo da política de
restart do orquestrador) — e, com o `catch` por candidato, sem impedir
que **outros** candidatos do mesmo ciclo sejam disparados normalmente
enquanto o bug não é corrigido.

### 4. `apps/workers`/`TaskJobConsumer` — verificado, não compartilha o defeito

Pergunta fechada antes desta proposta (convenção 6): `TaskJobConsumer`
(`apps/workers/src/Buteco.Workers/Messaging/TaskJobConsumer.cs`) também é
um `BackgroundService`, e seu `Program.cs` também não configura
`BackgroundServiceExceptionBehavior`. Mas o handler
`consumer.ReceivedAsync` (linha 45-62) já envolve o processamento de
**cada mensagem** em `try/catch (Exception ex)` próprio — loga
(`LogError`) e responde `BasicNackAsync(..., requeue: false)`, sem deixar
a exceção escapar de `ExecuteAsync`. O risco recorrente e permanente,
análogo ao de `DebounceSweepService` (qualquer falha transitória durante
o trabalho de rotina derruba o host), **não existe** ali.

O que fica desprotegido em `TaskJobConsumer` é só a sequência de
**setup inicial** (`CreateConnectionAsync`/`CreateChannelAsync`/
`QueueDeclareAsync`/`BasicQosAsync`/`BasicConsumeAsync`, todas antes do
loop de consumo) — um risco de natureza diferente (disponibilidade do
RabbitMQ no boot, não falha recorrente durante operação normal), mais
próximo da família de checagem de startup (convenção 8) do que do
defeito desta change. Fora de escopo aqui — registrado como item em
aberto em `02-HISTORICO_E_STATUS.md` (Tarefa 3.4 de tasks.md), não
apenas mencionado neste design.md, para não se perder no archive desta
change.

**Conclusão: esta change permanece restrita a `apps/inbox`.** Não há
crescimento de escopo para dois apps.

## Risks / Trade-offs

- **[Risco] Falha transitória de banco durante a consulta de candidatos
  derruba o host** → mitigação: Decisão 3, `catch` de nível de ciclo ao
  redor da consulta. Teste força a exceção via um `DbCommandInterceptor`
  do EF Core armado sob demanda (dispara uma vez na consulta de
  `pending_dispatches`, depois se desarma) e assere explicitamente que
  (a) a exceção é logada, (b) o processo permanece vivo/respondendo e
  (c) o ciclo seguinte consulta e processa normalmente — não apenas
  "processado corretamente" (convenção 5), o par com-falha/sem-falha
  completo.
- **[Risco] Falha determinística ao processar um candidato específico
  bloqueia os candidatos seguintes do mesmo ciclo, indefinidamente** →
  este era o defeito da primeira versão desta Decisão (`catch` único por
  lote) e é a razão da correção para dois níveis. Mitigação: Decisão 3,
  `catch` de nível de candidato dentro do `foreach`, replicando o
  precedente de `TaskJobConsumer`. Teste cria dois candidatos elegíveis
  no mesmo ciclo, força falha determinística só no primeiro (via
  `factory.A2AClientFactory.Handler`, lançando uma exceção fora de
  `A2AException`/`IsTransportFailure`) e assere que o segundo é
  disparado normalmente **no mesmo ciclo**, sem esperar por uma nova
  varredura.
- **[Risco] `catch` amplo demais escondendo bug de programação em
  silêncio** → mitigação: Decisão 3 — todo erro capturado, em qualquer
  um dos dois níveis, é logado em nível erro com a exceção original,
  sempre; um bug de programação real e determinístico se manifesta como
  log de erro repetido a cada ciclo (para o candidato ou para a consulta
  afetada), não como silêncio. Testes confirmam que cada `catch` gera
  uma entrada de log (via o `ILoggerProvider` de captura descrito nas
  tarefas), não apenas que o processo sobrevive.
- **[Risco] Cancelamento genuíno do processo (shutdown) é registrado
  como falha de erro** → mitigação: Decisão 3 exclui
  `OperationCanceledException` ligada a `cancellationToken
  .IsCancellationRequested` de ambos os `catch`, mesmo idioma já usado em
  `IsTransportFailure`.
- **[Risco] Correção não fecha o disposal do `IServiceProvider` em si**
  (o `IServiceProvider` da instância que já sofreu o crash antes desta
  correção continuaria morto) → não é mitigado por esta change nem
  precisa ser: depois da correção, o cenário observado (Npgsql 57P01
  dentro da consulta) deixa de propagar para `ExecuteAsync`, então o
  `StopHost`/disposal do host nunca é acionado por esta causa. Ver
  Migration Plan sobre a verificação empírica dessa previsão.

## Migration Plan

Sem migração de dados, sem mudança de schema, sem mudança de contrato
externo. Rollout é uma alteração de código em um único arquivo de
produção (`DebounceSweepService.cs`), sem flag de feature — a mudança de
comportamento (não derrubar o host) é estritamente uma melhoria sobre o
comportamento atual, sem caminho que piore algo hoje funcional.

Verificação pós-implementação (não bloqueia esta change, mas deve ser
registrada em `02-HISTORICO_E_STATUS.md` quando a implementação
acontecer): repetir a mesma rodada de reprodução usada nesta exploração
(execuções sucessivas da suíte de `apps/inbox` com log detalhado) e
confirmar que o evento `BackgroundService failed` / `StopHost` deixa de
aparecer, e que a `ObjectDisposedException` na suíte desaparece mesmo
sem tocar `InboxFactoryFixture`. Se a `ObjectDisposedException`
**persistir** depois desta correção, isso é achado a reportar — indica
uma segunda causa raiz não coberta por este design, e não deve ser
descartado como "resíduo esperado".

## Open Questions

(nenhuma — as únicas incertezas reais desta change, forma do tratamento
de exceção e escopo de `apps/workers`, foram fechadas nas Decisões 2-4
acima, com evidência já coletada na exploração anterior.)
