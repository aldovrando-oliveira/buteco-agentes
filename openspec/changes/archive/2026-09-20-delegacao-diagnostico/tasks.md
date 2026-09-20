Todas as tarefas rodam em **`apps/workers`**, salvo as de fechamento que medem a
suíte ou editam documentação de raiz — indicado em cada uma.

## 1. Baseline, antes de tocar em qualquer arquivo

- [x] 1.1 (`apps/workers`) Medir a baseline da suíte com a árvore limpa, e
  registrar **a carga na largada** (`uptime` e foto do `ps`), não só o resultado.
  O número registrado no fechamento da change anterior é **256/256** — com o
  escopo colado: medido em 20/09/2026, sobre `HEAD`, com **13** classes no
  `WorkerHostCollection`. Confirmar ou corrigir. Uma rodada feita enquanto a
  anterior ainda desmonta containers **não é medição** e é repetida.
- [x] 1.2 (`apps/api`, `apps/frontend`) Registrar a baseline das outras suítes
  **sem rodá-las**, a partir do fechamento anterior (`apps/api` 335/335,
  `apps/frontend` 921/921), como referência para a conferência de escopo — esta
  change não toca esses apps e a expectativa é que os números não se movam.
- [x] 1.3 (`apps/workers`) Reconferir contra a árvore, antes de escrever
  qualquer linha, as três afirmações de que o `design.md` depende: que `current`
  (`AgentDelegationToolSetResolver.cs:203`) não alcança o `catch` de `:220`; que
  `FailTaskAsync` é compartilhado pelos dois caminhos de falha e passa
  `FailAsync` **sem mensagem**; e que `a2a_tasks` já tem índice em `state`
  (`AppDbContext.cs:73`). Divergência aqui é achado e corrige o `design.md`
  (convenção 9), não o código.

## 2. Guardas do registro de desistência — vermelhos ANTES da correção

- [x] 2.1 (`apps/workers`) Acrescentar a `AgentDelegationConcurrencyTests` o
  provedor de log que captura o **estado estruturado** (pares chave/valor), como
  tipo privado aninhado, no molde dos dois precedentes da suíte. Filtrar por
  categoria — o precedente de `apps/inbox` registra que capturar tudo puxa o log
  de SQL do EF e torna outros testes flaky sob carga.
- [x] 2.2 (`apps/workers`) **G1** — instância única, Target nunca consumido: o
  registro da desistência carrega `Submitted` como último estado observado.
  Reusa o arranjo já existente de
  `SingleInstance_DelegationTimesOutGracefully_…`, sem alterá-lo.
- [x] 2.3 (`apps/workers`) **G2** — duas instâncias, Target consumido e lento
  além do timeout curto: o registro carrega `Working`. É o par de G1, e é o que
  prova que a distinção existe: os dois guardas diferem **pelo valor do campo**,
  não pela redação.
- [x] 2.4 (`apps/workers`) **G3** — Target em estado terminal de falha (ramo
  `:213`): o registro carrega os cinco identificadores (`SourceTaskId`,
  `SourceAgentId`, `TargetAgentId`, `TargetTaskId`, estado), que é o que permite
  ligar a falha ao log que o Target emitiu por conta própria.
- [x] 2.5 (`apps/workers`) **G4** — desistência sem nenhuma leitura
  bem-sucedida: o registro declara a ausência de observação, e **não** apresenta
  um estado. É a negativa de D1, com `it()` próprio.
- [x] 2.6 (`apps/workers`) Rodar G1..G4 contra `HEAD` e **registrar a causa
  atribuída de cada vermelho**, com a carga na largada. Os quatro compilam
  contra `HEAD` (só observam log) e devem reprovar pela **chave ausente**, nunca
  por texto de mensagem. Um vermelho por texto é guarda errado e volta para 2.1.

## 3. O registro de desistência

- [x] 3.1 (`apps/workers`) `IAgentDelegationToolSetResolver.ResolveAsync` recebe
  o `TaskId` do Source, com o XML doc dizendo para que ele serve (correlação com
  o log do Target) e não só que ele existe.
- [x] 3.2 (`apps/workers`) Propagar em `AgentExecutionService.cs:240` e no duplo
  `NullAgentDelegationToolSetResolver`. Conferir na **compilação** que não há
  outro sítio — a expectativa lida no código é de exatamente estes dois.
- [x] 3.3 (`apps/workers`) Em `WaitForTerminalStateAsync`, hoistar o último
  estado observado e o instante da observação para fora do `try`, e enriquecer
  os dois ramos (`:213` e `:226`) com os cinco identificadores. O campo se chama
  pelo que é — **último estado observado** —, e o caso "nunca observada" tem
  valor próprio (D1).
- [x] 3.4 (`apps/workers`) Registro de mecanismo no arquivo, onde quem investiga
  vai abrir primeiro: por que o campo é "último observado" e não "estado na
  desistência" (o intervalo de poll que cabe entre os dois), e por que
  `Failed` por contenção de lock é **indistinguível** de `Failed` por erro de
  provedor deste lado — com o caminho da correlação escrito.
