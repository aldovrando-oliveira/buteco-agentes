# agent-delegation-execution Specification

## Purpose

TBD - defined by change apps-workers-delegacao-execucao. Update Purpose
after archive. Cobre a execução em tempo real de delegações entre agentes
em `apps/workers`: exposição de tools de delegação ao LLM do Source com
base nos vínculos `AgentDelegation` cadastrados, validação fresca de
Source/Target no momento da chamada, criação de task delegada para o
Target no mesmo `contextId`, controle de profundidade da cadeia de
delegação, espera com timeout pelo resultado do Target, e o comportamento
esperado sob múltiplas réplicas de `apps/workers` consumindo a fila
`agent-tasks`.

## Requirements

### Requirement: Tool de delegação exposta por vínculo cadastrado
O sistema SHALL expor, para cada `AgentDelegation` (Source → Target) cadastrado, uma tool de delegação correspondente ao Target na lista de tools disponível para o LLM do agente Source, junto com as tools MCP já resolvidas para aquele agente.

#### Scenario: Agente com delegações cadastradas recebe uma tool por Target
- **WHEN** `apps/workers` processa uma task do agente Source, que tem dois
  `AgentDelegation` cadastrados (Target A e Target B)
- **THEN** `ChatOptions.Tools` passado ao LLM contém uma tool de
  delegação para o Target A e outra para o Target B, além de qualquer
  tool MCP já resolvida

#### Scenario: Agente sem nenhuma delegação cadastrada não recebe tool de delegação
- **WHEN** `apps/workers` processa uma task de um agente sem nenhum
  `AgentDelegation` de saída
- **THEN** `ChatOptions.Tools` não contém nenhuma tool de delegação

### Requirement: Nome estável e sem colisão para a tool de delegação
O nome de cada tool de delegação SHALL ser derivado de forma
determinística do `Agent.Name` do Target correspondente, e SHALL
permanecer único **no conjunto final de tools entregue ao LLM** — não apenas
entre as tools de delegação —, mesmo quando dois Targets vinculados ao mesmo
Source têm `Agent.Name` colidente, e mesmo quando o nome derivado colide com o
de uma tool MCP resolvida para o mesmo agente.

A unicidade SHALL ser garantida no ponto que une os dois conjuntos, não dentro
da resolução de delegação isoladamente. O sufixo determinístico de dedupe SHALL
respeitar o limite de 64 caracteres do nome exposto: aplicá-lo a um nome já no
limite SHALL encurtar a base para caber, nunca produzir um nome mais longo que
o limite.

#### Scenario: Dois Targets com o mesmo Agent.Name geram nomes de tool distintos
- **WHEN** o Source tem `AgentDelegation` para dois Targets diferentes,
  ambos com `Agent.Name` igual a "Atendimento"
- **THEN** as duas tools de delegação resolvidas têm nomes distintos,
  derivados do mesmo slug base com um sufixo determinístico de dedupe

#### Scenario: Mesmo conjunto de delegações produz os mesmos nomes de tool entre execuções
- **WHEN** a mesma lista de `AgentDelegation` do Source é resolvida em
  duas execuções diferentes, sem nenhuma mudança no cadastro
- **THEN** os nomes das tools de delegação resolvidas são idênticos nas
  duas execuções

#### Scenario: Target com nome longo que colide gera nomes dentro do limite
- **WHEN** o Source tem `AgentDelegation` para dois Targets diferentes com
  `Agent.Name` colidente e longo o bastante para o nome derivado ocupar
  exatamente 64 caracteres
- **THEN** as duas tools de delegação têm nomes distintos e ambos os nomes têm
  no máximo 64 caracteres

### Requirement: Validação fresca do Target antes de delegar
No momento em que a tool de delegação é chamada, o Target SHALL ser
validado com dados lidos do banco naquele instante (não de nenhum
cache): `Agent.IsActive` deve ser verdadeiro, e `Provider`/`Model` não
podem ser nulos. Se qualquer checagem falhar, a delegação SHALL ser
tratada como falha da tool, sem criar nenhuma task para o Target.

