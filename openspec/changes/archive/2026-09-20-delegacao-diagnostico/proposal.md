## Why

A exploração `replicas-de-worker` (20/09/2026) fechou o diagnóstico de
capacidade de `apps/workers`: são necessárias `N ≥ C × D + 1` instâncias, onde
`C` é o número de conversas delegando ao mesmo tempo e `D` a profundidade da
cadeia. O ponto de quebra medido é exatamente `C ≥ N`, e não é degradação
gradual — com `C ≥ N` **todas** as delegações expiram, e continuam expirando
enquanto chegar conversa nova.

**O `C` de pico não é observável hoje.** Sem ele, `N ≥ C × D + 1` é fórmula sem
entrada, e a change `replicas-de-worker` (posição 5 da fila em
`02-HISTORICO_E_STATUS.md` → `## Próximo passo`) não tem como decidir entre
capacidade e o redesenho de retomada. Tudo que vem depois na fila depende disso.

O único rastro de uma delegação que trava carrega **dois campos** —
`TargetTaskId` e `Timeout`
(`apps/workers/src/Buteco.Workers/AgentDelegations/AgentDelegationToolSetResolver.cs:226`):

```
warn: ...AgentDelegationToolSetResolver[0]
      Task delegada 867cb210045a4f18a5bbdf8fc799f592 não concluiu dentro do timeout de 00:00:08.
```

Ele **não distingue contenção de Target genuinamente lento**. A diferença está
no estado da task do Target naquele instante — `Submitted` (nunca consumida =
contenção) contra `Working` (consumida e lenta) — e esse estado já está em mãos:
é o `current` do laço em `:203`. **Verificado nesta change: `current` está
declarado DENTRO do `while`, dentro do `try`, e portanto não existe no `catch`
de `:220` que loga a desistência** — o valor está em mãos no laço e fora de
alcance no ponto que registra.

E nenhuma métrica de erro por estado terminal substitui isso: medido em todas as
rodadas da sonda, **a task do Source termina `Completed`** — o modelo recebe o
texto de indisponibilidade, responde alguma coisa, e o estado terminal é
sucesso.

## What Changes

Escopo deliberadamente mínimo: **o que torna o `C` observável, e nada além.**

- **`apps/workers` — o registro de desistência da tool de delegação passa a
  identificar a delegação inteira e o estado do Target.** Os dois ramos que
  devolvem falha ao LLM do Source (`:213`, estado terminal de falha; `:226`,
  timeout) passam a carregar `SourceTaskId`, `SourceAgentId`, `TargetAgentId`,
  `TargetTaskId` e o **último estado observado** da task do Target, com o
  instante dessa observação. O `:213` entra junto porque é ele — e não o `:226`
  — que hoje recebe o `Failed` por contenção de lock criado pela change
  `lock-de-contexto-falha-terminal`.

- **`apps/workers` — `IAgentDelegationToolSetResolver.ResolveAsync` recebe o
  `TaskId` do Source.** É o único campo do registro acima que não está ao
  alcance do resolvedor hoje. Blast radius lido na compilação: a interface, a
  implementação, o duplo de teste `NullAgentDelegationToolSetResolver` e o único
  sítio de chamada (`AgentExecutionService.cs:240`) — quatro arquivos; os 13
  `AddSingleton` dos testes não referenciam a assinatura.

- **`apps/workers` — detector periódico de task em estado não-terminal mais
  velha que uma janela**, como `BackgroundService` próprio, reportando
  **contagem por estado** (`Submitted` contra `Working`), a idade da mais velha
  e a janela usada. É a série dessas leituras — não uma leitura isolada — que
  dá o `C` de pico à `replicas-de-worker`.

- **A janela é derivada, não escolhida:** é lida em runtime de
  `AgentDelegationToolOptions.Timeout` (120 s), de forma que uma linha reportada
  já sobreviveu a uma espera de delegação inteira. Nenhum número novo é afirmado
  para ela (convenção 13). O **intervalo** de varredura não tem base medida, e o
  `design.md` diz isso em vez de inventar mecanismo.

- **Sem tabela nova, sem migration, sem rota, sem tela.** A linha de métricas
  (`metricas-execucao-coleta`, posição 6) vai absorver os dois como métrica de
  verdade; o `design.md` registra que esta change será **substituída** por ela —
  duplicação temporária consciente, não descuido.

**Dois achados que mudaram o enunciado antes de qualquer código** (convenção 6,
aplicada ao registro interno):

1. **`KnowledgeIndexingFailure.Describe` NÃO ganha um segundo consumidor aqui.**
   Ele é classificador de **texto de tela** — o próprio XML doc diz *"A tela de
   documentos mostra este texto completo, sem truncar"* — e o entregável desta
   change é log. Além disso não há exceção a classificar no caminho que decide:
   o ramo de expiração captura um único tipo (`OperationCanceledException`) de
   significado já conhecido. O que se reusa é o **princípio** (switch por tipo,
   nunca parse de mensagem), aplicado ao input certo: `TaskState`, que é enum
   fechado. Reusá-lo literalmente poria *"Reindexe o documento"* num log de
   delegação.

2. **`apps/workers` não tem precedente de trabalho periódico.** A fila de
   indexação é consumidor RabbitMQ orientado a evento, não varredura; o único
   componente orientado a timer do monorepo é `DebounceSweepService`, em
   `apps/inbox` — que o próprio comentário do arquivo declara *"Único componente
   orientado a timer/scheduling do projeto"*. Este detector é o **primeiro
   `BackgroundService` periódico de `apps/workers`**, e o `design.md` carrega o
   que isso faz com as checagens de startup e com o número de instâncias.

## Capabilities

### New Capabilities
- `workers-nonterminal-task-detection`: detecção periódica, em `apps/workers`,
  de tasks em estado não-terminal mais velhas que uma janela derivada do timeout
  de delegação, com contagem por estado e registro do regime da medição.

### Modified Capabilities
- `agent-delegation-execution`: o requisito *"Espera pela task delegada com
  timeout e degradação graciosa"* passa a exigir que a desistência registre a
  delegação de forma identificável e distinga a causa pelo último estado
  observado do Target — hoje a spec exige o resultado de falha, e nada sobre o
  que fica registrado.

## Impact

- **App afetado: `apps/workers`, apenas.** `apps/api`, `apps/inbox`,
  `apps/frontend` e `libs/` não são tocados, e há task de conferência de escopo
  de arquivo no `tasks.md` — suíte verde não prova que um arquivo não foi
  tocado.
- **Sem migration e sem mudança de schema.** O detector consulta `a2a_tasks`
  pelas colunas existentes (`state`, `status_timestamp`), e já existe
  `HasIndex(task => task.State)` (`AppDbContext.cs:73`). Nada a espelhar em
  `apps/api`, logo `KnowledgeSchemaMirrorTests` fica intocado.
- **Um `BackgroundService` a mais no processo**, iniciado em `Program.cs`.
- **Um volume de log novo em produção**, proporcional ao número de instâncias —
  consequência declarada no `design.md`, porque a leitura ingênua da série
  superestima.
- **Uma 14ª classe no `WorkerHostCollection`**, o que dispara o gatilho de
  recalibração da convenção 22 registrado no `02`.
- **Non-Goals explícitos:** não mexer em número de instâncias, compose ou
  documentação de deploy; não criar tabela de métricas nem rota de agregação;
  não mudar `AgentDelegationToolOptions.Timeout` nem `PollInterval`; não
  implementar o redesenho de retomada (V4); não acrescentar defesa de ciclo em
  runtime.
