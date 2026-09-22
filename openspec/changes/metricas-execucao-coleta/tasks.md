## 0. Conferência de escopo de arquivo

**Lista fechada de caminhos permitidos.** Qualquer arquivo fora desta lista que
apareça no `git status` ao fim é achado a reportar, não a commitar. **`apps/api`
entra só para a migração** — entidades de mapeamento, `AppDbContext`, os
arquivos gerados pelo `dotnet ef` e o teste da migração. Nenhuma rota, nenhum
handler, nenhum endpoint.

```
# apps/api — só migração
apps/api/src/Buteco.Api/ExecutionMetrics/Entities/TaskExecution.cs                       (novo)
apps/api/src/Buteco.Api/ExecutionMetrics/Entities/ProviderCall.cs                         (novo)
apps/api/src/Buteco.Api/ExecutionMetrics/Entities/DelegationOutcome.cs                    (novo)
apps/api/src/Buteco.Api/Infrastructure/AppDbContext.cs
apps/api/src/Buteco.Api/Infrastructure/Migrations/<ts>_AddExecutionMetrics.cs             (gerado)
apps/api/src/Buteco.Api/Infrastructure/Migrations/<ts>_AddExecutionMetrics.Designer.cs    (gerado)
apps/api/src/Buteco.Api/Infrastructure/Migrations/AppDbContextModelSnapshot.cs            (gerado)
apps/api/tests/Buteco.Api.Tests/ExecutionMetricsMigrationTests.cs                         (novo)

# apps/workers
apps/workers/src/Buteco.Workers/ExecutionMetrics/ExecutionMetricsScope.cs                 (novo)
apps/workers/src/Buteco.Workers/ExecutionMetrics/ExecutionMetricsWriter.cs                (novo)
apps/workers/src/Buteco.Workers/ExecutionMetrics/ExecutionMetricsValues.cs                (novo)
apps/workers/src/Buteco.Workers/ExecutionMetrics/CompactionCallChatClient.cs              (novo)
apps/workers/src/Buteco.Workers/ExecutionMetrics/Entities/TaskExecution.cs                (novo)
apps/workers/src/Buteco.Workers/ExecutionMetrics/Entities/ProviderCall.cs                 (novo)
apps/workers/src/Buteco.Workers/ExecutionMetrics/Entities/DelegationOutcome.cs            (novo)
apps/workers/src/Buteco.Workers/AgentDelegations/DelegationOrigin.cs                      (novo)
apps/workers/src/Buteco.Workers/AgentDelegations/AgentDelegationToolSetResolver.cs
apps/workers/src/Buteco.Workers/Agents/AgentExecutionService.cs
apps/workers/src/Buteco.Workers/Agents/LlmCallDurationChatClient.cs
apps/workers/src/Buteco.Workers/Diagnostics/NonTerminalTaskDetectorService.cs
apps/workers/src/Buteco.Workers/Infrastructure/AppDbContext.cs
apps/workers/src/Buteco.Workers/Infrastructure/Migrations/<ts>_AddExecutionMetrics.cs          (gerado)
apps/workers/src/Buteco.Workers/Infrastructure/Migrations/<ts>_AddExecutionMetrics.Designer.cs (gerado)
apps/workers/src/Buteco.Workers/Infrastructure/Migrations/AppDbContextModelSnapshot.cs         (gerado)
apps/workers/tests/Buteco.Workers.Tests/ExecutionMetrics/ExecutionMetricsScopeTests.cs          (novo)
apps/workers/tests/Buteco.Workers.Tests/ExecutionMetrics/ExecutionMetricsSchemaMirrorTests.cs   (novo)
apps/workers/tests/Buteco.Workers.Tests/Support/ExecutionMetricsReader.cs                       (novo)
apps/workers/tests/Buteco.Workers.Tests/Agents/LlmCallDurationChatClientTests.cs
apps/workers/tests/Buteco.Workers.Tests/TaskJobConsumerTests.cs
apps/workers/tests/Buteco.Workers.Tests/ConversationContextLockFailureTests.cs
apps/workers/tests/Buteco.Workers.Tests/AgentDelegationExecutionTests.cs
apps/workers/tests/Buteco.Workers.Tests/AgentDelegationConcurrencyTests.cs
apps/workers/tests/Buteco.Workers.Tests/HistorySummarizationTests.cs

# escopo 2 — guarda de estado terminal (D17), acrescentado no apply
apps/api/tests/Buteco.Api.Tests/A2ATaskLifecycleTests.cs
#   (AgentExecutionService.cs e TaskJobConsumerTests.cs já estão acima, pelo escopo 1;
#    o delta specs/a2a-task-lifecycle/ está coberto pelo openspec/** abaixo)

# escopo 3 — duplo do resolver no round-trip, acrescentado no apply
tests/InboxOrchestratorRoundTrip.Tests/Support/NullAgentDelegationToolSetResolver.cs

# registro
02-HISTORICO_E_STATUS.md
CHANGELOG.md
openspec/changes/metricas-execucao-coleta/**
```

