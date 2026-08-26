## MODIFIED Requirements

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
