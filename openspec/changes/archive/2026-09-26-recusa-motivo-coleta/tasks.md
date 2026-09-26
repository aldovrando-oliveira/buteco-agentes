## 0. Abertura, antes de qualquer código

- [x] 0.1 **(fora do repositório)** **Feito em 25/09/2026:** a **#51** saiu de
      `Ready` e está em `In progress` no board (projeto 3, conferido depois da
      escrita) — o gatilho é a change aberta, não o primeiro commit (convenção 24).
- [x] 0.2 **(fora do repositório)** **Feito em 26/09/2026.** Os sete achados da
      leitura e do apply têm número: **quatro issues novas**, todas em `Backlog`, e
      **três absorvidos** em issue existente — dois registros sobre o mesmo defeito
      fazem o próximo leitor corrigir metade e encerrar o assunto.

      | item | destino | labels |
      |---|---|---|
      | (a) spec do card de Motivos + (b) texto do card de Falhas + o **consumo do dado novo pela tela**, que faltava na lista a–g | **#75** (nova) | `tipo: feature`, `app: frontend` |
      | (c) razão ao cliente A2A | **#76** (nova) | `tipo: feature`, `app: api`, `aguardando gatilho` |
      | (d) `-32603` de agente apagado | **#77** (nova) | `tipo: bug`, `app: api`, `aguardando gatilho` |
      | (f) captura de log em três cópias | **#78** (nova) | `tipo: débito técnico`, `app: api` |
      | (e) flake de `apps/inbox` | **#61**, comentário | — |
      | (g) instante do regime `rejection` | **#50**, comentário | — |
      | o toque no `01` desta change | **#60**, comentário | — |

      **O achado que estava faltando na lista a–g** e entrou na #75: a #52 precisa de
      **change de seguimento** para consumir `rejectedAtEntryCount` e
      `rejectionsByReason` — estava no `proposal.md` e não tinha virado item do `02`.
      **Nada foi recusado.** O que NÃO virou issue, pelo critério "issue é trabalho,
      `02` é régua": a régua recalibrada para 41 classes, a quarta causa de desvio da
      convenção 18 e a sexta ocorrência da convenção 22.
      No PR, a #75 e as demais entram como `Refs`, nunca `Closes`.
- [x] 0.3 **As três Open Questions estão fechadas pelo dono (25/09/2026)**, antes
      de qualquer código, e as respostas vivem onde a implementação as lê:
      **quatro** valores de vocabulário (D4, com a alternativa recusada e o que ela
      custava), **`rejectedAtEntryCount` mantém o nome** (D5), e a **41ª classe de
      contêiner não bloqueia** (seção da projeção, com a régua recalibrada). Nada
      nesta change depende de resposta pendente.

## 1. A coleta em `apps/api`

- [x] 1.1 **(`apps/api`)** Criar `RejectionMetrics/RejectionMetricsValues.cs` com o
      vocabulário fechado como `const string` — `AgentNotFound`, `AgentInactive`,
      `ProviderOrModelMissing`, `ProviderNotConfigured` —, no molde de
      `ExecutionMetricsValues`, com o comentário dizendo que o valor sai do sítio
      do código e nunca de texto de mensagem (D3).
- [x] 1.2 **(`apps/api`)** Criar `RejectionMetrics/Entities/TaskRejection.cs`:
      `TaskId`, `AgentId`, `Reason`, `RejectedAt`, todos obrigatórios, sem
      provedor e sem modelo — com o registro de por que a tabela é própria e por
      que não tem FK (D2).
- [x] 1.3 **(`apps/api`)** Mapear em `AppDbContext`: `ToTable("task_rejections")`,
      chave em `TaskId`, índices em `RejectedAt` e `AgentId`, **nenhuma** FK.
- [x] 1.4 **(`apps/api`)** Gerar a migração `AddTaskRejections` e conferir no
      arquivo gerado que **nenhuma** FK e **nenhuma** alteração de tabela existente
      entrou.
- [x] 1.5 **(`apps/api`)** Criar `RejectionMetrics/RejectionMetricsWriter.cs`: um
      método, **não lança**, `LogWarning` com o `TaskId` na falha, escopo próprio
      pelo `IServiceScopeFactory` (D8).
- [x] 1.6 **(`apps/api`)** `EnqueueingAgentHandler`: `GetAgentStateAsync` passa a
      devolver `AgentState?`, o `null` vira `AgentNotFound`, e os quatro caminhos
      chamam o escritor **depois** do `RejectAsync` (D4, D8).

