## MODIFIED Requirements

### Requirement: Linha de recusa para toda task recusada antes de publicar job

`apps/api` SHALL gravar uma linha por task que recusar antes de publicar job de
execução, com a task, o agente, o motivo e o instante da recusa.

A linha SHALL ser gravada **depois** de a task ter sido transicionada para o
estado terminal de recusa, e nunca antes: métrica que muda o resultado do que ela
mede não é métrica.

A task recusada SHALL continuar sem produzir linha em `task_executions` — a
execução não aconteceu, e inventar uma linha de execução para ela contaria uma
execução que não houve.

#### Scenario: Recusa por agente inativo produz linha
- **WHEN** um `SendMessage` é recusado porque o agente está inativo
- **THEN** existe exatamente uma linha de recusa com o `taskId` dela, o agente, o
  motivo da inatividade e o instante da recusa
- **AND** não existe nenhuma linha em `task_executions` com aquele `taskId`

#### Scenario: Recusa por provedor ou modelo ausentes produz linha
- **WHEN** um `SendMessage` é recusado porque o provedor ou o modelo do agente
  estão nulos
- **THEN** existe exatamente uma linha de recusa com o motivo da configuração
  ausente, distinto do motivo de agente inativo

#### Scenario: Recusa por provedor não configurado no ambiente produz linha
- **WHEN** um `SendMessage` é recusado porque o provedor do agente não está
  configurado no ambiente de `apps/api`
- **THEN** existe exatamente uma linha de recusa com o motivo do provedor não
  configurado, distinto dos outros dois

#### Scenario: Recusa por modelo não habilitado produz linha
- **WHEN** um `SendMessage` é recusado porque o par `provider`+`model` do agente
  não está na lista curada de modelos habilitados, com o provedor configurado
  no ambiente
- **THEN** existe exatamente uma linha de recusa com o motivo `ModelNotEnabled`,
  distinto do motivo do provedor não configurado e do da configuração ausente
- **AND** a linha não carrega provedor nem modelo

#### Scenario: Provedor sem chave e modelo não habilitado gravam o motivo da chave
- **WHEN** um `SendMessage` chega para um agente cujo provedor está sem chave no
  ambiente **e** cujo modelo não está habilitado
- **THEN** a linha de recusa traz o motivo do provedor não configurado, e **não**
  `ModelNotEnabled`

#### Scenario: Agente que não existe mais não é gravado como inativo
- **WHEN** um `SendMessage` chega para um agente cujo registro já não está no
  catálogo
- **THEN** a linha de recusa traz o motivo de agente não encontrado, e **não** o
  de agente inativo

#### Scenario: Task aceita não produz linha de recusa
- **WHEN** um `SendMessage` é aceito e o job é publicado
- **THEN** não existe nenhuma linha de recusa com o `taskId` dela
