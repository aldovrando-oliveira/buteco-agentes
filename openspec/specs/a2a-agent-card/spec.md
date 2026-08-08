# a2a-agent-card Specification

## Purpose

TBD - defined by change backend-a2a-agent-card. Update Purpose after archive.

## Requirements

### Requirement: Endpoint de descoberta do AgentCard por agente
O sistema SHALL expor, para cada agente cadastrado em `apps/api`, um
endpoint HTTP `GET /agents/{id}/.well-known/agent-card.json` que retorna o
`AgentCard` do protocolo A2A correspondente a esse agente.

#### Scenario: Agente existente retorna o card com 200
- **WHEN** um cliente faz `GET /agents/{id}/.well-known/agent-card.json`
  para um `id` que corresponde a um agente cadastrado
- **THEN** a API responde `200 OK` com um `AgentCard` válido no corpo

#### Scenario: Agente inexistente retorna 404
- **WHEN** um cliente faz `GET /agents/{id}/.well-known/agent-card.json`
  para um `id` que não corresponde a nenhum agente cadastrado
- **THEN** a API responde `404 Not Found`

### Requirement: AgentCard reflete o estado atual do agente a cada requisição
O sistema SHALL montar o `AgentCard` lendo `Name`, `Description` e `Skills`
diretamente do agente persistido no momento da requisição, sem cache
intermediário que possa ficar desatualizado após uma edição.

#### Scenario: Card reflete os dados no momento da criação
- **WHEN** um agente é criado com `Name`, `Description` e `Skills`
  específicos e, em seguida, seu card é consultado
- **THEN** o `AgentCard` retornado contém exatamente esse `Name`,
  `Description` e as `Skills` mapeadas correspondentes

#### Scenario: Card reflete uma atualização subsequente
- **WHEN** um agente tem seu card consultado, depois é atualizado via
  `PUT /agents/{id}` com `Name`/`Description`/`Skills` diferentes, e o
  card é consultado novamente
- **THEN** o segundo `AgentCard` retornado reflete os novos valores, sem
  nenhum dado do estado anterior

### Requirement: Mapeamento de Skills do agente para AgentSkill do protocolo
O sistema SHALL mapear cada `Skill` do agente (`Name`, `Description`
opcional) para um `AgentSkill` do protocolo, incluindo um `Id` estável e
determinístico gerado a partir do `Name`, com `Description` vazio quando
o valor de origem for nulo.

#### Scenario: Skill com Description mapeada
- **WHEN** um agente tem uma `Skill` com `Name` e `Description` não nulos
- **THEN** o `AgentSkill` correspondente no card tem o mesmo `Name`, o
  mesmo `Description`, e um `Id` não vazio

#### Scenario: Skill sem Description mapeada como string vazia
- **WHEN** um agente tem uma `Skill` com `Description` nulo
- **THEN** o `AgentSkill` correspondente no card tem `Description` igual a
  string vazia, sem erro

#### Scenario: Skills com Name repetido geram Id únicos
- **WHEN** um agente tem duas ou mais `Skills` cujo `Name` produz o mesmo
  identificador base
- **THEN** cada `AgentSkill` correspondente no card recebe um `Id` distinto
  dos demais, de forma determinística (mesma entrada sempre produz a
  mesma saída)

#### Scenario: Agente sem nenhuma Skill expõe lista vazia
- **WHEN** um agente não tem nenhuma `Skill` cadastrada
- **THEN** o `AgentCard` retornado tem `Skills` como lista vazia, sem erro

### Requirement: Capabilities do card refletem funcionalidade real
O sistema SHALL declarar `Capabilities.Streaming` como `false` e
`Capabilities.PushNotifications` como `true` em todo `AgentCard` retornado.

#### Scenario: Streaming continua false, push notifications passa a true
- **WHEN** o card de qualquer agente é consultado
- **THEN** `Capabilities.Streaming` é `false` e `Capabilities.PushNotifications`
  é `true` no corpo da resposta

### Requirement: Card exposto independente do estado operacional do agente
O sistema SHALL retornar o `AgentCard` com `200 OK` para qualquer agente
existente, independente de `IsActive` ou de `Provider`/`Model` estarem
configurados.

#### Scenario: Agente inativo ainda expõe o card
- **WHEN** um agente foi desativado (`IsActive = false`) e seu card é
  consultado
- **THEN** a API responde `200 OK` com o `AgentCard` desse agente

#### Scenario: Agente sem Provider/Model configurados ainda expõe o card
- **WHEN** um agente está no estado "precisa de reconfiguração"
  (`Provider`/`Model` nulos) e seu card é consultado
- **THEN** a API responde `200 OK` com o `AgentCard` desse agente
