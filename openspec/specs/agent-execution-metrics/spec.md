# agent-execution-metrics Specification

## Purpose

TBD - defined by change metricas-execucao-coleta. Update Purpose after archive.

## Requirements

### Requirement: Linha de execução para toda task consumida
`apps/workers` SHALL gravar em `task_executions` uma linha por task que consumir,
conseguir ler do store e executar, com agente, `contextId`, provedor e modelo do agente
no início da execução, origem, profundidade de delegação, instante de início e,
quando a execução terminar, o estado terminal e o instante de término. A linha
SHALL ser criada antes de qualquer caminho que termine a task, de modo que
nenhum estado terminal gravado pela execução fique sem linha correspondente.

#### Scenario: Task concluída produz linha fechada
- **WHEN** uma task externa é consumida e o agente responde com sucesso
- **THEN** existe exatamente uma linha em `task_executions` com o `TaskId` dela, `Origin = External`, `TerminalState = Completed`, `StartedAt` e `EndedAt` preenchidos e `EndedAt >= StartedAt`

#### Scenario: Reentrega de task já terminal não produz segunda linha
- **WHEN** o RabbitMQ reentrega a mensagem de uma task que já tem linha em `task_executions` e que o worker lê em estado terminal
- **THEN** a task não é executada de novo e nenhuma linha nova é criada para o `TaskId` dela

#### Scenario: Execução interrompida deixa a linha aberta
- **WHEN** a execução é interrompida depois de a linha ser criada e antes de a task chegar a estado terminal
- **THEN** a linha permanece com `EndedAt` nulo e `TerminalState` nulo, e não é apagada

### Requirement: Task que termina antes de qualquer chamada ao provedor produz linha
O worker SHALL produzir linha em `task_executions` para toda task que chega a
estado terminal sem que nenhuma requisição ao provedor de LLM tenha sido feita,
com o estado
terminal e a fase em que a execução parou em `FailurePhase`, e SHALL NOT
produzir linha em `provider_calls`.

#### Scenario: Falha na aquisição do lock de contexto
- **WHEN** a aquisição do lock de contexto falha e a task termina em `Failed`
- **THEN** existe linha em `task_executions` com `TerminalState = Failed` e `FailurePhase = ContextLock`
- **AND** não existe nenhuma linha em `provider_calls` com o `TaskId` dela

#### Scenario: Rejeição por profundidade de delegação
- **WHEN** a task excede a profundidade máxima de delegação e é rejeitada
- **THEN** existe linha em `task_executions` com `TerminalState = Rejected`, `FailurePhase = DelegationDepthExceeded` e `LockAcquiredAt` nulo
- **AND** não existe nenhuma linha em `provider_calls` com o `TaskId` dela

#### Scenario: Falha ao resolver o client do provedor
- **WHEN** o provedor do agente não está configurado no worker e a task termina em `Failed`
- **THEN** existe linha em `task_executions` com `TerminalState = Failed` e `FailurePhase = ChatClientResolution`
- **AND** não existe nenhuma linha em `provider_calls` com o `TaskId` dela

### Requirement: O motivo da falha é a fase, nunca texto de exceção
`FailurePhase` SHALL ser um valor de vocabulário fechado, gravado como texto,
determinado pelo passo da execução em que a falha ocorreu, e SHALL NOT ser
derivado do texto da mensagem de exceção.

#### Scenario: Valor gravado pertence ao vocabulário
- **WHEN** qualquer task termina em `Failed` ou `Rejected`
- **THEN** `FailurePhase` é um de `DelegationDepthExceeded`, `ContextLock`, `ChatClientResolution`, `ToolResolution`, `SessionLoad`, `AgentRun` ou `Persistence`

### Requirement: Instante do submitted lido antes de ser sobrescrito
`SubmittedAt` SHALL receber o carimbo de status da task tal como lido pelo
worker antes da transição para `working`, apenas quando o estado lido for
`Submitted`. Em qualquer outro estado lido, `SubmittedAt` SHALL ser nulo.

