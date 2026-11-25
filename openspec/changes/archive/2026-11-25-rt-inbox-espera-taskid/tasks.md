## Regras desta change

**Guardas.** Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-309.sh` reintroduz o defeito nomeado. É cópia do `guarda-296.sh`, com o mesmo lock das suítes. A mutação toca só o trecho nomeado e **nunca** restaura um diretório ao `HEAD`;
2. a árvore volta **pela cópia** feita antes da mutação, com o `sha256` conferido contra o de antes;
3. a tarefa registra:
   - os casos reprovados em cada ponta;
   - a mensagem e a linha de cada um;
   - o "voltou: <arquivo> (<sha>)".

Uma mutação que quebra a compilação ou reprova por outra asserção não vale: é descartada e refeita. As mutações de produção (G1 e G2) rodam só em cópias descartáveis, nunca no worktree da change.

**Suítes.** Rodam pelo `~/.cache/buteco-agents/bin/suite-309.sh`, cópia do `suite-296.sh`, **uma por vez, com o lock** (`suites.lock`):

- suíte: `rt-inbox`. Fora: `api`, `inbox`, `connectors`, `workers`, `rt-crossapp`, `rt-connectors` e `frontend`. Nenhum código deles muda. A tarefa 0.4 confirma pelo `selecionar-testes.py`;
- regra assimétrica (#181). Reprovação por caso que esta change mudou não é inconclusiva: registra-se "reprovada pelo X (a → b), corrigida";
- registro por rodada (convenção 19): o veredito, a duração, a CPU mínima e a mediana;
- antes de cada rodada, o load de 1 e de 5 min abaixo do limite do script, e a VM do Podman com o relógio e a memória conferidos. Nunca `dotnet build` junto de uma suíte.

**Parada** (convenção 9):

- **parar e voltar ao dono** se a 0.1 achar chamada a `DeleteTaskAsync` no fluxo de um despacho;
- **parar e voltar ao dono** se a ponta vermelha do G1 não reprovar nem pelo plano B (a mutação determinística, depois de 8 tentativas do atraso sem reprovação válida);
- **parar** se, num guarda, a ponta vermelha passar: guarda que não reprova é perda de cobertura;
- pontas vermelhas de G2, G3 e G4 contam com qualquer CPU quando a reprovação está na asserção e na mensagem esperada (decisão do dono, 24/11/2026). Reprovação por tempo esgotado segue a regra da CPU;
- **parar** se algum caso precisar sair, mudar de nome ou afrouxar asserção para passar;
- **parar e voltar ao dono** se a 0.5 mostrar caso fora da lista do topo esperando o `taskId` no registro;
- **seguir e registrar** quando a correção só acrescentar teste ou asserção, ou só tocar comentário ou documentação.

**Nada de commit** até o dono pedir. Um commit só, depois do archive.

**O que não muda:**

- nenhum arquivo de `apps/*/src`, de `libs/` ou do compose;
- `Support/RoundTripFixture.cs`;
- `ServiceTokenRoundTripTests.cs`, `IssuerTokenRoundTripTests.cs` e `RoleRoundTripTests.cs`.

## Casos que saem ou mudam de asserção (a lista do topo)

**Nenhum caso sai.** Mudam de forma, com a mesma asserção, os 12 do design (Context, o universo):

- `OrphanDispatchRoundTripTests`: `Source2_PushFails_TaskCompleted_IsReconciled_AndTheReplyIsDelivered`, `Question2_WorkerFailurePath_SendsPush_AndDispatchIsResolved`, `Source2_PushFails_TaskFailed_IsReconciled_WithTheFailureNotice`, `TerminalTask_CarriesStatusTimestamp_InRawPersistedJson` (os dois `InlineData`) e `Source3_WorkerStopsBetweenTerminalWriteAndPush_IsReconciled_AndTheReplyIsDelivered`, com a conversa principal e a sentinela;
- `RoundTripTests`: `MessageReceived_TriggersFullRoundTrip_PushNotificationReceivedWithCorrectPayload`, `…_TaskCarriesMessageInstantInRawPersistedJson`, `…_TheInboxTaskHasNoOwner` e `…_TaskCarriesChannelTypeAndContactExternalIdInRawPersistedJson`;
- `SessionSerializationRoundTripTests`: `SecondMessage_WhileFirstTaskIsWorking_TwoWorkerInstances_BothCompleteAndBothRepliesAreDelivered`.

**Ganha asserção:** a sentinela do `Source3` passa a afirmar que a resposta foi entregue (design, D3).

Nos casos 1–3 e 5–11, o caso afirma que o agente tem uma só task, com `Assert.Single` e a lista das tasks (id e estado) na mensagem (design, D2). O caso 12 segue a D4.

Cada caso é conferido por nome na 3.3. Um caso fora desta lista que precise mudar é parada.

## 0. Preparação e baseline

- [x] 0.1 (apps/api, leitura) **Conferência da D0, com parada.** Conferir que nada chama `PostgresTaskStore.DeleteTaskAsync` no fluxo de um despacho:
  - no repositório, `apps/api` e `apps/workers`;
  - no SDK A2A (`A2AServer`, `TaskUpdater`, `AgentEventQueue`, o `ITaskStore`), pela fonte do pacote da versão do `Directory.Packages.props`;
  - no reinício do worker do caso 3.

  Registrar a versão do pacote e cada ponto conferido. **Se for falso, parar e voltar ao dono antes da tarefa 1.**

  **Feito (24/11/2026): a D0 vale, sem parada.**
  - **SDK** `A2A` e `A2A.AspNetCore` `1.0.0-preview2` (`Directory.Packages.props:10-11`), descompilados com `ilspycmd`, 113 arquivos: `DeleteTaskAsync` aparece só na declaração do `ITaskStore` (`:39`) e no `InMemoryTaskStore` (`:39`). `A2AServer`, `TaskProjection` e `A2AServiceCollectionExtensions` não o chamam.
  - **Repositório** (`apps`, `libs`, `tests`): só as duas definições, `apps/api/src/Buteco.Api/A2A/PostgresTaskStore.cs:45` e `apps/workers/src/Buteco.Workers/A2A/PostgresTaskStore.cs:47`. Nenhum `Remove` nem `ExecuteDelete` sobre `A2ATasks` fora das migrations.
  - **A outra via** de apagar a linha é o `Cascade` do agente, que existe "para o dia em que houver exclusão de agente" (`AppDbContext.cs:864`). Não há rota de exclusão de agente, e nenhum caso do `rt-inbox` apaga agente.
  - **No reinício do worker (caso 3)** nada apaga: o `apps/workers` não chama `DeleteTaskAsync`, e a reentrega usa a mesma linha.
- [x] 0.2 (scripts locais) Os scripts `suite-309.sh`, `teste-309.sh` e `guarda-309.sh`, copiados dos da #296, com o lock:
  - `suite-309.sh` roda só o `rt-inbox`;
  - `teste-309.sh` roda uma classe filtrada, nas duas pontas, com o veredito e a CPU;
  - `guarda-309.sh` é cópia do `guarda-296.sh`.

  Conferir o guarda com uma mutação nula: "defeito não aplicado" e arquivo intocado.

  **Feito:** `suite-309.sh` (o `suite-296.sh` mais o caso `rt-inbox-f`, filtrado por `$RT_FILTER`, com o filtro no `-estado.txt`), `teste-309.sh` (chama o `suite-309.sh` com `rt-inbox-f`, com o lock, a CPU e o veredito) e `guarda-309.sh` (o `guarda-296.sh` mais o caso `rt-inbox`). Mutação nula no `RoundTripTests.cs`: "defeito não aplicado", `rc=2`, arquivo intocado.
- [x] 0.3 (repo) O worktree da baseline, `.claude/worktrees/rt-inbox-espera-taskid-baseline`, destacado no `origin/main` do início do apply por `git worktree add --detach`, sem mexer na árvore da change nem na `main`. Registrar o SHA.

  **Feito:** `.claude/worktrees/rt-inbox-espera-taskid-baseline`, destacado em `4e44dd3` (o `origin/main` depois do `fetch`, o merge do #308). É a mesma base da branch da change.
- [x] 0.4 (scripts) O plano de `python3 scripts/selecionar-testes.py --passos` sobre o diff da change, com o SHA da base que ele imprime. Se ele pedir mais que o `rt-inbox`, a lista de suítes das Regras muda e a mudança fica registrada aqui.

  **Feito:** com os caminhos que a change vai tocar (`--caminhos -`, porque o diff ainda é só a pasta da change): `check-docs`, `scripts-testes` e `InboxOrchestratorRoundTrip.Tests`, pela "linha 3: tests/InboxOrchestratorRoundTrip.Tests tocado (o próprio projeto)". Base: os caminhos dados, sem diff. A lista de suítes das Regras não muda. O plano é refeito sobre o diff de verdade na 3.4.
- [x] 0.5 (tests/InboxOrchestratorRoundTrip.Tests) **A varredura de novo, na baseline, pelos dois critérios da convenção 25:**
  - o estreito: as chamadas de `WaitForTaskIdAsync`;
  - o largo: toda consulta a `PendingDispatches` e todo `.TaskId` lido do registro.

  Comparar com os 12 casos do design. Um caso a mais é parada.

  **Feito, na baseline `4e44dd3`:** o estreito dá 6 chamadas de `WaitForTaskIdAsync` (`:43`, `:69`, `:94`, `:112`, `:130` e `:154`), que são 7 casos com os dois `InlineData`. O largo acrescenta o `GetPendingDispatchTaskIdAsync` do `RoundTripTests` (`:56`, `:121`, `:170` e `:217`, 4 casos) e o `FindDispatchesAsync(...).Select(d => d.TaskId)` da `SessionSerialization` (`:65` e `:80`, 1 caso). Fora: o `PendingDispatches` do `ServiceToken` (`:186`), que só testa a existência. São os 12 do design, sem caso a mais.
- [x] 0.6 (tests/InboxOrchestratorRoundTrip.Tests) A baseline no worktree da 0.3, em `~/.cache/buteco-agents/309/baseline/`:
  - rodadas do `rt-inbox` **até uma válida**, com o relatório por nome (`nomes-trx.py`) e o registro por rodada;
  - as inconclusivas entram na série.

  **Feito** (`309/baseline/r1`): `4e44dd3`, **aprovada 16/16** (23 s pelo xUnit, 77 s de parede), CPU mínima 100 e mediana 100 (17 amostras). Válida na primeira tentativa. **Série do `rt-inbox` nesta change até aqui:** 1 rodada, 0 inconclusivas.

## 1. A correção (tests/InboxOrchestratorRoundTrip.Tests)

- [x] 1.1 A leitura das tasks de um agente em `a2a_tasks`, por `ApiAppDbContext`, no padrão do caso 10:
  - o id, o estado e o `IsTerminal()` do SDK;
  - num helper de `Support/` se as três classes a usarem, senão privado por classe (design, árvore).

  **Feito:** `Support/AgentTasks.cs`, usado pelas três classes. `ListAsync` (por `AgentId`, ordenado por `TaskId`), `WaitUntilAsync` (consulta a cada 100 ms; no timeout, a mensagem traz a condição e a lista), `WaitForTerminalAsync` (a condição positiva da D1) e `Single`/`SingleTaskIdAsync` (`Assert.True(rows.Count == 1, ...)` com "id (estado)" de cada task). O estado final é o `global::A2A.TaskStateExtensions.IsTerminal` do SDK sobre o `State` gravado (`TaskState.ToString()`, `PostgresTaskStore.SaveTaskAsync`).
- [x] 1.2 `OrphanDispatchRoundTripTests`, casos 1, 2, 5, 6 e 7:
  - sai o `WaitForTaskIdAsync`;
  - entram a condição positiva (uma task do agente em estado final) e depois a ausência do registro, com os prazos de hoje;
  - a task é lida com `Assert.Single`, com a lista das tasks do agente na mensagem (D2), e as asserções seguem iguais.

  **Feito:** casos 1 e 5 com `ReconciliationTimeout` nas duas esperas; casos 2, 6 e 7 com 20 s nas duas. O `taskId` vem do `SingleTaskIdAsync`, depois da ausência.
- [x] 1.3 `OrphanDispatchRoundTripTests`, caso 3, a conversa principal: a leitura da D4, com as tasks do agente até existir uma, antes do `PushGate.Entered`, e `Assert.Single` com a lista (D2).

  **Feito:** `WaitUntilAsync(rows => rows.Count > 0, 10 s)` e `Single`, no lugar da espera do registro; o resto do caso igual.
- [x] 1.4 `OrphanDispatchRoundTripTests`, caso 4, a sentinela: sai a espera do `:112`, e entram a resposta entregue (D3) e depois a ausência (`:113`).

  **Feito:** `HasDeliveredReplyAsync`, a mensagem `Outbound` com `MockedAgentReplyText` e `DeliveryStatus.Sent`, consultada até 20 s; depois a ausência do `:113`, como antes.
- [x] 1.5 Sai o `WaitForTaskIdAsync`, se nada mais o usar. Ele lê o dado transitório, e deixá-lo seria declaração viva sem consumidor (convenção 25).

  **Feito:** o `WaitForTaskIdAsync` saiu. O `FindPendingDispatchAsync` continua, pelo `HasPendingDispatchAsync`.
- [x] 1.6 `RoundTripTests`, casos 8–11: como a 1.2. Sai o `GetPendingDispatchTaskIdAsync`, se nada mais o usar. No caso 10, o `ApplicationId` é lido da task única.

  **Feito:** os quatro casos com `WaitForTerminalAsync` (20 s), a ausência (20 s) e o `SingleTaskIdAsync`. O `GetPendingDispatchTaskIdAsync` saiu; o `taskId!` virou `taskId`. O comentário do caso 8 que dizia "captura pra conferir depois" foi reescrito.
- [x] 1.7 `SessionSerializationRoundTripTests`, caso 12: a leitura da D4, com a 1ª task, a espera do `WORKING` como hoje, e a 2ª pelo id diferente.

  **Feito:** a 1ª pelo `WaitUntilAsync(rows => rows.Count > 0)` e `Single`; a 2ª pelo `WaitUntilAsync(rows => rows.Any(id != 1ª))` e `Single` sobre as diferentes. O `FindDispatchesAsync` continua, pela espera de `Count == 0`.
- [x] 1.8 Compilar `tests/InboxOrchestratorRoundTrip.Tests` com 0 erros e 0 avisos novos. Rodar uma vez as três classes mudadas pelo `teste-309.sh`.

  **Feito:** 0 erros. Os avisos (`--no-incremental`) são os mesmos 2 `NU1903` do `SSH.NET` da baseline, nenhum novo. As três classes pelo `teste-309.sh` (`309/change/classes-r1`): **aprovada 11/11**, CPU mínima 46, mediana 67.
## 2. Guardas

- [x] 2.1 **(guarda) G1, o atraso de 8 ms** (design, D5), em cópias descartáveis das duas pontas, com `Task.Delay(8)` logo antes da conferência da chave no `EnqueueingAgentHandler`:
  - **vermelho:** a baseline com o atraso reprova os casos expostos na espera do `taskId`. Pelo `teste-309.sh`, as classes `OrphanDispatchRoundTripTests` e `RoundTripTests`, até uma reprovação válida, com no máximo 8 tentativas antes da parada;
  - **verde:** a change com o atraso, com o `rt-inbox` inteiro, até uma rodada válida aprovada 16/16.

  Registrar os casos reprovados do vermelho, que têm de estar entre os expostos (2, 4 e 6–11), e a CPU de cada tentativa.

  **Plano B, já autorizado:** em 8 tentativas sem reprovação válida, sem parada, o vermelho passa a ser uma mutação determinística numa cópia da baseline: a espera do `taskId` só começa a consultar depois de a resposta do caso estar entregue. Os expostos têm de reprovar na linha da espera do `taskId`, e essa reprovação conta com qualquer CPU, porque não depende do tempo da máquina. **Só se nem assim reprovar, parar.** Registrar no design (D5, convenção 9) o que estabeleceu o G1 e por quê.

  **Feito: o G1 vale, estabelecido pelo atraso, sem o plano B** (design, D5). Saídas em `309/g1/`.
  - **Vermelho** (`296-exp-b`, a baseline `4e44dd3` com `INVEST_DELAY_MS=8`, as duas classes expostas): v1–v5 inconclusivas (CPU mínima 69, 90, 62, 62 e 58); **v6 reprovada válida**, com a CPU em 100 nas 10 amostras, 8/10. Caíram o `Source3` em `OrphanDispatchRoundTripTests.cs:112` e o `PushNotificationReceivedWithCorrectPayload` em `RoundTripTests.cs:55`, os dois na espera do `taskId` (`TimeoutException`). Nas 6 tentativas, os casos que caíram nessa espera foram só os expostos: 2, 4, 7, 8, 10 e 11. Na v3, o `Source3` caiu também em `:101`, com `AlreadyClosedException` (`CHANNEL_ERROR - expected 'channel.open'`) no `RestartWorkersAsync`. É a outra causa da #176, fora da espera do `taskId`, e não conta para o G1.
  - **Verde** (`309-g1-verde`, a change com o mesmo atraso, byte a byte o da `296-exp-b`, e o `rt-inbox` inteiro): g1 **aprovada 16/16**, CPU mínima 51.

- [x] 2.2 **(guarda) G2, a condição positiva** (design, D5; aceito pelo dono em 24/11/2026), numa cópia descartável da change:
  - **(i)** o `ReceiveMessageAsync` grava o registro 300 ms depois de voltar;
  - **(ii)** os casos sem a condição positiva.

  **Vermelho:** com (i) e (ii), os casos reprovam na leitura de `a2a_tasks`. **Verde:** com (i) só, passam. Registrar os casos das duas pontas.

  **Feito na 3ª passada** (`309/g2/*-r3`). Pela decisão do dono, a ponta vermelha conta com qualquer CPU, porque a reprovação está no `Single`.
  - **Verde** (só a (i)): **aprovada 11/11**, CPU mínima 60. O `Source3` sozinho também aprovado.
  - **Vermelho** ((i) e (ii)): 10/11 reprovados, CPU mínima 37. Os 9 casos da D1 que leem task reprovaram no `Single`, cada um com `Esperava uma só task do agente <id>; há 0: nenhuma.`. São os casos 1 (`:46`), 2 (`:72`), 5 (`:138`), 6 e 7 (`TerminalTask`), 8 (`:65`), 9 (`:125`), 10 (`:163`) e 11 (`:206`).
  - **Fora do G2:**
    - o `Source3` reprovou em `:102` com `TASK_STATE_SUBMITTED`, nas três passadas. O push no portão era de outro caso, que terminou cedo com a task ainda rodando, e não da sentinela. A `SessionSerialization` passou, porque (ii) não a toca;
    - **a sentinela (caso 4) não é guardada pelo G2:** o `Source3` sozinho, com (i) e (ii), passa (2ª passada, CPU mínima 97). A espera de 15 s da reconciliação da conversa principal (`ReconciliationTimeout`, `:115`) cobre a reentrega. O que a condição positiva da sentinela guarda é a passagem pelo worker, e esse é o G4 (design, D5).
  - **Passadas anteriores:**
    - 1ª: o verde reprovou o `Source3` em `:121` com `Sequence contains no elements`, porque a espera da sentinela lançava sem contato;
    - 2ª: com o mesmo resultado da 3ª.

    As duas correções estão no design (D3 e D4, "Corrigido no apply").
- [x] 2.3 **(guarda) G3, a segunda task** (design, D5), numa cópia descartável da change: o timeout do cliente A2A do inbox da fixture em 50 ms, e 100 ms de atraso no handler. Registrar:
  - que houve reintentativa: o aviso "Falha de transporte" e duas linhas em `a2a_tasks` para o agente;
  - que todos os casos que leem task (1–3 e 5–12) reprovam com a lista das tasks do agente, com id e estado.

  **Feito na 3ª e na 4ª passada** (`309/g3/r3` e `r4`, com timeout de 1 s e 2 s de atraso depois do `Submit`, na 1ª chamada de cada contexto). Conta com qualquer CPU, porque a reprovação está no `Single` com a lista. **11/11 reprovados nas duas** (CPU mínima 55 e 60), cada um com `Esperava uma só task do agente <id>; há 2: <id> (Submitted), <id> (<final>).`:
  - casos 1, 2, 5, 6, 7, 8, 9, 10 e 11: a task abandonada pelo timeout em `Submitted`, mais a da reintentativa, em `Completed` ou `Failed`;
  - caso 3 (`:103`): `Submitted` e `Completed`, agora com a lista;
  - **caso 12 (`:112`), exercitado:** `há 2: <id> (Working), <id> (Submitted)`. A reintentativa da `msg1` é vista antes do 1º id, como a D4 promete.

  **Passadas anteriores**, com 50 ms de timeout e 100 ms de atraso:
  - **1ª:** o caso 3 reprovou com `TASK_STATE_SUBMITTED`, e não com a lista. Isso levou à correção da D4;
  - **2ª:** o caso 12 não foi exercitado. Nenhuma tentativa chegou ao `Submit`, e o timeout de 60 s veio com a lista vazia.

  O que a #314 descreve se reproduz aqui: a task abandonada fica em `Submitted`.
- [x] 2.4 **(guarda) G4, a sentinela que passou pelo worker** (design, D3 e D5; decisão do dono, 24/11/2026), em duas cópias descartáveis da change. Nas duas, a sentinela fala com um agente que o api não conhece: o api recusa o `SendMessage` sem task, e o inbox entrega o aviso de falha.
  - **Vermelho** (`309-g4-com`, com a condição positiva da D3): o `Source3` reprova na asserção da resposta, com o aviso de falha.
  - **Contraprova** (`309-g4-sem`, só com a ausência do registro): o `Source3` passa.

  A reprovação é determinística (D5) e conta com qualquer CPU. Registrar a mensagem.

  **Feito** (`309/g4/`, só o `Source3`; conta com qualquer CPU, porque a reprovação está na asserção da resposta):
  - **vermelho** (`309-g4-com`, CPU mínima 90): reprovado em `:135` com `Assert.Equal() Failure: Strings differ`, `Expected: "Resposta do agente de teste do round-trip"···`, `Actual: "Não consegui responder agora. Pode tentar"···`;
  - **contraprova** (`309-g4-sem`, CPU mínima 65): aprovada.
## 3. Fechamento

- [x] 3.1 (tests/InboxOrchestratorRoundTrip.Tests) O `rt-inbox` na change pelo `suite-309.sh`, até uma rodada válida, com o registro por rodada.

  **Feito** (`309/fechamento/r1`): **aprovada 16/16** (21 s pelo xUnit, 61 s de parede), CPU mínima 48 e mediana 83. Aprovação conta com qualquer CPU.
  - **Série do `rt-inbox` nesta change** (convenção 19):
    - baseline: 1 rodada, aprovada, CPU mínima 100;
    - change: 1 rodada completa, aprovada;
    - 0 inconclusivas nas rodadas completas.
  - As rodadas filtradas e as dos guardas estão em 2.1–2.4. Entre elas, as três classes mudadas na change: 11/11 três vezes (CPU mínima 46, 100 e 86).
- [x] 3.2 (tests/InboxOrchestratorRoundTrip.Tests) A mesma rodada na baseline (0.3), se a 0.6 não tiver uma válida.

  **Não precisou:** a 0.6 tem uma rodada válida (16/16, CPU 100).
- [x] 3.3 (tests/InboxOrchestratorRoundTrip.Tests) A comparação por nome de caso entre baseline e change. Os 16 nos dois lados. Nenhum caso sai, e cada caso da lista do topo é conferido pela forma e pela asserção.

  **Feito** (`309/fechamento/nomes-comparados.txt`):
  - 16 nomes na baseline e 16 na change, **os mesmos**, todos `Passed`. Nenhum caso saiu, entrou ou mudou de nome;
  - os 12 da lista do topo mudaram só de forma, e as asserções seguem as mesmas;
  - a sentinela ganhou a asserção da resposta (D3);
  - os casos 1–3 e 5–11 passaram a afirmar uma só task do agente (D2).
- [x] 3.4 (scripts) `python3 -B -m unittest discover -s scripts/tests`, `openspec validate rt-inbox-espera-taskid --strict` e o `check-docs`.

  **Feito:** `scripts/tests` 12/12 OK; `openspec validate --strict` válido; `check-docs`: "Integridade da documentação: OK". O plano do `selecionar-testes.py --passos` sobre o diff real (base `4e44dd3`) dá `check-docs`, `scripts-testes` e `InboxOrchestratorRoundTrip.Tests`. São as suítes das Regras.
- [x] 3.5 (repo) Todo desvio do design encontrado no apply corrigido no próprio `design.md` (convenção 9).

  **Feito.** Os desvios corrigidos no design:
  - a D3: a condição positiva é qualquer saída entregue, mais a asserção da resposta;
  - a D4: o caso 3 lê depois do push entrar, e o caso 12 espera uma task fora de `Submitted`;
  - a D5: o G2 não guarda a sentinela, entra o G4, a validade das pontas vermelhas e como o G1 foi estabelecido.
## 4. Documentação, archive e PR

- [x] 4.1 (docs) O `02-HISTORICO_E_STATUS.md` ganha a seção da #309:
  - as medidas do Context;
  - o universo pelos dois critérios;
  - os três guardas com as duas pontas;
  - a série do `rt-inbox`;
  - o critério de fechamento da #176 (D6).

  O `CHANGELOG.md` ganha a entrada em `[Unreleased]`, `### Changed`. Os dois são escritos antes do archive.

  **Feito:**
  - a seção `rt-inbox-espera-taskid` no fim do `02`, com:
    - as medidas e o universo pelos dois critérios;
    - os quatro guardas com as duas pontas;
    - a série do `rt-inbox`;
    - a segunda causa da #176, com a evidência em `309/evidencia-176/` (decisão do dono, item 4);
    - a #314;
    - o critério da D6;
  - a entrada em `[Unreleased]` / `### Changed` do `CHANGELOG`, a primeira da lista;
  - `check-docs` OK.

- [x] 4.2 (repo) O archive com sync. O requisito novo entra em `openspec/specs/inbox-message-orchestration/spec.md`.

  **Feito (24/11/2026):** `openspec archive rt-inbox-espera-taskid --yes`, com sync, depois do ok do dono no fechamento.

- [ ] 4.3 (repo) Com o ok do dono para commit e push: um commit, o push e o PR com `Closes #309` e `Refs #176`, sem fechar a #176. Depois, o card da #309 em In review.
- [ ] 4.4 (pós-merge, com o ok do dono) O comentário na #176 com o critério da D6.

  Depois do merge, a #296 é levada à base nova como foi com a `4e44dd3`: cópia de segurança, plano e o ok do dono. As cópias `296-exp-a` e `296-exp-b` saem quando a correção estiver validada.
