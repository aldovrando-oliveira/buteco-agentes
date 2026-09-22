## ADDED Requirements

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
