## MODIFIED Requirements

### Requirement: Processamento concorrente de Source e Target entre réplicas de apps/workers
Uma delegação SHALL progredir com sucesso quando o ambiente tiver ao menos um slot
de execução livre além do ocupado pelo Source, contando os slots de todas as
réplicas de `apps/workers` ativas (`réplicas × N`, em que `N` é
`TaskConsumer:MaxConcurrentExecutions`). Isso SHALL valer tanto com o Target
consumido por outra réplica quanto pela mesma instância que executa o Source. A
delegação SHALL expirar de forma controlada (via timeout, sem travar o processo)
quando não houver slot livre para o Target — o caso de uma única réplica com
`N = 1`, em que a instância que espera a task do Target é a única que poderia
consumi-la.

#### Scenario: Duas réplicas processam Source e Target concorrentemente
- **WHEN** duas instâncias separadas de `apps/workers` estão ativas, uma
  delas processando a task do Source (que delega), e a task delegada do
  Target é publicada na fila
- **THEN** uma instância com slot livre consome e processa a task do Target
  enquanto o Source ainda aguarda, e a delegação conclui com sucesso
  dentro do timeout

#### Scenario: Instância única com dois slots conclui a delegação
- **WHEN** uma única instância de `apps/workers` com `N = 2` processa a task do
  Source, que delega para um Target
- **THEN** a mesma instância consome a task do Target no segundo slot enquanto o
  Source aguarda, e a delegação conclui com sucesso dentro do timeout

#### Scenario: Instância única expira pelo timeout em vez de travar indefinidamente
- **WHEN** uma única instância de `apps/workers` com `N = 1` processa a task do
  Source, que delega para um Target — a mesma instância é a única
  capaz de consumir a task delegada, mas está ocupada aguardando o
  resultado dessa mesma task
- **THEN** a tool de delegação expira pelo timeout configurado e retorna
  um resultado de falha para o LLM do Source, em vez de travar o
  processamento indefinidamente
