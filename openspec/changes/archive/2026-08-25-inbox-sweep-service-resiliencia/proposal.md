## Why

`DebounceSweepService` (`apps/inbox`, `BackgroundService` registrado em
`Program.cs:115`) varre `pending_dispatches` a cada 2s. Seu tratamento de
exceção cobre apenas `A2AException` e falha de transporte HTTP
(`IsTransportFailure`, dentro de `TryDispatchAsync`) — qualquer outra
exceção, incluindo falha transitória de Postgres na própria consulta de
`ProcessDueDispatchesAsync`, escapa de `ExecuteAsync` sem tratamento.

`HostOptions.BackgroundServiceExceptionBehavior` não é configurado em
lugar nenhum do repo (confirmado por varredura). O default do .NET 8+ é
`StopHost` (confirmado por decompilação de `Microsoft.Extensions.Hosting`):
uma exceção não tratada em qualquer `BackgroundService` derruba o `IHost`
inteiro, não só aquele serviço.

Isso foi reproduzido de verdade, não é hipotético: uma rodada de
`/opsx:explore` (`testes-isolamento-estado-processo`) capturou o evento
real em 4 de 4 execuções verificadas da suíte de `apps/inbox` —
`Npgsql.PostgresException: 57P01: terminating connection due to
administrator command` dentro de `DebounceSweepService
.ProcessDueDispatchesAsync`, seguido do log
`HostOptions.BackgroundServiceExceptionBehavior is configured to StopHost
(...) the IHost instance is stopping` — inclusive em rodadas onde a
suíte terminou 100% verde (o processo cai depois que os testes daquela
classe já haviam terminado, sem gerar falha visível).

`apps/inbox` é o processo que recebe webhook de entrada do WhatsApp
(WAHA) e do Telegram. Um processo derrubado por um soluço transitório de
banco (queda de conexão, failover, pool esgotado) durante a varredura
periódica significa webhook de entrada recusado até o processo subir de
novo — perda real de mensagem de usuário final, não apenas degradação,
já que a política de reentrega de webhook varia por canal e não é
garantida. Isso viola a convenção 4 da base ("Degradação graciosa: falha
de dependência externa nunca derruba a task principal — é logada, não
propagada"): `StopHost` é o oposto exato do que essa convenção pede.

Por que agora: o defeito foi encontrado durante uma investigação de
flake de teste (`testes-isolamento-estado-processo`), que revelou que a
hipótese original de isolamento entre classes estava errada e que a
causa real é este defeito de produção — convenção 6 (achado real de
produção, não de teste) exige tratar como prioridade, não como item
registrado para depois.

## What Changes

- `DebounceSweepService.ProcessDueDispatchesAsync` passa a capturar, em
  dois níveis, exceções de infraestrutura que hoje escapam sem
  tratamento: (a) na consulta que identifica os candidatos elegíveis do
  ciclo — onde o `57P01` reproduzido de fato ocorreu — e (b) ao
  processar o disparo de cada candidato individualmente, sem que a
  falha de um candidato bloqueie os demais no mesmo ciclo (precedente:
  `TaskJobConsumer.ExecuteAsync`, que já captura por mensagem, não por
  lote). Em ambos os níveis: log estruturado do erro, sem propagar, e a
  varredura continua.
- Nenhuma distinção por tipo de exceção ("transitória" vs "programação")
  — cada `catch` é amplo e sempre loga em nível erro; um bug de
  programação real se manifesta como log de erro repetido a cada ciclo,
  não como silêncio (design.md, Decisão 3).
- Correção de registro: a entrada da baseline de testes em
  `02-HISTORICO_E_STATUS.md` (mecanismo da flake de `apps/inbox`) está
  errada e será corrigida como parte desta change — não é a corrida de
  disposal do `WebApplicationFactory` entre classes, é este defeito.
- Convenção 4 em `01-ARQUITETURA_E_CONVENCOES.md` ganha uma linha
  explícita citando `BackgroundService`/
  `HostOptions.BackgroundServiceExceptionBehavior` como caso concreto de
  "degradação graciosa" — decisão já tomada (design.md, Decisão 2), não
  avaliação em aberto.

## Capabilities

### New Capabilities

(nenhuma)

### Modified Capabilities

- `inbox-message-orchestration`: três novos requisitos — (1) falha de
  infraestrutura durante a *consulta* de candidatos elegíveis do ciclo
  de varredura do debounce (`DebounceSweepService`) não deve derrubar o
  processo `apps/inbox`; (2) falha ao processar um candidato específico
  não deve bloquear os demais candidatos do mesmo ciclo; (3) toda falha
  capturada em qualquer um dos dois pontos acima deve ser logada, nunca
  silenciosa. Distintos do requisito já existente "Falha de transporte
  do SendMessage é reintentada..." (que cobre falha ao *chamar apps/api*
  durante o disparo de um buffer, não falha na consulta de candidatos
  nem no processamento individual de um candidato).

## Impact

- **Código afetado**: `apps/inbox/src/Buteco.Inbox/Orchestration/DebounceSweepService.cs`
  (único arquivo de produção tocado).
- **Testes afetados**: `apps/inbox/tests/Buteco.Inbox.Tests/DebounceSweepServiceTests.cs`
  (novos casos), `apps/inbox/tests/Buteco.Inbox.Tests/Support/OrchestrationFactoryFixture.cs`
  (registra um `ILoggerProvider` de captura e um `DbCommandInterceptor`
  armável sob demanda, usado para forçar a falha na consulta de
  candidatos — ver design.md, Risks e tasks.md).
- **Nenhuma mudança de schema, API pública ou contrato A2A.**
- **Nenhuma mudança em `apps/api` ou `apps/workers`** — verificado que
  `apps/workers` (`TaskJobConsumer`) já envolve o processamento de cada
  mensagem em `try/catch (Exception)` próprio (loga e `Nack` sem
  requeue), diferente do buraco real que existe em `DebounceSweepService`;
  não compartilha o mesmo defeito, então o escopo permanece em um app só.
- **Não altera** `InboxFactoryFixture` (fixture de teste) — fica como
  Non-Goal registrado para uma change de teste separada e subsequente.