## 2. Guardas da coleta (convenção 15 — cada um reprova contra `HEAD` primeiro)

- [x] 2.1 **(`apps/api`)** Em `AgentDeactivationTests`: recusa por agente inativo
      grava `AgentInactive`.
- [x] 2.2 **(`apps/api`)** Em `SendMessageProviderRejectionTests`: os dois casos de
      provedor gravam `ProviderOrModelMissing` e `ProviderNotConfigured`, e a
      asserção é o **valor de cada um**, nunca "são diferentes entre si" — escrito
      assim ficaria verde com as três causas gravando o mesmo valor (quinta forma
      da convenção 15).
- [x] 2.3 **(`apps/api`)** Em `SendMessageProviderRejectionTests`: agente apagado
      direto no banco grava `AgentNotFound`, e **não** `AgentInactive`.
- [x] 2.4 **(`apps/api`)** **Par negativo**, em `A2ATaskLifecycleTests`: task aceita
      e publicada **não** produz linha em `task_rejections`.
- [x] 2.5 **(`apps/api`)** `RejectionMetricsWriterTests` (sem contêiner): escritor
      apontado para banco inalcançável não lança, registra aviso, e a task segue
      recusada.
- [x] 2.6 **(`apps/api`)** `RejectionMetricsMigrationTests`: `Reason` é textual e
      `NOT NULL`, a tabela existe, e **não** há FK — no molde de
      `ExecutionMetricsMigrationTests`. **Autorizada pelo dono**: é a 41ª classe de
      contêiner de `apps/api`, e a régua recalibrada (40) não bloqueia.
- [x] 2.7 **Mutação:** gravar `AgentInactive` no sítio de
      `ProviderOrModelMissing`, rodar, e confirmar que reprova **só** o guarda
      daquela causa. Registrar o resultado no `design.md` se divergir.

## 3. O terceiro regime e a checagem de boot

- [x] 3.1 **(`apps/api`)** `MetricsOptions`: constante `RejectionRegime` e
      atualização do comentário dos regimes (o terceiro deixou de ser previsão).
- [x] 3.2 **(`apps/api`)** `appsettings.json`: chave `Metrics:Regimes:rejection`,
      com o aviso de que o valor é o instante do **deploy** desta coleta, não o do
      merge.
- [x] 3.3 **(`apps/api`)** Criar a checagem de boot: todo regime declarado pelas
      rotas tem instante no mapa, ou a inicialização falha nomeando o que falta
      (D7). Chamada no `Program.cs`, depois do `Build()`, ao lado da checagem de
      fuso.
- [x] 3.4 **(`apps/api`)** `MetricsRegimeStartupValidationTests` (sem contêiner,
      molde de `TimeZoneStartupValidationTests`): regime ausente reprova o boot com
      o nome dele na mensagem; mapa completo sobe.
- [x] 3.5 **(`apps/api`)** Fixar `Metrics:Regimes:rejection` nas duas fixtures de
      Insights — **obrigatório**, não cosmético: herdar o instante do
      `appsettings.json` cortaria a janela dos guardas novos.

## 4. As duas rotas de agregação

- [x] 4.1 **(`apps/api`)** `SystemInsightsResponse`: `RejectionRegime`,
      `RejectedAtEntryCount`, `RejectionsByReason` e o record
      `RejectionReasonResponse(string Reason, int Count)`; documentar no
      `RejectedCount` o que ele de fato conta (D5).
- [x] 4.2 **(`apps/api`)** `AgentInsightsResponse`: os mesmos três campos, mesma
      forma, mesmo vocabulário.
- [x] 4.3 **(`apps/api`)** `GetSystemInsightsQueryHandler`: recorte do novo regime
      (`Later(query.From, rejectionRegime)`), consulta de contagem e consulta de
      agrupamento por motivo, `order by count desc, reason` para desempate
      determinístico.
- [x] 4.4 **(`apps/api`)** `GetAgentInsightsQueryHandler`: as mesmas duas consultas
      com `AgentId`, lidas da coluna de agente da própria tabela — nunca por junção
      com `a2a_tasks` nem com o catálogo.
- [x] 4.5 **(`apps/api`)** Retirar `RejectionReasonNotCollected` das duas listas de
      `caveats` **e** a constante de `InsightsCaveats`; manter
      `RejectionsMissingFromExecutions` com o texto intacto e o comentário
      atualizado com o que continua parcial (D6).

## 5. Guardas das rotas

- [x] 5.1 **(`apps/api`)** Semear recusas nas duas fixtures de Insights: causas
      diferentes, um valor fora do vocabulário (para o caso do motivo
      desconhecido), e ao menos uma fora da janela de regime.