- [x] 3.5 (`apps/workers`) Rodar G1..G4 e registrar verde, com a carga na
  largada.

## 4. O detector

- [x] 4.1 (`apps/workers`) `Options/TaskDiagnosticsOptions.cs` — só
  `SweepInterval`. A janela **não** entra aqui: é lida de
  `AgentDelegationToolOptions.Timeout` em runtime (D3). O comentário registra
  que o intervalo não tem base medida e que ancorar em
  `DebounceOptions.SweepInterval` foi recusado por ser outra grandeza.
- [x] 4.2 (`apps/workers`) `Diagnostics/NonTerminalTaskDetector.cs` — consulta
  `a2a_tasks` por estado não-terminal com `status_timestamp` além da janela,
  agrupando **por estado**, e devolve o relatório (contagens, mais velha, idade,
  janela). Sem `ILogger` aqui: a emissão é do serviço.
- [x] 4.3 (`apps/workers`) `Diagnostics/NonTerminalTaskDetectorService.cs` —
  `PeriodicTimer`, escopo por ciclo, `try/catch` **abrindo antes** da abertura
  do escopo e da consulta, emissão em `Warning` com achado e `Debug` sem, mais o
  registro de início em `Information` com janela e intervalo.
- [x] 4.4 (`apps/workers`) Registro de mecanismo no serviço: que ele é o
  primeiro componente periódico deste app; que a leitura é **global**, então `N`
  instâncias produzem `N` linhas idênticas e somá-las superestima por fator `N`;
  que a detecção sob demanda foi recusada porque sob contenção sustentada não há
  conclusão que a dispare; e que esta change será **substituída** pela
  `metricas-execucao-coleta`.
- [x] 4.5 (`apps/workers`) Registrar em `Program.cs` — `Configure<TaskDiagnosticsOptions>`,
  o detector e o hosted service. **Não mexer** na ordem nem no conteúdo das duas
  checagens de startup já existentes.

## 5. Guardas do detector

- [x] 5.1 (`apps/workers`) Criar `Diagnostics/NonTerminalTaskDetectorTests.cs`.
  Ela usa `WorkerInfrastructureFixture`, logo entra no `WorkerHostCollection` —
  é a **14ª** classe, e a tarefa 7.3 depende disso.
- [x] 5.2 (`apps/workers`) **T1** — `Submitted` além da janela é contada como
  `Submitted`. **T2** — `Working` além da janela é contada como `Working`, em
  contagem separada.
- [x] 5.3 (`apps/workers`) **T3** — store **povoado** (tasks terminais antigas e
  tasks não-terminais recentes) e nenhum achado. O par "sem item" da convenção 5,
  escrito para não reprovar por **vacuidade**: um guarda que rode sobre store
  vazio fica verde com e sem a implementação, porque nunca chega à comparação.
  Há asserção explícita da precondição de store povoado.
- [x] 5.4 (`apps/workers`) **T4** — task em estado terminal, por mais velha que
  seja, nunca é reportada.
- [x] 5.5 (`apps/workers`) **T5** — a janela acompanha
  `AgentDelegationToolOptions.Timeout`: mudar o timeout no host de teste move o
  corte, sem nenhum valor próprio a ajustar.
- [x] 5.6 (`apps/workers`) **T6** — a negativa de D3, com `it()` próprio: o
  relatório e a emissão carregam estado, idade e janela, e **não** classificam a
  task como travada/presa/defeituosa.
- [x] 5.7 (`apps/workers`) **T7** — sobre o host composto: a varredura tica e
  emite o registro de início com janela e intervalo, de forma que ausência de
  achado seja distinguível de ausência de varredura.
- [x] 5.8 (`apps/workers`) **T8** — consulta que falha num ciclo: a falha é
  registrada, o processo continua de pé, o consumo de `agent-tasks` segue e o
  ciclo seguinte acontece. Reintroduzir o defeito (mover a consulta para fora do
  `try`) e ver reprovar antes de manter a proteção — convenção 15, quinta forma.
- [x] 5.9 (`apps/workers`) Rodar T1..T8. Contra `HEAD` eles são marcados `—`,
  não 🔴: não compilam, e ler isso como vermelho de convenção 15 seria o erro que
  o `design.md` nomeia. O peso da convenção 15 é de G1..G4.

## 6. Conferência de escopo