#### Scenario: Target inativo no momento da chamada
- **WHEN** a tool de delegação para um Target é chamada, e
  `Agent.IsActive` do Target é `false` no banco naquele momento
- **THEN** a tool retorna um resultado de falha para o LLM do Source,
  sem publicar nenhuma mensagem na fila `agent-tasks`

#### Scenario: Target sem Provider/Model configurados
- **WHEN** a tool de delegação para um Target é chamada, e o Target tem
  `Provider` ou `Model` nulos no banco naquele momento
- **THEN** a tool retorna um resultado de falha para o LLM do Source,
  sem publicar nenhuma mensagem na fila `agent-tasks`

#### Scenario: Target desativado entre o cadastro da delegação e a chamada da tool
- **WHEN** um `AgentDelegation` foi cadastrado enquanto o Target estava
  ativo, e o Target foi desativado antes de a tool de delegação ser
  efetivamente chamada
- **THEN** a tool retorna um resultado de falha, refletindo o estado
  atual do Target, não o estado no momento do cadastro

### Requirement: Validação do Source antes de delegar
O agente Source SHALL estar ativo e com `Provider`/`Model` configurados no momento em que a tool de delegação é chamada; se qualquer checagem falhar, a delegação SHALL ser tratada como falha da tool.

#### Scenario: Source desativado durante o processamento da própria task
- **WHEN** a tool de delegação é chamada, e o agente Source foi
  desativado depois que sua própria task começou a ser processada
- **THEN** a tool retorna um resultado de falha para o LLM continuar,
  sem criar nenhuma task para o Target

### Requirement: Task delegada compartilha o contextId da conversa
Ao delegar, `apps/workers` SHALL criar uma nova task A2A para o Target,
associada ao mesmo `contextId` da task do Source que originou a
delegação. Quando a task do Source tiver um instante de mensagem
disponível (`Message.Metadata["messageInstant"]` da sua própria última
mensagem de usuário, ver capability `a2a-task-lifecycle`), o sistema
SHALL propagar esse mesmo instante para a task criada para o Target,
gravando-o sob a mesma chave `messageInstant` em `Message.Metadata` do
`Message` inicial da task delegada, serializado com o mesmo cuidado de
formato de fio (`A2AJsonUtilities.DefaultOptions`).

#### Scenario: Task do Target nasce no mesmo contextId do Source
- **WHEN** a tool de delegação é chamada durante o processamento de uma
  task do Source com um `contextId` X
- **THEN** a task criada para o Target tem `contextId` igual a X

#### Scenario: Histórico do Target não se mistura com o do Source no mesmo contextId
- **WHEN** o Source e o Target, vinculados por delegação, processam
  tasks diferentes no mesmo `contextId`
- **THEN** a reconstrução de histórico de cada agente (via
  `AgentExecutionService`) considera apenas as tasks daquele `AgentId`
  específico, sem incluir mensagens do outro agente

#### Scenario: Task delegada herda o instante de mensagem do Source
- **WHEN** a tool de delegação é chamada durante o processamento de uma
  task do Source cuja própria última mensagem de usuário tem
  `Message.Metadata["messageInstant"]` disponível
- **THEN** a task criada para o Target tem, em
  `Message.Metadata["messageInstant"]` do seu próprio `Message` inicial,
  o mesmo valor de instante da task do Source

#### Scenario: Task delegada sem instante de mensagem no Source não inventa um
- **WHEN** a tool de delegação é chamada durante o processamento de uma
  task do Source cuja última mensagem de usuário não tem
  `Message.Metadata["messageInstant"]` disponível (ausente ou ilegível)
- **THEN** a task criada para o Target não tem a chave `messageInstant`
  em seu `Message.Metadata`, resolvendo, quando processada, contra seu
  próprio instante de processamento — mesmo comportamento de qualquer
  task sem instante de mensagem

#### Scenario: Source e Target resolvem expressões relativas contra o mesmo instante de mensagem, mesmo processados em momentos diferentes
- **WHEN** o Source processa sua task em um instante de processamento T1,
  delega para o Target, e o Target processa a task delegada em um
  instante de processamento T2 posterior a T1 (fila/latência real entre
  os dois)
