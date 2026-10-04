> **Trilha paralela.** Esta change roda na worktree `../buteco-agentes-47`, branch
> `fix/47-pending-dispatch-orfa`, a partir de `cb74315`. A linha principal edita
> `01`, `02`, `CHANGELOG` e `docs/` (change `ciclo-de-sincronizacao`, #105); esta
> change não toca nenhum deles até o dono pedir. **Antes e depois de cada suíte:**
> `podman ps` e `uptime` na mesma frase do número, e amostra de `vstest` de outras
> worktrees durante a rodada. Rodada acima de ~10 min não é medição. Variáveis:
> `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED=true`,
> `TZ=America/Sao_Paulo`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1`;
> `--blame-hang-timeout 4m`.
>
> Todo o código é em **`apps/inbox`**, mais os testes de
> `tests/InboxOrchestratorRoundTrip.Tests`. Nada em `apps/api`, `apps/workers`,
> `apps/frontend` nem `libs/`.

## 1. Conferência antes do código (exploração, feita)

- [x] 1.1 Baseline sobre `cb74315`, antes de qualquer edição. Em todas as
  rodadas, a VM tinha só o contêiner órfão `funny_elion` de outra sessão (ocioso,
  0,1% de CPU), e nenhum `vstest` de outra worktree rodou:
  - `apps/inbox`: **213/213**, 28 classes, 30 s, load 9,41 → 9,33;
  - `apps/workers`: **396/396**, 41 classes, 412 s, load 4,39 → 2,65;
  - `InboxOrchestratorRoundTrip`: **4/4**, 31 s, load 2,41 → 5,01;
  - `CrossAppTaskStoreCompatibility`: **2/2**, 18 s, load 5,01 → 4,69.

  Contagem por classe em `scratchpad/runs/base-*/r.trx` da sessão.
- [x] 1.2 Reprodução das fontes 2 e 3 e do caminho de falha do worker (round-trip,
  `tests/InboxOrchestratorRoundTrip.Tests/OrphanDispatchReproTests.cs`): 3/3
  afirmando o defeito. Fonte 3 confirmada pelo log da guarda terminal.
- [x] 1.3 Reprodução das fontes 1 e 4 (`apps/inbox/tests/…/OrphanDispatchReproTests.cs`):
  3/3 afirmando o defeito (parada com `SendMessage` em voo, exceção não-transporte
  depois do claim, push precoce).
- [x] 1.4 Varredura de escopo, estreita e larga, item a item: tabela no
  `design.md`, Context.
- [x] 1.5 SDK `A2A 1.0.0-preview2` decompilado: `A2AClient.GetTaskAsync`, o
  `GetTask` do `A2AServer` (`TaskNotFound`) e `TaskStateExtensions.IsTerminal`
  (`Completed`, `Failed`, `Canceled`, `Rejected`).
- [x] 1.6 Linhas do dev lidas (13, todas com `TaskId`): tabela em D9.
- [x] 1.7 Revisão do portão 1, fonte 1 na parada (D11), medida sobre `cb74315`:
  guarda `Source1_InboxStopsWithSendMessageInFlight_StopWaitsAndTaskIdIsRecorded`
  com o `SendMessage` levando 1 s. **Vermelho no código atual:** parada de 6 ms,
  `TaskId` nulo. **Verde com a unidade de 8 s** (patch provisório em
  `DebounceSweepService`, **revertido**; `git diff -- apps/inbox/src` vazio):
  1.009/1.010/1.008 ms, `TaskId` gravado, 3/3. Pior caso (`apps/api` sem
  resposta): a parada esperou o prazo, ~8 s. Load 8,82 na largada, VM só com o
  órfão `funny_elion`.
- [x] 1.8 Carimbo terminal: `status.timestamp` presente no JSON cru do `GetTask`
  em `Completed` e `Failed` (round-trip,
  `TerminalTask_CarriesStatusTimestamp_InRawPersistedJson`, 2/2). `Canceled` e
  `Rejected`, pelo SDK decompilado (`TaskUpdater` :135, :153); nenhum dos dois
  chega a uma linha com `TaskId` (D3).
- [x] 1.9 Condição do portão 1, parada com trabalhos em voo
  (`ShutdownBudgetProbeTests`, `Program` real sob Kestrel, D11 provisório,
  **revertido**): ordem real Kestrel → reconciliação → debounce →
  DataProtection → HealthCheck; três trabalhos em voo, **3.968 ms**; mensagem
  vencendo durante a espera do Kestrel, reivindicada 2,95 s depois do pedido,
  **10.933 ms**. Decisões do dono: observar `ApplicationStopping` antes de
  reivindicar; push em voo além do prazo, resíduo aceito.

## 2. Guardas vermelhos contra o código atual (testes)

- [x] 2.1 `apps/inbox` (testes), `Support/FakeA2AClientFactory.cs`: `GetTask`
  controlável por teste (task por `TaskId`, `TaskNotFound`, falha de transporte),
  além do `BeforeReturn` já acrescentado na exploração.
- [x] 2.2 `tests/InboxOrchestratorRoundTrip.Tests`: fixture com
  `DispatchReconciliation` curto (intervalo e carência em subsegundos/segundos).
  Os testes das fontes 2 e 3 passam a afirmar a correção: linha removida e
  resposta entregue (fonte 2/3, task `Completed`); aviso entregue e entradas
  `Failed` (task `Failed` com push falhando). **Rodar contra o código atual e
  registrar o vermelho, com tempo e motivo.**
- [x] 2.3 `apps/inbox` (testes): fontes 1 e 4 passam a afirmar a correção (sem
  `TaskId` além do limite → aviso + `Failed` + removida, sem novo `SendMessage`;
  push precoce → resolvida pela reconciliação). Vermelho registrado. O guarda da
  parada com envio em voo (1.7) fica como está, e o teste antigo de parada com a
  `apps/api` sem resposta passa a afirmar a parada limitada pelo prazo, e não mais
  o `TaskId` nulo como resultado aceito.
- [x] 2.4 `apps/inbox` (testes), `DispatchReconciliationServiceTests`: task
  terminal dentro da carência não é tocada; task não-terminal velha não é tocada;
  `TaskNotFound` e falha da consulta não alteram a linha e não impedem a linha
  seguinte; task terminal **sem carimbo** não é reconciliada na primeira
  observação e é reconciliada depois da carência contada dela; sem `TaskId` dentro do limite não é tocada; duas instâncias entregam
  uma vez; push depois da reivindicação recebe 401; **processador que perde o
  `xmin` no fim** (dois `DbContext`, D4) não propaga, devolve "já resolvido" e loga
  `Warning`, e o endpoint responde 200. Vermelho registrado.
- [x] 2.6 `apps/inbox` (testes), parada sob Kestrel (a sonda de 1.9 vira guarda):
  (a) mensagem que vence durante a espera do Kestrel **não** é reivindicada, e a
  parada termina abaixo de 10 s; (b) entrega em voo na reconciliação dentro do
  prazo é aguardada e a linha é removida; (c) entrega em voo além do prazo é
  cancelada, e a linha fica reivindicada em `Dispatching`. Duas pernas cada.
- [x] 2.5 `apps/inbox` (testes), `DispatchFailureNoticeTests` e
  `PushNotificationEndpointsTests`: aviso entregue e persistido como saída em push
  `Failed`, rejeição síncrona, rejeição de protocolo e esgotamento de tentativas;
  nenhum aviso em falha de transporte reintentável nem em `Completed` sem texto;
  falha ao entregar o aviso persiste `Failed` e remove a linha; entradas de task
  `Failed` ficam `Failed`. Vermelho registrado.

## 3. Processamento de desfecho e aviso (`apps/inbox`)

- [x] 3.1 `Orchestration/DispatchOutcomeProcessor.cs` (scoped): desfecho de uma
  `AgentTask` sobre a linha (tabela de D5), entrega ao canal movida de
  `PushNotificationEndpoints.DeliverResponseAsync` sem mudança de comportamento,
  aviso de falha (D6), encerramento por falha do lado do inbox.
- [x] 3.2 `PushNotificationEndpoints.cs`: valida o token e delega ao processador;
  comentários de mecanismo preservados (o `CancellationToken.None` depois da
  validação).
- [x] 3.3 `DebounceSweepService.cs`: rejeição de protocolo (`:142`), rejeição
  síncrona (`:241`) e esgotamento (`:268`) passam pelo encerramento por falha do
  processador.
- [x] 3.4 `PushNotificationEndpoints.cs`: `DbUpdateConcurrencyException` no fim do
  processamento vira 200 com log `Warning` (D4).
- [x] 3.5 `DebounceSweepService.cs`: unidade com prazo próprio de 8 s entre o
  commit do claim e a gravação do `TaskId` (D11), com a derivação e o gatilho de
  recalibração no comentário; `ApplicationStopping` conferido antes de cada
  reivindicação.
- [x] 3.6 Guardas de 2.5 e o guarda de 1.7 verdes.

## 4. Reconciliação (`apps/inbox`)

- [x] 4.1 `Orchestration/DispatchReconciliationOptions.cs` com os padrões de D8, e
  a derivação e o gatilho de recalibração escritos ao lado de cada valor.
- [x] 4.2 `Entities/PendingDispatch.cs`: reivindicação por troca do
  `ExpectedToken` (D4).
- [x] 4.3 `Orchestration/DispatchReconciliationService.cs`: as duas regras (D3, D7),
  o registro em memória da primeira observação de task terminal sem carimbo (D3),
  `ApplicationStopping` conferido antes de cada reivindicação e a entrega em voo
  com prazo de 8 s contado do pedido de parada (D11),
  `try` envolvendo a consulta de candidatos e cada linha (convenção 4), log de
  início com intervalo, carência e limite.
- [x] 4.4 `Program.cs`: opções e `AddHostedService`.
- [x] 4.5 Guardas de 2.2, 2.3, 2.4 e 2.6 verdes.
- [x] 4.6 **Divergência da implementação (D4, aprovada pelo dono em 04/10/2026):**
  coluna `ReconciliationClaimedAt` + migration `AddReconciliationClaimedAt` +
  `ClaimLease` (2 min). A segunda perna do guarda de duas instâncias, tornado
  determinístico, deu 2 entregas com a reivindicação só pelo token. Guardas: o de
  duas instâncias (com a primeira entrega presa no sender) e o de posse vencida.

## 5. Fechamento da implementação

- [x] 5.1 Suítes contra a baseline, por classe, com as condições de VM: `apps/inbox`,
  `apps/workers` (deve repetir 396/396; a change não toca o app),
  `InboxOrchestratorRoundTrip`, `CrossAppTaskStoreCompatibility`.
- [x] 5.2 Segunda perna de cada guarda (convenção 15): reverter a correção do
  componente que o guarda prende, ver reprovar, restaurar. Registrar tempo e motivo
  por guarda.
- [x] 5.3 `openspec validate pending-dispatch-orfa --strict` limpo.
- [x] 5.4 Achado novo durante o apply vira issue na hora, com os rótulos da
  taxonomia: **#145** (`TaskJobConsumer.StopAsync` lança `AlreadyClosedException`
  depois de `CHANNEL_ERROR` no `nack`, `app: workers`) e **#146** (`SSH.NET`
  vulnerável via Testcontainers, já presente na baseline). A divergência do D4 e o
  risco do `LastMessageAt` no D7 ficaram nos artefatos, não em issue: são desta
  change.
- [x] 5.5 Atualização sobre a `main` (04/10/2026), por stash com os não
  rastreados, de `cb74315` para `40b348d` (#105, #106, #138), sem conflito. A
  `main` não tocou `apps/inbox` nem as três specs dos deltas (os quatro `MODIFIED`
  continuam sobre o texto vigente) e não trouxe migration do inbox:
  `AddReconciliationClaimedAt` mantida, `has-pending-model-changes` limpo.
  Suítes: `apps/inbox` 241/241, `apps/workers` **397/397** (o caso novo de
  `KnowledgeIndexingTests` veio da #138), round-trip 10/10, cross-app 2/2, iguais
  ao portão 2 por classe. Durações de `apps/inbox` e `apps/workers` afetadas por
  pico de carga externo (load 10–24, 17:57–18:02); as contagens valem.

## 6. Linhas existentes (dono, na janela do deploy)

- [ ] 6.1 **Dev, antes do primeiro boot com esta change:** ler as linhas em
  `Dispatching` de `buteco_inbox.pending_dispatches` e o estado de cada `TaskId` em
  `buteco_agents.a2a_tasks`. O dono decide o que remover (em 03/10/2026: 2
  `Completed`, 9 `Failed`, 2 `Submitted`).
- [ ] 6.2 **Produção, na janela do deploy:** a mesma leitura (é a inspeção que o
  `02` já registra). Sem decisão do dono, o inbox novo não sobe.

## 7. Depois da revisão do dono (não executar sem pedido)

- [x] 7.1 `/opsx:sync` e `/opsx:archive`: feitos pelo dono.
- [x] 7.2 Entrada no `02-HISTORICO_E_STATUS.md` e no `CHANGELOG.md`, quando o dono
  pedir.
- [x] 7.3 `docs/configuration.md`: `DispatchReconciliation__Interval`,
  `DispatchReconciliation__TerminalGrace` e
  `DispatchReconciliation__UntrackedDispatchMaxAge`, ao lado de `Debounce__*` (`:224-226`), com
  o padrão e a derivação de cada um, quando o dono pedir.
- [x] 7.4 `docs/architecture.md`: a seção do buffer de debounce (`:308`)
  passa a incluir a reconciliação, o encerramento por idade, o aviso de falha e a
  parada que espera o envio em voo, quando o dono pedir.

> **Ordem registrada (04/10/2026):** 7.2 a 7.4 foram escritas **depois** do archive
> (7.1). O dono corrigiu na hora: a entrada no `02` e no `CHANGELOG` vem sempre
> **antes** do archive, e o archive espera por ela. Nada foi commitado entre os
> dois passos, então as entradas entram no mesmo PR da change.
