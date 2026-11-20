Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-303.sh` (cópia do `guarda-296.sh`, com o mesmo lock das suítes) reintroduz o defeito nomeado. A mutação toca só o trecho nomeado e **nunca** restaura um diretório ao `HEAD`;
2. a classe roda **sozinha**, nas duas pontas: no worktree da baseline (host por caso) e no da change (host por classe);
3. a árvore volta **pela cópia** feita antes da mutação, com o `sha256` conferido contra o de antes.

A tarefa registra a lista de casos reprovados nas duas pontas, que tem de ser **a mesma**, a mensagem de cada um e o "voltou: <arquivo> (<sha>)". Uma mutação que quebra a compilação ou reprova por outra asserção não vale: é descartada e refeita. Saídas em `~/.cache/buteco-agents/303-guardas/<tarefa>/`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-303.sh` (cópia do `suite-302.sh`), **uma por vez, com o lock** (`suites.lock`):

- suíte: só `api`. Fora: `inbox`, `connectors`, `workers`, `rt-*` e `frontend`. Nenhum código deles muda, e nenhum acordo entre apps passa por estes testes;
- regra assimétrica (#181). Reprovação por caso que esta change mudou não é inconclusiva: "reprovada pelo X (a → b), corrigida";
- **o registro por rodada que a #302 introduz:** as threads efetivas, o veredito, a duração e a CPU mínima;
- antes de cada rodada, o load de 1 e de 5 min abaixo do limite do script, e a VM do Podman com o relógio e a memória conferidos. Nunca `dotnet build` junto de uma suíte.

**Parada** (convenção 9):

- ~~**parar e voltar ao dono** no ponto de decisão da 0.5~~: cumprida. A 0.5 parou e voltou ao dono; a parte **[KDoc]** saiu por decisão dele (design, D0);
- **parar e voltar ao dono** se, na rodada válida da 0.3, `KnowledgeBaseCatalogTests` não for a maior janela de classe da suíte (decisão do dono, 20/11/2026);
- **parar** se, num guarda da tarefa 2, a lista de reprovação da change for diferente da baseline: um guarda que passou a aprovar com o defeito é perda de cobertura;
- **parar** se algum caso precisar sair, mudar de nome ou de asserção para passar;
- **parar e voltar ao dono** se a tarefa 3 mostrar resultado dependente de ordem que a baseline não mostra;
- **seguir e registrar** quando a correção só acrescentar teste do mecanismo, ou só tocar comentário ou documentação.

**Nada de commit** até o dono pedir. Um commit só, depois do archive.

**O que não muda:**

- nenhum arquivo de `apps/api/src`;
- `WithDispatchHost`, a segunda instância de `IndexingDispatch` e os hosts de `KnowledgeBaseCatalogTests.Deletion` (design, D3);
- nenhum outro app;
- nenhum projeto de `tests/`.

## Casos que saem ou mudam de asserção (a lista do topo)

Nenhum sai e nenhum muda de asserção. **Mudam de forma, com a asserção igual:**

- os 17 pontos de chamada de `WithConnectors` (16 em `KnowledgeBaseCatalogTests.SyncedCreation` e 1 em `…Deletion`; a proposta dizia 18, corrigido na 0.4): o host vem do cache da fixture, o cliente continua por caso;
**[KDoc]** marca o que saiu da change na 0.5, por decisão do dono (design, D0): as 4 chamadas de `WithFailingDocumentWrites` ficam como estão.

Cada caso dessas classes é conferido por nome na 4.3. Um caso fora desta lista que precise mudar é parada.

## 0. Preparação e baseline

- [x] 0.1 (scripts locais) `suite-303.sh` (cópia do `suite-302.sh`, só a suíte `api`), `teste-303.sh` (a rodada filtrada com o lock, de uma classe, nas duas pontas, com o veredito e a CPU) e `guarda-303.sh` (cópia do `guarda-296.sh`). Conferir o guarda com uma mutação nula ("defeito não aplicado") e uma real num comentário de `apps/api/src`; o arquivo volta com o mesmo sha.
  **Feito:** os três, mais `build-303.sh`, `diag-303.py` e `janelas-303.py`. O `teste-303.sh` passou a registrar a CPU mínima e o veredito, que o `teste-302.sh` não registrava; o lock é esperado nos três. Mutação nula: "defeito não aplicado", arquivo intocado. Mutação real num comentário de `RejectionMetricsWriter.cs`: 6/6 aprovados, voltou com o sha256 de antes (`bec21901bdf8`), `git status` vazio.
- [x] 0.2 (repo) O worktree da baseline, `.claude/worktrees/api-host-derivado-por-classe-baseline`, destacado no `origin/main` do início do apply, criado por `git worktree add --detach`, sem mexer na árvore da change nem na `main`. Registrar se a #302 já está nesse commit e com quantas threads a suíte roda (design, D8).
  **Feito:** destacado no `ef3e9f1` (merge do #304). A #302 (`552b6c3`) está nele, e o `.trx` das rodadas registra `parallel test collections = on [6 threads]`. A branch da change, criada sobre o `c9733ce` sem commit próprio, foi avançada para o `ef3e9f1` por fast-forward antes do apply.
- [x] 0.3 (apps/api) A baseline no worktree da 0.2, em `~/.cache/buteco-agents/303/baseline/`:
  - rodadas da suíte `api` **até uma válida**, com o relatório por nome (`nomes-trx.py`). As inconclusivas entram na série do `apps/api` (convenção 19), com a da 0.5;
  - da rodada válida, antes de codificar: a janela de `KnowledgeBaseCatalogTests`, a soma dos 41 casos de `SyncedCreation` e as cinco maiores janelas de classe (ponto de decisão da lista de parada);
  - **duas** rodadas de `KnowledgeBaseCatalogTests` sozinha;
  - em todas, o registro por rodada.

  **Feito** (`303/baseline/`):
  - suíte, `suite-r1`: `ef3e9f1`, **6 threads** (`.trx`), **aprovada**, 1860/1861 (1 ignorado), 462 s, CPU mínima 41. Válida na primeira tentativa. `nomes.txt`: 1861 resultados, 1860 `Passed`, 1 `NotExecuted`. Janelas em `suite-r1/janelas.txt`;
  - ponto de decisão: `KnowledgeBaseCatalogTests` é a maior janela (197,8 s, de +257 s ao fim da suíte), a soma dos 41 casos de `SyncedCreation` é 181,1 s, e as seguintes são `KnowledgeDocumentCatalogTests` 90,6 s, `ExecutionMetricsMigrationTests` 55,2 s, `PasswordResetLinkCommandTests` 45,9 s e `IdentityKeyRingGateTests` 43,0 s. Segue;
  - `KnowledgeBaseCatalogTests` sozinha, `kb-r1` e `kb-r2`: 106/106 nas duas, 73 s e 71 s pelo xUnit (85 s e 83 s de parede), CPU mínima 100, 6 threads;
  - **série do `apps/api` nesta change até aqui:** 2 rodadas, 1 inconclusiva (a da 0.5), 6 threads.
- [x] 0.4 (apps/api) A lista dos casos que passam por um host derivado (os 41 de `SyncedCreation`), pelos pontos de chamada, em `303/baseline/casos-com-host-derivado.txt`. É a lista da 3.2.

  **Feito:** **17 pontos de chamada em 17 métodos, 39 casos** (com as linhas de `Theory`), em `casos-com-host-derivado.txt` (ponto de chamada) e `casos-com-host-derivado-nomes.txt` (nome do `.trx`). A proposta dizia 18 pontos e 41 casos. São 16 pontos em `SyncedCreation` (38 casos), porque 3 dos 19 métodos dela usam o host da fixture (`CreateSynced_FolderAlreadyUsed_Returns409EvenWithoutConnectorsConfigured`, `CreateSynced_WithoutConnectorsConfigured_Is503AndOtherRoutesKeepWorking` e `RealComposition_ConnectorsClientHasThe35SecondLimit`). O 17º é `Deletion.cs:208` (`DeleteSyncedBase_FreesTheFolderForANewBase`), que passa ao host compartilhado sem mudar de forma. Os 41 continuam valendo como soma de `SyncedCreation`.
- [x] 0.5 (apps/api) **Diagnóstico do caso de 125 s, antes de qualquer código** (design, D0). No worktree da baseline, uma instrumentação **temporária** em `SyncWrites_WhenTheBaseIsDeletedBetweenReadAndSave_Return404`: `Stopwatch` e `Console.WriteLine`, no molde da linha `[janela]`. **Por iteração** (os 5 caminhos), ela separa:
  1. o boot do host derivado: do `WithFailingDocumentWrites` até o primeiro `CreateClient` voltar;
  2. o corpo, em partes: semear a base, o `UpsertOkAsync` prévio, a escrita com o interceptor (com o tempo da exclusão da base de dentro do `SaveChanges`), e as contagens;
  3. o descarte do host.

  Duas formas, em `~/.cache/buteco-agents/303/diagnostico/`: o caso sozinho (duas rodadas), e a classe inteira dentro de uma rodada da suíte. A instrumentação sai depois, conferida por `git status` vazio no worktree da baseline.

  **Ponto de decisão**, com o critério da D0: o host é o custo dominante se boots + descartes dos 5 hosts forem **pelo menos metade** do tempo do caso, nas duas formas.
  - **Se for:** segue tudo, inclusive o **[KDoc]**.
  - **Se não for:** **parada.** A parte **[KDoc]** (1.3, 1.5, 2.3, 2.4, 2.6 e a medição de `KnowledgeDocumentCatalogTests` da 4.1) vira "não se aplica", e o achado volta ao dono antes do resto do apply.

  Em qualquer dos dois casos, o resultado e onde estão os ~85 s vão para o design, em "O que o apply mudou".

  **Resultado** (baseline `ef3e9f1`, 6 threads lidas do `.trx`; saídas em `303/diagnostico/`, `resumo.txt`): boots + descartes foram **75% e 76%** do caso sozinho (8,1 s e 6,9 s, as duas aprovadas, CPU 100) e **83%** dele dentro da suíte (19,7 s de 23,7 s). Pelo critério, o host **é** o custo dominante. A exclusão de dentro do `SaveChanges` levou 6–30 ms (só a primeira iteração sozinha, 370–520 ms): a espera de lock da hipótese não apareceu. Os ~85 s não se reproduziram com 6 threads; eram do paralelismo de 12.
  - **A rodada da suíte foi inconclusiva:** 1859/1861, 1 reprovado fora do escopo (`SendMessageProviderRejectionTests.SendMessage_ForAgentDeletedAfterTheServerWasResolved_RecordsAgentNotFound_ThoughTheProtocolAnswersError`), CPU mínima 44, 416 s, 6 threads. Entra na série do `apps/api`. O binário tinha a instrumentação do caso.
  - **Decisão do dono (20/11/2026), apesar do critério:** a parte **[KDoc]** sai. Com 6 threads o caso leva 24 s e a janela da classe 76 s; o ganho restante (25–30 s, fora do caminho crítico) não paga o mecanismo e os três guardas. A classe fica como candidata nos itens em aberto do `02` (4.5).
  - Instrumentação removida: `git status` vazio no worktree da baseline, o arquivo com o sha256 do `HEAD` (`ad2e9898ee42`).

## 1. O mecanismo (apps/api, só testes)

- [x] 1.1 (apps/api) `ApiFactoryFixture`: o cache de host derivado por chave, que compõe na primeira chamada e devolve o mesmo nas seguintes. Os hosts compartilhados são descartados explicitamente no `DisposeAsync`, antes do banco (design, D1).
- [x] 1.2 (apps/api) `FakeConnectorsHttpMessageHandler`:
  - a contagem de requisições em andamento (`Interlocked`, `try/finally` em volta do `SendAsync`) e o nome do caso que pediu o host;
  - o `Reset(caso)`, que zera requisições, `Respond` e barreira, e lança `InvalidOperationException` com a contagem e **o nome do caso anterior** se houver requisição em andamento (design, D4).

  `LogEventCounter.Reset()` em `KnowledgeSyncTestSeed.cs`.
- [x] 1.3 **[KDoc]** **Não se aplica:** a parte **[KDoc]** saiu da change na 0.5 (decisão do dono, 20/11/2026). (apps/api) `Support/SwitchableSaveChangesInterceptor.cs`: delega o `SavingChangesAsync` ao interceptor da concessão, ou não faz nada sem concessão. A concessão (`IAsyncDisposable`) põe o interceptor ao entrar e volta ao neutro ao sair, e recusa uma segunda concessão aberta (design, D2 e D4).
- [x] 1.4 (apps/api) `KnowledgeBaseCatalogTests.SyncedCreation`:
  - `WithConnectors(clientTimeout, [CallerMemberName] caso)` pelo cache da 1.1, com a chave `connectors:<clientTimeout>`;
  - o handler e o contador do host, repostos a cada pedido, com o nome do caso;
  - o cliente por caso;
  - o descarte do `ConnectorsHost` descarta só o cliente e **confere a contagem em andamento**: se houver requisição, lança com a mensagem da primeira linha da D4, e a reprovação cai no caso que vazou.
- [x] 1.5 **[KDoc]** **Não se aplica:** a parte **[KDoc]** saiu da change na 0.5 (decisão do dono, 20/11/2026). (apps/api) `KnowledgeDocumentCatalogTests`: `WithFailingDocumentWrites(interceptor)` devolve a concessão sobre o host compartilhado. As 4 chamadas mudam de forma (lista do topo), e as asserções não.
- [x] 1.6 (apps/api) `dotnet build` do `apps/api/Api.sln` sem aviso novo, comparado à baseline (`--no-incremental` nas duas pontas).
  **Feito** (`303/build/`): 19 avisos e 0 erros nas duas pontas; os mesmos 4 avisos distintos (arquivo e código, sem a linha), `diff` vazio.

## 2. Convenção 15: os guardas com o host compartilhado (design, D5)

- [x] 2.1 **(guarda) d9.** `KnowledgeBaseEndpoints`: `"folder-in-use"` trocado. `KnowledgeBaseCatalogTests` sozinha nas duas pontas, com a mesma lista de reprovação (no estudo, 3 casos).
  **Feito** (`303-guardas/2.1/`): `"folder-in-use"` → `"folder-taken"`. **A mesma lista nas duas pontas, 3 casos:** `CreateSynced_FolderAlreadyUsed_Returns409WithTheBaseThatUsesIt` (`existingIsActive: True` e `False`) e `DeleteSyncedBase_FreesTheFolderForANewBase` (este, de `Deletion`, no host compartilhado), todos por "Assert.Equal() Failure: Strings differ". A classe levou 73 s na baseline e 10 s na change. Voltou: `KnowledgeBaseEndpoints.cs` (`d3f22fdc4d97`) nas duas.
- [x] 2.2 **(guarda) d10.** `CreateKnowledgeBaseCommandHandler`: sem a checagem de pasta em uso antes da chamada ao connectors. `KnowledgeBaseCatalogTests` sozinha nas duas pontas, com a mesma lista (no estudo, 4 casos, entre eles `CreateSynced_FolderAlreadyUsed_Returns409EvenWithConnectorsDown`).
  **Feito** (`303-guardas/2.2/`): sem o bloco `FindFolderOwnerAsync` + `FolderInUse` antes de `DescribeFolderAsync`. **A mesma lista nas duas pontas, 4 casos:** `CreateSynced_FolderAlreadyUsed_Returns409EvenWithConnectorsDown`, `…Returns409EvenWithoutConnectorsConfigured` e `…Returns409WithTheBaseThatUsesIt` (`True` e `False`), todos por "Assert.Equal() Failure: Values differ". 74 s na baseline, 10 s na change. Voltou: `CreateKnowledgeBaseCommandHandler.cs` (`931369c90176`) nas duas.
- [x] 2.3 **[KDoc]** **Não se aplica:** a parte **[KDoc]** saiu da change na 0.5 (decisão do dono, 20/11/2026). **(guarda) d11.** `KnowledgeBaseWriteFailures.MayBeDeletedBase`: sem o ramo da violação das duas FKs para a base. `KnowledgeDocumentCatalogTests` sozinha nas duas pontas. A lista tem de incluir `SyncWrites_WhenTheBaseIsDeletedBetweenReadAndSave_Return404`, que roda as 5 concessões no mesmo host.
- [x] 2.4 **[KDoc]** **Não se aplica:** a parte **[KDoc]** saiu da change na 0.5 (decisão do dono, 20/11/2026). **(guarda) d12.** `KnowledgeBaseWriteFailures.MayBeDeletedBase`: sem o `IsDeadlock`. `KnowledgeDocumentCatalogTests` sozinha nas duas pontas. A lista tem de incluir o caso do `DeadlockOnceInterceptor`.
- [x] 2.5 **(guarda) Estado em voo reprova, nas duas linhas** (`api-scaffold`, cenários "O caso que deixa estado em andamento reprova no próprio fim" e "Estado em andamento que chega ao caso seguinte reprova esse caso com o nome do anterior"). Só na change: na baseline não há host compartilhado. Um caso de `SyncedCreation`, alterado de propósito para deixar uma requisição ao connectors falso em andamento ao terminar (um `Respond` que ignora o cancelamento e só termina depois do fim do caso, disparado sem esperar):
  1. **primeira linha:** com a alteração, **o próprio caso** reprova no descarte do `ConnectorsHost`, com a mensagem "o caso <nome> terminou com … em andamento";
  2. **segunda linha:** com a alteração **e** a conferência do descarte desligada por uma segunda mutação, o caso seguinte da mesma chave reprova no `Reset()`, com a mensagem que **cita o nome do caso alterado**.

  As duas mutações voltam pela cópia, com o `sha256` conferido. Registrar as duas mensagens e qual caso reprovou em cada linha.

  **Feito** (`303-guardas/2.5/`; mutações em `303-guardas-25-mut.py`). O caso alterado é `CreateSynced_StoresFolderNameAndUrlFromConnectors_NotFromTheBody`. **Depois** da última asserção dele, a alteração dispara sem esperar um cadastro cuja chamada ao connectors falso ignora o cancelamento e dura 10 s, e espera a requisição entrar no handler.
  - **Primeira tentativa, descartada:** o vazamento vinha **antes** das asserções. Na linha 2 o caso alterado reprovou por uma asserção própria ("Values differ"; pela leitura, o `Assert.Equal(1, host.Connectors.RequestCount)`, que o vazamento aumenta), e não pela reposição. Mutação que reprova por outra asserção não vale; refeita.
  - **Primeira linha:** o caso alterado reprova no descarte do `ConnectorsHost`, com "o caso CreateSynced_StoresFolderNameAndUrlFromConnectors_NotFromTheBody terminou com 1 requisição(ões) ao connectors falso em andamento". Os 36 casos seguintes que pedem o host também reprovam enquanto a requisição dura (37/106 no total), cada um no `Reset` e citando o caso alterado.
  - **Segunda linha**, com a conferência do descarte desligada: o caso alterado **aprova**. O caso seguinte, `CreateSynced_WithInvalidShape_IsRefusedBeforeAnyConnectorsCall`, reprova no `Reset` com "1 requisição(ões) ao connectors falso ainda em andamento, deixada(s) pelo caso CreateSynced_StoresFolderNameAndUrlFromConnectors_NotFromTheBody, ao repor o handler para o caso CreateSynced_WithInvalidShape_IsRefusedBeforeAnyConnectorsCall". 36/106, todos citando o caso alterado.
  - Voltou: `KnowledgeBaseCatalogTests.SyncedCreation.cs` (`a44a649046b4`, o da change), nas duas linhas.
  - **Refeito depois do ajuste da mensagem da primeira linha** (decisão do dono, 20/11/2026; `303-guardas/2.5-ajuste/`). A mensagem agora diz que a exceção do descarte pode ter encoberto uma asserção, e manda rodar o caso isolado. Linha 1: o caso alterado reprova com "o caso CreateSynced_StoresFolderNameAndUrlFromConnectors_NotFromTheBody terminou com 1 requisição(ões) ao connectors falso em andamento. Se o caso também reprovou por asserção, esta exceção do descarte a encobriu: rode o caso … isolado para vê-la." (37/106, todos citando o caso). Linha 2: o caso alterado aprova, e o imediatamente seguinte reprova no `Reset`, com o nome dele (36/106). Voltou com o mesmo sha256. `KnowledgeBaseCatalogTests` sozinha, de novo, depois do ajuste (`303/ajuste/kb`): 106/106, 9 s, CPU 100, 6 threads.

- [x] 2.6 **[KDoc]** **Não se aplica:** a parte **[KDoc]** saiu da change na 0.5 (decisão do dono, 20/11/2026). **(guarda) d13.** `UpdateKnowledgeDocumentCommandHandler`: o primeiro `catch (DbUpdateConcurrencyException)` devolve `ConcurrentWrite()` direto, sem a releitura depois do conflito. `KnowledgeDocumentCatalogTests` sozinha nas duas pontas, com a mesma lista de reprovação. A lista tem de incluir o caso do `DeleteThenConflictOnceInterceptor` em `Concurrency`.

## 3. Ordem e isolamento (apps/api)

- [x] 3.1 (apps/api) Cada classe **inteira duas vezes** no worktree da change: o mesmo resultado nas duas, e o mesmo da baseline (0.3).
  **Feito** (`303/change/kb-r1`, `kb-r2`): `KnowledgeBaseCatalogTests`, 106/106 nas duas, 11 s e 10 s pelo xUnit (23 s e 21 s de parede), CPU mínima 100, 6 threads. Por nome (`nomes-trx.py`, resultado e nome), as duas iguais às duas da baseline: os mesmos 106, todos `Passed`. A outra classe saiu com o **[KDoc]**.
- [x] 3.2 (apps/api) **Isolado**, cada caso da lista da 0.4 (filtro por nome), na change: o mesmo veredito que dentro da classe. Um caso que passa na classe e reprova isolado, ou o contrário, é dependência de ordem, e é parada.
  **Feito** (`303/change/isolados/`): os 17 métodos da lista da 0.4, um filtro por método (`FullyQualifiedName=` exato), **39 casos, todos aprovados**, o mesmo veredito que dentro da classe (3.1). As linhas de uma `Theory` rodam juntas, no mesmo filtro. Nenhuma dependência de ordem.

## 4. Medição e fechamento

- [x] 4.1 (apps/api) **Cada classe sozinha**, duas rodadas na change, intercaladas com as duas da baseline da 0.3 quando a máquina permitir, ou repetidas na baseline (design, D7). Só `KnowledgeBaseCatalogTests` (a medição de `KnowledgeDocumentCatalogTests` saiu com o **[KDoc]**).
  **Feito** (`303/medicao/`), intercalado baseline, change, baseline, change: `KnowledgeBaseCatalogTests` sozinha, 106/106 nas quatro, CPU mínima 100, 6 threads.

  | Rodada | Baseline (host por caso) | Change (host por classe) |
  |---|---|---|
  | 1 | 73 s (85 s de parede) | **10 s** (22 s) |
  | 2 | 71 s (82 s) | **10 s** (22 s) |

  São −61 a −63 s pelo xUnit, contra os −62 a −66 s da prova de conceito.
- [x] 4.2 (apps/api) **A suíte inteira**, intercalada, em `~/.cache/buteco-agents/303/fechamento/`: change; baseline (worktree da 0.2); change. Cada uma com o registro por rodada.
  **Feito**, a suíte intercalada, uma por vez, com o lock e o registro por rodada:

  | Rodada | Worktree | Threads | Duração | CPU mín. | Veredito | Janela de `KnowledgeBaseCatalogTests` | Soma da `SyncedCreation` | Maior janela |
  |---|---|---|---|---|---|---|---|---|
  | `change-r1` | change | 6 | **372 s** | 51 | APROVADA, 1860/1861 | **27,9 s** | 5,9 s | `KnowledgeDocumentCatalogTests` 90,5 s |
  | `baseline-r1` | `ef3e9f1` | 6 | 464 s | 46 | APROVADA, 1860/1861 | 181,6 s | 163,1 s | `KnowledgeBaseCatalogTests` 181,6 s |
  | `change-r2` | change | 6 | **411 s** | 34 | APROVADA, 1860/1861 | **27,0 s** | 6,5 s | `KnowledgeDocumentCatalogTests` 78,0 s |

  Com a baseline da 0.3 (462 s), a baseline ficou em 462–464 s e a change em 372–411 s. **`KnowledgeBaseCatalogTests` saiu do caminho crítico:** a maior janela da suíte passou a ser `KnowledgeDocumentCatalogTests`.
- [x] 4.3 (apps/api) A comparação por nome (`nomes-trx.py`) de cada rodada válida da change contra a baseline da 0.3: os mesmos nomes, nenhum sai nem muda.
  **Feito:** `change-r1` e `change-r2` (e `baseline-r1`) contra `baseline/suite-r1`, por resultado e nome: **0 linhas diferentes**. Os mesmos 1861 nomes, 1860 aprovados e 1 ignorado.
- [x] 4.4 `openspec validate api-host-derivado-por-classe --strict` passa, com o resultado registrado.
  **Feito:** `openspec validate api-host-derivado-por-classe --strict`: "Change 'api-host-derivado-por-classe' is valid" (20/11/2026, depois da 4.5).
- [x] 4.5 (docs) **Antes do archive**:
  **Feito:** a entrada no `02` (no fim, depois da #302), com a série "`apps/api`: 5 rodadas, 1 inconclusiva, 6 threads"; os dois itens em aberto (`KnowledgeDocumentCatalogTests`, com o gatilho de a janela passar de um terço da suíte, decisão do dono, não atingido no fechamento; e os hosts de `Deletion`, sem gatilho); a linha do `CHANGELOG`; e "O que o apply mudou" no design.
  - **a entrada da change no `02-HISTORICO_E_STATUS.md`:**
    - as medições do design e as da 4.1 e da 4.2;
    - a linha "`apps/api`: N rodadas, M inconclusivas, K threads" (registro da #302);
    - os guardas 2.1, 2.2 e 2.5, com a lista de reprovação das duas pontas, e a 0.5 com a decisão do dono;
    - a 3.1 e a 3.2;
    - a comparação por nome;
  - **o `CHANGELOG.md`:** uma linha em `[Unreleased]`;
  - **o design corrigido** com o que o apply achou, numa seção "O que o apply mudou" (convenção 9);
  - **nos itens em aberto do `02`:** os hosts de `KnowledgeBaseCatalogTests.Deletion` como candidatos (design, D3), sem gatilho. `KnowledgeDocumentCatalogTests` como candidata, com os números da 0.5 (24 s do caso, 76 s de janela, 25–30 s de ganho estimado) e por que saiu desta change. Os ~85 s não viram issue: não se reproduziram com 6 threads.
- [x] 4.6 O worktree da baseline removido, conferido por `git worktree list`; as saídas ficam em `~/.cache/buteco-agents/303/`.
  **Feito:** `git worktree remove` com o `git status` vazio, em `ef3e9f1`. O `git worktree list` não o mostra mais. As saídas ficam em `~/.cache/buteco-agents/303/` e `303-guardas/`.
- [x] 4.7 `/opsx:archive` com sync: o requisito novo entra no `openspec/specs/api-scaffold/spec.md`. Depois, o commit único e o PR, **quando o dono pedir** (convenção 24: archive antes do push; `Closes #303`).
  **Feito** (20/11/2026): archive com sync. O requisito "Host derivado compartilhado entre casos da suíte do apps/api repõe o estado e acusa vazamento" entrou no fim de `openspec/specs/api-scaffold/spec.md`, com os três cenários. O commit único e o PR, a pedido do dono.