**D2, custo de DI zero:** dos 14 arquivos de teste que registram
`AgentExecutionService` (13 em `apps/workers/tests/` + `RoundTripFixture.cs`;
contagem de 21/09), **nenhum registro de DI existente pode mudar**. Cinco deles
estão na lista acima por outro motivo — recebem guardas —, e os outros nove,
mais o resto de `tests/InboxOrchestratorRoundTrip.Tests/**` — **exceto o duplo
do resolver**, escopo 3 —, ficam **fora**. Se
algum desses nove aparecer no `git status`, ou se alguma linha de registro de
serviço existente for alterada nos cinco, D2 caiu, e isso é achado. *(A
primeira redação dizia "os 14 fora da lista", contradizendo a própria lista —
corrigida no apply, 21/09.)*

- [x] 0.1 Ao fim da change, conferir `git status` contra a lista acima.

## 1. Baselines e pré-condições

- [x] 1.1 Registrar as baselines **antes de tocar em qualquer arquivo**, com o
      regime colado (convenção 22): `podman ps` devolvendo **zero**, rodada
      abaixo de ~10 min. `apps/workers` — esperado **280/280**, **14 classes** na
      `WorkerHostCollection`, contadas por `grep -rn "^\[Collection("` (busca
      ancorada, que exclui menção em comentário). `apps/api` — esperado
      **335/335** (medido na proposta, 21/09/2026 01:24 -03, `2ee34d3`, 1m09s).
      **Não trocar o número à mão:** se a medição não der o esperado, a
      divergência é achado a explicar antes de seguir. Anotar contagem e
      duração; comparação é por nome de teste.