#### Scenario: Task consumida pela primeira vez
- **WHEN** o worker lê a task em `Submitted` e a executa
- **THEN** `SubmittedAt` é igual ao carimbo do `submitted` gravado por quem a criou, e `SubmittedAt <= StartedAt`

#### Scenario: Reentrega de task já iniciada
- **WHEN** o worker lê uma task que já está em `Working`
- **THEN** `SubmittedAt` é nulo, e não o carimbo do `Working`

### Requirement: Origem gravada na linha da execução
A linha de execução de uma task criada por delegação SHALL ter
`Origin = Delegation`, com `SourceAgentId` e `SourceTaskId` do agente e da task
que delegaram. Toda outra task SHALL ter `Origin = External` e os dois campos
nulos.

#### Scenario: Task delegada entre duas instâncias
- **WHEN** um agente Source delega para um Target e a task do Target é executada por outra instância
- **THEN** a linha do Target tem `Origin = Delegation`, `SourceAgentId` igual ao Source, `SourceTaskId` igual à task do Source e `DelegationDepth = 1`
- **AND** a linha do Source tem `Origin = External`

### Requirement: Linha por requisição ao provedor, com finalidade
Toda requisição ao provedor de LLM feita dentro de uma execução SHALL produzir
uma linha em `provider_calls` com o `TaskId` da execução, provedor e modelo do
client que fez a requisição, duração, tokens reportados, se falhou, e
`Purpose` igual a `Turn` para as requisições do turno do agente e `Compaction`
para a requisição de resumo do histórico. Isso SHALL valer tanto para o caminho
não-streaming quanto para o streaming do client.

#### Scenario: Turno com resumo do histórico
- **WHEN** uma task cruza o limiar de resumo do histórico
- **THEN** existe ao menos uma linha com `Purpose = Compaction` e ao menos uma com `Purpose = Turn`, todas com o `TaskId` dela

#### Scenario: Caminho de streaming
- **WHEN** uma requisição passa pelo caminho de streaming do client dentro de uma execução e o provedor reporta uso
- **THEN** existe uma linha com os tokens reportados e a duração da enumeração inteira

#### Scenario: Requisição que falha
- **WHEN** a requisição ao provedor lança uma exceção HTTP com status tipado
- **THEN** existe uma linha com `Failed = true` e `HttpStatus` igual ao status, e a exceção continua propagando para quem chamou

#### Scenario: Requisição fora de qualquer execução
- **WHEN** o client é chamado sem execução de task em andamento
- **THEN** nenhuma linha é produzida e a chamada não falha por isso

### Requirement: Token não reportado é nulo, nunca zero
`InputTokens`, `OutputTokens` e `CachedInputTokens` SHALL ser gravados nulos
quando o provedor não reportar o valor, e SHALL NOT ser normalizados para zero
em nenhum ponto entre o client e o banco. Zero SHALL ser gravado somente quando
o provedor reportar zero.

#### Scenario: Provedor reporta entrada e saída, mas não cache
- **WHEN** a resposta traz `InputTokenCount` e `OutputTokenCount` e `CachedInputTokenCount` nulo
- **THEN** a linha tem `CachedInputTokens` nulo
- **AND** a linha NÃO tem `CachedInputTokens = 0`

#### Scenario: Provedor não reporta uso nenhum
- **WHEN** a resposta não traz `Usage`
- **THEN** os três campos de token são nulos
- **AND** nenhum dos três é zero

#### Scenario: Provedor reporta zero de cache
- **WHEN** a resposta traz `CachedInputTokenCount = 0`
- **THEN** a linha tem `CachedInputTokens = 0`

### Requirement: Provedor e modelo são snapshot, sem join com agents
Provedor e modelo SHALL ser gravados na própria linha, em `task_executions` e em
`provider_calls`, e nenhuma das tabelas de métrica SHALL ter chave estrangeira
para `agents`. A troca de provedor ou modelo de um agente SHALL NOT alterar
linhas já gravadas.

