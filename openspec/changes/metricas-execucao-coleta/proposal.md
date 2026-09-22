## Why

A linha de trabalho `metricas-de-operacao` tem catálogo fechado — registrado no
`02` como referência viva da linha, com M18 (cache **escrito**) recusada por
verificação de dado e M34 (delegações) puxada para dentro — e
protótipos aprovados das duas superfícies (página Insights e aba Insights do
agente, nos três cenários de delegação). Nada disso é construível hoje: o
instante do `submitted` é destruído na transição para `working`
(`A2ATaskRecord.Update`), tokens e duração por chamada só existem em log, a
origem de uma task delegada não é gravada em lugar nenhum, e **uma delegação que
expira não deixa rastro durável** — a task do Source termina `Completed`, e o
card de falhas aprovado para o cenário 3 mostra a expiração como motivo sem ter
de onde tirá-la. Esta é a **etapa 1** da linha: a coleta de execução. A métrica
não é retroativa, então cada dia sem coleta é um dia que a tela nunca vai
mostrar.

**Posição na fila:** subiu da 7 para a 6, à frente da `replicas-de-worker`, em
21/09/2026 — registrado em `## Próximo passo` do `02` com os dois motivos. O
segundo motivo (o `C` da `replicas-de-worker` passa a sair de consulta sobre dado
durável, não de `grep` em log) **depende de esta change gravar o resultado de
cada delegação com o último estado observado do alvo**; o `design.md` (D5)
conclui que cabe, e a reordenação ficou.

## Escopos desta change

**Três, declarados separadamente** — misturar correção de defeito pré-existente
com a coleta sem dizer é o que torna o diff ilegível. Os escopos 2 e 3 nasceram
no apply, com causa medida.

| escopo | o que é | por que está aqui |
|---|---|---|
| **1** | a coleta de execução — o objeto da change | é a etapa 1 da linha `metricas-de-operacao` |
| **2** | guarda de estado terminal: task lida já terminal não executa (`design.md`, D17) | **defeito pré-existente que esta change alargou**: a escrita de fechamento das métricas alargou a janela entre gravar o estado terminal e confirmar a mensagem, e a reentrega do RabbitMQ passou a reexecutar tasks concluídas na suíte. Isolado por experimento, não por leitura |
| **3** | duplo de `IAgentDelegationToolSetResolver` em `tests/InboxOrchestratorRoundTrip.Tests` | **regressão de change arquivada** (`delegacao-diagnostico`, `0f8dede`): o projeto não compilava desde 20/09. Pré-requisito da verificação desta change (tarefa 5.4) |

## What Changes

Tudo em `apps/workers`, exceto a migração, que sai de `apps/api` (precedente
`KnowledgeFragment`: só `apps/api` migra banco real).

- **Tabela pai `task_executions`** (`apps/api` migra, `apps/workers` escreve) —
  uma linha por execução de task consumida: agente, `contextId`, provedor e
  modelo **em snapshot**, estado terminal, instante do `submitted` (lido pelo
  worker **antes** de sobrescrevê-lo), de início, de lock adquirido e de
  término, **origem** (externa × delegação), agente e task de origem quando
  houver, profundidade, e a **fase** em que a falha aconteceu.
- **Tabela filha `provider_calls`** — uma linha por requisição HTTP ao provedor
  de LLM: task, provedor e modelo em snapshot, duração, tokens de entrada, saída
  e cache lido (**anuláveis**, nulo ≠ zero), **finalidade** (turno ×
  compactação), se falhou, e o status HTTP quando o SDK o expõe tipado.
- **Tabela `delegation_outcomes`** — uma linha por delegação disparada: task e
  agente de origem, agente e task alvo, desfecho (concluída, alvo terminou sem
  sucesso, expirou, não iniciada) e o **último estado observado do alvo**,
  com a contagem de leituras que separa "não sei" de "a linha não estava lá".
- **Task delegada passa a registrar a origem** em `AgentTask.Metadata`
  (`delegationSourceAgentId`, `delegationSourceTaskId`), no mesmo ponto onde já
  grava `delegationDepth`.