- **THEN** tanto a chamada ao LLM do Source quanto a chamada ao LLM do
  Target incluem o mesmo instante de mensagem original, e ambos resolvem
  uma expressão relativa como "amanhã" para a mesma data — a defasagem
  entre T1 e T2 não afeta a data resolvida por nenhum dos dois

### Requirement: Controle de profundidade da cadeia de delegação
Toda task criada por delegação SHALL registrar, em `AgentTask.Metadata`,
quantos saltos de delegação a cadeia já deu desde a mensagem original.
Antes de processar qualquer task, `apps/workers` SHALL checar essa
profundidade contra um teto configurado; se excedido, a delegação que
originou aquela task SHALL ser tratada como falha da tool que a
solicitou, sem processar a task excedente.

#### Scenario: Delegação dentro do teto de profundidade é processada normalmente
- **WHEN** uma task é criada por delegação com profundidade 1 (abaixo do
  teto configurado)
- **THEN** `apps/workers` processa a task normalmente, chamando o LLM do
  Target

#### Scenario: Delegação que excede o teto de profundidade falha graciosamente
- **WHEN** uma cadeia de delegações já atingiu o teto configurado, e uma
  nova delegação tentaria criar uma task além desse teto
- **THEN** a tool de delegação que tentou criar essa task retorna um
  resultado de falha para o LLM que a chamou, e nenhuma task nova é
  processada além do teto

#### Scenario: Profundidade excedida não derruba a task de nenhum agente da cadeia
- **WHEN** uma delegação é rejeitada por exceder o teto de profundidade
- **THEN** a task do agente que tentou delegar continua seu
  processamento normalmente (não é marcada como `failed` por causa
  disso), tratando a rejeição como qualquer outro resultado de falha de
  tool

### Requirement: Espera pela task delegada com timeout e degradação graciosa
A tool de delegação SHALL aguardar a task do Target chegar a um estado
terminal (`Completed`, `Failed`, `Rejected` ou `Canceled`) antes de
retornar um resultado ao LLM do Source, respeitando um timeout
configurado. Se o timeout expirar, ou se o Target chegar a um estado
terminal de falha, a tool SHALL retornar um resultado de falha — a task
do Source SHALL permanecer seu fluxo normal, nunca falhando por causa
disso.

Em **qualquer** dos dois caminhos de falha, a tool SHALL registrar um
registro de diagnóstico que identifique a delegação inteira — o
identificador da task do Source, o do Target, e os identificadores dos
agentes Source e Target — e que carregue o **último estado observado** da
task do Target junto com o instante dessa observação. O registro SHALL
nomear esse campo pelo que ele é: o último estado que a tool chegou a ler,
nunca "o estado no instante da desistência", porque entre a última leitura
bem-sucedida e a desistência cabe um intervalo de poll. Quando a tool
desistir sem nunca ter conseguido ler a task do Target, o registro SHALL
dizer isso explicitamente, e não SHALL apresentá-lo como um estado.

O registro SHALL expor o estado como o valor do estado da task, não como
texto livre reinterpretado, de forma que um consumidor possa separar
`Submitted` (task publicada e nunca consumida) de `Working` (task
consumida e ainda em execução) sem interpretar a redação da mensagem.

#### Scenario: Delegação bem-sucedida devolve o resultado ao Source
- **WHEN** a task delegada ao Target chega a `Completed` dentro do
  timeout
- **THEN** a tool de delegação retorna o conteúdo produzido pelo Target
  como resultado da chamada, e o LLM do Source continua o turno com
  esse resultado disponível

#### Scenario: Timeout expira sem a task do Target concluir
- **WHEN** a task delegada ao Target não chega a nenhum estado terminal
  antes do timeout configurado expirar
- **THEN** a tool de delegação retorna um resultado de falha para o LLM
  do Source, e a task do Source não é marcada como `failed` por causa
  disso