- [x] 5.2 **(`apps/api`)** `InsightsEndpointsTests`: as duas contagens chegam
      separadas; a lista de motivos soma exatamente a contagem de recusa de
      entrada; motivo desconhecido passa cru; janela sem recusa dá `0` medido e
      lista vazia.
- [x] 5.3 **(`apps/api`)** `InsightsEndpointsTests`: `DoesNotContain` de
      `rejection-reason-not-collected` (o caso adaptado) e o terceiro regime no
      mapa e no bloco de erros.
- [x] 5.4 **(`apps/api`)** `AgentInsightsEndpointsTests`: recorte pelo agente
      consultado com outro agente semeado; agente sem recusa recebe `0`; o
      `caveat` também sai daqui — **caso novo**, porque a rota do agente não tem
      hoje nenhuma asserção sobre os `caveats` do bloco de erros.
- [x] 5.5 **(`apps/api`)** Afirmar o **nome de fio** dos três campos novos lendo o
      JSON cru, nas duas rotas (convenção 12 — round-trip pelo mesmo tipo é cego).
- [x] 5.6 **(`apps/api`)** Confirmar que os guardas existentes de `rejectedCount`
      continuam válidos **sem edição** — se algum precisar mudar, a D5 foi violada
      e o `design.md` é corrigido (convenção 9).

## 6. Verificação de suíte

- [x] 6.1 Rodar `Buteco.Api.Tests` inteira e comparar com a baseline **medida hoje**:
      `404/404` em 3m46s sobre `5f2f6f8`, com dois contêineres de pé
      (`postgres`, `rabbitmq`) e `waha` parado. Declarar o `podman ps` junto do
      número, **e a contagem de classes de contêiner (40) junto da duração** — foi
      a falta disso que deixou a régua citada sem estado por três semanas.
- [x] 6.2 Ao registrar qualquer baseline desta change, escrever que
      **`apps/frontend` foi de 927 (84 arquivos) para 1218 (107 arquivos) porque a
      #52 mergeou** — sem isso, os +291 casos parecem anomalia de medição em vez de
      consequência de uma change, e a próxima projeção citaria 927.
- [x] 6.3 Rodar `Buteco.Workers.Tests` (`386/386`, 9m21s na mesma medição) —
      **nada** deve mudar: esta change não toca `apps/workers`, e o espelho de
      schema não vê a tabela nova.
- [x] 6.4 **Não** rodar `apps/frontend` nem `apps/inbox` esperando mudança: nenhuma
      linha delas entra no diff. Se `apps/inbox` for rodada, lembrar dos 3 casos já
      medidos e **não** classificá-los nesta change.
- [x] 6.5 Rodar o binário de migração contra um banco limpo e contra um banco já
      migrado, confirmando que a migração é aditiva nos dois casos.

## 7. Registro e fechamento

- [x] 7.1 Escrever no `02-HISTORICO_E_STATUS.md`: o fechamento desta change, a
      **vigésima medição** da convenção 18 comparada com a projeção do
      `design.md` (cenários por categoria, casos por categoria, linhas nos quatro
      níveis, arquivos criados × modificados), e os itens abertos com gatilho.
- [x] 7.2 Corrigir no `02-HISTORICO_E_STATUS.md` a régua de contenção de
      `apps/api`: **40** classes, não 31 — com a causa (a contagem original não
      contou `Knowledge/`; medida também no commit do registro, também 40), a
      duração e a contagem de testes ao lado, **o que o número serve para ler**
      (registra o crescimento, e nada mais), os dois candidatos que a tornariam
      acionável com o gatilho de reabertura, e a menção cruzada à régua gêmea de
      `apps/workers`. Sem isso, o número errado continua citável.
- [x] 7.3 **Conferido: o `01` NÃO descreve as tabelas de métrica nem o mapa de
      regimes** — a premissa desta tarefa estava errada, e a leitura corrige
      (`grep` por `task_executions`, `provider_calls`, `MetricsOptions` e
      `insights` no `01` devolve só duas menções incidentais). O que entrou ali foi
      o que ganhou forma de convenção: a **sexta ocorrência da convenção 22**, de
      tipo novo — referência que não envelheceu, **nasceu errada** —, com a régua de
      recontar no commit original antes de concluir crescimento.
- [x] 7.4 Atualizar o `CHANGELOG.md` e rodar `scripts/check-docs.py`.
- [x] 7.5 **Feito em 26/09/2026**, antes de qualquer push (convenção 24). Specs
      sincronizadas: `agent-rejection-metrics` nasce, as duas de agregação recebem
      os deltas.
