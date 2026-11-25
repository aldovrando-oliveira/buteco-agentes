## MODIFIED Requirements

### Requirement: Validação fresca do Target antes de delegar
No momento em que a tool de delegação é chamada, o Target SHALL ser
validado com dados lidos do banco naquele instante (não de nenhum
cache): `Agent.IsActive` deve ser verdadeiro, `Provider`/`Model` não
podem ser nulos, e o par `Provider`+`Model` SHALL estar na lista curada de
modelos habilitados, lida do banco naquele mesmo instante. Se qualquer checagem falhar, a delegação SHALL ser
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

#### Scenario: Target com modelo não habilitado
- **WHEN** a tool de delegação para um Target é chamada, e o par
  `Provider`+`Model` do Target não está na lista curada de modelos
  habilitados naquele momento
- **THEN** a tool retorna um resultado de falha para o LLM do Source, sem
  publicar nenhuma mensagem na fila `agent-tasks` e sem criar task para o
  Target, e o resultado da delegação é gravado como não iniciada

#### Scenario: Target com modelo habilitado de novo volta a receber delegação
- **WHEN** o modelo do Target foi retirado e habilitado de novo antes de a
  tool de delegação ser chamada
- **THEN** a delegação cria a task do Target e publica o job, sem reiniciar
  nenhum processo

### Requirement: Validação do Source antes de delegar
O agente Source SHALL estar ativo, com `Provider`/`Model` configurados e com o par `Provider`+`Model` na lista curada de modelos habilitados, lidos do banco no momento em que a tool de delegação é chamada; se qualquer checagem falhar, a delegação SHALL ser tratada como falha da tool.

#### Scenario: Source desativado durante o processamento da própria task
- **WHEN** a tool de delegação é chamada, e o agente Source foi
  desativado depois que sua própria task começou a ser processada
- **THEN** a tool retorna um resultado de falha para o LLM continuar,
  sem criar nenhuma task para o Target

#### Scenario: Modelo do Source retirado durante o processamento da própria task
- **WHEN** a tool de delegação é chamada, e o modelo do agente Source foi
  retirado da lista curada depois que a task dele começou a ser processada
- **THEN** a tool retorna um resultado de falha para o LLM continuar, sem
  criar nenhuma task para o Target