#### Scenario: Task do Target falha durante o processamento
- **WHEN** a task delegada ao Target chega a `Failed`
- **THEN** a tool de delegação retorna um resultado de falha para o LLM
  do Source, tratado pelo LLM como qualquer outra falha de tool, sem
  tratamento especial

#### Scenario: Expiração com o Target nunca consumido é distinguível de Target em execução
- **WHEN** o timeout expira e o último estado lido da task do Target era
  `Submitted` — publicada e nunca consumida por instância nenhuma
- **THEN** o registro de diagnóstico da desistência carrega `Submitted`
  como último estado observado, distinguível sem ambiguidade do registro
  produzido quando o último estado lido era `Working`

#### Scenario: Expiração com o Target em execução é distinguível de Target nunca consumido
- **WHEN** o timeout expira e o último estado lido da task do Target era
  `Working` — consumida por alguma instância e ainda em execução
- **THEN** o registro de diagnóstico da desistência carrega `Working`
  como último estado observado, e os dois registros diferem por esse
  valor, não pela redação da mensagem

#### Scenario: A desistência identifica a delegação inteira, não só a task do Target
- **WHEN** a tool de delegação devolve um resultado de falha ao LLM do
  Source, por timeout ou por estado terminal de falha do Target
- **THEN** o registro de diagnóstico carrega o identificador da task do
  Source, o da task do Target e os identificadores dos agentes Source e
  Target, de forma que a falha possa ser ligada ao registro que a
  execução do Target produziu por conta própria

#### Scenario: Desistência sem nenhuma leitura bem-sucedida do Target
- **WHEN** a tool de delegação desiste sem ter conseguido ler a task do
  Target nenhuma vez
- **THEN** o registro de diagnóstico declara a ausência de observação, em
  vez de apresentar um estado que nunca foi lido

### Requirement: Processamento concorrente de Source e Target entre réplicas de apps/workers
Uma delegação SHALL progredir com sucesso quando processada por um ambiente com pelo menos duas réplicas de `apps/workers` ativas, e SHALL expirar de forma controlada (via timeout, sem travar o processo) quando processada por uma única réplica — consequência do consumidor RabbitMQ de `apps/workers` processar no máximo uma mensagem por vez por instância (`prefetchCount: 1`).

#### Scenario: Duas réplicas processam Source e Target concorrentemente
- **WHEN** duas instâncias separadas de `apps/workers` estão ativas, uma
  delas processando a task do Source (que delega), e a task delegada do
  Target é publicada na fila
- **THEN** a segunda instância consome e processa a task do Target
  enquanto a primeira ainda aguarda, e a delegação conclui com sucesso
  dentro do timeout

#### Scenario: Instância única expira pelo timeout em vez de travar indefinidamente
- **WHEN** uma única instância de `apps/workers` processa a task do
  Source, que delega para um Target — a mesma instância é a única
  capaz de consumir a task delegada, mas está ocupada aguardando o
  resultado dessa mesma task
- **THEN** a tool de delegação expira pelo timeout configurado e retorna
  um resultado de falha para o LLM do Source, em vez de travar o
  processamento indefinidamente

### Requirement: Task delegada registra a origem
Toda task criada por delegação SHALL registrar em `AgentTask.Metadata`, no
mesmo momento em que registra `delegationDepth` — antes do primeiro
`SaveTaskAsync` —, o agente de origem em `delegationSourceAgentId` e a task de
origem em `delegationSourceTaskId`. Task não criada por delegação SHALL NOT ter
nenhuma das duas chaves.

#### Scenario: Metadata da task do Target
- **WHEN** um agente Source, executando a task S, delega para um Target
- **THEN** a task criada para o Target tem `delegationSourceAgentId` igual ao id do Source, `delegationSourceTaskId` igual a S e `delegationDepth` igual à profundidade do Source mais um

#### Scenario: Task externa não tem as chaves
- **WHEN** uma task é criada por `SendMessage` em `apps/api`
- **THEN** o `Metadata` dela não tem `delegationSourceAgentId` nem `delegationSourceTaskId`