- [x] 6.1 (raiz) Conferência de **escopo de arquivo** por `git status`/`git diff
  --stat`, contra a lista fechada abaixo. Suíte verde não prova que um arquivo
  não foi tocado — a change anterior deixou isso registrado. Caminhos
  permitidos, e **nenhum outro**:
  - `apps/workers/src/Buteco.Workers/AgentDelegations/AgentDelegationToolSetResolver.cs`
  - `apps/workers/src/Buteco.Workers/AgentDelegations/IAgentDelegationToolSetResolver.cs`
  - `apps/workers/src/Buteco.Workers/Agents/AgentExecutionService.cs`
  - `apps/workers/src/Buteco.Workers/Diagnostics/NonTerminalTaskDetector.cs`
  - `apps/workers/src/Buteco.Workers/Diagnostics/NonTerminalTaskDetectorService.cs`
  - `apps/workers/src/Buteco.Workers/Options/TaskDiagnosticsOptions.cs`
  - `apps/workers/src/Buteco.Workers/Program.cs`
  - `apps/workers/tests/Buteco.Workers.Tests/AgentDelegationConcurrencyTests.cs`
  - `apps/workers/tests/Buteco.Workers.Tests/Diagnostics/NonTerminalTaskDetectorTests.cs`
  - `apps/workers/tests/Buteco.Workers.Tests/Support/NullAgentDelegationToolSetResolver.cs`
  - `apps/workers/tests/Buteco.Workers.Tests/DelegationToolNameSlugifierTests.cs`
    — **acrescentado à lista durante a implementação, e não em silêncio:** é o
    quinto sítio de chamada de `ResolveAsync`, que o `grep` pela interface não
    alcançava (ele instancia a classe concreta) e que só a compilação achou.
    Consequência direta da mudança de assinatura da task 3.1, não escopo novo.
    Ver a nota de correção no `design.md`.
  - `apps/workers/tests/Buteco.Workers.Tests/Support/WorkerHostCollection.cs`
    — **acrescentado à lista durante a implementação:** é onde mora o critério de
    leitura da suíte, e a task 7.3 obriga esta change a recalibrá-lo por ter
    acrescentado a 14ª classe (convenção 22). Alteração só de documentação, sem
    uma linha de código.
  - `openspec/changes/delegacao-diagnostico/**`
  - `CHANGELOG.md`, `01-ARQUITETURA_E_CONVENCOES.md`, `02-HISTORICO_E_STATUS.md`
    (fechamento)
- [x] 6.2 (raiz) Afirmar explicitamente **zero** arquivo tocado em `apps/api`,
  `apps/inbox`, `apps/frontend`, `libs/`, `deploy/`, `docs/`,
  `docker-compose*.yml` e em qualquer pasta `Migrations/`.
- [x] 6.3 (`apps/workers`) Conferir **à mão** que `Program.cs` registra o hosted
  service — nenhum teste desta suíte prova isso, porque os testes montam o host
  próprio (D6). Remover a linha e ver T1..T8 continuarem verdes é a demonstração
  de que a conferência é necessária; refazer o registro depois.

## 7. Fechamento

- [x] 7.1 (`apps/workers`) Suíte completa, com a carga na largada registrada.
  Projetado: **268** (baseline 256 + 12). Diferença é achado a explicar, não
  número a ajustar.
- [x] 7.2 (raiz) Rodar `openspec validate --all` — a base já teve uma spec viva
  inválida por um dia sem ninguém ver.
- [x] 7.3 (`apps/workers`) **Recalibrar o critério de leitura da suíte para 14
  classes de host** e registrar o número novo com o escopo colado. Convenção 22:
  recalibrar é tarefa da change que muda o estado, e esta referência já quebrou
  **duas vezes** por ninguém ter feito isso. Registrar também se a 14ª classe
  mudou o tempo de suíte.
- [x] 7.4 (raiz) **Comparar** o entregue com a projeção do `design.md` —
  **apenas comparar**, a projeção já está escrita. Diffstat **decomposto**
  (produção × teste, lógica × comentário, criado × modificado), nunca o
  headline do commit. Comparar só o escopo que estava projetado; escopo
  acrescentado durante a implementação entra como linha à parte.
- [x] 7.5 (raiz) Registrar, no `02`, se as duas direções de erro nomeadas
  aconteceram — e, se o desvio veio de um terceiro lugar pela segunda vez
  seguida, isso é o achado da oitava medição e vale mais que o número.
- [x] 7.6 (raiz) `CHANGELOG.md` e seção própria no `02`, com: a série que esta
  change passa a produzir, como lê-la (o fator `N` das linhas idênticas **e qual
  coluna responde o `C`** — `Submitted` envelhecido aproxima, `Working`
  envelhecido subestima por construção, ver D10), e o gatilho de descarte na
  `metricas-execucao-coleta`. É esse parágrafo que a `replicas-de-worker` vai
  ler, não o `design.md`.
- [x] 7.7 (raiz) Registrar como item aberto, com gatilho **e** posição: a
  extração do provedor de log estruturado para `Support/` — terceira cópia
  atingida nesta change (convenção 2), posição na change que precisar da quarta.
- [x] 7.8 (raiz) Atualizar `## Próximo passo` no `02`: posição 3 aplicada, a
  fila anda para `frontend-mensagem-recusa-ciclo`. **Sem commit** — o trabalho
  fica na árvore e o commit é decisão de quem revisa.