#### Scenario: Agente troca de modelo
- **WHEN** um agente executa uma task com o modelo A, é editado para o modelo B e executa outra task
- **THEN** as linhas da primeira task continuam com o modelo A, e as da segunda têm o modelo B

#### Scenario: Nenhuma chave estrangeira para agents
- **WHEN** o schema migrado é inspecionado
- **THEN** nenhuma das tabelas `task_executions`, `provider_calls` e `delegation_outcomes` tem chave estrangeira que referencie `agents`

### Requirement: Resultado de cada delegação disparada
Toda chamada à tool de delegação dentro de uma execução SHALL produzir uma linha
em `delegation_outcomes` com a task e o agente de origem, o agente alvo, a task
alvo quando criada, o desfecho, o último estado observado do alvo e a contagem
de leituras bem-sucedidas. O desfecho SHALL ser `Completed`,
`TargetUnsuccessful`, `Expired` ou `NotStarted`. Ausência de leitura SHALL ser
gravada como estado nulo com contagem zero, e SHALL NOT ser gravada como um
estado.

#### Scenario: Delegação concluída
- **WHEN** o alvo termina em `Completed` dentro do prazo
- **THEN** existe linha com `Outcome = Completed`, `TargetTaskId` preenchido e `LastObservedTargetState = Completed`

#### Scenario: Delegação expira com o alvo nunca consumido
- **WHEN** o prazo da delegação expira e a última leitura do alvo o encontrou em `Submitted`
- **THEN** existe linha com `Outcome = Expired` e `LastObservedTargetState = Submitted`
- **AND** a task do Source termina em `Completed`, sem alteração do comportamento de degradação da delegação

#### Scenario: Delegação expira com o alvo em execução
- **WHEN** o prazo expira e a última leitura do alvo o encontrou em `Working`
- **THEN** existe linha com `Outcome = Expired` e `LastObservedTargetState = Working`

#### Scenario: Alvo termina sem sucesso
- **WHEN** o alvo termina em `Failed`
- **THEN** existe linha com `Outcome = TargetUnsuccessful` e `LastObservedTargetState = Failed`

#### Scenario: Nenhuma leitura concluída
- **WHEN** o prazo expira antes de qualquer leitura do alvo concluir
- **THEN** existe linha com `Outcome = Expired`, `LastObservedTargetState` nulo e `SuccessfulReadCount = 0`

#### Scenario: Alvo indisponível antes de criar a task
- **WHEN** o alvo está inativo no momento da chamada da tool
- **THEN** existe linha com `Outcome = NotStarted` e `TargetTaskId` nulo

### Requirement: Falha ao gravar métrica nunca muda o estado terminal da task
Falha em qualquer escrita das tabelas de métrica SHALL ser registrada em log de
aviso e SHALL NOT alterar o estado terminal da task, o artefato produzido, nem o
envio de push notification. A escrita que fecha a execução SHALL acontecer
depois de o estado terminal estar gravado.

#### Scenario: Tabela de métrica indisponível
- **WHEN** a tabela `task_executions` não existe no banco durante a execução de uma task cujo agente responde com sucesso
- **THEN** a task termina em `Completed` com o artefato da resposta
- **AND** é emitido um log de aviso de falha de gravação de métrica com o `TaskId`

### Requirement: Execuções concorrentes não misturam métricas
As linhas de `provider_calls` e `delegation_outcomes` SHALL pertencer à execução
em que a requisição ou a delegação aconteceu, mesmo com várias execuções no
mesmo processo.

#### Scenario: Source e Target no mesmo processo de teste
- **WHEN** Source e Target executam em duas instâncias hospedadas no mesmo processo
- **THEN** toda linha de `provider_calls` com o `TaskId` do Target foi produzida pela execução do Target, e nenhuma linha de `delegation_outcomes` tem o `TaskId` do Target como `SourceTaskId`