- [ ] 7.6 **(pós-archive)** Abrir **um** PR com `Closes #51` no corpo, e `Refs` para
      as issues abertas na 0.2 — nunca `Closes` nelas.
- [ ] 7.7 **(pós-archive)** Mover a **#51** para `In review`; não tocar em `Done`,
      que o workflow move sozinho no merge.
- [ ] 7.8 **(pós-merge)** Apagar branch e worktree, e registrar na #51 o instante do
      regime `rejection` de fato usado no deploy.

## 8. Conferência de escopo — a lista fechada

Montada por **leitura do repositório** em 25/09/2026 sobre `5f2f6f8`, não por
analogia. Antes de fechar, confirmar com `git status` que o diff contém **apenas**
o que está na primeira tabela.

**Pode ser tocado:**

| caminho | o que muda |
|---|---|
| `apps/api/src/Buteco.Api/RejectionMetrics/**` | tudo novo |
| `apps/api/src/Buteco.Api/A2A/EnqueueingAgentHandler.cs` | a leitura de estado e os quatro caminhos de recusa |
| `apps/api/src/Buteco.Api/Infrastructure/AppDbContext.cs` | `DbSet` e mapeamento da tabela nova |
| `apps/api/src/Buteco.Api/Infrastructure/Migrations/**` | a migração nova e o snapshot regerado |
| `apps/api/src/Buteco.Api/Options/MetricsOptions.cs` | a constante do terceiro regime |
| `apps/api/src/Buteco.Api/Insights/Queries/**` | as duas consultas novas em cada handler |
| `apps/api/src/Buteco.Api/Insights/Responses/**` | os três campos novos e o `caveat` que sai |
| `apps/api/src/Buteco.Api/Insights/MetricsRegimeValidation.cs` | a checagem de boot |
| `apps/api/src/Buteco.Api/Program.cs` | a chamada da checagem |
| `apps/api/src/Buteco.Api/appsettings.json` | a chave `rejection` |
| `apps/api/tests/Buteco.Api.Tests/**` | os três arquivos novos e os cinco modificados da lista da D9 |
| `openspec/changes/recusa-motivo-coleta/**` | os artefatos desta change |
| `01-ARQUITETURA_E_CONVENCOES.md`, `02-HISTORICO_E_STATUS.md`, `CHANGELOG.md` | itens 7.1 a 7.3 |

**Não pode ser tocado, e a razão de cada um:**

| caminho | razão |
|---|---|
| `apps/frontend/**` | consumir o dado novo é change de seguimento da #52, com issue própria (convenção 1). Inclui `caveatLabels.ts`, cuja entrada morta é **entrada não alcançada**, não defeito |
| `apps/workers/**` | não recusa na entrada e não lê a tabela nova. O espelho de schema é por tabela, então nada ali precisa acompanhar |
| `apps/workers/src/Buteco.Workers/ExecutionMetrics/**` | `FailurePhase` é vocabulário de outra capability; o motivo de recusa **não** entra nele (D1) |
| `apps/inbox/**` | cliente A2A: continua recebendo `rejected`, sem mudança de contrato. A razão ao cliente é a issue (c) |
| `libs/**` | um produtor e um consumidor, os dois em `apps/api` — `libs/` sem consumidor é abstração prematura (convenção 2) |
| `tests/CrossAppTaskStoreCompatibility.Tests`, `tests/InboxOrchestratorRoundTrip.Tests` | contratos entre apps; nenhum dos dois toca métrica de recusa |
| `apps/api/src/Buteco.Api/A2A/PostgresTaskStore.cs`, `A2ATaskRecord.cs` | a saída 1 foi recusada (D2); o store do protocolo continua com um escritor só |
| `apps/api/src/Buteco.Api/ExecutionMetrics/**` | a saída 2 foi recusada (D2); `task_executions` continua sendo "o que o worker consumiu" |
| `deploy/migrate` | a migração nova entra pelo bundle de `apps/api`, que o migrator já empacota; nada a configurar |
| `docker-compose.yml`, `docker-compose.prod.yml` | o instante do regime vai por `appsettings.json`, como os outros dois |
| `apps/frontend/deploy/nginx.conf` | nenhuma rota nova; os dois caminhos de Insights já roteiam |
| `docs/**` | esta change cumpre convenções, não as altera |
| `openspec/specs/**` | main specs só mudam no archive, nunca no apply |
| `openspec/changes/archive/**` | registro do que se decidiu então; não se edita |