- **Captura por `AsyncLocal`** aberta em `AgentExecutionService.ExecuteAsync`,
  alimentada por `LlmCallDurationChatClient` (os **dois** caminhos, streaming e
  não-streaming) e pela tool de delegação; um marcador fino identifica a chamada
  de compactação. **Nenhum construtor muda** — o custo de DI nos 14 harness de
  teste é zero por desenho, a conferir por compilação e pela suíte.
- **Degradação graciosa:** falha ao gravar telemetria é registrada em log e
  **nunca** altera o estado terminal da task — a escrita final acontece depois
  do `SaveTaskAsync` terminal.
- **Registro, sem remoção:** o detector de tasks não-terminais e o log de
  desistência de delegação ficam. A condição e o momento da remoção vão escritos
  no `design.md` (D11) e no comentário do serviço.

## Capabilities

### New Capabilities

- `agent-execution-metrics`: coleta durável de execução de task, de chamada ao
  provedor de LLM e de resultado de delegação, gravada por `apps/workers` em
  tabelas migradas por `apps/api`, com nulo preservado e sem efeito sobre o
  estado terminal da task.

### Modified Capabilities

- `agent-delegation-execution`: a task criada por delegação passa a registrar o
  agente e a task de origem em `AgentTask.Metadata`, ao lado de
  `delegationDepth` (requisito acrescentado; nenhum requisito existente muda).
- `a2a-task-lifecycle` (**escopo 2**): task que o worker lê já em estado
  terminal não é executada de novo — a reentrega é confirmada sem reexecutar.
  Requisito acrescentado; nenhum existente muda.

## Impact

- **`apps/workers`** (escrita): `Agents/AgentExecutionService.cs`,
  `Agents/LlmCallDurationChatClient.cs`,
  `AgentDelegations/AgentDelegationToolSetResolver.cs`,
  `Infrastructure/AppDbContext.cs`, pasta nova `ExecutionMetrics/`, migração
  **espelho** (só para detectar divergência e para os testes com
  Testcontainers — nunca roda contra banco real), e comentário de
  `Diagnostics/NonTerminalTaskDetectorService.cs` (condição de remoção).
- **`apps/api`** (só migração): três entidades de mapeamento, `AppDbContext`, a
  migração e o snapshot, mais um teste de migração. **Nenhuma rota.**
- **`apps/api`, escopo 2:** um guarda em `A2ATaskLifecycleTests` — a premissa da
  guarda terminal (o protocolo recusa mensagem para task terminal). Nenhum código
  de produção de `apps/api` muda por esse escopo.
- **`tests/InboxOrchestratorRoundTrip.Tests`, escopo 3:** o duplo do resolver
  acompanha a assinatura atual da interface.
- **`apps/frontend`, `apps/inbox`, `libs/`:** nada.
- **Banco:** três tabelas novas; `a2a_tasks` **não** muda e **não** é
  consultada por esta etapa (por isso o índice em `status_timestamp`, D4 da
  exploração, não entra — ver D10 do `design.md`).
- **Operação:** nenhuma variável de ambiente nova. A data em que a coleta começa
  em produção é registrada no `02` no deploy, com o fuso
  (*"medindo desde DD/MM/AAAA, `America/Sao_Paulo`"*), porque é o texto que a
  tela aprovada exibe.

### Non-Goals explícitos

- Nenhuma rota de agregação, nenhuma tela (etapas 3 a 5).
- **Não coletar embedding** — etapa 2. A tabela filha só precisa *comportar* uma
  linha por lote (D7).
- **Não remover o detector nem o log de desistência** — só registrar a condição.
- Não coletar uso de MCP (M33) nem acesso a base (M35); não tocar o bloco do
  inbox (M36–M39).
- Não mexer em número de instâncias de `apps/workers`.
- Não corrigir `apps/api` sem `TZ` (D2 da exploração) — é das rotas de
  agregação, e esta etapa grava `timestamptz`, indiferente ao fuso.