- [x] 1.2 Reconciliar o mapa métrica → coluna do `design.md` com o catálogo
      numerado. **Feito na proposta:** o catálogo foi registrado no `02`
      ("Linha de trabalho `metricas-de-operacao` — catálogo de métricas
      (REFERÊNCIA VIVA)") e o mapa passou a citar os M-números contra ele; as
      divergências (M21, "chamadas sem reporte de cache", M9, M14, M16b) estão
      corrigidas e listadas no `design.md`. A contagem total é **27** (o "25"
      era a lista original, antes de o catálogo crescer — tabela de passos no
      `02`).

## 2. Schema — `apps/api` migra, `apps/workers` espelha

- [x] 2.1 (`apps/api`) Criar `ExecutionMetrics/Entities/` com
      `TaskExecution`, `ProviderCall` e `DelegationOutcome` — só mapeamento,
      com o comentário de que `apps/api` nunca escreve nelas (precedente
      `KnowledgeFragment`).
- [x] 2.2 (`apps/api`) Mapear as três em `AppDbContext`: tabelas, tipos e
      nulidade exatamente como D1; enumerações como texto; FKs de
      `provider_calls.TaskId` e `delegation_outcomes.SourceTaskId` para
      `task_executions` com `Restrict`; **nenhuma** FK para `agents` (D13);
      índices de D1.
- [x] 2.3 (`apps/api`) Gerar a migração `AddExecutionMetrics`. Ler o `.cs`
      gerado e conferir que ele **só cria** as três tabelas e índices — nada em
      `a2a_tasks` ou em outra tabela.
- [x] 2.4 (`apps/api`) `ExecutionMetricsMigrationTests`: as três tabelas
      existem depois de migrar; `[Theory]` de tipo e nulidade das colunas que
      carregam decisão (tokens anuláveis, `SubmittedAt`, `EndedAt`,
      `TerminalState`, `TargetTaskId`, `LastObservedTargetState`); nenhuma FK
      referencia `agents`.
- [x] 2.5 (`apps/workers`) Espelho: entidades em
      `ExecutionMetrics/Entities/`, mapeamento em `AppDbContext`, e a migração
      espelho gerada — ela só roda contra Testcontainers, nunca contra banco
      real.
- [x] 2.6 (`apps/workers`) `ExecutionMetricsSchemaMirrorTests`
      (`IClassFixture<WorkerInfrastructureFixture>`, **fora** da
      `WorkerHostCollection`): mesmas asserções de 2.4 contra a migração
      espelho, e a ausência de FK para `agents` também no **modelo do EF**
      (asserção estrutural de D13, o par determinístico do guarda de snapshot).
- [x] 2.7 Rodar 2.4 e 2.6: verdes. A partir daqui, "`HEAD`" nos guardas quer
      dizer `HEAD` + schema (D15).

## 3. Guardas vermelhos contra `HEAD` + schema (`apps/workers`)

Cada guarda é escrito, **rodado e visto reprovando pela propriedade** — tabela
vazia, chave ausente —, com a mensagem de reprovação anotada. Reprovação por
compilação não conta (D15).

- [x] 3.1 `Support/ExecutionMetricsReader.cs`: leitura das três tabelas por
      `TaskId`, usada pelas cinco classes abaixo.
- [x] 3.2 `TaskJobConsumerTests` — task externa concluída: uma linha pai,
      `Origin = External`, `Completed`, `SubmittedAt <= StartedAt <= EndedAt`, e
      ao menos uma filha `Turn`.
- [x] 3.3 `TaskJobConsumerTests` — linha aberta: com o client falso bloqueado
      num `TaskCompletionSource`, a linha pai existe com `EndedAt` nulo **antes**
      de a task terminar.
- [x] 3.4 `TaskJobConsumerTests` — reentrega: task semeada já em `Working` e
      publicada; `SubmittedAt` é nulo (e não o carimbo do `Working`).
- [x] 3.5 `TaskJobConsumerTests` — nulo persistido: client falso com `Usage`
      de entrada e saída e `CachedInputTokenCount` nulo; a coluna é nula **e não
      é zero** (guarda negativo: afirma a ausência do zero).
- [x] 3.6 `TaskJobConsumerTests` — snapshot: task com modelo A, `UPDATE` do
      agente para B, segunda task; as linhas da primeira continuam A.
- [x] 3.7 `TaskJobConsumerTests` — degradação: renomear `task_executions` no
      `try`, restaurar no `finally` (a coleção roda em série, então o
      renomeio não vaza para outra classe); a task termina `Completed` com
      artefato **e** sai o log de aviso de gravação de métrica com o `TaskId`
      no estado estruturado. Contra `HEAD` reprova pela ausência do log.
      Comentário no teste: por que renomear e não mockar (não há ponto de
      injeção, D2).
- [x] 3.8 `TaskJobConsumerTests` — provedor não configurado: linha com
      `Failed`, `FailurePhase = ChatClientResolution`, zero filhas.
- [x] 3.9 `ConversationContextLockFailureTests` — no arranjo de
      `AcquisitionTimingOut_…`: linha com `Failed`, `FailurePhase = ContextLock`,
      `LockAcquiredAt` nulo, zero filhas.
- [x] 3.10 `AgentDelegationExecutionTests` — profundidade: linha do alvo
      `Rejected`, `FailurePhase = DelegationDepthExceeded`, zero filhas.
- [x] 3.11 `AgentDelegationExecutionTests` — chaves de origem no `Metadata` da
      task do alvo (delta de `agent-delegation-execution`).
- [x] 3.12 `AgentDelegationExecutionTests` — alvo inativo: linha de resultado
      `NotStarted`, `TargetTaskId` nulo.
- [x] 3.13 `AgentDelegationConcurrencyTests` — duas instâncias, grafo
      semeado: linha do alvo `Origin = Delegation` com `SourceAgentId`,
      `SourceTaskId` e profundidade 1; linha do Source `External`; resultado
      `Completed`; **contaminação**: nenhuma filha com o `TaskId` do alvo foi
      produzida pela execução do Source, e nenhum resultado tem o alvo como
      origem.
- [x] 3.14 `AgentDelegationConcurrencyTests` — nos quatro testes de log da
      `delegacao-diagnostico` (`:199`, `:247`, `:314`, `:374`), acrescentar a
      asserção da linha gêmea: `Expired`/`Submitted`, `Expired`/`Working`,
      `TargetUnsuccessful`/`Failed`, `Expired`/nulo com `SuccessfulReadCount =
      0`. **Se** o harness desses testes não expuser o banco, virar testes
      novos e registrar o desvio da projeção.
- [x] 3.15 `HistorySummarizationTests` — no arranjo de
      `CrossingThreshold_…`: filhas com `Purpose = Compaction` e com
      `Purpose = Turn` na mesma task.
- [x] 3.16 `Agents/LlmCallDurationChatClientTests` — dentro de escopo aberto:
      turno registra tokens; streaming registra tokens do `UsageContent` e a
      duração da enumeração; requisição que lança `HttpRequestException` com
      status registra `Failed` e `HttpStatus`, e a exceção continua propagando.
- [x] 3.17 Rodar 3.2–3.16 e anotar: **todos vermelhos**, e cada um pela
      propriedade.

## 4. Implementação (`apps/workers`)

- [x] 4.1 `ExecutionMetricsValues`: vocabulário de `Origin`, `Purpose`,
      `FailurePhase` e desfecho, como constantes de texto.
- [x] 4.2 `ExecutionMetricsScope`: `AsyncLocal` estático; `Begin` que
      **sobrescreve** e devolve o descartável que restaura; registro de chamada
      e de delegação sob `lock`; registro fora de escopo é ignorado; marca de
      finalidade. Comentários de D2.
- [x] 4.3 `ExecutionMetricsScopeTests` (unitário, fora da coleção): fora de
      escopo não registra nem lança; dois escopos concorrentes não se misturam;
      registros paralelos no mesmo escopo não se perdem; a marca de compactação
      vale só durante a chamada marcada; `Usage` parcial, ausente e zero
      (os três estados de nulo); `[Theory]` do status HTTP tipado
      (`HttpRequestException`, `ClientResultException`, outra → nulo).
- [x] 4.4 `ExecutionMetricsWriter`: abrir (INSERT) e fechar (UPDATE da pai +
      filhas + resultados num `SaveChangesAsync`; insere a pai se a abertura
      falhou), escopo de DI próprio, `CancellationToken.None` com prazo curto,
      `try/catch` com `LogWarning` estruturado que **não relança**. Comentários
      de D3.
- [x] 4.5 `LlmCallDurationChatClient`: registrar no escopo, nos **dois**
      caminhos, provedor, modelo, duração, tokens sem normalizar nulo, falha e
      status tipado. O log de duração existente **permanece**.
- [x] 4.6 `CompactionCallChatClient`: `IChatClient` direto, `Dispose` no-op,
      marca `Compaction` e delega. Comentário de D8 (por que não
      `DelegatingChatClient`, por que não `options is null`).
- [x] 4.7 `DelegationOrigin` e `AgentDelegationToolSetResolver`: gravar as duas
      chaves em `CreateDelegatedTaskAsync` junto de `delegationDepth`; registrar
      um resultado em cada uma das saídas da tabela de D5, **sem mudar** o texto
      devolvido ao LLM nem os logs existentes.
- [x] 4.8 `AgentExecutionService`: abrir o escopo e a linha pai logo depois de
      `GetTaskWithRetryAsync`, com `SubmittedAt` pela regra de D4 e a origem por
      `DelegationOrigin`; manter a fase atual numa variável local (D12);
      `LockAcquiredAt` depois da aquisição; passar `CompactionCallChatClient` à
      `SummarizationCompactionStrategy`; fechar no `finally`. **Não** mover o
      `await using` do lock (o comentário existente explica por quê).
- [x] 4.9 `Diagnostics/NonTerminalTaskDetectorService.cs`: reescrever o
      parágrafo "ESTE COMPONENTE É TEMPORÁRIO" com a condição e o momento de
      remoção de D11, datados de 21/09/2026. **Não remover** o serviço nem o log
      de desistência.

## 5. Verde

- [x] 5.1 Rodar os guardas do grupo 3 e os unitários de 4.3: verdes.
- [x] 5.2 Suíte completa de `apps/workers`, mesmo regime de 1.1: projeção
      **319/319**, **14 classes**. Comparar **por nome** com a baseline: nenhum
      teste existente sumiu.
- [x] 5.3 Suíte completa de `apps/api`: projeção **343/343** (335 da 1.1 +
      8). Comparar por nome com a baseline.
- [x] 5.4 `tests/CrossAppTaskStoreCompatibility.Tests` e
      `tests/InboxOrchestratorRoundTrip.Tests`: verdes sem modificação — o
      segundo registra `AgentExecutionService` e é o sítio de fora de
      `apps/workers` da régua de D2.

## 6. Conferência de DI por compilação (D2)

- [x] 6.1 `dotnet build` da solução inteira sem erro; os **nove** harness que
      não recebem guarda saem sem diff, e nos **cinco** que recebem nenhuma
      linha de registro de serviço existente aparece como removida ou alterada
      (`git diff -U0 -w`, linhas `-`). A lista dos 14 sai de busca **estrutural** (registro
      `AddSingleton<AgentExecutionService>` em código, não em comentário), não
      de `grep` cru — régua textual da décima medição.

## 6a. Escopo 3 — duplo do resolver no round-trip (acrescentado no apply)

- [x] 6a.1 Compilar os **10** `.csproj` do repositório contra `HEAD`, em worktree
      limpa (`2ee34d3`) — não existe `.sln` na raiz, e os três por app não
      cobrem `tests/` nem `libs/`. Vermelho: **1 erro**, `CS0535` em
      `tests/InboxOrchestratorRoundTrip.Tests/Support/NullAgentDelegationToolSetResolver.cs`;
      nenhum outro projeto quebrado.
- [x] 6a.2 Conferir se esta change mexeu na interface do resolver: **não**
      (`git diff` vazio em `IAgentDelegationToolSetResolver.cs`). O duplo acompanha
      só o `sourceTaskId` do `0f8dede`.
- [x] 6a.3 Atualizar o duplo; os 10 `.csproj` compilam; o projeto roda **4/4**.
- [x] 6a.4 `02`: entrada própria da regressão, a régua (blast radius de
      assinatura se mede compilando todos os `.csproj`), e a correção ao lado do
      fechamento da `delegacao-diagnostico` (convenção 9).

## 6b. Escopo 2 — guarda de estado terminal (acrescentado no apply; D17)

- [x] 6b.1 Experimento antes da causa: instrumentação temporária (`TaskId`,
      `Redelivered`, estado lido, falha de gravação de métrica), três rodadas das
      duas classes de delegação. Toda falha de gravação foi `23505` na abertura e
      coincidiu com reentrega; toda falha de teste teve reentrega na janela.
      Instrumentação revertida (nenhuma linha marcada restante; `TaskJobConsumer`
      sem diff).
- [x] 6b.2 O travamento de ~70 s: mesmo gatilho + segundo achado pré-existente
      (`TaskJobConsumer.StopAsync` fecha o canal antes de cancelar a execução).
      Registrado, não corrigido.
- [x] 6b.3 Premissa verificada no pacote, não suposta: o `A2AServer` recusa
      mensagem para task terminal (`GuardTerminalState`).
- [x] 6b.4 Guardas: `[Theory]` de 4 estados terminais, vermelho em `HEAD` pela
      reexecução (*"was 2 times"*), ancorado em sentinela; o par (conversa
      continua em task nova) e a premissa em `apps/api`, verdes nos dois lados.
- [x] 6b.5 A guarda em `AgentExecutionService`, antes da linha pai, com
      `TaskStateExtensions.IsTerminal`. Guardas verdes (6/6 com a reentrega de
      `Working`); as duas classes de delegação **20/20 em três rodadas**.
- [x] 6b.6 Artefatos: `proposal.md` (três escopos, `a2a-task-lifecycle` em
      Modified Capabilities), D17 no `design.md`, requisito da linha pai
      redigido com cenário novo, delta de `a2a-task-lifecycle`.

## 7. Verificação por execução real (convenção 6)

- [x] 7.1 Subir o stack de desenvolvimento, aplicar a migração por `apps/api`,
      mandar uma mensagem a um agente e consultar as três tabelas: uma pai
      fechada, filhas com tokens, nenhuma linha de resultado.
- [x] 7.2 **Q5 da exploração — reformulada em 21/09/2026.** A pergunta é *"o
      endpoint que atende a maior fatia do volume reporta `Usage`?"*. A redação
      anterior mandava apontar para "o Ollama local" — **premissa errada: não há
      modelo local rodando, confirmado pelo dono.** O "Ollama" era dedução da
      exploração a partir do nome `openai/llama3.2:3b` (188 de 244 tasks do dev),
      nunca medida. Primeiro **identificar qual endpoint compatível com OpenAI
      servia esse modelo, e se o agente ainda o usa em produção**; se usa, uma
      chamada real conferindo `InputTokens`/`OutputTokens` preenchidos e
      `CachedInputTokens` nulo, registrada no `02` com modelo, endpoint e data; se
      não usa, a pergunta cai, e isso se registra. Nulo é dado certo — registrar,
      não corrigir.
      *(**Fechada com registro em 22/09/2026:** o `llama3.2:3b` era Ollama local de
      teste pontual, descartado pelo dono em 20/09/2026, fora de uso em produção.
      A pergunta cai — não há o que medir. No piloto o Triagem usa Gemini, e não existe
      `llama3.2:3b` nem Ollama em ambiente nenhum.)*
- [x] 7.3 Delegação com grafo semeado e **duas** instâncias de worker (como
      `docs/development.md` instrui): linha do alvo com origem, resultado
      `Completed`. Com **uma** instância: resultado `Expired` com último estado
      `Submitted` — o `C` lido de consulta, que é o motivo 2 da reordenação.
      *(Feita em 22/09/2026 com 3 instâncias: Triagem → Gestor de Reservas,
      `Origin = Delegation`, resultado `Completed`. O caso de instância única
      **não** foi exercido à mão — está coberto pelo guarda automático
      `DelegationTimeout_WithTargetNeverConsumed_LogsLastObservedStateAsSubmitted`.)*

## 8. Registro

- [x] 8.1 `02-HISTORICO_E_STATUS.md`: seção da change em "Changes aplicadas";
      a **décima primeira medição da convenção 18** comparando com a projeção do
      `design.md` sem inventar fator (arquivos criados/modificados separados,
      unidades públicas, linhas por tipo de registro, comentário de teste,
      contagem de casos xUnit); as lacunas nomeadas do mapa (recusas de
      `apps/api` e M32 dependem de `a2a_tasks` na etapa 3; D4 da exploração
      muda para a etapa 3); a condição de remoção do detector (D11); a assimetria
      "Delega para" × "Acionado por" (D16) como restrição de desenho entregue à
      etapa 4; a correção da régua "13 sítios" → 14 arquivos (D2) com a causa; e
      a posição da fila atualizada.
- [x] 8.2 `02-HISTORICO_E_STATUS.md`: item aberto com gatilho **"deploy desta
      change em produção"** e ação *registrar "medindo desde DD/MM/AAAA,
      `America/Sao_Paulo`"* (D14), e o segundo marco de regime como tarefa da
      `replicas-de-worker`.
- [x] 8.3 `CHANGELOG.md`: entrada da coleta.
- [x] 8.4 `openspec validate --all` verde.
